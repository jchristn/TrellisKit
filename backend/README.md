# Backend

C# backend of the TrellisKit engine: Watson 7.3 on .NET 10 on PostgreSQL 17 or later with pgvector (the only database, in every environment). See `../docs/TRELLISKIT.md` for the engine and property split and `../REST_API.md` for the API.

A property is a folder with `property.json` and `brand/` that supplies the name, domains, membership product and price, minimum age, profile taxonomy (ranks, levels, goals, affiliation, rank history), and reactions. Nothing brand-specific lives in this code. In `SingleProperty` mode (default) the server runs the property in `property.directory`; in `MultiProperty` mode (`TK_DEPLOYMENT_MODE=MultiProperty`) it serves every property registered by platform administrators, each request resolved to exactly one property and every row scoped by `propertyid` (see `../REST_API.md`, "Properties and deployment modes").

## Layout

| Project | Purpose |
|---|---|
| `src/TrellisKit.Core` | Settings, models, request and response DTOs, the property model, loader, and validator (`PropertyConfig/`), the database layer (`DatabaseDriverBase` with `ForProperty` property-bound drivers, `PropertyScopeGuard`, `DatabaseDriverFactory`, shared SQL in `Shared/`, the `Postgresql/` provider with `Implementations/` and `Queries/`), telemetry instruments |
| `src/TrellisKit.Server` | `Program` and `Bootstrapper` (composition), `TrellisKitServer` (Watson host), route registrars, services, settings registry, background workers |
| `src/Test.Shared` | Touchstone suites shared by every test host |
| `src/Test.Automated` | Console runner |
| `src/Test.Xunit3`, `Test.Xunit2`, `Test.Nunit`, `Test.Mstest` | Framework hosts running the same suites |
| `trelliskit.json` | Reference default settings for running outside Docker |

## Build and run

```bash
cd backend/src
dotnet build TrellisKit.sln
dotnet run --project TrellisKit.Server
```

Running from source requires a PostgreSQL server: `docker compose up -d postgres` in `../docker` (with the env files `dev.sh` uses) provides one, and the server fails fast with a clear message when it cannot connect. Point it at the database with `TK_DB_HOSTNAME`, `TK_DB_PORT`, `TK_DB_DATABASE`, `TK_DB_USERNAME`, and `TK_DB_PASSWORD`. The first run creates `trelliskit.json` in the working directory, generates an admin token, and logs it once. Pass a settings path as the first argument, or set `TK_SETTINGS_FILE`. Any setting with an environment variable (see `../REST_API.md`) can be overridden, for example `TK_REST_PORT=8100`.

### Choosing the property

The property is selected only by directory path: setting `property.directory` or the environment variable `TK_PROPERTY_DIR`, absolute or relative (a relative path is tried against the working directory and each of its parents, so it can be relative to the engine root). When nothing is set the engine's neutral sample property `samples/example` is used (found by searching upward from the working directory and the application directory), and in a container the mount at `/app/property`. The server refuses to start, naming each problem, when `property.json` is missing or invalid.

```bash
TK_PROPERTY_DIR=/path/to/my-property dotnet run --project TrellisKit.Server
```

Values the property supplies by default and settings may override: `web.publicBaseUrl` (default `https://` plus `domains.web`), `email.fromName` and `email.fromAddress` (default `contact.emailFromName` and `contact.emailFromAddress`), `appStore.productId` and `googlePlay.productId` (default `membership.storeProductId`).

To export telemetry from a local run to the Docker observability stack, set `telemetry.otlpEndpoint` to `http://127.0.0.1:14317`. Telemetry carries `service.name` (default `trelliskit-server`) and the resource attribute `trelliskit.property` with the property id; metrics are named `trelliskit.*`.

## Tests

```bash
cd backend/src
dotnet run --project Test.Automated                         # needs PostgreSQL with pgvector (see below)
dotnet test Test.Xunit3
dotnet test Test.Xunit2
dotnet test Test.Nunit
dotnet test Test.Mstest
```

Every test runs against PostgreSQL with pgvector. Tests use the sample property `samples/example` (or temporary copies of it); the isolation suites register a second test-local property through the platform API. PostgreSQL connection values come from `TK_TEST_PG_HOST`, `TK_TEST_PG_PORT`, `TK_TEST_PG_DATABASE` (default `trelliskit_test`), `TK_TEST_PG_USERNAME`, and `TK_TEST_PG_PASSWORD` (defaults match the CI service container). With a throwaway container:

```bash
docker run -d --rm --name tk-pg-test -e POSTGRES_PASSWORD=Test_Passw0rd -e POSTGRES_DB=trelliskit_test -p 127.0.0.1:55434:5432 pgvector/pgvector:pg17 -c max_locks_per_transaction=1024
TK_TEST_PG_PORT=55434 dotnet run --project Test.Automated
docker stop tk-pg-test
```

Each database case runs in its own PostgreSQL schema, which is removed afterwards. Schema creation and teardown are serialized within each test host to avoid catalog-inspection deadlocks in historic migrations; the test actions still run concurrently. Concurrent schema teardown needs a larger lock table than PostgreSQL's default; the command above configures the disposable server for parallel suites. Set `TK_TEST_LOG_CONSOLE=true` when diagnosing a failing case to include server logs.

## AOT support

Not supported. API JSON uses reflection-based `System.Text.Json` serialization, and the planned Stripe and Google Play client libraries depend on Newtonsoft.Json reflection. There is therefore no `Test.Aot` project; the full suite still runs on Linux, Windows, and macOS in CI.
