using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Testigo.Infrastructure.Persistence;

/// <summary>
/// Testigo's case data, in its own "testigo" schema of the platform database, with its own
/// migrations history. The platform tables (users, organizations, audit log) stay in "public".
/// </summary>
public sealed class TestigoDbContext(DbContextOptions<TestigoDbContext> options) : DbContext(options)
{
    public const string Schema = "testigo";

    public DbSet<DataKeyRow> DataKeys => Set<DataKeyRow>();
    public DbSet<ReportRow> Reports => Set<ReportRow>();
    public DbSet<ContactRow> Contacts => Set<ContactRow>();
    public DbSet<ConsentRow> Consents => Set<ConsentRow>();
    public DbSet<CaseRow> Cases => Set<CaseRow>();
    public DbSet<StatusChangeRow> StatusChanges => Set<StatusChangeRow>();
    public DbSet<ClassificationRow> Classifications => Set<ClassificationRow>();
    public DbSet<RedactedCopyRow> RedactedCopies => Set<RedactedCopyRow>();
    public DbSet<CaseLinkRow> CaseLinks => Set<CaseLinkRow>();
    public DbSet<TriageCacheRow> TriageCache => Set<TriageCacheRow>();
    public DbSet<StaffRoleRow> StaffRoles => Set<StaffRoleRow>();
    public DbSet<CaseCounterRow> CaseCounters => Set<CaseCounterRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<DataKeyRow>(e =>
        {
            e.ToTable("data_keys");
            e.HasKey(x => x.KeyId);
            e.Property(x => x.KeyId).HasMaxLength(64);
            e.Property(x => x.OrganizationId).HasMaxLength(64);
            e.Property(x => x.Purpose).HasMaxLength(16);
            e.Property(x => x.MasterKeyId).HasMaxLength(64);
            e.HasIndex(x => new { x.OrganizationId, x.Purpose });
        });

        b.Entity<ReportRow>(e =>
        {
            e.ToTable("reports");
            e.HasKey(x => x.Id);
            e.Property(x => x.OrganizationId).HasMaxLength(64);
            e.Property(x => x.CaseCodeHash).HasMaxLength(64);
            e.HasIndex(x => x.CaseCodeHash).IsUnique();
            e.Property(x => x.PassphraseHash).HasMaxLength(256);
            e.Property(x => x.FormVersion).HasMaxLength(32);
            e.Property(x => x.NoticeVersion).HasMaxLength(32);
            e.Property(x => x.Channel).HasMaxLength(16);
            e.Property(x => x.Language).HasMaxLength(8);
            e.Property(x => x.Country).HasMaxLength(8);
            e.Property(x => x.AnalysisState).HasMaxLength(16);
            e.Property(x => x.AnalysisNote).HasMaxLength(300);
            e.HasIndex(x => new { x.AnalysisState, x.SubmittedAt });
            e.HasIndex(x => new { x.OrganizationId, x.SubmittedAt });
        });

        b.Entity<ContactRow>(e =>
        {
            e.ToTable("contacts");
            e.HasKey(x => x.ReportId);
            e.Property(x => x.OrganizationId).HasMaxLength(64);
        });

        b.Entity<ConsentRow>(e =>
        {
            e.ToTable("consents");
            e.HasKey(x => x.Id);
            e.Property(x => x.OrganizationId).HasMaxLength(64);
            e.Property(x => x.NoticeVersion).HasMaxLength(32);
            e.HasIndex(x => x.ReportId);
        });

        b.Entity<CaseRow>(e =>
        {
            e.ToTable("cases");
            e.HasKey(x => x.Id);
            e.Property(x => x.OrganizationId).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.DangerSource).HasMaxLength(16);
            e.Property(x => x.Severity).HasMaxLength(16);
            e.Property(x => x.AssigneeId).HasMaxLength(64);
            e.Property(x => x.TaxonomyVersion).HasMaxLength(32);
            e.Property(x => x.VerifiedBy).HasMaxLength(64);
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => x.ReportId).IsUnique();
            e.HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.OrganizationId, x.Status, x.ImmediateDanger, x.CreatedAt });
        });

        b.Entity<StatusChangeRow>(e =>
        {
            e.ToTable("status_changes");
            e.HasKey(x => x.Id);
            e.Property(x => x.OrganizationId).HasMaxLength(64);
            e.Property(x => x.From).HasMaxLength(32);
            e.Property(x => x.To).HasMaxLength(32);
            e.Property(x => x.ByUserId).HasMaxLength(64);
            e.HasIndex(x => new { x.CaseId, x.At });
        });

        b.Entity<ClassificationRow>(e =>
        {
            e.ToTable("classifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.OrganizationId).HasMaxLength(64);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Dimension).HasMaxLength(64);
            e.Property(x => x.Term).HasMaxLength(64);
            e.Property(x => x.Source).HasMaxLength(16);
            e.Property(x => x.State).HasMaxLength(16);
            e.Property(x => x.Model).HasMaxLength(128);
            e.Property(x => x.PromptVersion).HasMaxLength(32);
            e.Property(x => x.TaxonomyVersion).HasMaxLength(32);
            e.Property(x => x.DecidedBy).HasMaxLength(64);
            e.HasIndex(x => x.CaseId);
            e.HasIndex(x => new { x.OrganizationId, x.Dimension, x.Term, x.State });
        });

        b.Entity<RedactedCopyRow>(e =>
        {
            e.ToTable("redacted_copies");
            e.HasKey(x => x.ReportId);
            e.Property(x => x.OrganizationId).HasMaxLength(64);
            e.Property(x => x.MethodVersion).HasMaxLength(32);
            e.Property(x => x.Types).HasMaxLength(128);
            e.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
        });

        b.Entity<CaseLinkRow>(e =>
        {
            e.ToTable("case_links");
            e.HasKey(x => x.Id);
            e.Property(x => x.OrganizationId).HasMaxLength(64);
            e.Property(x => x.Kind).HasMaxLength(32);
            e.Property(x => x.SuggestedBy).HasMaxLength(64);
            e.Property(x => x.State).HasMaxLength(16);
            e.Property(x => x.DecidedBy).HasMaxLength(64);
            e.HasIndex(x => new { x.CaseId, x.OtherCaseId }).IsUnique();
            e.HasIndex(x => x.OtherCaseId);
        });

        b.Entity<TriageCacheRow>(e =>
        {
            e.ToTable("triage_cache");
            e.HasKey(x => new { x.OrganizationId, x.CacheKey });
            e.Property(x => x.OrganizationId).HasMaxLength(64);
            e.Property(x => x.CacheKey).HasMaxLength(64);
        });

        b.Entity<StaffRoleRow>(e =>
        {
            e.ToTable("staff_roles");
            e.HasKey(x => new { x.OrganizationId, x.UserId, x.Role });
            e.Property(x => x.OrganizationId).HasMaxLength(64);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.Role).HasMaxLength(32);
            e.Property(x => x.GrantedBy).HasMaxLength(64);
        });

        b.Entity<CaseCounterRow>(e =>
        {
            e.ToTable("case_counters");
            e.HasKey(x => x.OrganizationId);
            e.Property(x => x.OrganizationId).HasMaxLength(64);
        });
    }
}

/// <summary>Used by <c>dotnet ef migrations add</c> only; it never connects.</summary>
public sealed class TestigoDesignTimeFactory : IDesignTimeDbContextFactory<TestigoDbContext>
{
    public TestigoDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TestigoDbContext>()
            .UseNpgsql("Host=design-time;Database=design-time", o => o.MigrationsHistoryTable("__ef_migrations", TestigoDbContext.Schema))
            .Options);
}
