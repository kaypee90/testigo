using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgentRuntime.Infrastructure.Identity;
using AgentRuntime.IntegrationTests.TestSupport;
using AgentRuntime.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace AgentRuntime.IntegrationTests;

/// <summary>The platform admin portal and early access (docs/platform.md#platform-administration),
/// through the real API: with open sign-up off, people request access, an operator approves them into
/// a new organization, and they join from the invitation; operators create, suspend and reactivate
/// organizations, and suspension really cuts off sessions, sign-in and API keys.</summary>
public sealed class PlatformAdminTests(PlatformAdminTests.Fixture fixture) : IClassFixture<PlatformAdminTests.Fixture>
{
    private const string Password = "correct-horse-battery-staple-1";
    private const string OperatorEmail = "ops@example.test";
    // Named as an operator in the configuration, but nobody has an account with it yet.
    private const string UnclaimedOperatorEmail = "second-ops@example.test";
    private const string StampedCommit = "b87d7519f0c2a4e6d8b1c3e5f7a9b0c2d4e6f8a1";

    public sealed class Fixture : IAsyncLifetime
    {
        public ApiTestHost Host { get; } = new();
        public HttpClient Operator { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            Host.Settings["Auth:AllowSignup"] = "false";
            Host.Settings["Auth:PlatformAdmins:0"] = OperatorEmail;
            Host.Settings["Auth:PlatformAdmins:1"] = UnclaimedOperatorEmail;
            // What the deploy stamps into the image (docker/Dockerfile.api).
            Host.Settings["App:Version"] = StampedCommit;
            Host.Settings["App:BuiltAt"] = "2026-11-05T14:52:00Z";
            await Host.InitializeAsync();

            // The first account is always allowed, and takes over the default organization.
            using var scope = Host.Factory.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<IdentityService>();
            var signedUp = await identity.SignUpAsync(OperatorEmail, Password, "Ops", null, null, CancellationToken.None);
            if (signedUp.Value is null) throw new InvalidOperationException(signedUp.Error);

            Operator = Host.Factory.CreateClient();
            var login = await Operator.PostAsJsonAsync("/api/auth/login", new { email = OperatorEmail, password = Password });
            if (!login.IsSuccessStatusCode) throw new InvalidOperationException(await login.Content.ReadAsStringAsync());
        }

        public async Task DisposeAsync()
        {
            Operator?.Dispose();
            await Host.DisposeAsync();
        }
    }

    private ApiTestHost Host => fixture.Host;
    private HttpClient Operator => fixture.Operator;


    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static string TokenOf(string inviteUrl) => Uri.UnescapeDataString(inviteUrl[(inviteUrl.IndexOf("invite=", StringComparison.Ordinal) + "invite=".Length)..]);

    [Fact]
    public async Task The_overview_says_which_build_is_running()
    {
        var version = (await Json(await Operator.GetAsync("/api/admin/overview"))).GetProperty("version");

        Assert.Equal(StampedCommit, version.GetProperty("commit").GetString());
        Assert.Equal("b87d751", version.GetProperty("short_commit").GetString());
        Assert.Equal(DateTimeOffset.Parse("2026-11-05T14:52:00Z"), version.GetProperty("built_at").GetDateTimeOffset());
        // Plain text: no link to the repository.
        Assert.False(version.TryGetProperty("commit_url", out _));
        Assert.True(version.GetProperty("started_at").GetDateTimeOffset() <= DateTimeOffset.UtcNow);

        // Operators only: the build isn't something to hand to everyone.
        var ownerEmail = $"owner-{Guid.NewGuid():n}@plain.test";
        var created = await Json(await Operator.PostAsJsonAsync("/api/admin/organizations", new { name = "Plain", owner_email = ownerEmail }));
        using var member = Host.Factory.CreateClient();
        await Json(await member.PostAsJsonAsync("/api/auth/signup",
            new { email = ownerEmail, password = Password, invitation = TokenOf(created.GetProperty("owner").GetProperty("invite_url").GetString()!) }));
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/overview")).StatusCode);
    }

