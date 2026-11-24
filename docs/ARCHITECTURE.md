# Architecture

How TrellisKit (the engine) and the properties that run on it are built today, for an engineer joining the project. It describes the code as of Phase 1 (identity and membership). Anything not built yet is marked **Planned** with the phase it belongs to. The engine/property split is specified in `docs/TRELLISKIT.md` (binding); each property keeps its plan of record in its own folder; the binding API contracts are `docs/PHASE1_CONTRACT.md` (built) and `docs/PHASE2_CONTRACT.md` (next). `REST_API.md` documents routes once they ship.

---

## 0. Engine and properties

TrellisKit is the engine: every folder under `backend/`, `www/`, `mobile/`, `shared/`, `docker/`, `infra/`, `assets/`, and `docs/`. This standalone repository includes the neutral sample Example Community in `samples/example/`; real properties live in separate repositories. A **property** is one configured community. Deployments run in `SingleProperty` mode by default or `MultiProperty` mode with per-request property resolution and isolated data; see `TRELLISKIT.md` section 10 for the current binding contract.

```text
 property folder (selected by TK_PROPERTY_DIR; default samples/example)
   property.json, brand/, web/, legal/, mobile/, server/settings.json, deploy/
        |
        +--> backend   reads and validates property.json at startup; serves GET /api/v1/property and brand assets
        +--> website   build: theme, copy, legal pages, SEO, icons; runtime: GET /api/v1/property
        +--> mobile    app.config.js: name, ids, scheme, icons, splash; runtime: GET /api/v1/property
        +--> Docker    mounts the folder at /app/property, builds both images with it, seeds server settings
        +--> infra     CDK context propertyDir: stack names, domains, addresses; deploy/infra.json: accounts
```

| Concern | Engine | Property |
|---|---|---|
| Identity, domains, contact, mobile ids, membership product | Reads them; never hard-codes them | `property.json` |
| Brand, theme, copy, legal text, app icons | Renders them | `brand/`, `web/`, `legal/`, `mobile/` |
| Profile taxonomy (rank, rank level, rank history, affiliation, goals), reactions, features | Generic fields (`rank`, `rankLevel`, `rankHistory`, `affiliation`, `goals`, reaction keys) driven by config; disabled sections hidden and rejected | `property.json` `profile`, `reactions`, `features` |
| Server defaults for local development | Built-in defaults | `server/settings.json` |
| Deployment selection | `TK_PROPERTY_DIR`, CDK `propertyDir` | `deploy/local.env`, `deploy/infra.json` |
| Secrets | `TK_*` environment variables, AWS Secrets Manager | Never |
| Names | `TrellisKit.*` projects, `trelliskit` database and Docker project, `TK_*` variables, `trelliskit.*` metrics | Stack names `<id>-<env>-<Name>`, website image `trelliskit/www:<id>` |

Rules: engine code never names a property, brand, sport, person, domain, price, or store product id; engine tests and CI use the sample property; paths resolve relative to the engine component or from `TK_PROPERTY_DIR`, never the repository root, so the engine and each property can live in separate repositories.

---

## 1. The system in one picture

```text
                      +------------------------------------------+
   Browser            |  www (React 19 + Vite SPA)               |
   ---------------->  |  marketing  /   member app /app/*        |
                      |  admin console /admin/* (lazy bundle)    |
                      +-------------------+----------------------+
                                          | same-origin /api/*
                                          | (nginx :8080 locally, Vite proxy :5173 in dev,
                                          |  CloudFront in production [Planned, Phase 6])
                                          v
   iOS / Android      +------------------------------------------+       +-------------------+
   (Expo app)  ---->  |  TrellisKit.Server   (Watson 7, .NET 10) | ----> | PostgreSQL 17 +   |
   absolute URL       |  REST API /api/v1, OpenAPI, workers      |       | pgvector (every   |
   to :8000           +----+-----------+-----------+------------+       |  environment)     |
                           |           |           |                     +-------------------+
                           |           |           +--> OTLP --> otel-collector --> Prometheus, Tempo, Loki --> Grafana
                           |           |
                           |           +--> SMTP (Amazon SES in production; logged when unset)
                           |
                           +--> Apple, Google, Facebook token verification (JWKS)
                           +--> App Store Server API, Google Play Developer API, Stripe API
                           <--  Webhooks: Stripe, App Store Server Notifications v2, Google RTDN (Pub/Sub push)
```

