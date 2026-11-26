using Microsoft.AspNetCore.HttpOverrides;

namespace AgentRuntime.Api.Platform;

/// <summary>
/// Behind a reverse proxy that terminates TLS (Caddy in docker-compose.prod.yml), every request
/// reaches the API over plain HTTP from the proxy's own address. With
/// <c>ForwardedHeaders:Enabled</c>, the API takes the original scheme and client address from the
/// proxy's <c>X-Forwarded-Proto</c> and <c>X-Forwarded-For</c>, so it knows a request was HTTPS
/// (the session cookie is then <c>Secure</c>, and links it builds start with https) and who sent
/// it (the sign-in rate limit counts each visitor, not one bucket for everyone behind the proxy).
/// </summary>
/// <remarks>Off by default: without a proxy in front, those headers come from the caller and can't
/// be believed. Only connections from <see cref="TrustedNetworks"/> are believed even when on.</remarks>
public sealed class ProxyHeadersOptions
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>The private ranges Docker networks use. A proxy anywhere in them is trusted, so the
    /// API itself must not be reachable from outside (in production only Caddy publishes ports).</summary>
    public static readonly string[] DefaultTrustedNetworks = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];

    public bool Enabled { get; set; }

    /// <summary>CIDR ranges the proxy connects from. Empty: <see cref="DefaultTrustedNetworks"/>.</summary>
    public List<string> TrustedNetworks { get; set; } = [];
}

public static class ProxyHeaders
{
    /// <summary>Applies the proxy's forwarded headers when enabled. Runs first, before anything
    /// reads the request's scheme or client address.</summary>
    public static IApplicationBuilder UseTestigoProxyHeaders(this IApplicationBuilder app, IConfiguration configuration)
    {
        var settings = configuration.GetSection(ProxyHeadersOptions.SectionName).Get<ProxyHeadersOptions>() ?? new();
        if (!settings.Enabled) return app;

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            // One proxy (Caddy): only its own entry is believed, never ones the client added before it.
            ForwardLimit = 1
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in settings.TrustedNetworks.Count > 0 ? settings.TrustedNetworks : [.. ProxyHeadersOptions.DefaultTrustedNetworks])
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }

        return app.UseForwardedHeaders(options);
    }
}
