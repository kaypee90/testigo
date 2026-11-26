using System.Reflection;
using System.Text.Json;
using AgentRuntime.Infrastructure.Identity;
using AgentRuntime.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testigo.Forms;
using Testigo.Infrastructure.Persistence;
using Testigo.Infrastructure.Services;
using Testigo.Support;

namespace Testigo.Infrastructure.Demo;

/// <summary>
/// The demo's synthetic reports and roles (Phase 0). Every report is invented: fictional people,
/// fictional companies, RUTs that fail the check digit (§10.4). Off unless Testigo:Demo is set.
/// </summary>
public sealed class DemoSeeder(
    IDbContextFactory<TestigoDbContext> factory,
    IDbContextFactory<AgentDbContext> platform,
    IntakeService intake,
    IOptions<TestigoOptions> options,
    IOptions<AuthOptions> auth,
    ILogger<DemoSeeder> logger,
    TimeProvider clock)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var demo = options.Value.Demo;
        var org = options.Value.Intake.Organization;
        if (demo.GrantLocalAdminRoles) await GrantLocalAdminAsync(org, cancellationToken);
        if (!demo.Seed) return;

        await using (var db = await factory.CreateDbContextAsync(cancellationToken))
        {
            if (await db.Reports.AnyAsync(r => r.OrganizationId == org, cancellationToken)) return;
        }

        var reports = Load();
        foreach (var r in reports)
        {
            var result = await intake.SubmitAsync(new ReportSubmission
            {
                FormVersion = intake.Form.Version,
                Answers = r.Answers,
                Consents = r.Consents,
                ContactMethod = r.ContactMethod,
                ContactValue = r.ContactValue,
            }, "public", cancellationToken, org);
            if (result.Receipt is null) logger.LogWarning("A demo report didn't validate: {Fields}", string.Join(", ", result.Errors.Select(e => e.Field)));
        }

        logger.LogInformation("Loaded {Count} synthetic demo reports", reports.Count);
    }

    private async Task GrantLocalAdminAsync(string org, CancellationToken cancellationToken)
    {
        var a = auth.Value;
        if (!a.LocalAdmin.Enabled || string.IsNullOrWhiteSpace(a.LocalAdmin.Username)) return;
        var email = a.LocalAdmin.Username.Trim().ToLowerInvariant();
        await using var p = await platform.CreateDbContextAsync(cancellationToken);
        var userId = await p.Users.AsNoTracking().Where(u => u.Email == email).Select(u => u.UserId).FirstOrDefaultAsync(cancellationToken);
        if (userId is null || !await p.Memberships.AnyAsync(m => m.TenantId == org && m.UserId == userId, cancellationToken)) return;

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        foreach (var role in new[] { StaffRoles.Analyst, StaffRoles.Reviewer })
        {
            if (await db.StaffRoles.AnyAsync(r => r.OrganizationId == org && r.UserId == userId && r.Role == role, cancellationToken)) continue;
            db.StaffRoles.Add(new StaffRoleRow { OrganizationId = org, UserId = userId, Role = role, GrantedBy = "demo-setup", GrantedAt = clock.GetUtcNow() });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    internal static List<DemoReport> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Testigo.Infrastructure.Demo.demo-reports.json")
            ?? throw new InvalidOperationException("The demo reports aren't embedded.");
        return JsonSerializer.Deserialize<List<DemoReport>>(stream, TestigoData.Json) ?? [];
    }

    internal sealed record DemoReport
    {
        public Dictionary<string, List<string>> Answers { get; init; } = [];
        public List<string> Consents { get; init; } = [ConsentItem.Processing];
        public string? ContactMethod { get; init; }
        public string? ContactValue { get; init; }
    }
}