One backend process serves everything: the REST API, webhooks, OpenAPI, and background workers. The website and the mobile app share one JavaScript API client. There is no separate auth server, cache, or message broker.

---

## 2. Components and where to find them

| Path | What it is | Entry points |
|---|---|---|
| `backend/src/TrellisKit.Core` | Models, enums, request and response DTOs, database drivers, integrations, settings classes, telemetry instruments | `Database/DatabaseDriverFactory.cs`, `Integrations/IntegrationSet.cs`, `Observability/TrellisKitTelemetry.cs` |
| `backend/src/TrellisKit.Server` | Watson host, route registrars, services, workers, settings registry | `Program.cs` (thin) calls `Bootstrapper.Run`, which builds `TrellisKitServer` |
| `backend/src/Test.*` | Touchstone test suites and the five runners | `Test.Shared/TrellisKitSuites.cs` |
| `backend/trelliskit.json` | Default settings for `dotnet run` (PostgreSQL on 127.0.0.1:5432), generated on first run | |
| `shared/` | Plain JS package used by both apps: `ApiClient`, `ApiError`, enums, validators, formatters, identity helpers, `singleFlight` | `shared/src/index.js` |
| `www/` | Website: marketing pages, member app (`/app/*`), admin console (`/admin/*`) | `src/App.jsx` (routes), `src/AdminApp.jsx` |
| `mobile/` | Expo app (Expo Router), standalone project that depends on `shared` via `file:../shared` | `app/_layout.jsx`, `src/auth/AuthProvider.jsx` |
| `docker/` | Local stack: Compose file, server and www Dockerfiles, nginx config, observability configs, update and factory reset scripts | `docker/compose.yaml`, `docker/README.md` |
| `infra/` | **In progress.** AWS CDK in C# (stacks for network, data, storage, media, API, web, email, observability) | |
| `assets/` | Logos and icons, Grafana dashboard JSON (`assets/grafana`), Postman collection and local environment (`assets/postman`) | |
| `docs/` | This file, phase contracts, legal drafts | |
| `.github/workflows/tests.yml` | CI | |

npm workspaces at the root cover `shared` and `www`. `mobile/` has its own lockfile to avoid Expo monorepo hoisting problems.

### 2.1 Backend layout

```text
TrellisKit.Server
  Program.cs ............ Main -> Bootstrapper.Run(args)
  Bootstrapper.cs ....... load settings, logging, telemetry, connect DB (20 tries x 3 s), build host, start workers, wait for SIGTERM
  TrellisKitServer.cs ... Watson Webserver: CORS, auth hook, OpenAPI, capture, route registration
  Routes/ ............... one registrar per feature (AuthRoutes, MeRoutes, MembershipRoutes, AdminMemberRoutes, ...)
  Services/ ............. business logic (AccountService, SessionService, TokenService, PurchaseService,
                          WebhookService, SignupCodeService, AdminMemberService, AuthorizationService, ...)
  Workers/ .............. IntervalWorker base + RequestHistoryPrune, SubscriptionReconciliation, SignupCodeReminder
  Settings/ ............. SettingsRegistry (every runtime setting), SettingsStore (file + env), SettingFieldDefinition

TrellisKit.Core
  Database/ ............. DatabaseDriverBase, factory, SchemaMigration, Interfaces/I*Methods,
                          Shared/*MethodsBase (SQL via SqlDialect, bound to one property), Postgresql/
  Integrations/ ......... interfaces + implementations for Apple, Google, Facebook, App Store, Google Play, Stripe, SMTP
  Models/ Requests/ Responses/ Enums/ Exceptions/ Settings/ Security/ Observability/ Helpers/
```

