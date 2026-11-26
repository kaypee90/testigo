using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentRuntime.Api.Platform;
using AgentRuntime.Infrastructure.Identity;
using AgentRuntime.Infrastructure.Persistence;
using AgentRuntime.IntegrationTests.TestSupport;
using AgentRuntime.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace AgentRuntime.IntegrationTests;

/// <summary>Deleting your own account (docs/platform.md#deleting-your-account), through the real
/// API: a solo organization closes with the account, an organization's only owner can't leave it
/// ownerless, a member just leaves, and the password is checked.</summary>
public sealed class AccountDeletionTests(AccountDeletionTests.Fixture fixture) : IClassFixture<AccountDeletionTests.Fixture>
{
    private const string Password = "correct-horse-battery-staple-1";

    public sealed class Fixture : IAsyncLifetime
    {
        public ApiTestHost Host { get; } = new();

        public async Task InitializeAsync()
        {
            await Host.InitializeAsync();
            // The first account takes over the default organization; the tests use later ones.
            using var scope = Host.Factory.Services.CreateScope();
            var signedUp = await scope.ServiceProvider.GetRequiredService<IdentityService>()
                .SignUpAsync("first@example.test", Password, "First", null, null, CancellationToken.None);
            if (signedUp.Value is null) throw new InvalidOperationException(signedUp.Error);
        }

        public Task DisposeAsync() => Host.DisposeAsync();
    }

    private ApiTestHost Host => fixture.Host;


    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private async Task<(HttpClient Client, string UserId, string TenantId)> SignUpAsync(string email, string? invitation = null)
    {
        var client = Host.Factory.CreateClient();
        var body = await Json(await client.PostAsJsonAsync("/api/auth/signup",
            new { email, password = Password, name = email.Split('@')[0], organization = $"{email} org", invitation }));
        return (client, body.GetProperty("user_id").GetString()!, body.GetProperty("tenant_id").GetString()!);
    }

    private AgentDbContext Db() => Host.Factory.Services.GetRequiredService<IDbContextFactory<AgentDbContext>>().CreateDbContext();

    [Fact]
    public async Task A_solo_account_closes_its_organization_and_is_gone()
    {
        var email = $"solo-{Guid.NewGuid():n}@example.test";
        var (client, userId, tenantId) = await SignUpAsync(email);
        using var _ = client;

        var preview = await Json(await client.GetAsync("/api/account/deletion"));
        Assert.True(preview.GetProperty("allowed").GetBoolean());
        Assert.Equal(tenantId, Assert.Single(preview.GetProperty("closes").EnumerateArray().ToList()).GetProperty("tenant_id").GetString());

        // The password is checked, and a wrong one deletes nothing.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/account/delete", new { password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/account/deletion")).StatusCode);

        var done = await Json(await client.PostAsJsonAsync("/api/account/delete", new { password = Password }));
        Assert.Equal(1, done.GetProperty("organizations_closed").GetInt32());

        await using (var db = Db())
        {
            Assert.False(await db.Users.AnyAsync(u => u.UserId == userId));
            Assert.False(await db.Memberships.AnyAsync(m => m.UserId == userId));
            Assert.False(await db.Sessions.AnyAsync(s => s.UserId == userId));
            var tenant = await db.Tenants.SingleAsync(t => t.TenantId == tenantId);
            Assert.Equal(TenantStatuses.Suspended, tenant.Status);
            Assert.Equal(AccountDeletionService.ClosedReason, tenant.SuspendedReason);
        }

        // Signed out, and the account can't sign in again; the email is free to sign up anew.
        using var fresh = Host.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account/deletion")).StatusCode);
        Assert.False((await fresh.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).IsSuccessStatusCode);
        var again = await SignUpAsync(email);
        again.Client.Dispose();
        Assert.NotEqual(tenantId, again.TenantId);
    }

    [Fact]
    public async Task The_only_owner_must_hand_over_and_a_member_just_leaves()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():n}@example.test";
        var memberEmail = $"member-{Guid.NewGuid():n}@example.test";
        var (owner, ownerId, tenantId) = await SignUpAsync(ownerEmail);
        using var _o = owner;

        string token;
        using (var scope = Host.Factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityService>();
            var caller = new Caller(tenantId, TenantRole.Owner, ownerId, ownerEmail, false, null);
            token = (await identity.InviteAsync(caller, memberEmail, TenantRole.Member, CancellationToken.None)).Value.Token;
        }

        var (member, memberId, memberTenant) = await SignUpAsync(memberEmail, token);
        using var _m = member;
        Assert.Equal(tenantId, memberTenant);

        // The owner is blocked: the organization would be left without one.
        var preview = await Json(await owner.GetAsync("/api/account/deletion"));
        Assert.False(preview.GetProperty("allowed").GetBoolean());
        Assert.Equal(tenantId, Assert.Single(preview.GetProperty("blocks").EnumerateArray().ToList()).GetProperty("tenant_id").GetString());
        var refused = await owner.PostAsJsonAsync("/api/account/delete", new { password = Password });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("only owner", await refused.Content.ReadAsStringAsync());

        // The member leaves; the organization and its owner are untouched.
        var left = await Json(await member.PostAsJsonAsync("/api/account/delete", new { password = Password }));
        Assert.Equal(1, left.GetProperty("organizations_left").GetInt32());
        Assert.Equal(0, left.GetProperty("organizations_closed").GetInt32());
        await using (var db = Db())
        {
            Assert.False(await db.Users.AnyAsync(u => u.UserId == memberId));
            Assert.Equal(TenantStatuses.Active, (await db.Tenants.SingleAsync(t => t.TenantId == tenantId)).Status);
            Assert.Equal(ownerId, (await db.Memberships.SingleAsync(m => m.TenantId == tenantId)).UserId);
        }

        // Now alone, the owner can delete the account, closing the organization with it.
        preview = await Json(await owner.GetAsync("/api/account/deletion"));
        Assert.True(preview.GetProperty("allowed").GetBoolean());
        Assert.Single(preview.GetProperty("closes").EnumerateArray().ToList());
    }
}
