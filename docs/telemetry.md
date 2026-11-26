# Telemetry

The staff dashboard and the reporter app send nothing to any analytics or error-reporting
service: there is none built in (NFR-SEC-2, requirements §7.4). The API can optionally send its
own logs, traces and metrics over OpenTelemetry to [Axiom](https://axiom.co). It is off until its
settings are filled in, so a self-hosted install keeps everything on the server by default.

**Logs never contain report content, contact details, case codes or reporter IP addresses**
(NFR-OPS-4, NFR-SEC-3). Tests check this for the intake and case flow. Still, check your log
levels before sending logs from a real deployment to any third party.

## What is kept out

- **Reporter requests aren't traced at all**: a trace carries the user agent and can carry the
  client address (`ReporterPaths.IsReporterFacing`).
- **Secrets in paths.** A trace of a route with a non-id parameter (an invitation token) records
  the route's template instead of the path. A request that matched no route keeps only the first
  two segments of its path.
- **Outgoing calls** (to a model provider) are recorded with the server's address only, nothing
  after it.

## Sending to Axiom

Create an **Events** dataset (logs and traces) and a **Metrics** dataset in Axiom, and an API
token that can ingest into both. Then set, on the API (environment variables in compose, or
configuration):

| Setting | Env | |
|---|---|---|
| `Axiom:Token` | `Axiom__Token` | Empty turns Axiom off. |
| `Axiom:Domain` | `Axiom__Domain` | `us-east-1.aws.edge.axiom.co` or `eu-central-1.aws.edge.axiom.co`. |
| `Axiom:Dataset` | `Axiom__Dataset` | Logs and traces. Empty: neither is sent. |
| `Axiom:MetricsDataset` | `Axiom__MetricsDataset` | Metrics. Empty: not sent. |
| `Axiom:ServiceName` | `Axiom__ServiceName` | Default `testigo-api`. |

They are read when the API starts. Without Axiom, traces are written to the console.

| Signal | Contents |
|---|---|
| Logs | What the API logs at its configured level (`Logging:LogLevel`), with log scopes as fields. |
| Traces | HTTP requests the API serves (not reporter-facing ones) and HTTP calls it makes, plus the `Testigo` activity source. |
| Metrics | ASP.NET Core request rates and durations, HttpClient calls, the .NET runtime, and the `Testigo` meter. |

Every record carries `service.name` and `deployment.environment.name`, so one dataset can hold
several servers or environments.