`ApplicationServices` is the composition root: it constructs every service with explicit dependencies (no DI container). `IntegrationSet` bundles the external seams so tests swap in fakes.

---

## 3. Request flow

```text
 client
   |
   v
 Watson Webserver (TrellisKitServer)
   |-- Preflight (OPTIONS) ............ CORS from cors.allowedOrigins, 204
   |-- PreRouting ..................... CORS headers for the request
   |
   |-- PreAuthentication routes ....... anonymous: /api/v1/health*, /api/v1/auth/* (sign-in, register, refresh, ...),
   |                                    /api/v1/auth/providers, /api/v1/webhooks/*, /api/v1/media/avatars/{file},
   |                                    /openapi.json, /swagger
   |
   |-- AuthenticateRequest hook ....... AuthenticationService:
   |      1. Bearer equals auth.adminToken (constant-time)?  -> break-glass admin context
   |      2. else Bearer is an access token?  TokenService decrypts (AES-256-GCM), checks the session
   |         (cached auth.sessionCacheSeconds), loads roles and entitlements -> RequestContext
   |      3. else 401 Unauthorized
   |      then AuthorizationService.Classify(path) -> route group, Authorize(context):
   |         Admin group without SystemAdmin ................. 403 Forbidden
   |         Member group without age attestation ............ 403 AgeAttestationRequired
   |         Member group without an active entitlement ...... 402 MembershipRequired
   |         (SystemAdmin and the break-glass token pass every check)
   |
   |-- PostAuthentication routes ...... handler reads RequestContext from ctx.Metadata,
   |                                    calls a service, RouteHelper.SendJsonAsync
   |      errors: ApiErrorException -> its status and { error, message, fieldErrors };
   |              JsonException -> 400 InvalidJson; IntegrationNotConfigured -> 503; anything else -> 500 (logged)
   |
   |-- Default route .................. 404 NotFound (401 for unauthenticated callers, so admin paths are not revealed)
   |
   '-- PostRouting .................... optional request log line; request history capture (secrets redacted)
```

### 3.1 Route groups (the paywall)

The paywall is one decision in one place: `AuthorizationService.Classify` maps a path to a group and `Authorize` applies the group's rule. A new route cannot forget the paywall, because anything not explicitly classified falls into the Member group.

| Group | Paths today | Rule |
|---|---|---|
| Anonymous | `/api/v1/health*`, `/api/v1/auth/*` except `/auth/me` and `/auth/email/resend`, `/api/v1/webhooks/*`, `/openapi.json`, `/swagger` | none |
| SignedIn | `/api/v1/auth/me`, `/api/v1/auth/email/resend`, `/api/v1/me/*`, `/api/v1/handles/*`, `/api/v1/membership*`, `/api/v1/signup-codes/*` (the last two also need age attestation) | valid session |
| Member | everything else (today `/api/v1/users/{id}`) | session, age attested, active entitlement |
| Admin | `/api/v1/admin/*`, `/api/v1/settings*`, `/api/v1/request-history*` | `SystemAdmin` or break-glass token |
| Moderator | **Planned (Phase 2):** `/api/v1/admin/moderation/*` for Moderator and FeedModerator roles | |

Membership is not a role. A member is entitled when any `Entitlement` row (store subscription, sign-up code, admin comp) is active; `MembershipService` computes it and `TokenService` caches it with the session.

### 3.2 Sign-in and sessions

```text
 sign-in (email+password | Apple | Google | Facebook)
   -> AccountService verifies (PBKDF2 hash, or provider JWT against cached JWKS)
   -> SessionService creates authsessions row + refresh token (stored hashed)
   -> AuthResponse { accessToken (60 min, opaque AES-GCM), refreshToken (mobile) | tk_refresh cookie (web) }

 refresh: POST /api/v1/auth/refresh -> rotate refresh token; reuse of an old one revokes the whole session
```

