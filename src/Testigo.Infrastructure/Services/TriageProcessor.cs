using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testigo.Infrastructure.Persistence;
using Testigo.Redaction;
using Testigo.Security;
using Testigo.Triage;

namespace Testigo.Infrastructure.Services;

/// <summary>
/// Runs the automatic first pass on new reports (FR-ANA-1): redaction, classification with
/// quotes, the immediate-danger check and related reports. Each report gets exactly one attempt
/// (NFR-AI-5): when it fails, or the process stops halfway, the report is marked "incomplete" and
/// a person takes over. Results are suggestions; nothing here changes a case's status.
/// </summary>
public sealed class TriageProcessor(
    IDbContextFactory<TestigoDbContext> factory,
    IFieldCipher cipher,
    TriageService triage,
    CaseAudit audit,
    IOptions<TestigoOptions> options,
    ILogger<TriageProcessor> logger,
    TimeProvider clock)
{
    /// <summary>Reports left "running" by a stopped process: never retried, handed to a person.</summary>
    public async Task<int> AbandonInterruptedAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow();
        return await db.Reports.Where(r => r.AnalysisState == "running")
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.AnalysisState, "incomplete")
                .SetProperty(r => r.AnalysisFinishedAt, now)
                .SetProperty(r => r.AnalysisNote, "El análisis automático se interrumpió. Requiere revisión manual."), cancellationToken);
    }

    /// <summary>Claims and analyzes up to <paramref name="max"/> pending reports, oldest first.</summary>
    public async Task<int> RunPendingAsync(int max, CancellationToken cancellationToken)
    {
        List<Guid> candidates;
        await using (var db = await factory.CreateDbContextAsync(cancellationToken))
        {
            candidates = await db.Reports.AsNoTracking().Where(r => r.AnalysisState == "pending")
                .OrderBy(r => r.SubmittedAt).Select(r => r.Id).Take(Math.Clamp(max, 1, 50)).ToListAsync(cancellationToken);
        }

        var done = 0;
        foreach (var id in candidates)
        {
            if (await ClaimAsync(id, cancellationToken))
            {
                await AnalyzeAsync(id, cancellationToken);
                done++;
            }
        }

        return done;
    }

    private async Task<bool> ClaimAsync(Guid reportId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow();
        // Only one worker wins a report: the update applies only while it's still pending.
        return await db.Reports.Where(r => r.Id == reportId && r.AnalysisState == "pending")
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AnalysisState, "running").SetProperty(r => r.AnalysisStartedAt, now), cancellationToken) == 1;
    }

    private async Task AnalyzeAsync(Guid reportId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var report = await db.Reports.FirstAsync(r => r.Id == reportId, cancellationToken);
        var @case = await db.Cases.FirstAsync(c => c.ReportId == reportId, cancellationToken);
        var org = report.OrganizationId;

        TriageOutcome outcome;
        try
        {
            var text = await cipher.DecryptAsync(org, KeyPurpose.Content, report.FreeTextCipher, FieldAad.FreeText(reportId), cancellationToken);
            outcome = await triage.AnalyzeAsync(org, text, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The exception's message can't carry report content into the log: only its type is logged.
            logger.LogWarning("Automatic analysis of case {CaseNumber} failed ({Error})", CaseNumbers.Format(@case.Number), e.GetType().Name);
            report.AnalysisState = "incomplete";
            report.AnalysisFinishedAt = clock.GetUtcNow();
            report.AnalysisNote = "El análisis automático falló. Requiere revisión manual.";
            await db.SaveChangesAsync(CancellationToken.None);
            await audit.RecordAsync(org, Actor.System, "triage.incomplete", @case.Id, $"Análisis automático incompleto del caso {CaseNumbers.Format(@case.Number)}", key: $"triage:{reportId:N}", outcome: "failed", cancellationToken: CancellationToken.None);
            return;
        }

        var now = clock.GetUtcNow();
        report.Language = outcome.Language;
        await StoreRedactionAsync(db, report, outcome.Redaction, now, cancellationToken);

        if (outcome.Complete)
        {
            foreach (var t in outcome.Terms)
            {
                db.Classifications.Add(await NewSuggestionAsync(org, @case.Id, "term", t.Dimension, t.Term, null, t.Evidence, outcome, now, cancellationToken));
            }

            foreach (var f in outcome.Fields)
            {
                db.Classifications.Add(await NewSuggestionAsync(org, @case.Id, "field", f.Field, null, f.Value, f.Evidence, outcome, now, cancellationToken));
            }
        }

        // The danger flag: rules alone already raised it at intake; the model can add to it.
        if (outcome.Danger.Flag)
        {
            @case.ImmediateDanger = true;
            @case.DangerSource = outcome.Danger.ByRules && outcome.Danger.ByModel ? "rules+model" : outcome.Danger.ByRules ? "rules" : "model";
            foreach (var m in outcome.Danger.RuleMatches)
            {
                var evidence = new Evidence(m.Start, m.Length, outcome.Redaction.Original.Substring(m.Start, m.Length));
                db.Classifications.Add(await NewSuggestionAsync(org, @case.Id, "danger", "immediate_danger", m.RuleKey, null, evidence, outcome, now, cancellationToken, source: "rules"));
            }

            if (outcome.Danger.ModelEvidence is { } me)
            {
                db.Classifications.Add(await NewSuggestionAsync(org, @case.Id, "danger", "immediate_danger", "model", null, me, outcome, now, cancellationToken));
            }
        }

        report.AnalysisState = outcome.Complete ? "complete" : "incomplete";
        report.AnalysisFinishedAt = now;
        report.AnalysisNote = outcome.Complete ? null : outcome.IncompleteReason;
        @case.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var links = await SuggestLinksAsync(db, report, @case, outcome, now, cancellationToken);

        await audit.RecordAsync(org, Actor.System, outcome.Complete ? "triage.completed" : "triage.incomplete", @case.Id,
            outcome.Complete
                ? $"Sugerencias automáticas para el caso {CaseNumbers.Format(@case.Number)}: {outcome.Terms.Count} clasificaciones, {outcome.Fields.Count} datos, {links} posibles relaciones"
                : $"Análisis automático incompleto del caso {CaseNumbers.Format(@case.Number)}",
            new
            {
                model = outcome.Model,
                prompt_version = outcome.PromptVersion,
                taxonomy_version = outcome.TaxonomyVersion,
                redaction = outcome.Redaction.MethodVersion,
                quotes_returned = outcome.QuotesReturned,
                quotes_grounded = outcome.QuotesGrounded,
                dropped = outcome.Dropped,
                from_cache = outcome.FromCache,
                input_tokens = outcome.InputTokens,
                output_tokens = outcome.OutputTokens,
                danger = outcome.Danger.Flag,
            },
            key: $"triage:{reportId:N}", outcome: outcome.Complete ? "ok" : "failed", cancellationToken: cancellationToken);
        logger.LogInformation("Automatic analysis of case {CaseNumber}: {State}", CaseNumbers.Format(@case.Number), report.AnalysisState);
    }

    private async Task StoreRedactionAsync(TestigoDbContext db, ReportRow report, RedactionResult redaction, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var org = report.OrganizationId;
        var existing = await db.RedactedCopies.FirstOrDefaultAsync(r => r.ReportId == report.Id, cancellationToken);
        if (existing is not null) db.RedactedCopies.Remove(existing);
        db.RedactedCopies.Add(new RedactedCopyRow
        {
            ReportId = report.Id,
            OrganizationId = org,
            TextCipher = await cipher.EncryptAsync(org, KeyPurpose.Content, redaction.Text, FieldAad.RedactedText(report.Id), cancellationToken),
            MapCipher = await cipher.EncryptAsync(org, KeyPurpose.Content, JsonSerializer.Serialize(redaction.Map), FieldAad.RedactedMap(report.Id), cancellationToken),
            MethodVersion = redaction.MethodVersion,
            Types = string.Join(',', redaction.Types),
            CreatedAt = now,
        });
    }

    private async Task<ClassificationRow> NewSuggestionAsync(string org, Guid caseId, string kind, string dimension, string? term, string? value, Evidence evidence,
        TriageOutcome outcome, DateTimeOffset now, CancellationToken cancellationToken, string source = "ai")
    {
        var id = Guid.CreateVersion7(now);
        return new ClassificationRow
        {
            Id = id,
            CaseId = caseId,
            OrganizationId = org,
            Kind = kind,
            Dimension = dimension,
            Term = term,
            ValueCipher = value is null ? null : await cipher.EncryptAsync(org, KeyPurpose.Content, value, FieldAad.Value(id), cancellationToken),
            QuoteCipher = string.IsNullOrEmpty(evidence.Quote) ? null : await cipher.EncryptAsync(org, KeyPurpose.Content, evidence.Quote, FieldAad.Quote(id), cancellationToken),
            EvidenceStart = evidence.Start,
            EvidenceLength = evidence.Length,
            Source = source,
            Model = source == "ai" ? outcome.Model : outcome.Danger.RulesVersion,
            PromptVersion = source == "ai" ? outcome.PromptVersion : null,
            TaxonomyVersion = outcome.TaxonomyVersion,
            CreatedAt = now,
        };
    }

    /// <summary>Compares the new report's redacted copy with the organization's recent ones (FR-ANA-5).</summary>
    private async Task<int> SuggestLinksAsync(TestigoDbContext db, ReportRow report, CaseRow @case, TriageOutcome outcome, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var org = report.OrganizationId;
        var window = Math.Clamp(options.Value.Staff.RelatedWindow, 10, 5000);
        var recent = await db.RedactedCopies.AsNoTracking()
            .Where(r => r.OrganizationId == org && r.ReportId != report.Id)
            .OrderByDescending(r => r.CreatedAt).Take(window)
            .Join(db.Cases.Where(c => c.Status != "withdrawn"), r => r.ReportId, c => c.ReportId, (r, c) => new { r.ReportId, r.TextCipher, CaseId = c.Id })
            .ToListAsync(cancellationToken);
        if (recent.Count == 0) return 0;

        var caseIds = recent.Select(r => r.CaseId).ToList();
        var terms = await db.Classifications.AsNoTracking()
            .Where(c => caseIds.Contains(c.CaseId) && c.State != "rejected" && (c.Kind == "term" && c.Dimension == "technology" || c.Kind == "field" && c.Dimension == "actor_name"))
            .ToListAsync(cancellationToken);

        var others = new List<RelatedCandidate>();
        foreach (var r in recent)
        {
            string text;
            try
            {
                text = await cipher.DecryptAsync(org, KeyPurpose.Content, r.TextCipher, FieldAad.RedactedText(r.ReportId), cancellationToken);
            }
            catch (DecryptionFailedException)
            {
                continue;
            }

            var mine = terms.Where(t => t.CaseId == r.CaseId).ToList();
            var actors = new List<string>();
            foreach (var a in mine.Where(t => t.Kind == "field" && t.ValueCipher is not null))
            {
                actors.Add(await cipher.DecryptAsync(org, KeyPurpose.Content, a.ValueCipher!, FieldAad.Value(a.Id), cancellationToken));
            }

            others.Add(new RelatedCandidate(r.CaseId, text, [.. mine.Where(t => t.Kind == "term").Select(t => t.Term!)], actors));
        }

        var self = new RelatedCandidate(@case.Id, outcome.Redaction.Text,
            [.. outcome.Terms.Where(t => t.Dimension == "technology").Select(t => t.Term)],
            [.. outcome.Fields.Where(f => f.Field == "actor_name").Select(f => f.Value)]);
        var suggestions = RelatedReports.Suggest(self, others);
        foreach (var s in suggestions)
        {
            db.CaseLinks.Add(new CaseLinkRow
            {
                Id = Guid.CreateVersion7(now),
                OrganizationId = org,
                CaseId = @case.Id,
                OtherCaseId = s.CaseId,
                Kind = s.Kind,
                Score = s.Score,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return suggestions.Count;
    }
}

/// <summary>Polls for new reports and runs <see cref="TriageProcessor"/> on them, a few at a time.</summary>
public sealed class TriageWorker(TriageProcessor processor, IOptions<TestigoOptions> options, ILogger<TriageWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value.Worker;
        if (!o.Enabled) return;
        try
        {
            var abandoned = await processor.AbandonInterruptedAsync(stoppingToken);
            if (abandoned > 0) logger.LogWarning("{Count} interrupted analyses were handed to people", abandoned);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning("Couldn't check for interrupted analyses ({Error})", e.GetType().Name);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(o.PollSeconds, 1, 300)));
        do
        {
            try
            {
                await processor.RunPendingAsync(o.BatchSize, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning("The triage worker's poll failed ({Error})", e.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
