# CLAUDE.md

Guidance for agents working in this repository.

## Project

This standalone repository contains TrellisKit: a reusable community and membership engine (backend, website, mobile app, Docker stack, AWS infrastructure). Properties (configured communities) run on it from separate repositories; the neutral sample property in `samples/example/` ships with the engine. The binding engine/property specification is `docs/TRELLISKIT.md`; read it before making changes. Each property keeps its plan of record in its own folder.

## Engine rules (binding)

- **No property-specific literals in engine code.** Nothing in `backend/`, `www/`, `mobile/`, `shared/`, `docker/` (engine files), `infra/src`, `assets/`, engine docs, or engine tests may name a property, a brand, a sport, a person, a domain, a price, or a store product id, or default to a specific property. Those values come from the property configuration.
- **The default property is the neutral sample** `samples/example/` (Example Community). Engine tests, fixtures, CI jobs, and doc examples use it (or test-local fixtures), never a real property.
- **A property is selected only by directory path:** `TK_PROPERTY_DIR` (absolute, or relative to the engine root). Backend setting `property.directory`; website and mobile builds read the env var; Docker (`dev.sh`) reads it from `docker/.env` or a property's `deploy/local.env` (relative to `docker/`); infra reads CDK context `propertyDir`.
- **Keep paths independent of the workspace layout.** This repository contains the engine at its root; real properties live outside it. Resolve paths relative to the engine component or from `TK_PROPERTY_DIR`, never from an assumed umbrella checkout.
- **Zero tolerance for unbounded retrieval:** every list uses `EnumerationQuery` in and `EnumerationResult<T>` out with an enforced maximum page size, every object carries `labels` (list) and `tags` (dictionary), every enumeration filters on them, and every client search surface offers label and tag filters. Spec: `docs/ENUMERATION_AND_METADATA.md`.
- If a property needs something the engine cannot express, add a configuration option to the engine with a neutral default. Never special-case a property.

## Where property configuration lives

| File (in the property folder) | Contents |
|---|---|
| `property.json` | Identity, domains, contact, brand colors, mobile ids, membership product, profile taxonomy, reactions, features (public; never secrets) |
| `brand/`, `web/`, `legal/`, `mobile/` | Logos and icons, theme CSS and marketing copy, legal drafts, generated app icons and splash |
| `email/templates/` | Optional email templates as JSON (`<key>.json`): replace an engine template's default text and blocks, or add new templates (`docs/TRELLISKIT.md` section 12.2) |
| `server/settings.json` | Server settings defaults, seeded into the Docker stack's settings volume on first start (local development values only) |
| `deploy/local.env` | Docker selection: `TK_PROPERTY_DIR` (relative to the engine's `docker/`) and `TK_PROPERTY_ID` |
| `deploy/infra.json` | AWS account ids per environment, alarm email, GitHub owner and repository of the deploy role, `dns` (`mode`: `Route53` or `External`), `mail` (`provider`, for example `Mailgun` so no SES identity is created, plus the Mailgun domain and region), and the optional `ai`, `location`, and `partio` sections (see `infra/README.md`) |

Secrets never go in a property folder: they come from environment variables (`TK_*`) and AWS Secrets Manager.

## Requirements

Follow `~/Code/Agents/requirements` (`CODE_STYLE.md`, `BACKEND_ARCHITECTURE.md`, `AUTHENTICATION.md`, `BACKEND_TEST_ARCHITECTURE.md`, `FRONTEND_ARCHITECTURE.md`, `DASHBOARD_STYLE_AND_USABILITY.md`, `TELEMETRY_REQUIREMENTS.md`, `REPOSITORY_REQUIREMENTS.md`, `VERSIONING.md`, `SIMULATED_USER_TESTING.md`) with these project-specific deviations:

- Tenancy: a property is the tenant. Deployments run `SingleProperty` (default) or `MultiProperty`; every property-owned row carries `propertyid` and every query is property-scoped (`docs/TRELLISKIT.md` section 10). New code must keep data access in the property-bound shared helpers
- Database: **PostgreSQL only** (with pgvector; RecallDB for vectors). SQLite was removed at the start of the multi-property milestone (`docs/TRELLISKIT.md` section 13). Property-owned data is reached only through a property-bound driver (`DatabaseDriverBase.ForProperty`); new SQL on a property-owned table must carry `propertyid` (the scope guard rejects it otherwise) and new tables get `propertyid` and an entry in `PropertyTables`
- No `sdk/` and no `DOCKERHUB_README.md`
- English only (no i18next); keep UI strings in one strings module per app, with `{name}` placeholders for the property name
- Source lives under each component (`backend/src`, `www/src`, `mobile/`, `shared/src`, `infra/src`)

## C# style (strict, from CODE_STYLE.md)

- Namespace at top; `using` statements inside the namespace; System/Microsoft usings first, alphabetical, then others alphabetical
- XML docs on all public members; none on private members
- Private fields `_PascalCase`; no `var`; no tuples
- One class or enum per file
- Async methods take a `CancellationToken` and use `.ConfigureAwait(false)`
- Nullable enabled; guard clauses; specific exception types with `/// <exception>` docs
- No `Console.WriteLine` in library code
- Configurable values are public properties with backing fields and documented defaults/min/max
- Never use em-dashes anywhere (code, comments, docs, Postman)

## Build and test

```bash
cd backend/src && dotnet build TrellisKit.sln           # must be 0 warnings, 0 errors
cd backend/src && dotnet run --project Test.Automated   # PostgreSQL with pgvector via TK_TEST_PG_* variables
cd backend/src && dotnet test Test.Xunit3 && dotnet test Test.Xunit2 && dotnet test Test.Nunit && dotnet test Test.Mstest
npm install && npm test --workspace shared && npm run lint --workspace www && npm test --workspace www && npm run build --workspace www
cd mobile && npm install && npm run lint && npm test
cd infra/src && dotnet build TrellisKit.Infra.sln && dotnet test TrellisKit.Infra.sln   # 0 warnings; cdk-nag clean
cd infra && npx aws-cdk@2.1147.0 synth -c env=dev --no-lookups                         # and -c env=prod; add -c propertyDir=<folder> for a property
cd docker && ./dev.sh                                       # build from source, sample property (creates docker/.env.local)
cd docker && ./dev.sh <property folder>/deploy/local.env      # build from source for a specific property
cd docker && ./update.sh                                    # published images pinned in compose.yaml
./build-all.sh v1.0.0                                       # publish jchristn77/trelliskit-server and -www (amd64, arm64)
cd docker && docker compose --env-file .env --env-file .env.local config -q
```

Builds for a property: set `TK_PROPERTY_DIR` (for example `TK_PROPERTY_DIR=<property folder> npm run build --workspace www`).

Keep `REST_API.md` and the Postman collection in `assets/postman/` in sync with every route change, and add new settings to `backend/src/TrellisKit.Server/Settings/SettingsRegistry.cs`. Environment variables are `TK_*`; metrics are `trelliskit.*` (Grafana queries `trelliskit_*`).

## Versioning

Never change a version number unless the user explicitly asks.
