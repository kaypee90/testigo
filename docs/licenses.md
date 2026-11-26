# Dependency licenses (NFR-OSS-1)

Testigo is licensed under [MPL-2.0](../LICENSE). This page records the licenses of
its dependencies, checked on 2026-11-26 from the package metadata on a development machine: the
`.nuspec` of every NuGet package the API and the reporter app restore (direct and transitive), and
the `package.json` of every production npm dependency of the staff dashboard
(`npm ls --omit=dev --all`). Test-only packages aren't shipped and aren't listed.

**Result:** every dependency is under a permissive license compatible with MPL-2.0. One native
library is LGPL (see below). No dependency needs a paid service (NFR-OSS-4).

## .NET (API, Testigo, reporter app): 42 packages

| License | Packages |
|---|---|
| MIT | 31, including ASP.NET Core and EF Core extensions, Konscious Argon2, QRCoder, Newtonsoft.Json |
| Apache-2.0 | 9: OpenTelemetry and its exporters and instrumentation |
| PostgreSQL License (permissive, BSD-like) | Npgsql 10.0.3, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 |

The .NET runtime and ASP.NET Core themselves are MIT. Four of the MIT packages (Humanizer,
Mono.TextTemplating and the EF Core design tools) are only used to create migrations and aren't in
the published API.

## Staff dashboard (Next.js): 22 production npm packages

| License | Count | Notes |
|---|---|---|
| MIT | 11 | Next.js, React, React DOM, styled-jsx, PostCSS, … |
| Apache-2.0 | 5 | sharp, @swc/helpers, … |
| ISC | 2 | |
| 0BSD, BSD-3-Clause | 2 | |
| CC-BY-4.0 | 1 | caniuse-lite (browser-support data, used at build time; attribution is in the package) |
| LGPL-3.0-or-later | 1 | `@img/sharp-libvips-*`, the native image library behind Next.js image optimization, loaded dynamically by `sharp`. LGPL allows this use; a distributed image must let users replace the library, which a container with the unmodified npm package does |

34 more entries have no installed `package.json` on this machine: platform-specific optional
binaries for other operating systems (`@next/swc-*`, `@img/sharp-*`) and optional peer
dependencies that aren't installed (`@playwright/test`, `sass`, `babel-plugin-react-compiler`,
`@opentelemetry/api`). Their published licenses are MIT or Apache-2.0, except the libvips
binaries (LGPL-3.0-or-later, as above).

## Container images

| Image | License |
|---|---|
| `mcr.microsoft.com/dotnet/aspnet:10.0`, `sdk:10.0` | MIT (.NET); Debian base packages under their own free licenses |
| `node` (dashboard build) | MIT (Node.js) |
| `pgvector/pgvector:pg16` | PostgreSQL License (PostgreSQL and pgvector) |
| `caddy:2` | Apache-2.0 |
| `ollama/ollama` (optional) | MIT. Model weights pulled into it have their own licenses: check them before use |

## Data

- The synthetic eval set and demo reports in this repository are original and are covered by the
  repository license.
- The open dataset license (FR-PAT-5, CC BY 4.0 proposed) is a separate decision with the host.

## How to repeat the check

Restore the solution, then read the license expressions from
`~/.nuget/packages/<id>/<version>/<id>.nuspec` for the packages in each project's
`obj/project.assets.json`, and run `npm ls --omit=dev --all --json` in `src/web` and read each
package's `license` field. Do this again before each release.
