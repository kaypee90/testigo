using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testigo.Forms;
using Testigo.Infrastructure.Persistence;
using Testigo.Security;
using Testigo.Support;
using Testigo.Taxonomy;
using Testigo.Triage;

namespace Testigo.Infrastructure.Services;

/// <summary>What the reporter gets back after submitting (FR-INT-9).</summary>
public sealed record IntakeReceipt
{
    public required string CaseCode { get; init; }
    /// <summary>Only when the reporter didn't choose one: shown once, never stored in clear.</summary>
    public string? GeneratedPassphrase { get; init; }
    /// <summary>The keyword rules suggest a risk to someone's safety: show emergency contacts first.</summary>
    public bool ImmediateDanger { get; init; }
    public required CountryContacts Contacts { get; init; }
    public required string ResponseTime { get; init; }
}

public sealed record IntakeResult(IntakeReceipt? Receipt, IReadOnlyList<FieldError> Errors);

/// <summary>
/// Receives a report: validates it against the form, encrypts it, creates its case and records
/// consent, all in one transaction (FR-INT-1 to FR-INT-3, FR-INT-6, FR-INT-9, NFR-SEC-4).
/// Nothing about the report's content, the case code or the reporter is logged.
/// </summary>
public sealed class IntakeService(
    IDbContextFactory<TestigoDbContext> factory,
    IFieldCipher cipher,
    TestigoKeys keys,
    PassphraseHasher passphrases,
    FormDefinition form,
    HarmTaxonomy taxonomy,
    DangerDetector danger,
    SupportContacts contacts,
    CaseAudit audit,
    IOptions<TestigoOptions> options,
    ILogger<IntakeService> logger,
    TimeProvider clock)
{
    public FormDefinition Form => form;

    public async Task<IntakeResult> SubmitAsync(ReportSubmission submission, string channel, CancellationToken cancellationToken, string? organizationId = null)
    {
        var (report, errors) = ReportValidator.Validate(form, taxonomy, submission);
        var list = errors.ToList();
        string? generated = null;
        var passphrase = submission.Passphrase?.Trim();
        if (string.IsNullOrEmpty(passphrase))
        {
            generated = PassphraseGenerator.Generate();
            passphrase = generated;
        }
        else if (passphrases.Problem(passphrase) is { } problem)
        {
            list.Add(new FieldError("passphrase", problem));
        }

        if (report is null || list.Count > 0) return new IntakeResult(null, list);

        var org = organizationId ?? options.Value.Intake.Organization;
        var now = clock.GetUtcNow();
        var reportId = Guid.CreateVersion7(now);
        var caseId = Guid.CreateVersion7(now);
        var code = CaseCodes.Generate();
        var dangerMatches = danger.Find(report.FreeText);

        var row = new ReportRow
        {
            Id = reportId,
            OrganizationId = org,
            CaseCodeHash = CaseCodes.LookupHash(CaseCodes.Normalize(code)!, keys.LookupKey),
            PassphraseHash = passphrases.Hash(passphrase),
            FormVersion = report.FormVersion,
            NoticeVersion = report.NoticeVersion,
            Channel = channel,
            Language = LanguageDetector.Detect(report.FreeText),
            Country = report.Country,
            AnswersCipher = await cipher.EncryptAsync(org, KeyPurpose.Content, JsonSerializer.Serialize(report.Answers), FieldAad.Answers(reportId), cancellationToken),
            FreeTextCipher = await cipher.EncryptAsync(org, KeyPurpose.Content, report.FreeText, FieldAad.FreeText(reportId), cancellationToken),
            SubmittedAt = now,
        };

        ContactRow? contact = null;
        if (report.ConsentToContact && report.ContactMethod is { } method && report.ContactValue is { } value)
        {
            contact = new ContactRow
            {
                ReportId = reportId,
                OrganizationId = org,
                MethodCipher = await cipher.EncryptAsync(org, KeyPurpose.Contact, method, FieldAad.ContactMethod(reportId), cancellationToken),
                ValueCipher = await cipher.EncryptAsync(org, KeyPurpose.Contact, value, FieldAad.ContactValue(reportId), cancellationToken),
            };
        }

        int number;
        await using (var db = await factory.CreateDbContextAsync(cancellationToken))
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            number = await NextCaseNumberAsync(db, org, cancellationToken);
            db.Reports.Add(row);
            if (contact is not null) db.Contacts.Add(contact);
            db.Consents.Add(new ConsentRow
            {
                Id = Guid.CreateVersion7(now),
                ReportId = reportId,
                OrganizationId = org,
                NoticeVersion = report.NoticeVersion,
                Processing = true,
                Contact = report.ConsentToContact,
                Publication = report.ConsentToPublication,
                RecordedAt = now,
            });
            db.Cases.Add(new CaseRow
            {
                Id = caseId,
                OrganizationId = org,
                ReportId = reportId,
                Number = number,
                ImmediateDanger = dangerMatches.Count > 0,
                DangerSource = dangerMatches.Count > 0 ? "rules" : null,
                TaxonomyVersion = taxonomy.Version,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        await audit.RecordAsync(org, Actor.Reporter, "report.received", caseId,
            $"Caso {CaseNumbers.Format(number)} recibido ({channel})" + (dangerMatches.Count > 0 ? "; posible riesgo inmediato según las reglas" : string.Empty),
            new { channel, form_version = report.FormVersion, form_language = report.FormLanguage, notice_version = report.NoticeVersion, danger_rules = dangerMatches.Select(m => m.RuleKey) },
            key: $"report.received:{reportId:N}", cancellationToken: cancellationToken);
        logger.LogInformation("Report received for case {CaseNumber} via {Channel}", CaseNumbers.Format(number), channel);

        var forCountry = (contacts.For(report.Country) ?? new CountryContacts()).In(report.FormLanguage);
        return new IntakeResult(new IntakeReceipt
        {
            CaseCode = code,
            GeneratedPassphrase = generated,
            ImmediateDanger = dangerMatches.Count > 0,
            Contacts = forCountry,
            ResponseTime = forCountry.ResponseTime,
        }, []);
    }

    /// <summary>The organization's next case number, taken inside the caller's transaction.</summary>
    private static async Task<int> NextCaseNumberAsync(TestigoDbContext db, string org, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            INSERT INTO testigo.case_counters ("OrganizationId", "Next") VALUES (@org, 2)
            ON CONFLICT ("OrganizationId") DO UPDATE SET "Next" = testigo.case_counters."Next" + 1
            RETURNING "Next" - 1
            """;
        command.Parameters.AddWithValue("org", org);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}

/// <summary>The associated data each encrypted field is bound to, so a ciphertext can't be moved to another row or field.</summary>
public static class FieldAad
{
    public static string Answers(Guid reportId) => $"report:{reportId:N}:answers";
    public static string FreeText(Guid reportId) => $"report:{reportId:N}:free_text";
    public static string ContactMethod(Guid reportId) => $"contact:{reportId:N}:method";
    public static string ContactValue(Guid reportId) => $"contact:{reportId:N}:value";
    public static string RedactedText(Guid reportId) => $"redacted:{reportId:N}:text";
    public static string RedactedMap(Guid reportId) => $"redacted:{reportId:N}:map";
    public static string Quote(Guid classificationId) => $"classification:{classificationId:N}:quote";
    public static string Value(Guid classificationId) => $"classification:{classificationId:N}:value";
    public static string Reason(Guid statusChangeId) => $"status_change:{statusChangeId:N}:reason";
}

public static class CaseNumbers
{
    public static string Format(int number) => $"T-{number:D4}";
}
