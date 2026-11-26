using System.Text.Json;
using AgentRuntime.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Testigo.Cases;
using Testigo.Forms;
using Testigo.Infrastructure.Persistence;
using Testigo.Security;
using Testigo.Taxonomy;

namespace Testigo.Infrastructure.Services;

public enum CaseError { None, NotFound, Forbidden, Invalid, Conflict }

public sealed record CaseResult<T>(T? Value, CaseError Error = CaseError.None, string? Message = null)
{
    public static CaseResult<T> Ok(T value) => new(value);
    public static CaseResult<T> Fail(CaseError error, string message) => new(default, error, message);
}

public sealed record QueueFilter
{
    public string? Status { get; init; }
    /// <summary>"dimension:term".</summary>
    public string? Term { get; init; }
    public string? Country { get; init; }
    /// <summary>A user id, "me" or "none".</summary>
    public string? Assignee { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public bool DangerOnly { get; init; }
    public int Limit { get; init; } = 50;
    public int Offset { get; init; }
}

public sealed record TermView(string Dimension, string Term, string Label, string State);

/// <summary>A queue row: no report content, so listing the queue reads nothing sensitive.</summary>
public sealed record CaseSummary(Guid Id, string Number, string Status, bool ImmediateDanger, string? DangerSource, string AnalysisState,
    string? Country, string Language, DateTimeOffset SubmittedAt, string? AssigneeId, int PendingSuggestions, IReadOnlyList<TermView> Terms);

public sealed record QueuePage(IReadOnlyList<CaseSummary> Items, int Total);

public sealed record SuggestionView(Guid Id, string Kind, string Dimension, string? Term, string? Label, string? Value, string? Quote, int? Start, int? Length,
    string Source, string State, string? Model, string? PromptVersion, string TaxonomyVersion, DateTimeOffset CreatedAt, string? DecidedBy, DateTimeOffset? DecidedAt);

public sealed record LinkView(Guid Id, Guid OtherCaseId, string OtherNumber, string OtherStatus, string Kind, double Score, string State, string? DecidedBy);

public sealed record StatusChangeView(string From, string To, string ByUserId, DateTimeOffset At, string Reason);

public sealed record AnswerView(string Field, string Label, IReadOnlyList<string> Values);

public sealed record ConsentView(string NoticeVersion, bool Processing, bool Contact, bool Publication, DateTimeOffset RecordedAt, DateTimeOffset? WithdrawnAt, bool HasContactDetails);

public sealed record CaseView
{
    public required CaseSummary Summary { get; init; }
    public required string Text { get; init; }
    public string? RedactedText { get; init; }
    public IReadOnlyList<string> RedactionTypes { get; init; } = [];
    public string? RedactionMethod { get; init; }
    public required IReadOnlyList<AnswerView> Answers { get; init; }
    public required IReadOnlyList<SuggestionView> Suggestions { get; init; }
    public required IReadOnlyList<LinkView> Links { get; init; }
    public required IReadOnlyList<StatusChangeView> History { get; init; }
    public required ConsentView Consent { get; init; }
    public string? AnalysisNote { get; init; }
    public string Severity { get; init; } = "unset";
    public string? VerifiedBy { get; init; }
    public DateTimeOffset? VerifiedAt { get; init; }
    public required IReadOnlyList<string> AllowedStatuses { get; init; }
    public required string FormVersion { get; init; }
    public required string Channel { get; init; }
}

public sealed record AccessEntry(DateTimeOffset At, string ActorType, string ActorId, string ActorName, string Action, string Summary, string Outcome);

public sealed record StaffMember(string UserId, string Email, string Name, string OrganizationRole, bool TwoFactor, IReadOnlyList<string> Roles);

/// <summary>
/// The analyst queue and case view (FR-CASE-1 to FR-CASE-4, FR-CASE-7). Every read of report
/// content and every decision goes to the audit log (NFR-SEC-10). Agents' suggestions only change
/// when a person confirms, edits or rejects them (NFR-AI-3).
/// </summary>
public sealed class CaseService(
    IDbContextFactory<TestigoDbContext> factory,
    IDbContextFactory<AgentDbContext> platform,
    IFieldCipher cipher,
    HarmTaxonomy taxonomy,
    FormDefinition form,
    CaseAudit audit,
    IOptions<TestigoOptions> options,
    TimeProvider clock)
{
    public const int MaxReasonLength = 1000;

    public async Task<QueuePage> QueueAsync(StaffContext staff, QueueFilter f, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var q = db.Cases.AsNoTracking().Where(c => c.OrganizationId == staff.OrganizationId);
        if (!string.IsNullOrWhiteSpace(f.Status)) q = q.Where(c => c.Status == f.Status);
        if (f.DangerOnly) q = q.Where(c => c.ImmediateDanger);
        if (f.Assignee is "me") q = q.Where(c => c.AssigneeId == staff.UserId);
        else if (f.Assignee is "none") q = q.Where(c => c.AssigneeId == null);
        else if (!string.IsNullOrWhiteSpace(f.Assignee)) q = q.Where(c => c.AssigneeId == f.Assignee);
        if (f.From is { } from) q = q.Where(c => c.CreatedAt >= from);
        if (f.To is { } to) q = q.Where(c => c.CreatedAt < to);
        if (f.Term?.Split(':', 2) is [var dim, var term])
        {
            q = q.Where(c => db.Classifications.Any(x => x.CaseId == c.Id && x.Kind == "term" && x.Dimension == dim && x.Term == term && x.State != "rejected"));
        }

        var joined = q.Join(db.Reports, c => c.ReportId, r => r.Id, (c, r) => new { c, r });
        if (!string.IsNullOrWhiteSpace(f.Country)) joined = joined.Where(x => x.r.Country == f.Country);
        var total = await joined.CountAsync(cancellationToken);
        // Urgent first, then oldest first (FR-CASE-1).
        var rows = await joined.OrderByDescending(x => x.c.ImmediateDanger).ThenBy(x => x.c.CreatedAt)
            .Skip(Math.Max(0, f.Offset)).Take(Math.Clamp(f.Limit, 1, 200)).ToListAsync(cancellationToken);
        var ids = rows.Select(x => x.c.Id).ToList();
        var classifications = await db.Classifications.AsNoTracking().Where(x => ids.Contains(x.CaseId)).ToListAsync(cancellationToken);
        return new QueuePage([.. rows.Select(x => Summary(x.c, x.r, classifications.Where(k => k.CaseId == x.c.Id).ToList()))], total);
    }

    public async Task<CaseResult<CaseView>> GetAsync(StaffContext staff, Guid caseId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var c = await db.Cases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == caseId && x.OrganizationId == staff.OrganizationId, cancellationToken);
        if (c is null) return CaseResult<CaseView>.Fail(CaseError.NotFound, "No existe ese caso.");
        var r = await db.Reports.AsNoTracking().FirstAsync(x => x.Id == c.ReportId, cancellationToken);
        var org = staff.OrganizationId;

        // The read is recorded before anything is decrypted (NFR-SEC-10).
        await audit.RecordAsync(org, staff.Actor, "case.read", c.Id, $"Lectura del caso {CaseNumbers.Format(c.Number)}", new { reason = "case_review" }, cancellationToken: cancellationToken);

        var text = await cipher.DecryptAsync(org, KeyPurpose.Content, r.FreeTextCipher, FieldAad.FreeText(r.Id), cancellationToken);
        var answers = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(
            await cipher.DecryptAsync(org, KeyPurpose.Content, r.AnswersCipher, FieldAad.Answers(r.Id), cancellationToken)) ?? [];

        var redacted = await db.RedactedCopies.AsNoTracking().FirstOrDefaultAsync(x => x.ReportId == r.Id, cancellationToken);
        string? redactedText = redacted is null ? null : await cipher.DecryptAsync(org, KeyPurpose.Content, redacted.TextCipher, FieldAad.RedactedText(r.Id), cancellationToken);

        var classifications = await db.Classifications.AsNoTracking().Where(x => x.CaseId == c.Id).OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
        var suggestions = new List<SuggestionView>();
        foreach (var k in classifications)
        {
            var quote = k.QuoteCipher is null ? null : await cipher.DecryptAsync(org, KeyPurpose.Content, k.QuoteCipher, FieldAad.Quote(k.Id), cancellationToken);
            var value = k.ValueCipher is null ? null : await cipher.DecryptAsync(org, KeyPurpose.Content, k.ValueCipher, FieldAad.Value(k.Id), cancellationToken);
            suggestions.Add(new SuggestionView(k.Id, k.Kind, k.Dimension, k.Term, LabelOf(k), value, quote, k.EvidenceStart, k.EvidenceLength,
                k.Source, k.State, k.Model, k.PromptVersion, k.TaxonomyVersion, k.CreatedAt, k.DecidedBy, k.DecidedAt));
        }

        var links = await db.CaseLinks.AsNoTracking().Where(l => l.OrganizationId == org && (l.CaseId == c.Id || l.OtherCaseId == c.Id))
            .Join(db.Cases, l => l.CaseId == c.Id ? l.OtherCaseId : l.CaseId, o => o.Id, (l, o) => new { l, o })
            .ToListAsync(cancellationToken);

        var history = new List<StatusChangeView>();
        foreach (var h in await db.StatusChanges.AsNoTracking().Where(x => x.CaseId == c.Id).OrderBy(x => x.At).ToListAsync(cancellationToken))
        {
            history.Add(new StatusChangeView(h.From, h.To, h.ByUserId, h.At, await cipher.DecryptAsync(org, KeyPurpose.Content, h.ReasonCipher, FieldAad.Reason(h.Id), cancellationToken)));
        }

        var consent = await db.Consents.AsNoTracking().FirstAsync(x => x.ReportId == r.Id, cancellationToken);
        var hasContact = await db.Contacts.AnyAsync(x => x.ReportId == r.Id, cancellationToken);
        CaseStatusTransitions.TryParse(c.Status, out var status);

        return CaseResult<CaseView>.Ok(new CaseView
        {
            Summary = Summary(c, r, classifications),
            Text = text,
            RedactedText = redactedText,
            RedactionTypes = redacted?.Types.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [],
            RedactionMethod = redacted?.MethodVersion,
            Answers = [.. answers.Select(a => Answer(a.Key, a.Value))],
            Suggestions = suggestions,
            Links = [.. links.Select(x => new LinkView(x.l.Id, x.o.Id, CaseNumbers.Format(x.o.Number), x.o.Status, x.l.Kind, x.l.Score, x.l.State, x.l.DecidedBy))],
            History = history,
            Consent = new ConsentView(consent.NoticeVersion, consent.Processing, consent.Contact, consent.Publication, consent.RecordedAt, consent.WithdrawnAt, hasContact),
            AnalysisNote = r.AnalysisNote,
            Severity = c.Severity,
            VerifiedBy = c.VerifiedBy,
            VerifiedAt = c.VerifiedAt,
            AllowedStatuses = [.. CaseStatusTransitions.From(status).Where(s => !CaseStatusTransitions.NeedsReviewer(s) || staff.IsReviewer || !options.Value.Staff.ReviewerVerifies).Select(CaseStatusTransitions.Name)],
            FormVersion = r.FormVersion,
            Channel = r.Channel,
        });
    }