    [Fact]
    public async Task Suspending_an_organization_cuts_off_its_sessions_and_sign_in_until_it_is_reactivated()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():n}@beta.test";
        var created = await Json(await Operator.PostAsJsonAsync("/api/admin/organizations", new { name = "Beta", owner_email = ownerEmail }));
        var tenantId = created.GetProperty("tenant_id").GetString()!;

        using var owner = Host.Factory.CreateClient();
        await Json(await owner.PostAsJsonAsync("/api/auth/signup",
            new { email = ownerEmail, password = Password, invitation = TokenOf(created.GetProperty("owner").GetProperty("invite_url").GetString()!) }));
        Assert.Equal(tenantId, (await Json(await owner.GetAsync("/api/organization"))).GetProperty("tenant_id").GetString());

        // The operator adds a second person: they have no account yet, so they're invited.
        var added = await Json(await Operator.PostAsJsonAsync($"/api/admin/organizations/{tenantId}/members", new { email = $"dev-{Guid.NewGuid():n}@beta.test", role = "Member" }));
        Assert.False(added.GetProperty("joined").GetBoolean());

        // An organization an operator belongs to can't be suspended (here, the default one).
        Assert.Equal(HttpStatusCode.BadRequest, (await Operator.PostAsJsonAsync($"/api/admin/organizations/{TenantIds.Default}/suspend", new { reason = "test" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Operator.PostAsJsonAsync($"/api/admin/organizations/{tenantId}/suspend", new { reason = "unpaid invoice" })).StatusCode);
        var suspended = await Json(await Operator.GetAsync($"/api/admin/organizations/{tenantId}"));
        Assert.Equal("Suspended", suspended.GetProperty("organization").GetProperty("status").GetString());

        Assert.False((await Json(await owner.GetAsync("/api/auth/me"))).GetProperty("authenticated").GetBoolean());
        var refused = await owner.PostAsJsonAsync("/api/auth/login", new { email = ownerEmail, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Contains("suspended", await refused.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await Operator.PostAsync($"/api/admin/organizations/{tenantId}/reactivate", null)).StatusCode);
        Assert.True((await owner.PostAsJsonAsync("/api/auth/login", new { email = ownerEmail, password = Password })).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Operators_can_not_leave_an_organization_without_an_owner()
    {
        using var scope = Host.Factory.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<PlatformAdminService>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityService>();
        var ownerEmail = $"solo-{Guid.NewGuid():n}@gamma.test";
        var created = await admin.CreateAsync("Gamma", ownerEmail, OperatorEmail, CancellationToken.None);
        Assert.True(created.Success, created.Error);
        var (tenantId, owner) = created.Value;
        var signedUp = await identity.SignUpAsync(ownerEmail, Password, null, null, TokenOf(owner.InviteUrl!), CancellationToken.None);
        Assert.True(signedUp.Success, signedUp.Error);
        var userId = signedUp.Value!.User.UserId;

        Assert.False((await admin.SetMemberRoleAsync(tenantId, userId, TenantRole.Admin, OperatorEmail, CancellationToken.None)).Success);
        Assert.False((await admin.RemoveMemberAsync(tenantId, userId, OperatorEmail, CancellationToken.None)).Success);
        Assert.False((await admin.CreateAsync("  ", ownerEmail, OperatorEmail, CancellationToken.None)).Success);
    }

    /// <summary>Emails aren't verified, so signing up with an operator's email must not hand over
    /// the Platform admin portal: not even with an invitation, which anyone can send to any address.</summary>
    [Fact]
    public async Task An_operators_email_can_not_be_claimed_by_signing_up()
    {
        using var scope = Host.Factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityService>();

        var open = await identity.SignUpAsync(UnclaimedOperatorEmail, Password, "Not ops", null, null, CancellationToken.None);
        Assert.False(open.Success);

        var invited = await identity.InviteAsOperatorAsync(AgentRuntime.Tenancy.TenantIds.Default, UnclaimedOperatorEmail, TenantRole.Member, "test", CancellationToken.None);
        Assert.True(invited.Success, invited.Error);
        var withInvitation = await identity.SignUpAsync(UnclaimedOperatorEmail, Password, "Not ops", null, invited.Value.Token, CancellationToken.None);
        Assert.False(withInvitation.Success);
        Assert.DoesNotContain("invitation", withInvitation.Error, StringComparison.OrdinalIgnoreCase);

        // Anyone else's invitation still works.
        var other = $"member-{Guid.NewGuid():n}@example.test";
        var otherInvite = await identity.InviteAsOperatorAsync(AgentRuntime.Tenancy.TenantIds.Default, other, TenantRole.Member, "test", CancellationToken.None);
        Assert.True((await identity.SignUpAsync(other, Password, null, null, otherInvite.Value.Token, CancellationToken.None)).Success);
    }
}
