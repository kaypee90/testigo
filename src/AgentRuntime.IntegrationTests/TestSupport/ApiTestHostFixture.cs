using AgentRuntime.IntegrationTests.TestSupport;

namespace AgentRuntime.IntegrationTests;

/// <summary>One API host (and Postgres container) shared by a test class.</summary>
public sealed class ApiTestHostFixture : IAsyncLifetime
{
    public ApiTestHost Host { get; } = new();

    public Task InitializeAsync() => Host.InitializeAsync();
    public Task DisposeAsync() => Host.DisposeAsync();
}