    /// <summary>Confirms, rejects or edits a suggestion. An edit keeps the original (rejected) and adds the analyst's version (confirmed).</summary>
    public async Task<CaseResult<bool>> DecideSuggestionAsync(StaffContext staff, Guid caseId, Guid suggestionId, string action, string? term, string? value, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == caseId && x.OrganizationId == staff.OrganizationId, cancellationToken);
        var k = c is null ? null : await db.Classifications.FirstOrDefaultAsync(x => x.Id == suggestionId && x.CaseId == caseId, cancellationToken);
        if (c is null || k is null) return CaseResult<bool>.Fail(CaseError.NotFound, "No existe esa sugerencia.");
        if (c.Status is "withdrawn") return CaseResult<bool>.Fail(CaseError.Conflict, "El caso fue retirado.");
        var now = clock.GetUtcNow();
        switch (action)
        {
            case "confirm":
            case "reject":
                k.State = action == "confirm" ? "confirmed" : "rejected";
                k.DecidedBy = staff.UserId;
                k.DecidedAt = now;
                break;
            case "edit":
                if (k.Kind == "term")
                {
                    if (string.IsNullOrWhiteSpace(term) || !taxonomy.Has(k.Dimension, term)) return CaseResult<bool>.Fail(CaseError.Invalid, "Elige un término de la taxonomía.");
                }
                else if (k.Kind == "field")
                {
                    if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 200) return CaseResult<bool>.Fail(CaseError.Invalid, "Escribe un valor de hasta 200 caracteres.");
                }
                else
                {
                    return CaseResult<bool>.Fail(CaseError.Invalid, "Esta sugerencia solo se puede confirmar o descartar.");
                }

