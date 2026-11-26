using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentRuntime.Infrastructure.Identity;
using AgentRuntime.IntegrationTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace AgentRuntime.IntegrationTests;

/// <summary>Two-factor sign-in (docs/platform.md#two-factor-sign-in) through the real API: a code
/// from the authenticator app after the password, recovery codes, turning it off, and an operator's
/// reset. The clock is the test's, so new codes don't need a 30-second wait.</summary>
public sealed class TwoFactorTests(TwoFactorTests.Fixture fixture, ITestOutputHelper output) : IClassFixture<TwoFactorTests.Fixture>
{
    private const string Password = "correct-horse-battery-staple-1";
    private const string OperatorEmail = "ops-2fa@example.test";

    /// <summary>A clock the tests move forward by hand.</summary>
    public sealed class ManualClock : TimeProvider
    {
        // The middle of a 30-second step, so nothing lands on a boundary by accident.
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(Totp.StepAt(DateTimeOffset.UtcNow) * Totp.PeriodSeconds + 15);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public sealed class Fixture : IAsyncLifetime
    {
        public ApiTestHost Host { get; } = new();
        public ManualClock Clock { get; } = new();
        public HttpClient Operator { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            Host.Settings["Auth:PlatformAdmins:0"] = OperatorEmail;
            // Every test signs in several times from the same (test) address.
            Host.Settings["Auth:SignInAttemptsPerMinute"] = "1000";
            Host.ConfigureServices = services => services.AddSingleton<TimeProvider>(Clock);
            await Host.InitializeAsync();
            if (Host.Unavailable is not null) return;

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
    private ManualClock Clock => fixture.Clock;

    private bool Skip()
    {
        if (Host.Unavailable is null) return false;
        output.WriteLine("SKIPPED: " + Host.Unavailable);
        return true;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<string> Error(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.False(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("error").GetString()!;
    }

    private sealed record Account(string Email, HttpClient Browser, byte[] Secret, IReadOnlyList<string> RecoveryCodes);

    /// <summary>A new account, signed in, with two-factor sign-in turned on through the API.</summary>
    private async Task<Account> AccountWithTwoFactorAsync()
    {
        var email = $"jane-{Guid.NewGuid():n}@acme.test";
        var browser = Host.Factory.CreateClient();
        await Json(await browser.PostAsJsonAsync("/api/auth/signup", new { email, password = Password, name = "Jane" }));

        Assert.Contains("password is wrong", await Error(await browser.PostAsJsonAsync("/api/auth/two-factor/setup", new { password = "not-it-at-all" })));
        var setup = await Json(await browser.PostAsJsonAsync("/api/auth/two-factor/setup", new { password = Password }));
        Assert.StartsWith("<svg", setup.GetProperty("qr_svg").GetString());
        Assert.StartsWith("otpauth://totp/Testigo:", setup.GetProperty("otpauth_uri").GetString());
        var secret = Totp.FromBase32(setup.GetProperty("secret").GetString()!);

        Assert.Contains("isn't right", await Error(await browser.PostAsJsonAsync("/api/auth/two-factor/enable", new { code = "000000" == CurrentCode(secret) ? "111111" : "000000" })));
        var enabled = await Json(await browser.PostAsJsonAsync("/api/auth/two-factor/enable", new { code = CurrentCode(secret) }));
        var codes = enabled.GetProperty("recovery_codes").EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.Equal(TwoFactorService.RecoveryCodeCount, codes.Count);
        return new Account(email, browser, secret, codes);
    }

    private string CurrentCode(byte[] secret) => Totp.Code(secret, Totp.StepAt(Clock.Now));

    /// <summary>Thirty seconds on: the app shows a new code.</summary>
    private string NextCode(byte[] secret)
    {
        Clock.Now += TimeSpan.FromSeconds(Totp.PeriodSeconds);
        return CurrentCode(secret);
    }

    private async Task<(HttpClient Browser, string Challenge)> PasswordStepAsync(string email)
    {
        var browser = Host.Factory.CreateClient();
        var login = await Json(await browser.PostAsJsonAsync("/api/auth/login", new { email, password = Password }));
        Assert.True(login.GetProperty("two_factor_required").GetBoolean());
        return (browser, login.GetProperty("challenge").GetString()!);
    }

    private static async Task<bool> SignedInAsync(HttpClient browser) =>
        (await Json(await browser.GetAsync("/api/auth/me"))).GetProperty("authenticated").GetBoolean();

    [Fact]
    public async Task After_the_password_a_code_from_the_app_signs_in_and_only_once()
    {
        if (Skip()) return;
        var account = await AccountWithTwoFactorAsync();

        // The browser that turned it on stays signed in, and says so.
        var me = await Json(await account.Browser.GetAsync("/api/auth/me"));
        Assert.True(me.GetProperty("user").GetProperty("two_factor_enabled").GetBoolean());

        var (browser, challenge) = await PasswordStepAsync(account.Email);
        Assert.False(await SignedInAsync(browser)); // a right password alone opens no session
        var code = NextCode(account.Secret);
        await Json(await browser.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge, code = code[..3] + " " + code[3..] }));
        Assert.True(await SignedInAsync(browser));

        // Someone who saw that code can't use it again.
        var (other, again) = await PasswordStepAsync(account.Email);
        Assert.Contains("just used", await Error(await other.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge = again, code })));
        Assert.False(await SignedInAsync(other));
    }

