using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Testigo.Reporter;

/// <summary>
/// Headers on every reporter response (NFR-SEC-2, NFR-SEC-6): a strict Content Security Policy
/// with no inline scripts and nothing from other origins, no referrer, no caching (a shared
/// device keeps nothing), and no framing.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";

    public Task InvokeAsync(HttpContext http)
    {
        http.Response.OnStarting(() =>
        {
            var h = http.Response.Headers;
            h.ContentSecurityPolicy = ContentSecurityPolicy;
            h["Referrer-Policy"] = "no-referrer";
            h.XContentTypeOptions = "nosniff";
            h.XFrameOptions = "DENY";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            h["Cross-Origin-Resource-Policy"] = "same-origin";
            h["X-Robots-Tag"] = "noindex, nofollow";
            if (!http.Request.Path.StartsWithSegments("/css") && !http.Request.Path.StartsWithSegments("/js") && !http.Request.Path.StartsWithSegments("/img"))
            {
                h.CacheControl = "no-store";
                h.Pragma = "no-cache";
            }

            return Task.CompletedTask;
        });
        return next(http);
    }
}

/// <summary>
/// Rate-limit buckets that never hold an address (NFR-SEC-3): each client is an HMAC of its
/// address under a random salt that lives only in memory and changes every hour, so a bucket
/// can't be tied back to an address, and nothing survives a restart.
/// </summary>
public sealed class ClientBuckets(TimeProvider clock)
{
    private readonly object gate = new();
    private byte[] salt = RandomNumberGenerator.GetBytes(32);
    private DateTimeOffset saltSince = clock.GetUtcNow();

    public string KeyFor(IPAddress? address)
    {
        byte[] current;
        lock (gate)
        {
            if (clock.GetUtcNow() - saltSince > TimeSpan.FromHours(1))
            {
                salt = RandomNumberGenerator.GetBytes(32);
                saltSince = clock.GetUtcNow();
            }

            current = salt;
        }

        var raw = Encoding.ASCII.GetBytes(address?.ToString() ?? "unknown");
        return Convert.ToHexString(HMACSHA256.HashData(current, raw))[..32];
    }
}

/// <summary>Submissions per client bucket (FR-INT-1, NFR-SEC-3). Only report submissions count, not page views.</summary>
public sealed class SubmissionLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter;
    private readonly ClientBuckets buckets;

    public SubmissionLimiter(ClientBuckets buckets, IOptions<ReporterOptions> options)
    {
        this.buckets = buckets;
        var o = options.Value;
        limiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, o.SubmissionsPerClient),
            Window = TimeSpan.FromMinutes(Math.Max(1, o.SubmissionWindowMinutes)),
            QueueLimit = 0,
        }));
    }

    public bool TryAcquire(IPAddress? address)
    {
        using var lease = limiter.AttemptAcquire(buckets.KeyFor(address));
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}
