using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AgentRuntime.Api.Platform;

/// <summary>
/// Where the API sends its logs, traces and metrics. Axiom takes OpenTelemetry over OTLP/HTTP:
/// logs and traces go to an events dataset, metrics to a separate metrics dataset.
/// </summary>
public sealed class AxiomOptions
{
    public const string Section = "Axiom";

    /// <summary>An Axiom API token with ingest rights on the datasets below. Empty turns Axiom off.</summary>
    public string Token { get; init; } = "";

    /// <summary>The edge deployment that ingests: us-east-1.aws.edge.axiom.co or eu-central-1.aws.edge.axiom.co.</summary>
    public string Domain { get; init; } = "us-east-1.aws.edge.axiom.co";

    /// <summary>An Events dataset for logs and traces. Empty: neither is sent.</summary>
    public string Dataset { get; init; } = "";

    /// <summary>A Metrics dataset. Empty: metrics aren't sent.</summary>
    public string MetricsDataset { get; init; } = "";

    public string ServiceName { get; init; } = "testigo-api";
}

public static class Telemetry
{
    /// <summary>Testigo's own traces and metrics, alongside ASP.NET Core and HttpClient.</summary>
    public const string SourceName = "Testigo";

    public static void AddTestigoTelemetry(this WebApplicationBuilder builder)
    {
        var axiom = builder.Configuration.GetSection(AxiomOptions.Section).Get<AxiomOptions>() ?? new AxiomOptions();
        var sendEvents = axiom.Token != "" && axiom.Dataset != "";
        var sendMetrics = axiom.Token != "" && axiom.MetricsDataset != "";

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(axiom.ServiceName)
                .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(o =>
                    {
                        o.EnrichWithHttpResponse = (activity, response) => RedactRequestPath(activity, response.HttpContext);
                        // Reporter requests aren't traced at all: a trace carries the user agent and
                        // can carry the client address (NFR-SEC-3).
                        o.Filter = http => !ReporterPaths.IsReporterFacing(http.Request.Path);
                    })
                    .AddHttpClientInstrumentation(o => o.EnrichWithHttpRequestMessage = RedactOutgoingUrl)
                    .AddSource(SourceName);
                // Without a destination, traces go to the console as before.
                if (sendEvents) t.AddOtlpExporter(o => Configure(o, axiom, "traces", $"X-Axiom-Dataset={axiom.Dataset}"));
                else t.AddConsoleExporter();
            });

        if (sendMetrics)
        {
            otel.WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(SourceName)
                .AddOtlpExporter(o => Configure(o, axiom, "metrics", $"X-Axiom-Metrics-Dataset={axiom.MetricsDataset}")));
        }

        if (sendEvents)
        {
            // Scopes are kept so ids logged in a scope can be queried as fields. Logs never hold report
            // content, contact details, case codes or IP addresses (NFR-OPS-4).
            otel.WithLogging(
                l => l.AddOtlpExporter(o => Configure(o, axiom, "logs", $"X-Axiom-Dataset={axiom.Dataset}")),
                o =>
                {
                    o.IncludeScopes = true;
                    o.IncludeFormattedMessage = true;
                });
        }
    }

    /// <summary>
    /// Some routes carry a secret in their path, such as an invitation token.
    /// A trace of one records the route's template instead of the path that was called. Routes
    /// whose parameters all name records (id, taskId, workspaceId…) keep their path. A request that
    /// matched no route (such a URL opened in a browser: right path, wrong method) has no template
    /// to go by, so its path is cut to where it starts.
    /// </summary>
    internal static void RedactRequestPath(System.Diagnostics.Activity activity, HttpContext http)
    {
        if (http.GetEndpoint() is not RouteEndpoint { RoutePattern: { RawText: { } template } pattern })
        {
            activity.SetTag("url.path", PathStart(http.Request.Path));
            return;
        }

        if (pattern.Parameters.All(p => IsRecordId(p.Name) || http.GetRouteValue(p.Name) is null)) return;
        activity.SetTag("url.path", template.StartsWith('/') ? template : "/" + template);
    }

    /// <summary>A path's first <see cref="KeptSegments"/> segments: enough to tell what was asked for.</summary>
    internal static string PathStart(PathString path)
    {
        var segments = (path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length <= KeptSegments ? path.Value ?? "/" : "/" + string.Join('/', segments.Take(KeptSegments)) + "/…";
    }

    private const int KeptSegments = 2;

    internal static bool IsRecordId(string parameter) =>
        parameter.Equals("id", StringComparison.OrdinalIgnoreCase) || parameter.EndsWith("Id", StringComparison.Ordinal);

    /// <summary>
    /// Outgoing calls can have secrets or identifiers in their address too (some model providers put
    /// the key or model in the path or query). A trace of one names the server that was called and
    /// nothing after it.
    /// </summary>
    internal static void RedactOutgoingUrl(System.Diagnostics.Activity activity, HttpRequestMessage request)
    {
        if (request.RequestUri is { IsAbsoluteUri: true } uri) activity.SetTag("url.full", uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    private static void Configure(OtlpExporterOptions o, AxiomOptions axiom, string signal, string datasetHeader)
    {
        o.Endpoint = new Uri($"https://{axiom.Domain.Trim().TrimEnd('/')}/v1/{signal}");
        o.Protocol = OtlpExportProtocol.HttpProtobuf;
        o.Headers = $"Authorization=Bearer {axiom.Token},{datasetHeader}";
    }
}