    [Fact]
    public async Task Turning_it_on_signs_out_other_browsers()
    {
        if (Skip()) return;
        var email = $"sam-{Guid.NewGuid():n}@acme.test";
        using var first = Host.Factory.CreateClient();
        await Json(await first.PostAsJsonAsync("/api/auth/signup", new { email, password = Password, name = "Sam" }));
        using var second = Host.Factory.CreateClient();
        await Json(await second.PostAsJsonAsync("/api/auth/login", new { email, password = Password }));

        var setup = await Json(await first.PostAsJsonAsync("/api/auth/two-factor/setup", new { password = Password }));
        await Json(await first.PostAsJsonAsync("/api/auth/two-factor/enable", new { code = CurrentCode(Totp.FromBase32(setup.GetProperty("secret").GetString()!)) }));

        Assert.True(await SignedInAsync(first));
        Assert.False(await SignedInAsync(second));
    }

    [Fact]
    public async Task A_few_wrong_codes_end_the_sign_in()
    {
        if (Skip()) return;
        var account = await AccountWithTwoFactorAsync();
        var (browser, challenge) = await PasswordStepAsync(account.Email);
        var right = NextCode(account.Secret);
        var wrong = right == "123456" ? "654321" : "123456";

        for (var i = 1; i < TwoFactorService.MaxChallengeAttempts; i++)
        {
            Assert.Contains("isn't right", await Error(await browser.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge, code = wrong })));
        }

