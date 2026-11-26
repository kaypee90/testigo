# Testigo staff dashboard

Spanish-first Next.js staff interface for the synthetic-data prototype. The public reporter
application is separate (`../Testigo.Reporter`). Every page retains the prototype banner.
Authentication, recovery and account settings are in Spanish; human language review remains pending.

Read `AGENTS.md` and the relevant guides in `node_modules/next/dist/docs/` before editing.
This project uses system fonts and self-hosted resources, not remote font services.

## Local development

```bash
npm ci
npm run dev
```

The development server uses port 3000; the repository Compose setup exposes the dashboard at
http://localhost:3101 and the API at http://localhost:5180. Set `NEXT_PUBLIC_API_BASE_URL` to
the API origin when needed. See the root README for accounts and two-factor setup.

## Checks

```bash
npx tsc --noEmit
npm run lint
npm run build
```

These checks do not establish browser accessibility or screen-reader conformance.
Deployment uses the repository's Docker/Caddy configuration; see `../../docs/`.
