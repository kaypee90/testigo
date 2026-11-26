using AgentRuntime.Infrastructure.Identity;

namespace AgentRuntime.Api.Platform;

/// <summary>How the dashboard's session cookie is set (and deleted, which needs the same options).</summary>
public static class SessionCookie
{
    public static CookieOptions Options(HttpRequest request, AuthOptions auth, DateTimeOffset? expires = null) => new()
    {
        HttpOnly = true,
        // Lax: the cookie isn't sent on cross-site POSTs, which (with CORS) keeps other sites from
        // acting as the user.
        SameSite = SameSiteMode.Lax,
        // Behind a TLS-terminating proxy, IsHttps reflects the visitor's connection only with
        // ForwardedHeaders:Enabled (ProxyHeaders); Auth:CookieSecure=always doesn't depend on it.
        Secure = auth.AlwaysSecureCookie || request.IsHttps,
        Path = "/",
        Expires = expires
    };
}