        Assert.Contains("Too many wrong codes", await Error(await browser.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge, code = wrong })));
        Assert.Contains("password again", await Error(await browser.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge, code = right })));
        Assert.False(await SignedInAsync(browser));
    }

    [Fact]
    public async Task Wrong_codes_add_up_across_sign_ins_until_signing_in_waits()
    {
        if (Skip()) return;
        var account = await AccountWithTwoFactorAsync();
        var right = NextCode(account.Secret);
        var wrong = right == "123456" ? "654321" : "123456";

        // Starting sign-in after sign-in doesn't give more guesses.
        for (var signIn = 0; signIn < TwoFactorService.MaxWrongCodesPerWindow / TwoFactorService.MaxChallengeAttempts; signIn++)
        {
            var (browser, challenge) = await PasswordStepAsync(account.Email);
            for (var i = 0; i < TwoFactorService.MaxChallengeAttempts; i++)
            {
                await Error(await browser.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge, code = wrong }));
            }
        }

        using var locked = Host.Factory.CreateClient();
        Assert.Contains("Too many wrong codes", await Error(await locked.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password = Password })));

        Clock.Now += TwoFactorService.WrongCodeWindow + TimeSpan.FromSeconds(1);
        var (again, challenge2) = await PasswordStepAsync(account.Email);
        await Json(await again.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge = challenge2, code = CurrentCode(account.Secret) }));
        Assert.True(await SignedInAsync(again));
    }

    [Fact]
    public async Task The_same_code_sent_twice_at_once_signs_in_only_once()
    {
        if (Skip()) return;
        var account = await AccountWithTwoFactorAsync();
        var code = NextCode(account.Secret);
        var recovery = account.RecoveryCodes[0];

        foreach (var sameCode in new[] { code, recovery })
        {
            var sides = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PasswordStepAsync(account.Email)));
            var results = await Task.WhenAll(sides.Select(s => s.Browser.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge = s.Challenge, code = sameCode })));
            Assert.Single(results, r => r.IsSuccessStatusCode);
        }
    }

    [Fact]
    public async Task Guesses_sent_at_once_still_stop_at_the_limit()
    {
        if (Skip()) return;
        var account = await AccountWithTwoFactorAsync();
        var (browser, challenge) = await PasswordStepAsync(account.Email);
        var right = NextCode(account.Secret);

        // Twenty different wrong guesses in parallel: only the first few are even checked.
        var guesses = Enumerable.Range(0, 20).Select(i => (100000 + i * 7919).ToString()).Where(g => g != right).Take(20);
        await Task.WhenAll(guesses.Select(g => browser.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge, code = g })));

        Assert.Contains("password again", await Error(await browser.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge, code = right })));
        Assert.False(await SignedInAsync(browser));
    }

    [Fact]
    public async Task A_code_just_used_says_so_and_isnt_counted_as_a_guess()
    {
        if (Skip()) return;
        var account = await AccountWithTwoFactorAsync();
        var (browser, challenge) = await PasswordStepAsync(account.Email);

        // The code that turned it on, typed again within its 30 seconds.
        for (var i = 0; i < TwoFactorService.MaxChallengeAttempts + 1; i++)
        {
            Assert.Contains("just used", await Error(await browser.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge, code = CurrentCode(account.Secret) })));
        }

        await Json(await browser.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge, code = NextCode(account.Secret) }));
        Assert.True(await SignedInAsync(browser));
    }

    [Fact]
    public async Task A_recovery_code_turns_it_off()
    {
        if (Skip()) return;
        var account = await AccountWithTwoFactorAsync();
        await Json(await account.Browser.PostAsJsonAsync("/api/auth/two-factor/disable", new { password = Password, code = account.RecoveryCodes[3] }));
        Assert.False((await Json(await account.Browser.GetAsync("/api/auth/two-factor"))).GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Changing_the_password_ends_a_sign_in_waiting_for_its_code()
    {
        if (Skip()) return;
        var account = await AccountWithTwoFactorAsync();
        var (browser, challenge) = await PasswordStepAsync(account.Email);

        await Json(await account.Browser.PostAsJsonAsync("/api/auth/password", new { current_password = Password, new_password = Password + "-new" }));

        Assert.Contains("password again", await Error(await browser.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge, code = NextCode(account.Secret) })));
        Assert.False(await SignedInAsync(browser));
    }

    [Fact]
    public async Task An_operator_reset_leaves_an_account_without_it_alone()
    {
        if (Skip()) return;
        var email = $"kim-{Guid.NewGuid():n}@acme.test";
        using var browser = Host.Factory.CreateClient();
        await Json(await browser.PostAsJsonAsync("/api/auth/signup", new { email, password = Password, name = "Kim" }));

        Assert.Equal(HttpStatusCode.Conflict, (await fixture.Operator.PostAsJsonAsync("/api/admin/users/two-factor/reset", new { email })).StatusCode);
        Assert.True(await SignedInAsync(browser));
    }

    [Fact]
    public async Task The_code_step_expires()
    {
        if (Skip()) return;
        var account = await AccountWithTwoFactorAsync();
        var (browser, challenge) = await PasswordStepAsync(account.Email);

        Clock.Now += IdentityService.LoginChallengeLifetime + TimeSpan.FromSeconds(1);

        Assert.Contains("expired", await Error(await browser.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge, code = CurrentCode(account.Secret) })));
    }

    [Fact]
    public async Task Each_recovery_code_signs_in_once()
    {
        if (Skip()) return;
        var account = await AccountWithTwoFactorAsync();
        var recovery = account.RecoveryCodes[0].ToUpperInvariant(); // typed any old way

        var (browser, challenge) = await PasswordStepAsync(account.Email);
        await Json(await browser.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge, code = recovery }));
        Assert.True(await SignedInAsync(browser));
        Assert.Equal(TwoFactorService.RecoveryCodeCount - 1, (await Json(await browser.GetAsync("/api/auth/two-factor"))).GetProperty("recovery_codes_left").GetInt32());

        var (other, again) = await PasswordStepAsync(account.Email);
        Assert.Contains("isn't right", await Error(await other.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge = again, code = recovery })));

        // New codes replace the old ones.
        var renewed = await Json(await account.Browser.PostAsJsonAsync("/api/auth/two-factor/recovery-codes", new { password = Password, code = NextCode(account.Secret) }));
        Assert.Equal(TwoFactorService.RecoveryCodeCount, renewed.GetProperty("recovery_codes").GetArrayLength());
        var (third, challenge3) = await PasswordStepAsync(account.Email);
        Assert.Contains("isn't right", await Error(await third.PostAsJsonAsync("/api/auth/login/two-factor", new { challenge = challenge3, code = account.RecoveryCodes[1] })));
    }

    [Fact]
    public async Task Turning_it_off_takes_the_password_and_a_code()
    {
        if (Skip()) return;
        var account = await AccountWithTwoFactorAsync();
        var code = NextCode(account.Secret);

        Assert.Contains("password is wrong", await Error(await account.Browser.PostAsJsonAsync("/api/auth/two-factor/disable", new { password = "not-it-at-all", code })));
        Assert.Contains("isn't right", await Error(await account.Browser.PostAsJsonAsync("/api/auth/two-factor/disable", new { password = Password, code = "000000" == code ? "111111" : "000000" })));
        await Json(await account.Browser.PostAsJsonAsync("/api/auth/two-factor/disable", new { password = Password, code }));

        Assert.False((await Json(await account.Browser.GetAsync("/api/auth/two-factor"))).GetProperty("enabled").GetBoolean());
        using var browser = Host.Factory.CreateClient();
        var login = await Json(await browser.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password = Password }));
        Assert.False(login.TryGetProperty("two_factor_required", out _));
        Assert.True(await SignedInAsync(browser));
    }

    [Fact]
    public async Task An_operator_can_turn_it_off_for_someone_locked_out()
    {
        if (Skip()) return;
        var account = await AccountWithTwoFactorAsync();

        // Not something people can do for each other.
        using var stranger = Host.Factory.CreateClient();
        await Json(await stranger.PostAsJsonAsync("/api/auth/signup", new { email = $"eve-{Guid.NewGuid():n}@acme.test", password = Password, name = "Eve" }));
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsJsonAsync("/api/admin/users/two-factor/reset", new { email = account.Email })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Operator.PostAsJsonAsync("/api/admin/users/two-factor/reset", new { email = "nobody@acme.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await fixture.Operator.PostAsJsonAsync("/api/admin/users/two-factor/reset", new { email = account.Email.ToUpperInvariant() })).StatusCode);

        // Every browser is signed out; the password alone signs in again.
        Assert.False(await SignedInAsync(account.Browser));
        using var browser = Host.Factory.CreateClient();
        var login = await Json(await browser.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password = Password }));
        Assert.False(login.TryGetProperty("two_factor_required", out _));
    }
}
