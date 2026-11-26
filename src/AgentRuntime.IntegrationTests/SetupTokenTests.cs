using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentRuntime.IntegrationTests.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace AgentRuntime.IntegrationTests;

/// <summary>
/// The first account on a server owns everything there, so creating it takes the operator's setup
/// token (Auth:SetupToken), whatever the sign-up setting; nobody who merely finds a new server
/// first can claim it. Once an account exists the token is never asked for again.
/// </summary>
public sealed class SetupTokenTests(ITestOutputHelper output)
{
    private const string Password = "a-long-enough-password";

    [Fact]
    public async Task The_first_account_needs_the_setup_token_and_later_ones_do_not()
    {
        var host = new ApiTestHost();
        host.Settings["Auth:SetupTokenRequired"] = "true";
        host.Settings["Auth:SetupToken"] = "the-setup-token";
        host.Settings["Auth:SignInAttemptsPerMinute"] = "1000";
        await host.InitializeAsync();
        try
        {
            if (host.Unavailable is not null)
            {
                output.WriteLine("SKIPPED: " + host.Unavailable);
                return;
            }

            using var client = host.Factory.CreateClient();
            Assert.True(await SetupRequired(client));

            var none = await client.PostAsJsonAsync("/api/auth/signup", new { email = "first@example.test", password = Password });
            Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
            var wrong = await client.PostAsJsonAsync("/api/auth/signup", new { email = "first@example.test", password = Password, setup_token = "a-guess" });
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            Assert.True(await SetupRequired(client));

            var right = await client.PostAsJsonAsync("/api/auth/signup", new { email = "first@example.test", password = Password, setup_token = "the-setup-token" });
            Assert.True(right.IsSuccessStatusCode, await right.Content.ReadAsStringAsync());

            using var second = host.Factory.CreateClient();
            Assert.False(await SetupRequired(second));
            var later = await second.PostAsJsonAsync("/api/auth/signup", new { email = "second@example.test", password = Password });
            Assert.True(later.IsSuccessStatusCode, await later.Content.ReadAsStringAsync());
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    private static async Task<bool> SetupRequired(HttpClient client)
    {
        using var me = JsonDocument.Parse(await client.GetStringAsync("/api/auth/me"));
        return me.RootElement.GetProperty("server").GetProperty("setup_required").GetBoolean();
    }
}