| Client | Access token | Refresh token |
|---|---|---|
| Web (`X-Client: web`) | memory only | `tk_refresh` HttpOnly cookie, path `/api/v1/auth`, SameSite=Lax |
| Mobile (`X-Client: mobile`) | memory | `expo-secure-store` |

Both apps funnel a `401` through one refresh attempt (`singleFlight` in `shared`) and retry once; a failed refresh signs out. The break-glass admin token (`auth.adminToken`) works only on admin routes and exists so the console is never locked out by an OAuth outage.

---

## 4. Data layer

```text
 services
    |
    v
 DatabaseDriverBase  (Users, Credentials, ExternalIdentities, Sessions, RefreshTokens, OneTimeTokens,
    |                 RoleAssignments, rank history (rankhistoryentries), Goals, Subscriptions, SubscriptionEvents, Entitlements,
    |                 SignupCodes, SignupCodeRedemptions, Audit, RequestHistory)   <- I*Methods interfaces
    |
    +-- PostgresqlDatabaseDriver .. Npgsql, pooled (database.maxPoolSize); the deployment driver
    '-- PropertyBoundDriver ....... ForProperty(id): every method set bound to one property, every
                                    statement checked by PropertyScopeGuard before it runs
          each provides Queries/<Provider>Schema.cs (migrations) and <Provider>Dialect (SqlDialect)
```

- **Provider:** PostgreSQL only (`database.type` `Postgresql`), with pgvector. Property-owned data is reached only through `ForProperty(propertyId)`; see `docs/TRELLISKIT.md` section 10 and `REST_API.md` "Properties and deployment modes". `DatabaseDriverFactory.CreateAndInitializeAsync` builds the driver, applies pending migrations, and seeds.
- **Migrations:** versioned `SchemaMigration` lists per provider, tracked in `schema_migrations`, idempotent (`CREATE ... IF NOT EXISTS`). Version 1 is request history, version 2 is identity and membership. `/api/v1/health/ready` reports `Degraded` while any are pending.
- **SQL:** handwritten, provider-aware, built in `Shared/*MethodsBase` with dialect helpers that escape values (`D.Str`, `D.InList`, `D.Ts`). Each dialect owns the provider differences (booleans, timestamps, upserts). Transactions are statement batches (`ExecuteQueriesAsync(..., isTransaction: true)`); there is no interactive transaction API.
- **Schema style:** lower-case table and column names, `TEXT` PrettyId primary keys with fixed prefixes (`usr_`, `ses_`, `sub_`, `ent_`, `suc_`, `aud_`, ...), case-insensitive uniqueness through `lower(...)` unique indexes, structured columns and child tables (`usergoals`, `rankhistoryentries`, `signupcodetags`) instead of JSON. The one deliberate JSON column is `subscriptionevents.rawpayloadjson` (raw store payloads kept for forensics).

---

## 5. Integrations

| Integration | Code | Direction | Notes |
|---|---|---|---|
| Sign in with Apple | `AppleIdentityTokenVerifier` | outbound (JWKS) | Validates identity token, audience, nonce |
| Google Sign-In | `GoogleIdentityTokenVerifier` | outbound (JWKS) | Accepts web, iOS, and Android client ids |
| Facebook Login | `FacebookTokenVerifier` | outbound | Limited Login JWT or `debug_token` |
| App Store | `AppStoreClient`, `AppleJwsVerifier` | both | Verifies StoreKit 2 JWS; Server Notifications v2 at `/api/v1/webhooks/apple` |
| Google Play | `GooglePlayClient` | both | `subscriptionsv2.get` and acknowledge; RTDN via Pub/Sub push (OIDC token checked) at `/api/v1/webhooks/google` |
| Stripe | `StripeClient` | both | Checkout and Customer Portal; signed webhooks at `/api/v1/webhooks/stripe` |
| Email | `SmtpEmailSender` (MailKit) | outbound | SES SMTP in production; when `email.smtpHost` is empty, emails are logged instead of sent |
| Expo Push, S3, CloudFront, MediaConvert | | | **Planned (Phase 2)**, see `PHASE2_CONTRACT.md` |
| AWS Location Service | | | **Planned (Phase 4)** |

