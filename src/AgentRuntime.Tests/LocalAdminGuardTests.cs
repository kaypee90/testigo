using AgentRuntime.Infrastructure.Identity;
using Xunit;

namespace AgentRuntime.Tests;

/// <summary>The local admin (admin / admin by default) is an owner and operator: fine on one
/// machine, never on a server set up for the internet, which must refuse to start with it.</summary>
public sealed class LocalAdminGuardTests
{
    private static AuthOptions Auth(string? username, string? password, string mode = "accounts") =>
        new() { Mode = mode, LocalAdmin = new LocalAdminOptions { Username = username, Password = password } };

    [Theory]
    [InlineData("admin", "admin", true, true)]                   // the local default, behind a proxy
    [InlineData("admin", "admin", false, false)]                 // the local default, on one machine
    [InlineData("ops", "a-long-enough-password", true, false)]   // the operator's own strong password
    [InlineData("", "", true, false)]                            // no local admin
    public void A_public_server_refuses_a_guessable_local_admin(string username, string password, bool publicServer, bool refused) =>
        Assert.Equal(refused, LocalAdminGuard.Problem(Auth(username, password), publicServer) is not null);

    [Fact]
    public void A_server_without_accounts_has_no_local_admin_to_guard() =>
        Assert.Null(LocalAdminGuard.Problem(Auth("admin", "admin", mode: "disabled"), publicServer: true));
}
