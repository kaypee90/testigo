using Microsoft.EntityFrameworkCore;

namespace AgentRuntime.Infrastructure.Persistence;

public sealed class AgentDbContext(DbContextOptions<AgentDbContext> options) : DbContext(options)
{
    public DbSet<AuditRecord> AuditEntries => Set<AuditRecord>();
    public DbSet<AuditHeadRecord> AuditHeads => Set<AuditHeadRecord>();
    public DbSet<TenantRecord> Tenants => Set<TenantRecord>();
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<MembershipRecord> Memberships => Set<MembershipRecord>();
    public DbSet<SessionRecord> Sessions => Set<SessionRecord>();
    public DbSet<LoginChallengeRecord> LoginChallenges => Set<LoginChallengeRecord>();
    public DbSet<PendingSignUpRecord> PendingSignUps => Set<PendingSignUpRecord>();
    public DbSet<InvitationRecord> Invitations => Set<InvitationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditRecord>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedOnAdd();
            b.HasIndex(a => a.Key).IsUnique();
            b.HasIndex(a => new { a.Scope, a.Seq }).IsUnique();
            b.HasIndex(a => new { a.Scope, a.ActorId });
        });

        modelBuilder.Entity<TenantRecord>(b =>
        {
            b.HasKey(t => t.TenantId);
            b.Property(t => t.Status).HasDefaultValue(TenantStatuses.Active);
        });

        modelBuilder.Entity<UserRecord>(b =>
        {
            b.HasKey(u => u.UserId);
            b.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<MembershipRecord>(b =>
        {
            b.HasKey(m => new { m.TenantId, m.UserId });
            b.HasIndex(m => m.UserId);
        });

        modelBuilder.Entity<SessionRecord>(b =>
        {
            b.HasKey(x => x.TokenHash);
            b.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<LoginChallengeRecord>(b =>
        {
            b.HasKey(x => x.TokenHash);
            b.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<PendingSignUpRecord>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => x.Email);
            b.HasIndex(x => x.ExpiresAt);
        });

        modelBuilder.Entity<InvitationRecord>(b =>
        {
            b.HasKey(i => i.InvitationId);
            b.HasIndex(i => i.TokenHash).IsUnique();
            b.HasIndex(i => i.TenantId);
        });


        modelBuilder.Entity<AuditHeadRecord>(b =>
        {
            b.HasKey(h => h.Scope);
        });

    }
}