Every integration reports `IsConfigured`; an unconfigured one makes its routes return `503 IntegrationNotConfigured`, and `GET /api/v1/auth/providers` tells clients which options to show. Webhooks are anonymous, signature-verified, stored raw in `subscriptionevents`, and deduplicated by store event id.

### 5.1 Background workers

| Worker | Interval | Job |
|---|---|---|
| `RequestHistoryPruneWorker` | `RequestHistory.PruneIntervalMinutes` in the settings file (default 60) | Delete captured requests older than the retention |
| `SubscriptionReconciliationWorker` | `membership.reconcileIntervalMinutes` | Re-query every non-expired store subscription |
| `SignupCodeReminderWorker` | `membership.reminderIntervalMinutes` | Email code members 14 and 3 days before their free membership ends |

Workers derive from `IntervalWorker`, which wraps each pass in a span and records `trelliskit.job.runs` and `trelliskit.job.duration`. Today every instance runs every worker; with one instance that is correct. **Planned (Phase 2):** a `jobleases` table so singleton workers run once across ECS tasks, plus row claims for queue-style work.

---

## 6. Settings

Settings live in a JSON file, `trelliskit.json` (`backend/trelliskit.json` for `dotnet run`; in Docker `/app/config/trelliskit.json` on the `server-config` volume, seeded on first start from the property's `server/settings.json`; path from `TK_SETTINGS_FILE`). Environment variables override individual keys (for example `TK_DB_PASSWORD`, `TK_ADMIN_TOKEN`) and production secrets arrive that way. `SettingsRegistry.cs` declares every key with its section, type, bounds, apply mode, and environment variable; the admin Settings page is generated from it through `GET/PUT /api/v1/settings`. Live settings apply on save. Restart settings apply after `POST /api/v1/settings/restart`, which exits with code 75 so the container runtime relaunches the process. Secret values are never returned by the API. When no admin token is configured, the first boot generates one, saves it, and logs it once.

---

## 7. Telemetry

```text
 Watson built-in meter + activity source ("Watson": HTTP request metrics, one server span per request)
 Application meter + activity source ("TrellisKit": db queries, jobs, auth, purchases, webhooks, codes, emails)
 Logs (AppLogger -> syslog-style console, optional file, and an OTLP logger)
        |
        |  Radiant (OpenTelemetry SDK wrapper), OTLP gRPC or HTTP to telemetry.otlpEndpoint
        v
 otel-collector :4317 / :4318
        |-- metrics -> Prometheus scrape endpoint :8889 -> Prometheus :9090
        |-- traces  -> Tempo :3200
        '-- logs    -> Loki :3100
                              all three -> Grafana :3000 (dashboards from assets/grafana: overview, http, database, jobs)
```

Application instruments are defined in `TrellisKitTelemetry.cs` with `trelliskit.` names (Grafana queries `trelliskit_*`; service name `trelliskit-server`; resource attribute `trelliskit.property` carries the property id) and low-cardinality labels (`outcome`, `provider`, `store`, `route_group`); ids never appear in metric labels. Database calls record a counter and a duration histogram tagged with `db.system`. In production the same OTLP export is planned to point at an ADOT collector feeding Amazon Managed Prometheus and a hosted Grafana and Tempo (**Planned, Phase 6**).

---

## 8. Local Docker stack and AWS

### 8.1 Local (`docker/compose.yaml`, all ports bound to 127.0.0.1)

```text
 postgres:17 :5432 ----+
 tempo :3200 ----------+--> otel-collector :14317/:14318 (host) --> server :8000 --> www (nginx) :8080
 loki  :3100 ----------+                                     |
                                    prometheus :9090 <--------+
                                    grafana :3000  (after prometheus, tempo, loki)
```

- Every service has a curl or wget healthcheck (5 s interval, 2 retries) and starts only when its dependencies are healthy.
- The stack runs one property: `TK_PROPERTY_DIR` from `docker/.env` (the sample) or from the property's `deploy/local.env` passed with `docker compose --env-file`. The folder is mounted read-only at `/app/property` and is the named build context `property` of both images (`trelliskit/server:local`, `trelliskit/www:<property id>`).
- `server` runs on PostgreSQL with its writable settings on the `server-config` volume (seeded from the property's `server/settings.json` by `docker/server/entrypoint.sh` on first start), `restart: unless-stopped` (so the admin Restart button works).
- The website is the production Vite build served by nginx inside the server container (the Dockerfile's `combined` target): nginx listens on 8080, serves the site, and proxies `/api/` and `/openapi.json` (including WebSocket upgrades) to the server on 127.0.0.1:8000. A standalone website image (`docker/www/Dockerfile`) proxies to `server:8000` instead.
- Email: the Docker stack delivers all email to Mailpit (SMTP `mailpit:1025`, inbox at http://127.0.0.1:8025). When no SMTP host is configured (for example a server run outside Docker), emails are written to the server log instead.
- `dev.sh` rebuilds from source and `update.sh` pulls the published images; both recreate while keeping data; `factory/reset.sh` deletes every volume, so the settings are seeded again from the property on the next start.
- Developing one component outside Docker: `dotnet run --project TrellisKit.Server` (on 127.0.0.1:8000, against the stack's PostgreSQL), `npm run dev --workspace www` (Vite on :5173 proxying `/api` to :8000), `npx expo start` in `mobile/` (simulator defaults `http://127.0.0.1:8000`, Android emulator `http://10.0.2.2:8000`, or `EXPO_PUBLIC_API_URL`).

### 8.2 AWS (**Planned, Phase 6**; nothing is deployed yet)

```text
 <web domain>  --> CloudFront --> S3 (www build)             /api/* --> ALB
 <api domain> ----------------------------------------------------------> ALB --> ECS Fargate (server, 2+ tasks)
 <media domain> --> CloudFront (signed cookies) --> S3 images, transcoded  |--> RDS PostgreSQL (Multi-AZ)
                                                                         |--> S3 source, video source, retained; MediaConvert, SES, Expo Push
 <each redirect domain> --> CloudFront 301 --> https://<web domain>      '--> Secrets Manager (env vars)
```

Region `us-west-2`, CloudFront certificates in `us-east-1`, CI deploys through GitHub OIDC. The infrastructure is written (`infra/`, CDK in C#) and synthesized in CI for the sample property and for each property; every stack name, domain, and address comes from the property (`-c propertyDir=<folder>`). Media uses one S3 bucket per purpose (source, video source, images, transcoded, retained); see `infra/README.md`.

---

## 9. Security model

| Concern | How it is handled today |
|---|---|
| Passwords | PBKDF2-HMAC-SHA256 with 600,000 iterations and a random salt (`Pbkdf2PasswordHasher`), 10 to 128 characters, lockout after `auth.lockoutThreshold` failures within `auth.lockoutMinutes` |
| Tokens | Access tokens are opaque AES-256-GCM blobs (random IV) carrying a version, session id, user id, issued and expiry times, and a nonce, keyed by `auth.tokenEncryptionKey`. Refresh tokens are random, stored hashed, single-use, with reuse detection that revokes the session |
| Admin access | `SystemAdmin` role, or the break-glass token compared in constant time. Admin actions write `auditrecords` |
| Paywall and age gate | One authorization stage (section 3.1). Age is self-attested; only the attestation time is stored, not the date of birth |
| Browser | CORS allow-list (`cors.allowedOrigins`), credentials only for listed origins; refresh cookie HttpOnly, Secure (except on http://127.0.0.1), SameSite=Lax; nginx adds `X-Content-Type-Options`, `X-Frame-Options: DENY`, and `Referrer-Policy` |
| Webhooks | Stripe signatures, Apple JWS certificate chain, Google Pub/Sub OIDC token; idempotent by event id |
| Account enumeration | Same error for unknown email and wrong password; forgot-password always returns 202; sign-up codes return one message for every failure |
| Rate limiting | In-process `SlidingWindowRateLimiter` on password reset, verification resend, and sign-up code attempts. Per instance, keyed by account and by `ctx.Request.Source` IP (forwarded headers are not trusted yet, which matters once an ALB is in front) |
| Logging and capture | Request history redacts `Authorization`, cookies, and any header containing `token`, `secret`, `password`, or `api-key`; bodies are truncated at configured limits. Secrets are never returned by the settings API |
| Media | Avatars only, served anonymously by the backend from `storage.directory/avatars` with sniffed types and `nosniff`. Paywalled media delivery is **Planned (Phase 2)** |
| Moderation, blocking, reports | **Planned (Phase 2 and 3).** `isMuted` and `isBanned` exist; ban revokes sessions, mute is stored but not yet enforced on writes because no write surfaces exist yet |

---

## 10. Testing

| Layer | Where | How to run |
|---|---|---|
| Backend | `Test.Shared` holds every test once as Touchstone suites (`ModelSuite`, `SecuritySuite`, `SettingsSuite`, `DatabaseSuite`, `HttpSuite`, `AuthSuite`, `ProfileSuite`, `MembershipSuite`, plus `IntegrationTests/*` for each provider against fake HTTP handlers). Fakes in `Test.Shared/Fakes` replace the `IntegrationSet` | `dotnet run --project Test.Automated` (add `-- --provider postgresql` for PostgreSQL), then `dotnet test` on `Test.Xunit3`, `Test.Xunit2`, `Test.Nunit`, `Test.Mstest` |
| Shared JS | `shared/test/*.test.js` (Node test runner) | `npm test --workspace shared` |
| Website | `www/test/*.test.jsx` (Vitest and Testing Library), `www/e2e/smoke.spec.js` (Playwright against a running stack) | `npm test --workspace www`, `npm run test:e2e --workspace www` |
| Mobile | `mobile/__tests__/*.test.js` (Jest and React Native Testing Library, with `test-utils/fakeServer.js`) | `cd mobile && npm test` |
| API | Postman collection in `assets/postman` | Postman or Postman CLI |

CI (`.github/workflows/tests.yml`) runs every backend runner on Ubuntu against a PostgreSQL 17 with pgvector service container, builds the backend on Windows and macOS, and runs the website and mobile checks on every push and pull request. The build must have zero warnings. AOT is not supported (reflection-based JSON and store client libraries), so there is no `Test.Aot`.

---

## 11. Conventions worth knowing on day one

- Routes are under `/api/v1`, JSON is camelCase with string enums and ISO 8601 UTC timestamps, and every error body is `{ "error", "message", "fieldErrors" }`.
- Every collection uses `EnumerationQuery` / `EnumerationResult`: admin page controls use `skip`, while scrolling follows opaque continuation tokens. Each request and response remains bounded by `maxResults`.
- Every new route updates `REST_API.md` and the Postman collection; every new setting goes in `SettingsRegistry.cs`.
- C# style is strict (`CLAUDE.md` and `~/Code/Agents/requirements/CODE_STYLE.md`): no `var`, no tuples, one type per file, XML docs on public members, `ConfigureAwait(false)` everywhere.
- No em-dashes anywhere, including docs and comments.
- Version numbers change only when the owner asks.

## 12. What is coming

| Phase | Adds to this architecture |
|---|---|
| 2 | Feed posts, media pipeline (S3, CloudFront signed cookies, MediaConvert; local storage and ffmpeg in Docker), reactions, threaded comments, mentions, notifications, Expo push, reports, blocking, Moderator route group, job leases |
| 3 | Forums, scoped moderators, moderation queue across forums, trending worker, promote to feed, search (PostgreSQL `tsvector`, SQLite FTS5) |
| 4 | Gym and trainer directory with maps (AWS Location Service), events and RSVPs, content library with progress |
| 5 | WebSocket endpoint on the same Watson port (DMs and live notifications, `LISTEN/NOTIFY` fan-out), support tickets, ideas board, broadcasts |
| 6 | Prerendered marketing pages, AWS production environment in `infra/`, store submissions |
