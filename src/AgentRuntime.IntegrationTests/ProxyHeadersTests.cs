using System.Net;
using AgentRuntime.Api.Platform;
using AgentRuntime.Infrastructure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace AgentRuntime.IntegrationTests;

/// <summary>Behind Caddy, which terminates TLS and connects from the Docker network, the API sees
/// the visitor's scheme and address only through the proxy's forwarded headers (ProxyHeaders), and
/// the session cookie is Secure when the visitor's connection was HTTPS (SessionCookie). The same
/// middleware and cookie options the API uses, on an in-memory server (no Postgres needed).</summary>
public sealed class ProxyHeadersTests
{
    private const string DockerProxy = "::ffff:172.18.0.5"; // how Kestrel reports an IPv4 peer
    private const string Visitor = "203.0.113.7";

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings, AuthOptions? auth = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        var app = builder.Build();
        app.UseTestigoProxyHeaders(app.Configuration);

        var options = auth ?? new AuthOptions();
        app.MapGet("/sign-in", (HttpContext http) =>
        {
            http.Response.Cookies.Append(options.CookieName, "token", SessionCookie.Options(http.Request, options));
            return $"{http.Request.Scheme} {http.Connection.RemoteIpAddress?.MapToIPv4()}";
        });
        await app.StartAsync();
        return app;
    }

    private static async Task<(string Scheme, string Client, bool Secure)> SignInAsync(
        WebApplication app, string from, string? forwardedProto = null, string? forwardedFor = null)
    {
        var context = await app.GetTestServer().SendAsync(c =>
        {
            c.Request.Method = "GET";
            c.Request.Scheme = "http";
            c.Request.Path = "/sign-in";
            c.Connection.RemoteIpAddress = IPAddress.Parse(from);
            if (forwardedProto is not null) c.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
            if (forwardedFor is not null) c.Request.Headers["X-Forwarded-For"] = forwardedFor;
        });

        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var parts = body.Split(' ');
        var cookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("testigo_session=token", cookie);
        return (parts[0], parts[1], cookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }

    private static readonly Dictionary<string, string?> Enabled = new() { ["ForwardedHeaders:Enabled"] = "true" };

    [Fact]
    public async Task Behind_the_proxy_an_https_visit_gets_a_secure_cookie_and_the_visitors_address()
    {
        await using var app = await StartAsync(Enabled);

        var (scheme, client, secure) = await SignInAsync(app, DockerProxy, "https", Visitor);

        Assert.Equal("https", scheme);
        Assert.Equal(Visitor, client);
        Assert.True(secure);
    }

    [Fact]
    public async Task Only_the_proxys_own_entry_counts_not_addresses_the_client_added_before_it()
    {
        await using var app = await StartAsync(Enabled);

        var (_, client, _) = await SignInAsync(app, DockerProxy, "https", $"198.51.100.1, {Visitor}");

        Assert.Equal(Visitor, client);
    }

    [Fact]
    public async Task Forwarded_headers_from_outside_the_trusted_networks_are_ignored()
    {
        await using var app = await StartAsync(Enabled);

        var (scheme, client, secure) = await SignInAsync(app, "198.51.100.9", "https", Visitor);

        Assert.Equal("http", scheme);
        Assert.Equal("198.51.100.9", client);
        Assert.False(secure);
    }

    [Fact]
    public async Task Without_the_setting_forwarded_headers_are_ignored()
    {
        await using var app = await StartAsync([]);

        var (scheme, _, secure) = await SignInAsync(app, DockerProxy, "https", Visitor);

        Assert.Equal("http", scheme);
        Assert.False(secure);
    }

    [Fact]
    public async Task Cookie_secure_always_marks_the_cookie_secure_even_over_plain_http()
    {
        await using var app = await StartAsync([], new AuthOptions { CookieSecure = "always" });

        var (scheme, _, secure) = await SignInAsync(app, "127.0.0.1");

        Assert.Equal("http", scheme);
        Assert.True(secure);
    }
}
