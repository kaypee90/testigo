using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentRuntime.Infrastructure.Identity;
using AgentRuntime.Infrastructure.Persistence;
using AgentRuntime.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace AgentRuntime.IntegrationTests;

/// <summary>
/// Confirming a new account's email (docs/platform.md#email-verification): a sign-up creates nothing
/// until the link sent to the address is opened with the password chosen at sign-up, so nobody can
/// sign up with an address that isn't theirs, with or without an invitation. The test server has no email, so with Auth:EmailVerification=required the links go
/// to the log, where these tests pick them up.
/// </summary>
public sealed partial class EmailVerificationApiTests(EmailVerificationApiTests.Fixture fixture) : IClassFixture<EmailVerificationApiTests.Fixture>
{
    private const string Password = "correct-horse-battery-staple-1";

    public sealed class Fixture : IAsyncLifetime
    {
        public ApiTestHost Host { get; } = new();
        public LinkCapture Links { get; } = new();

        public async Task InitializeAsync()
        {
            Host.Settings["Auth:EmailVerification"] = "required";
            Host.Settings["Auth:SignInAttemptsPerMinute"] = "1000";
            Host.ConfigureServices = services => services.AddSingleton<ILoggerProvider>(Links);
            await Host.InitializeAsync();
            // The first account (the operator's, with the setup token) is never asked to confirm.
            using var scope = Host.Factory.Services.CreateScope();
            var first = await scope.ServiceProvider.GetRequiredService<IdentityService>().SignUpAsync("first@example.test", Password, "First", null, null, CancellationToken.None);
            if (first.Value is null) throw new InvalidOperationException(first.Error);
        }

        public Task DisposeAsync() => Host.DisposeAsync();
    }

    /// <summary>Keeps the confirmation links the API logs when it can't send email.</summary>
    public sealed partial class LinkCapture : ILoggerProvider
    {
        private readonly ConcurrentQueue<(string Email, string Token)> _links = new();

        public string? LatestFor(string email) => _links.Where(l => l.Email == email).Select(l => l.Token).LastOrDefault();

        public ILogger CreateLogger(string categoryName) => new Logger(this);
        public void Dispose() { }