                k.State = "rejected";
                k.DecidedBy = staff.UserId;
                k.DecidedAt = now;
                var id = Guid.CreateVersion7(now);
                db.Classifications.Add(new ClassificationRow
                {
                    Id = id,
                    CaseId = caseId,
                    OrganizationId = staff.OrganizationId,
                    Kind = k.Kind,
                    Dimension = k.Dimension,
                    Term = k.Kind == "term" ? term : null,
                    ValueCipher = k.Kind == "field" ? await cipher.EncryptAsync(staff.OrganizationId, KeyPurpose.Content, value!.Trim(), FieldAad.Value(id), cancellationToken) : null,
                    QuoteCipher = k.QuoteCipher is null ? null : await cipher.EncryptAsync(staff.OrganizationId, KeyPurpose.Content,
                        await cipher.DecryptAsync(staff.OrganizationId, KeyPurpose.Content, k.QuoteCipher, FieldAad.Quote(k.Id), cancellationToken), FieldAad.Quote(id), cancellationToken),
                    EvidenceStart = k.EvidenceStart,
                    EvidenceLength = k.EvidenceLength,
                    Source = "analyst",
                    State = "confirmed",
                    TaxonomyVersion = taxonomy.Version,
                    CreatedAt = now,
                    DecidedBy = staff.UserId,
                    DecidedAt = now,
                });
                break;
            default:
                return CaseResult<bool>.Fail(CaseError.Invalid, "Acción desconocida.");
        }

        c.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(staff.OrganizationId, staff.Actor, $"suggestion.{action}", caseId,
            $"Sugerencia {Describe(k)} {Verb(action)} en el caso {CaseNumbers.Format(c.Number)}",
            new { suggestion = suggestionId, kind = k.Kind, dimension = k.Dimension, term = k.Kind == "term" ? k.Term : null, new_term = action == "edit" && k.Kind == "term" ? term : null },
            cancellationToken: cancellationToken);
        return CaseResult<bool>.Ok(true);
    }

    /// <summary>An analyst's own classification, without a model suggestion behind it.</summary>
    public async Task<CaseResult<Guid>> AddClassificationAsync(StaffContext staff, Guid caseId, string dimension, string term, CancellationToken cancellationToken)
    {
        if (!taxonomy.Has(dimension, term)) return CaseResult<Guid>.Fail(CaseError.Invalid, "Elige un término de la taxonomía.");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == caseId && x.OrganizationId == staff.OrganizationId, cancellationToken);
        if (c is null) return CaseResult<Guid>.Fail(CaseError.NotFound, "No existe ese caso.");
        if (await db.Classifications.AnyAsync(x => x.CaseId == caseId && x.Kind == "term" && x.Dimension == dimension && x.Term == term && x.State == "confirmed", cancellationToken))
        {
            return CaseResult<Guid>.Fail(CaseError.Conflict, "Ese término ya está confirmado.");
        }

        var now = clock.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        db.Classifications.Add(new ClassificationRow
        {
            Id = id, CaseId = caseId, OrganizationId = staff.OrganizationId, Kind = "term", Dimension = dimension, Term = term,
            Source = "analyst", State = "confirmed", TaxonomyVersion = taxonomy.Version, CreatedAt = now, DecidedBy = staff.UserId, DecidedAt = now,
        });
        c.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(staff.OrganizationId, staff.Actor, "classification.added", caseId, $"Clasificación {dimension}:{term} agregada al caso {CaseNumbers.Format(c.Number)}",
            new { dimension, term }, cancellationToken: cancellationToken);
        return CaseResult<Guid>.Ok(id);
    }

    /// <summary>Moves a case along the status workflow (FR-CASE-3, FR-CASE-4). Who, when and why are recorded.</summary>
    public async Task<CaseResult<string>> ChangeStatusAsync(StaffContext staff, Guid caseId, string to, string? reason, CancellationToken cancellationToken)
    {
        if (!CaseStatusTransitions.TryParse(to, out var target)) return CaseResult<string>.Fail(CaseError.Invalid, "Estado desconocido.");
        reason = reason?.Trim();
        if (string.IsNullOrEmpty(reason)) return CaseResult<string>.Fail(CaseError.Invalid, "Explica el motivo del cambio.");
        if (reason.Length > MaxReasonLength) return CaseResult<string>.Fail(CaseError.Invalid, $"Usa como máximo {MaxReasonLength} caracteres.");

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == caseId && x.OrganizationId == staff.OrganizationId, cancellationToken);
        if (c is null) return CaseResult<string>.Fail(CaseError.NotFound, "No existe ese caso.");
        if (!CaseStatusTransitions.TryParse(c.Status, out var from) || !CaseStatusTransitions.CanMove(from, target))
        {
            return CaseResult<string>.Fail(CaseError.Conflict, $"Un caso en estado {c.Status} no puede pasar a {to}.");
        }

        if (CaseStatusTransitions.NeedsReviewer(target))
        {
            if (options.Value.Staff.ReviewerVerifies && !staff.IsReviewer) return CaseResult<string>.Fail(CaseError.Forbidden, "Verificar un caso requiere el rol de Revisión.");
            // Verification records what supports it: at least one confirmed classification (FR-CASE-4).
            if (!await db.Classifications.AnyAsync(x => x.CaseId == caseId && x.State == "confirmed" && x.Kind == "term", cancellationToken))
            {
                return CaseResult<string>.Fail(CaseError.Conflict, "Confirma al menos una clasificación antes de verificar el caso.");
            }
        }

        var now = clock.GetUtcNow();
        var changeId = Guid.CreateVersion7(now);
        db.StatusChanges.Add(new StatusChangeRow
        {
            Id = changeId, CaseId = caseId, OrganizationId = staff.OrganizationId, From = c.Status, To = to, ByUserId = staff.UserId, At = now,
            ReasonCipher = await cipher.EncryptAsync(staff.OrganizationId, KeyPurpose.Content, reason, FieldAad.Reason(changeId), cancellationToken),
        });
        var previous = c.Status;
        c.Status = to;
        c.UpdatedAt = now;
        if (target == CaseStatus.Verified)
        {
            c.VerifiedBy = staff.UserId;
            c.VerifiedAt = now;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CaseResult<string>.Fail(CaseError.Conflict, "Alguien cambió el caso al mismo tiempo. Vuelve a cargarlo.");
        }

        var confirmed = target == CaseStatus.Verified
            ? await db.Classifications.AsNoTracking().Where(x => x.CaseId == caseId && x.State == "confirmed" && x.Kind == "term").Select(x => x.Dimension + ":" + x.Term).ToListAsync(cancellationToken)
            : null;
        // The reason can quote the report, so it stays in the encrypted table, not the audit log.
        await audit.RecordAsync(staff.OrganizationId, staff.Actor, "case.status", caseId, $"Caso {CaseNumbers.Format(c.Number)}: {previous} → {to}",
            new { from = previous, to, status_change = changeId, evidence = confirmed }, cancellationToken: cancellationToken);
        return CaseResult<string>.Ok(to);
    }

    public async Task<CaseResult<bool>> DecideLinkAsync(StaffContext staff, Guid caseId, Guid linkId, string action, CancellationToken cancellationToken)
    {
        if (action is not ("confirm" or "reject")) return CaseResult<bool>.Fail(CaseError.Invalid, "Acción desconocida.");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var link = await db.CaseLinks.FirstOrDefaultAsync(l => l.Id == linkId && l.OrganizationId == staff.OrganizationId && (l.CaseId == caseId || l.OtherCaseId == caseId), cancellationToken);
        if (link is null) return CaseResult<bool>.Fail(CaseError.NotFound, "No existe esa relación.");
        link.State = action == "confirm" ? "confirmed" : "rejected";
        link.DecidedBy = staff.UserId;
        link.DecidedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(staff.OrganizationId, staff.Actor, $"link.{action}", caseId, $"Relación {link.Kind} {Verb(action)}", new { link = linkId, other = link.CaseId == caseId ? link.OtherCaseId : link.CaseId, kind = link.Kind },
            cancellationToken: cancellationToken);
        return CaseResult<bool>.Ok(true);
    }

    /// <summary>Assigns a case to an analyst or reviewer of the organization, or unassigns it (FR-CASE-7).</summary>
    public async Task<CaseResult<bool>> AssignAsync(StaffContext staff, Guid caseId, string? assigneeId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == caseId && x.OrganizationId == staff.OrganizationId, cancellationToken);
        if (c is null) return CaseResult<bool>.Fail(CaseError.NotFound, "No existe ese caso.");
        if (assigneeId is not null && !await db.StaffRoles.AnyAsync(r => r.OrganizationId == staff.OrganizationId && r.UserId == assigneeId && (r.Role == StaffRoles.Analyst || r.Role == StaffRoles.Reviewer), cancellationToken))
        {
            return CaseResult<bool>.Fail(CaseError.Invalid, "Solo se puede asignar a personas con rol de Análisis o Revisión.");
        }

        c.AssigneeId = assigneeId;
        c.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(staff.OrganizationId, staff.Actor, "case.assign", caseId, $"Caso {CaseNumbers.Format(c.Number)} asignado", new { assignee = assigneeId }, cancellationToken: cancellationToken);
        return CaseResult<bool>.Ok(true);
    }

    /// <summary>The reporter's contact details: a separate, recorded read that needs a reason (NFR-SEC-4, NFR-SEC-10).</summary>
    public async Task<CaseResult<(string Method, string Value)>> RevealContactAsync(StaffContext staff, Guid caseId, string? reason, CancellationToken cancellationToken)
    {
        // NFR-OPS-4: an audit reason is a fixed code, never arbitrary text that could identify someone.
        if (reason is not ("follow_up" or "clarification" or "support_referral"))
            return CaseResult<(string, string)>.Fail(CaseError.Invalid, "Elige un motivo de contacto de la lista.");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var c = await db.Cases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == caseId && x.OrganizationId == staff.OrganizationId, cancellationToken);
        var contact = c is null ? null : await db.Contacts.AsNoTracking().FirstOrDefaultAsync(x => x.ReportId == c.ReportId, cancellationToken);
        if (c is null || contact is null) return CaseResult<(string, string)>.Fail(CaseError.NotFound, "Este caso no tiene datos de contacto.");
        await audit.RecordAsync(staff.OrganizationId, staff.Actor, "contact.read", caseId, $"Lectura del contacto del caso {CaseNumbers.Format(c.Number)}", new { reason }, cancellationToken: cancellationToken);
        var method = await cipher.DecryptAsync(staff.OrganizationId, KeyPurpose.Contact, contact.MethodCipher, FieldAad.ContactMethod(c.ReportId), cancellationToken);
        var value = await cipher.DecryptAsync(staff.OrganizationId, KeyPurpose.Contact, contact.ValueCipher, FieldAad.ContactValue(c.ReportId), cancellationToken);
        return CaseResult<(string, string)>.Ok((method, value));
    }

    /// <summary>Who read or changed a case (NFR-SEC-10). Reviewers only.</summary>
    public async Task<CaseResult<IReadOnlyList<AccessEntry>>> AccessLogAsync(StaffContext staff, Guid caseId, CancellationToken cancellationToken)
    {
        if (!staff.IsReviewer) return CaseResult<IReadOnlyList<AccessEntry>>.Fail(CaseError.Forbidden, "El registro de accesos es para el rol de Revisión.");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (!await db.Cases.AnyAsync(x => x.Id == caseId && x.OrganizationId == staff.OrganizationId, cancellationToken))
        {
            return CaseResult<IReadOnlyList<AccessEntry>>.Fail(CaseError.NotFound, "No existe ese caso.");
        }

        var entries = await audit.ForCaseAsync(staff.OrganizationId, caseId, null, 500, cancellationToken);
        return CaseResult<IReadOnlyList<AccessEntry>>.Ok([.. entries.Select(e => new AccessEntry(e.At, e.ActorType, e.ActorId, e.ActorName, e.Action, e.Summary, e.Outcome))]);
    }

    /// <summary>The organization's members and their Testigo roles, for admins and reviewers.</summary>
    public async Task<IReadOnlyList<StaffMember>> StaffAsync(string organizationId, CancellationToken cancellationToken)
    {
        await using var p = await platform.CreateDbContextAsync(cancellationToken);
        var members = await p.Memberships.AsNoTracking().Where(m => m.TenantId == organizationId)
            .Join(p.Users, m => m.UserId, u => u.UserId, (m, u) => new { u.UserId, u.Email, u.Name, m.Role, u.TwoFactorEnabledAt })
            .ToListAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var roles = await db.StaffRoles.AsNoTracking().Where(r => r.OrganizationId == organizationId).ToListAsync(cancellationToken);
        return [.. members.OrderBy(m => m.Email).Select(m => new StaffMember(m.UserId, m.Email, m.Name, m.Role, m.TwoFactorEnabledAt is not null,
            [.. roles.Where(r => r.UserId == m.UserId).Select(r => r.Role).Order()]))];
    }

    /// <summary>Grants or revokes a Testigo role. Organization admins only; recorded in the audit log.</summary>
    public async Task<CaseResult<bool>> SetRoleAsync(StaffContext admin, string userId, string role, bool granted, CancellationToken cancellationToken)
    {
        if (!admin.IsOrganizationAdmin) return CaseResult<bool>.Fail(CaseError.Forbidden, "Solo la administración de la organización asigna roles.");
        if (!StaffRoles.All.Contains(role)) return CaseResult<bool>.Fail(CaseError.Invalid, "Rol desconocido.");
        await using (var p = await platform.CreateDbContextAsync(cancellationToken))
        {
            if (!await p.Memberships.AnyAsync(m => m.TenantId == admin.OrganizationId && m.UserId == userId, cancellationToken))
            {
                return CaseResult<bool>.Fail(CaseError.NotFound, "Esa persona no es parte de la organización.");
            }
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var existing = await db.StaffRoles.FirstOrDefaultAsync(r => r.OrganizationId == admin.OrganizationId && r.UserId == userId && r.Role == role, cancellationToken);
        if (granted && existing is null)
        {
            db.StaffRoles.Add(new StaffRoleRow { OrganizationId = admin.OrganizationId, UserId = userId, Role = role, GrantedBy = admin.UserId, GrantedAt = clock.GetUtcNow() });
        }
        else if (!granted && existing is not null)
        {
            db.StaffRoles.Remove(existing);
        }
        else
        {
            return CaseResult<bool>.Ok(false);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(admin.OrganizationId, admin.Actor, granted ? "role.granted" : "role.revoked", null, $"Rol {role} {(granted ? "otorgado a" : "retirado de")} {userId}", new { user = userId, role },
            cancellationToken: cancellationToken);
        return CaseResult<bool>.Ok(true);
    }

    private CaseSummary Summary(CaseRow c, ReportRow r, IReadOnlyList<ClassificationRow> classifications) =>
        new(c.Id, CaseNumbers.Format(c.Number), c.Status, c.ImmediateDanger, c.DangerSource, r.AnalysisState, r.Country, r.Language, r.SubmittedAt, c.AssigneeId,
            classifications.Count(k => k.State == "suggested" && k.Kind != "danger"),
            [.. classifications.Where(k => k.Kind == "term" && k.State != "rejected").Select(k => new TermView(k.Dimension, k.Term!, taxonomy.Label(k.Dimension, k.Term!), k.State))]);

    private string? LabelOf(ClassificationRow k) => k.Kind switch
    {
        "term" when k.Term is not null => taxonomy.Label(k.Dimension, k.Term),
        "field" => k.Dimension switch { "actor_name" => "Empresa u organismo", "product" => "Producto o sistema", "place" => "Lugar", "date" => "Fecha", _ => k.Dimension },
        "danger" => k.Source == "rules" ? "Riesgo inmediato (reglas)" : "Riesgo inmediato (modelo)",
        _ => null,
    };

    private AnswerView Answer(string key, List<string> values)
    {
        var field = form.Field(key);
        if (field is null) return new AnswerView(key, key, values);
        var options = form.OptionsFor(field, taxonomy).ToDictionary(o => o.Key, o => o.Label);
        return new AnswerView(key, field.Label, [.. values.Select(v => options.GetValueOrDefault(v, v))]);
    }

    private static string Describe(ClassificationRow k) => k.Kind == "term" ? $"{k.Dimension}:{k.Term}" : k.Dimension;
    private static string Verb(string action) => action switch { "confirm" => "confirmada", "reject" => "descartada", "edit" => "corregida", _ => action };
}
