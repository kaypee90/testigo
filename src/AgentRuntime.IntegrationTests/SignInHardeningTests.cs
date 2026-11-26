using System.Net;
using System.Net.Http.Json;
using AgentRuntime.IntegrationTests.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace AgentRuntime.IntegrationTests;

/// <summary>
/// Guessing one account's password is stopped per account, not only per address (an attacker has
/// many addresses), and the ACP WebSocket, which CORS doesn't cover, answers the dashboard's pages
/// only when the caller is a browser session.
/// </summary>
public sealed class SignInHardeningTests
{
    private const string Password = "a-long-enough-password";

    private static async Task<ApiTestHost> StartAsync()
    {
        var host = new ApiTestHost();
        host.Settings["Auth:SignInAttemptsPerMinute"] = "1000";
        host.Settings["Auth:WrongPasswordsPerAccount"] = "3";
        await host.InitializeAsync();
        return host;
    }

    [Fact]
    public async Task Wrong_passwords_pause_sign_in_to_that_account_only()
    {
        var host = await StartAsync();
        try
        {
            using var client = host.Factory.CreateClient();
            foreach (var email in new[] { "target@example.test", "bystander@example.test" })
            {
                using var signUp = host.Factory.CreateClient();
                Assert.True((await signUp.PostAsJsonAsync("/api/auth/signup", new { email, password = Password })).IsSuccessStatusCode);
            }

            for (var i = 0; i < 3; i++)
            {
                var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = "target@example.test", password = "not-the-password" });
                Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            }

            // Even the right password waits now, and the answer says why.
            var paused = await client.PostAsJsonAsync("/api/auth/login", new { email = "target@example.test", password = Password });
            Assert.Equal(HttpStatusCode.Unauthorized, paused.StatusCode);
            Assert.Contains("Too many wrong passwords", await paused.Content.ReadAsStringAsync());

            var other = await client.PostAsJsonAsync("/api/auth/login", new { email = "bystander@example.test", password = Password });
            Assert.True(other.IsSuccessStatusCode, await other.Content.ReadAsStringAsync());
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

}