        private sealed class Logger(LinkCapture capture) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (LinkRegex().Match(formatter(state, exception)) is { Success: true } m)
                {
                    capture._links.Enqueue((m.Groups[1].Value, Uri.UnescapeDataString(m.Groups[2].Value)));
                }
            }
        }

        [GeneratedRegex(@"confirmation link for (\S+) wasn't sent.*?token=(\S+)")]
        private static partial Regex LinkRegex();
    }

    private ApiTestHost Host => fixture.Host;


    private static string NewEmail() => $"new-{Guid.NewGuid():n}@example.test";

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private Task<HttpResponseMessage> SignUp(HttpClient client, string email, string password = Password) =>
        client.PostAsJsonAsync("/api/auth/signup", new { email, password, name = "Ama", organization = "Ama's lettings" });

    private static Task<HttpResponseMessage> Confirm(HttpClient client, string? token, string password = Password) =>
        client.PostAsJsonAsync("/api/auth/signup/verify", new { token, password });

    private async Task<bool> HasAccountAsync(string email)
    {
        await using var db = await Host.Factory.Services.GetRequiredService<IDbContextFactory<AgentDbContext>>().CreateDbContextAsync();
        return await db.Users.AnyAsync(u => u.Email == email);
    }

    /// <summary>A sign-up from the network <paramref name="address"/>, made straight on the service
    /// (the test server's requests all come from one place).</summary>
    private async Task<string> SignUpFromAsync(string email, string password, string address)
    {
        using var scope = Host.Factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IdentityService>()
            .RequestSignUpAsync(email, password, "Ama", "Ama's lettings", null, CancellationToken.None, clientAddress: address);
        Assert.True(result.Success, result.Error);
        Assert.True(result.Value!.VerificationRequired);
        return fixture.Links.LatestFor(email)!;
    }

    [Fact]
    public async Task An_open_sign_up_creates_the_account_only_when_the_link_is_opened_with_its_password()
    {
        var email = NewEmail();
        using var browser = Host.Factory.CreateClient();

        var signedUp = await Json(await SignUp(browser, email));
        Assert.True(signedUp.GetProperty("verification_required").GetBoolean());
        Assert.Equal(email, signedUp.GetProperty("email").GetString());

        // Nothing exists yet: no account, no session, and the password doesn't sign in.
        Assert.False(await HasAccountAsync(email));
        Assert.False((await Json(await browser.GetAsync("/api/auth/me"))).GetProperty("authenticated").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);

        // Opened from the email, perhaps on another device. A wrong password is refused and the link
        // keeps working; the right one creates the account and signs it in there.
        var token = fixture.Links.LatestFor(email);
        Assert.NotNull(token);
        using var phone = Host.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Confirm(phone, token, "not-the-password-77")).StatusCode);
        Assert.False(await HasAccountAsync(email));
        await Json(await Confirm(phone, token));
        var me = await Json(await phone.GetAsync("/api/auth/me"));
        Assert.True(me.GetProperty("authenticated").GetBoolean());
        Assert.Equal(email, me.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal("Ama's lettings", me.GetProperty("organizations")[0].GetProperty("name").GetString());
        Assert.Equal("Owner", me.GetProperty("role").GetString());

        // The link works once; the password chosen at sign-up signs in from now on.
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(phone, token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
    }

    [Fact]
    public async Task Someone_elses_sign_up_with_your_address_neither_blocks_yours_nor_opens_with_your_password()
    {
        var email = NewEmail();
        // A stranger signs up with the owner's address first, from their own network.
        var strangersLink = await SignUpFromAsync(email, "the-strangers-password-9", "198.51.100.7");

        // The owner signs up straight after from theirs: not held back, and each sign-up has its own link.
        var ownersLink = await SignUpFromAsync(email, Password, "203.0.113.5");
        Assert.NotEqual(strangersLink, ownersLink);

        // The owner, opening the stranger's email by mistake, can't finish the stranger's sign-up:
        // their own password doesn't match it.
        using var owner = Host.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Confirm(owner, strangersLink)).StatusCode);
        Assert.False(await HasAccountAsync(email));

        // The owner's own link works, and the stranger's password never signs in.
        await Json(await Confirm(owner, ownersLink));
        using var stranger = Host.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.PostAsJsonAsync("/api/auth/login", new { email, password = "the-strangers-password-9" })).StatusCode);
        // Every link for the address ends once it's confirmed.
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(stranger, strangersLink, "the-strangers-password-9")).StatusCode);
    }

    [Fact]
    public async Task Signing_up_again_from_the_same_network_waits_a_minute_then_replaces_the_earlier_link()
    {
        var email = NewEmail();
        using var client = Host.Factory.CreateClient();
        await Json(await SignUp(client, email));
        var first = fixture.Links.LatestFor(email)!;

        var again = await SignUp(client, email, "a-different-password-9");
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains("less than a minute ago", await again.Content.ReadAsStringAsync());
        Assert.Equal(first, fixture.Links.LatestFor(email));
        await Json(await Confirm(client, first));
    }

    [Fact]
    public async Task A_link_stops_working_after_five_wrong_passwords()
    {
        var email = NewEmail();
        using var client = Host.Factory.CreateClient();
        await Json(await SignUp(client, email));
        var token = fixture.Links.LatestFor(email);

        for (var i = 0; i < 4; i++) Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Confirm(client, token, $"wrong-password-{i}-x")).StatusCode);
        var last = await Confirm(client, token, "wrong-password-4-x");
        Assert.Equal(HttpStatusCode.BadRequest, last.StatusCode);
        Assert.Contains("no longer works", await last.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, token)).StatusCode);
        Assert.False(await HasAccountAsync(email));
    }

    [Fact]
    public async Task Bad_links_and_resends_give_nothing_away()
    {
        using var client = Host.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, "v_not-a-real-link")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, "")).StatusCode);
        // A resend for a sign-up that doesn't exist answers like one that does.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/signup/resend", new { id = "ps_unknown" })).StatusCode);

        // Within the minute a resend sends nothing new, and the first link still works.
        var email = NewEmail();
        var signedUp = await Json(await SignUp(client, email));
        var first = fixture.Links.LatestFor(email);
        await Json(await client.PostAsJsonAsync("/api/auth/signup/resend", new { id = signedUp.GetProperty("resend_id").GetString() }));
        Assert.Equal(first, fixture.Links.LatestFor(email));
        await Json(await Confirm(client, first));
    }

    [Fact]
    public async Task An_invitation_confirms_the_email_too_and_is_checked_again_when_the_link_is_opened()
    {
        var org = await Host.CreateOrganizationAsync("Inviting");
        using var admin = Host.ClientFor(org);
        using var client = Host.Factory.CreateClient();

        // Whoever has the invitation link (an admin can copy it) still has to own the address it names.
        var invitee = NewEmail();
        var invitation = (await Json(await admin.PostAsJsonAsync("/api/organization/invitations", new { email = invitee, role = "Member" }))).GetProperty("token").GetString();
        var signedUp = await Json(await client.PostAsJsonAsync("/api/auth/signup", new { email = invitee, password = Password, invitation }));
        Assert.True(signedUp.GetProperty("verification_required").GetBoolean());
        Assert.False(await HasAccountAsync(invitee));

        await Json(await Confirm(client, fixture.Links.LatestFor(invitee)));
        var me = await Json(await client.GetAsync("/api/auth/me"));
        Assert.Equal(org.TenantId, me.GetProperty("tenant_id").GetString());
        Assert.Equal("Member", me.GetProperty("role").GetString());

        // An invitation revoked after the sign-up, before the link is opened, no longer lets it in.
        var late = NewEmail();
        var lateInvitation = (await Json(await admin.PostAsJsonAsync("/api/organization/invitations", new { email = late, role = "Member" }))).GetProperty("token").GetString();
        using var other = Host.Factory.CreateClient();
        await Json(await other.PostAsJsonAsync("/api/auth/signup", new { email = late, password = Password, invitation = lateInvitation }));
        await using (var db = await Host.Factory.Services.GetRequiredService<IDbContextFactory<AgentDbContext>>().CreateDbContextAsync())
        {
            await db.Invitations.Where(i => i.Email == late).ExecuteUpdateAsync(u => u.SetProperty(i => i.RevokedAt, DateTimeOffset.UtcNow));
        }

        var refused = await Confirm(other, fixture.Links.LatestFor(late));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("invalid or has expired", await refused.Content.ReadAsStringAsync());
        Assert.False(await HasAccountAsync(late));
    }

    [Fact]
    public async Task An_address_with_an_account_gets_no_link()
    {
        using var client = Host.Factory.CreateClient();
        var taken = await SignUp(client, "first@example.test");
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Null(fixture.Links.LatestFor("first@example.test"));
    }

    [Fact]
    public async Task The_sign_in_page_is_told_that_new_accounts_confirm_their_email()
    {
        using var client = Host.Factory.CreateClient();
        var me = await Json(await client.GetAsync("/api/auth/me"));
        Assert.True(me.GetProperty("server").GetProperty("email_verification").GetBoolean());
    }
}
