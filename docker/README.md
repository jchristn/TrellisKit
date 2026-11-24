# Docker stack

Everything needed to run one TrellisKit property: the server with the website and admin console (one container), PostgreSQL with pgvector, RecallDB, a mail inbox (Mailpit), and the full observability stack (OpenTelemetry Collector, Prometheus, Tempo, Loki, Grafana).

Two ways to run it:

| Script | Images | Use |
|---|---|---|
| `./update.sh` (`update.bat`) | The published `jchristn77/trelliskit-server:v1.0.0` (pinned in `compose.yaml`; `TK_SERVER_IMAGE` overrides it). It runs the server and the website (nginx on port 8080, proxying `/api` to the server) in one container, built for and baking in the neutral sample property | Running a released stack. Pulls the images and recreates, keeping data |
| `./dev.sh [<property>/deploy/local.env]` (`dev.bat`) | Built from this engine's source with `compose.dev.yaml` (the Dockerfile's `combined` target, image `trelliskit/server:local`) for any property folder, which is also mounted read-only at `/app/property` | Development |

`../build-server.sh <tag>` publishes `jchristn77/trelliskit-server` (and `../build-www.sh` the standalone website image `jchristn77/trelliskit-www`, which this stack does not use) for linux/amd64 and linux/arm64/v8, tagged `latest` plus the given tag; `../build-all.sh <tag>` runs both. A property publishes its own images the same way and keeps its own deployment folder with a `compose.yaml` derived from this one.

## Choosing the property

The stack runs one property in `SingleProperty` mode (default). With `update.sh` it is the property baked into the image (the sample). With `dev.sh` it is selected only by `TK_PROPERTY_DIR` (a path relative to this `docker/` folder, or absolute); `TK_PROPERTY_ID` is the property's opaque id (its `property.json` `id`) and the cosmetic `TK_PROPERTY_SLUG` names the folder of live update keys. For a multi-property stack set `TK_DEPLOYMENT_MODE=MultiProperty`, `TK_PLATFORM_HOSTS` (for example `platform.localhost`), and `TK_PLATFORM_BOOTSTRAP_ADMIN_EMAIL`; the bootstrap password is `TK_PLATFORM_BOOTSTRAP_ADMIN_PASSWORD` in `.env.local`.

**Passwords are never committed.** `./update.sh`, `./dev.sh`, and `factory/reset.sh` create `docker/.env.local` (git-ignored) on first use with random values for `TK_POSTGRES_PASSWORD` (the `postgres` superuser), `TK_DB_PASSWORD` (role `trelliskit`), `TK_RECALLDB_DB_PASSWORD` (role `recalldb`), `TK_RECALLDB_ADMIN_API_KEY` (RecallDB's admin key), `TK_RECALLDB_TOKEN` (the server's RecallDB bearer token), `TK_PLATFORM_BOOTSTRAP_ADMIN_PASSWORD`, and `TK_SECRETS_ENCRYPTION_KEY`, and pass `--env-file .env [--env-file <property env>] --env-file .env.local` to every `docker compose` command. The compose file refuses to start without them. `postgres/init/01-databases.sh` runs once on an empty volume: it creates the `trelliskit` and `recalldb` databases and their login roles, enables `pgvector` in both (and `pg_trgm` for RecallDB), and revokes `PUBLIC` access (see Search below for the exact `trelliskit` rights). `env-local.sh` also appends keys added later (the two RecallDB values) to an existing `.env.local` without touching the others. Keep `.env.local` as long as the volume exists (the roles were created with those passwords); delete both together with a factory reset to rotate them.

| How | Property |
|---|---|
| `./update.sh` | The sample property baked into the published image |
| `./dev.sh` | The defaults in `docker/.env`: the engine's neutral sample, `../samples/example`, built from source |
| `./dev.sh <property folder>/deploy/local.env` | Another property built from source (its `deploy/local.env` sets `TK_PROPERTY_DIR`, `TK_PROPERTY_ID`, and optionally `TK_PROPERTY_SLUG`) |

Pass the same `--env-file` arguments to every `docker compose` command for that stack (`--env-file .env --env-file <property env> --env-file .env.local`), or set them once in your shell with `export COMPOSE_ENV_FILES=.env,.env.local` (add the property env file in the middle for another property); the plain `docker compose ...` commands below assume that. With `dev.sh` the property folder is mounted read-only at `/app/property` in the server and is passed to the image build as the named build context `property`, so the website is built for that property and the server image carries it too. Container names start with `TK_STACK_NAME` (default `trelliskit`), every host port is a `TK_*_PORT` variable (`TK_POSTGRES_PORT`, `TK_SERVER_PORT`, `TK_WWW_PORT`, `TK_MAILPIT_PORT`, `TK_GRAFANA_PORT`, `TK_PROMETHEUS_PORT`, `TK_TEMPO_PORT`, `TK_LOKI_PORT`, `TK_OTLP_GRPC_PORT`, `TK_OTLP_HTTP_PORT`, `TK_RECALLDB_PORT`), and local images are tagged `TK_IMAGE_TAG` (default `local`), so a second stack can run beside the first with `docker compose -p <name>` and its own values; switching properties with `down` and `up` keeps the same database volume, so run a factory reset when you want a clean database for the other property.

## Prerequisites

- Docker Desktop (or Docker Engine) with Compose v2
- About 3 GB of free disk for images and volumes
- These host ports free on 127.0.0.1: 3000, 3100, 3200, 5432, 8000, 8025, 8080, 8600, 9090, 14317, 14318 (or override them, see above)

## Start

```bash
cd docker
./update.sh                           # published images (sample property)
./dev.sh                              # or build from source: ./dev.sh <property>/deploy/local.env
docker compose --env-file .env --env-file .env.local ps
```

The first `dev.sh` build takes a few minutes (it compiles the .NET server and the website). Services start in dependency order and each waits for its dependencies to report healthy, so `docker compose ps` shows every service as `healthy` once the stack is ready (about a minute).

## What runs where

| Service | URL | Credentials |
|---|---|---|
| Website, member app, admin console | http://127.0.0.1:8080 (admin at http://127.0.0.1:8080/admin) | Admin token below |
| API (direct) | http://127.0.0.1:8000/api/v1/health | Admin token below |
| API through the website | http://127.0.0.1:8080/api/v1/health | Same origin as the site |
| OpenAPI / Swagger | http://127.0.0.1:8000/openapi.json, http://127.0.0.1:8000/swagger | None |
| Grafana | http://127.0.0.1:3000 | admin / admin |
| Prometheus | http://127.0.0.1:9090 | None |
| Tempo (traces API) | http://127.0.0.1:3200 | None |
| Loki (logs API) | http://127.0.0.1:3100 | None |
| PostgreSQL 17 with pgvector | 127.0.0.1:5432, databases `trelliskit` and `recalldb` | roles `trelliskit` and `recalldb`; passwords in `docker/.env.local` |
| RecallDB | http://127.0.0.1:8600 (server: `http://recalldb:8600`) | admin API key `TK_RECALLDB_ADMIN_API_KEY`, server token `TK_RECALLDB_TOKEN` (both in `docker/.env.local`) |
| Mailpit (every email the server sends) | http://127.0.0.1:8025 (API at http://127.0.0.1:8025/api/v1/messages) | None |
| OTLP collector (for a server run outside Docker) | 127.0.0.1:14317 (gRPC), 127.0.0.1:14318 (HTTP) | None |

All ports bind to 127.0.0.1 only, so nothing is exposed to your network.

**Local admin token:** `auth.adminToken` in the property's `server/settings.json` (the defaults seeded on first start). Read the active value with `docker compose exec server cat /app/config/trelliskit.json`. Paste it into the admin console login. It is for local development only; production tokens come from AWS Secrets Manager through the `TK_ADMIN_TOKEN` environment variable.

## Server settings

The server's writable settings file is `/app/config/trelliskit.json` (`TK_SETTINGS_FILE`) on the named volume `server-config`. It is not a tracked file. On first start, `server/entrypoint.sh` copies the property's `server/settings.json` there when the file does not exist yet; afterwards the file is never overwritten, so changes saved in the admin console's Settings page survive restarts, rebuilds, and container recreation. A property without `server/settings.json` (such as the sample) starts with the server's built-in defaults, which the server writes on first boot. A factory reset deletes the volume, so the next start seeds the defaults again.

The compose file also sets the stack wiring as `TK_*` environment variables (database host, name, user, and password; Mailpit SMTP; OTLP endpoint; CORS origins; public URL; storage directory), which override the settings file. That keeps any property's defaults pointed at this stack.

## Media (uploads, images, video)

The stack runs the media pipeline in Local mode (`media.storageMode = Local`), so no AWS account is needed and the client flow is the same as production: create an upload session, PUT the bytes to the returned URL, call `complete`, then poll until the media is `Ready`.

| Concern | In this stack |
|---|---|
| Storage | Folders under `storage.directory` (`/app/data/objects`) on the `server-data` volume, one per purpose: `source/` (original images, kept), `video-source/` (original videos, kept), `images/` (image variants, video posters, link preview images), `transcoded/` (HLS playlists and segments), `retained/` (served objects of moderator-removed content), next to `avatars/`. Media survives restarts and rebuilds; a factory reset deletes it |
| Upload URLs | `PUT http://127.0.0.1:8080/api/v1/media/local-uploads/{id}?exp=...&sig=...` (and `.../parts/{n}` for multipart), signed by the server; no bearer token is sent. Parts are 16 MB and images at most 20 MB, both under nginx's `client_max_body_size 50m` (only `/api/v1/admin/updates` allows more; see Live updates) |
| Images | Decoded and re-encoded in the server (SkiaSharp): EXIF orientation applied, all metadata (including GPS) removed, `thumb`/`medium`/`large` in WebP and JPEG |
| Video | `TK_MEDIA_VIDEO_PROCESSOR=Ffmpeg`: the server image is built from the `local` target of `server/Dockerfile`, which adds `ffmpeg`. Videos are transcoded to HLS (360p and 720p, metadata stripped) with a poster at 1 second, one job at a time (`media.videoConcurrency`) |
| Delivery | `POST /api/v1/media/grant` returns `mode: "Local"` and sets the `tk_media` cookie (path `/api/v1/media/files`); files are served by `GET /api/v1/media/files/{id}/{file}` (cookie or bearer token, with `Range` support for Safari video). Media URLs are root-relative (`media.publicBaseUrl` is empty), so they resolve against the API origin |
| Link previews | Fetched from inside the server container. Private, loopback, and link-local addresses (including the Docker networks) are refused by design, so previews of local services do not work |
| Push | Real Expo push from a development build works without an access token; `TK_PUSH_ENABLED=false` keeps notifications in-app only |
| Feed post emails | Delivered to Mailpit with working one-click unsubscribe links |

Try an image upload (with `$ACCESS` a member access token from sign-in):

```bash
SESSION=$(curl -s -X POST http://127.0.0.1:8000/api/v1/media/uploads -H "Authorization: Bearer $ACCESS" -H "Content-Type: application/json" \
  -d "{\"kind\":\"Image\",\"contentType\":\"image/jpeg\",\"sizeBytes\":$(wc -c < photo.jpg)}")
URL=$(echo "$SESSION" | python3 -c "import json,sys; print(json.load(sys.stdin)['upload']['single']['url'])")
MEDIA=$(echo "$SESSION" | python3 -c "import json,sys; print(json.load(sys.stdin)['media']['id'])")
curl -s -X PUT "$URL" -H "Content-Type: image/jpeg" --data-binary @photo.jpg -D - -o /dev/null | grep -i etag
curl -s -X POST "http://127.0.0.1:8000/api/v1/media/$MEDIA/complete" -H "Authorization: Bearer $ACCESS"
curl -s "http://127.0.0.1:8000/api/v1/media/$MEDIA" -H "Authorization: Bearer $ACCESS"   # poll until status is Ready
```

Root-relative URLs in the response (for example `/api/v1/media/files/med_.../medium.webp`) load with the same bearer token or after `POST /api/v1/media/grant`.

When you run the server outside Docker (`dotnet run`), video uses `Passthrough` (the original is copied and served as MP4, metadata not stripped, and a warning is logged at startup) unless you set `media.videoProcessor` to `Ffmpeg` and have `ffmpeg` and `ffprobe` on your PATH.

## Live updates (signing keys and uploads)

- **Uploads.** nginx accepts up to 500 MB on `/api/v1/admin/updates` (`client_max_body_size 500m`) and streams the body to the server (`proxy_request_buffering off`); every other route keeps the 50 MB limit. The server's own cap is the console setting `updates.maxUploadMegabytes`.
- **Signing keys.** `~/.trelliskit/keys/<property slug>/` (where `npm run updates-certificate` in `mobile/` writes the private keys; `TK_PROPERTY_SLUG` from the property env file picks the folder, and `TK_UPDATES_KEYS_HOST_DIR` replaces the whole path) is mounted read-only at `/app/keys` in the server. On start, `server/entrypoint.sh` sets `TK_UPDATES_SIGNING_KEY_FILE=/app/keys/updates-private-key.pem` and `TK_UPDATES_STANDBY_SIGNING_KEY_FILE=/app/keys/updates-standby-private-key.pem`, each only when that file exists and no key or key file variable is already set. Without keys the folder is created empty, the stack starts normally, and the updates admin page reports that no signing key is configured. After generating keys, run `docker compose up -d --no-deps server` (a restart is enough) so the entrypoint picks them up.

## AI (RecallDB and the assistant widget)

- **RecallDB.** The published image binds to `localhost` inside its container and ships the well-known admin key `recalldbadmin`, with no environment variable for either, so nothing else (not the server, not the published host port) could reach it. `recalldb/start.sh` (mounted read-only and used as the entrypoint) writes `recalldb.json` before every start: all interfaces on 8600, the admin key `TK_RECALLDB_ADMIN_API_KEY`, no MCP, no file logging. Once RecallDB answers, it creates the server's credential (bearer token `TK_RECALLDB_TOKEN` on tenant `default`) if missing and deletes the image's seeded credential whose token is `default`. The server gets `TK_RECALLDB_ENDPOINT=http://recalldb:8600`, `TK_RECALLDB_TENANT=default`, and `TK_RECALLDB_TOKEN`, so vector and hybrid knowledge work out of the box (these live settings are locked in the console by the stack). Check it from the server: `docker compose exec server sh -c 'curl -s -o /dev/null -w "%{http_code}\n" -H "Authorization: Bearer $TK_RECALLDB_TOKEN" http://recalldb:8600/v1.0/tenants/default/collections'` prints `200`.
- **Embedding the assistant widget.** Every page sends `X-Frame-Options: DENY` except `/embed/assistants/*`, which sends `Content-Security-Policy: frame-ancestors <TK_EMBED_FRAME_ANCESTORS>` instead (default `'self'`). nginx emits it because the embed page is a static client route; the backend never serves that page, so it cannot add the header per assistant. The embed page itself still checks the embedding origin against the assistant's `embed.allowedOrigins`. To try the widget from a local page, add its origin (space separated CSP sources) to `docker/.env.local`, for example `TK_EMBED_FRAME_ANCESTORS='self' http://localhost:5500`, and run `docker compose up -d --no-deps www` (no rebuild needed: `www/nginx.conf` is an nginx template filled in at container start). Add the same origin to the assistant's embed origins in the console (the console accepts `https://` origins, plus plain `http://localhost` and `http://127.0.0.1` on any port for local development).
- **Model endpoints** are entered in the admin console. A local Ollama on the host is reached at `http://host.docker.internal:11434`. Text Embeddings Inference (TEI) is not part of the stack; run it separately (for example `docker run -p 8081:80 ghcr.io/huggingface/text-embeddings-inference:cpu-1.5 --model-id BAAI/bge-small-en-v1.5`) and add an endpoint with `apiType` `Tei` at `http://host.docker.internal:8081`.

## Search

Forum and global search use the Verbex index in the schema `search` (`search.schema`, empty by default, resolves to `search`). On a fresh volume, `postgres/init/01-databases.sh` gives the `trelliskit` role the same least-privilege rights as the AWS `db-bootstrap` container: the database is owned by `postgres`; the role has `CONNECT`, `TEMPORARY`, and `CREATE` on it, `USAGE` and `CREATE` on schema `public` (its migrations own the tables), and owns schema `search`, which the script creates. `CREATE` on the database is only there because the server runs `CREATE SCHEMA IF NOT EXISTS search` at start and PostgreSQL checks that privilege even when the schema exists; it allows creating schemas, never databases or roles. Stacks initialized before this change have a database owned by `trelliskit`, which already allows all of this, so search works there too; a factory reset applies the narrower rights.

## Email (Mailpit)

The server sends every email (verification, password reset, code reminders, new feed post emails) to Mailpit over SMTP at `mailpit:1025` without TLS. Nothing leaves your machine. Open http://127.0.0.1:8025 to read the inbox; links in the emails point at the website on http://127.0.0.1:8080 (`web.publicBaseUrl`).

If `email.smtpHost` is empty (for example when running the server outside Docker), emails are not sent; the server logs each message's subject and link instead.

## The first administrator

The property's `server/settings.json` can set `auth.bootstrapAdminEmail` (for the sample, set it in the admin console or with `TK_BOOTSTRAP_ADMIN_EMAIL`). That account becomes a `SystemAdmin` as soon as its email is verified:

1. Open http://127.0.0.1:8080 and register with that address (any password of 10 or more characters, a date of birth 18 or more years ago).
2. Open Mailpit at http://127.0.0.1:8025, open the "Confirm your email" message, and click the link (or paste it into the browser).
3. Sign in to the admin console at http://127.0.0.1:8080/admin with that email and password.

To skip those steps, also set `auth.bootstrapAdminPassword` (or set `TK_BOOTSTRAP_ADMIN_EMAIL` and `TK_BOOTSTRAP_ADMIN_PASSWORD` in the property's env file, which the compose file passes to the server). On a start where the property has no members, the server creates that account already verified, as `SystemAdmin`, with that password, and logs `created the bootstrap administrator`. An existing database with members is left alone; a factory reset gives a fresh one.

The break-glass admin token keeps working for admin routes. The access token encryption key (`auth.tokenEncryptionKey`) is generated on first boot and saved to `/app/config/trelliskit.json`; changing it signs every member out.

Stripe, Sign in with Apple, Google, Facebook, the App Store, and Google Play stay disabled until their settings are filled in (Settings in the admin console, or the `TK_*` environment variables). Their routes return `503 IntegrationNotConfigured` and `GET /api/v1/auth/providers` reports them as disabled.

## Try it

```bash
TOKEN=$(docker compose exec -T server cat /app/config/trelliskit.json | python3 -c "import json,sys; print(json.load(sys.stdin)['Auth']['AdminToken'])")
curl http://127.0.0.1:8080/api/v1/health
curl -H "Authorization: Bearer $TOKEN" "http://127.0.0.1:8080/api/v1/request-history?pageSize=5"

# Register, verify through the Mailpit API, and sign in
curl -s -X POST http://127.0.0.1:8000/api/v1/auth/register -H "Content-Type: application/json" -H "X-Client: mobile" \
  -d '{"email":"me@example.test","password":"a long local password","displayName":"Local Me","dateOfBirth":"1990-01-01","acceptTerms":true}'
ID=$(curl -s "http://127.0.0.1:8025/api/v1/search?query=to:me@example.test" | python3 -c "import json,sys; print(json.load(sys.stdin)['messages'][0]['ID'])")
VERIFY=$(curl -s "http://127.0.0.1:8025/api/v1/message/$ID" | python3 -c "import json,re,sys; print(re.search('token=([A-Za-z0-9_-]+)', json.load(sys.stdin)['Text']).group(1))")
curl -s -X POST http://127.0.0.1:8000/api/v1/auth/email/verify -H "Content-Type: application/json" -d "{\"token\":\"$VERIFY\"}"
```

Or import the collection and `local.postman_environment.json` from `assets/postman/` into Postman.

In Grafana, open **Dashboards > TrellisKit** for the Overview, HTTP, Database, and Jobs and Platform dashboards (metrics `trelliskit_*`). Traces are under **Explore > Tempo** (service `trelliskit-server`), and logs under **Explore > Loki** (`{service_name="trelliskit-server"}`), with trace-to-log links between them. Telemetry carries the resource attribute `trelliskit.property` with the property id.

## Layout

| Path | Purpose |
|---|---|
| `compose.yaml` | The stack from published images: services, healthchecks, startup order, volumes. Project `trelliskit`, containers `trelliskit-*` |
| `compose.dev.yaml` | Development override: builds the server image from source for `TK_PROPERTY_DIR` and mounts the property |
| `.env` | Default property selection (`TK_PROPERTY_DIR=../samples/example`, `TK_PROPERTY_ID=app_mwczw7mg_F8RsDcNidltPWZS1seB`, `TK_PROPERTY_SLUG=example`); no secrets |
| `.env.local` | Generated local passwords (git-ignored; created by `env-local.sh` / `env-local.ps1`) |
| `postgres/init/01-databases.sh` | First-boot databases, roles, pgvector, and the search schema |
| `recalldb/start.sh` | RecallDB entrypoint: writes its settings (all interfaces, admin key) and registers the server's credential. The AWS RecallDB task runs the same file |
| `server/Dockerfile` | Server image (multi-stage .NET 10 build of `TrellisKit.Server`, runtime with curl for the healthcheck, property baked in at `/app/property`). Targets: `runtime` (default, production on ECS, no ffmpeg), `local` (adds `ffmpeg`), and `combined` (`local` plus the website built for the property, served by nginx on 8080 and proxying `/api` to the server on 127.0.0.1:8000; the published image and `compose.dev.yaml` use it) |
| `server/entrypoint.sh` | Seeds `/app/config/trelliskit.json` from the property's `server/settings.json` on first start, points the server at any live update signing keys in `/app/keys`, starts nginx for the website in the combined image (`TK_WWW_ENABLED`, rendering `TK_WWW_BACKEND` and `TK_EMBED_FRAME_ANCESTORS` into the nginx template), then runs the server |
| `server-config` volume | Server settings at `/app/config`. Changes saved in the admin console's Settings page are written here and survive restarts |
| `server-data` volume | Uploaded objects at `/app/data/objects` (`storage.directory`): avatars and every media folder (see Media above) |
| `www/Dockerfile`, `www/nginx.conf` | Standalone website image (`jchristn77/trelliskit-www`, not used by this stack): Vite build for the property, served by nginx on 8080, proxying `/api/` and `/openapi.json` to `TK_WWW_BACKEND` (default `http://server:8000`). The combined server image uses the same `nginx.conf` template |
| `otel-collector/` | Collector config for the upstream `otel/opentelemetry-collector-contrib` image (it has no shell or curl, so it has no container healthcheck; dependents wait for it to start) |
| `prometheus/`, `tempo/`, `loki/` | Backend configs for metrics, traces, and logs |
| `grafana/provisioning/` | Datasources (Prometheus, Tempo, Loki with fixed UIDs) and the dashboard provider; dashboard JSON lives in `../assets/grafana/` |
| `update.sh`, `update.bat` | Pull the pinned images and recreate the stack. Keeps all data. Optional argument: an extra env file |
| `dev.sh`, `dev.bat` | Build from source and recreate the stack. Keeps all data. Optional argument: a property env file |
| `factory/reset.sh`, `factory/reset.bat` | **Destructive.** Deletes every volume (including the settings volume, so defaults are seeded again) and starts fresh from the pinned images, keeping `.env.local`. Asks you to type RESET first. With a property env file it builds from source instead |

## Telemetry flow

The server exports metrics, traces, and logs over OTLP to `otel-collector:4317`. The collector exposes metrics for Prometheus to scrape (`otel-collector:8889`), forwards traces to Tempo, and forwards logs to Loki. Grafana reads all three. The same server image points at the AWS-hosted equivalents in production.

## Common tasks

```bash
docker compose logs -f server            # follow server logs
./dev.sh                                 # rebuild from source after code changes
docker compose restart server            # restart one service
docker compose down                      # stop, keeping data
./update.sh                              # pull the pinned images and recreate, keeping data
./dev.sh <property folder>/deploy/local.env      # build from source for a property
./factory/reset.sh                       # wipe everything and start over (also takes a property env file)
```

The admin console's **Settings > Restart** button exits the server process with code 75; `restart: unless-stopped` relaunches it with the saved settings.

## Healthchecks

Every HTTP service has a healthcheck with `interval: 5s`, `retries: 2`, and a 2 second timeout, probing 127.0.0.1 inside the container. The server image (which also checks the website on 8080) and Grafana include curl and probe with it. The upstream Tempo, Loki, and Prometheus images ship wget but not curl, so those three probe with wget against the same endpoints. Mailpit has neither and runs its built-in `/mailpit readyz` probe; the collector has no healthcheck. PostgreSQL is not an HTTP service and uses `pg_isready` over TCP.

## Troubleshooting

- **Port already in use:** another process is bound to one of the ports above. Stop it, or set the matching `TK_*_PORT` variable (for example `TK_POSTGRES_PORT=15432`) in an env file.
- **Server keeps restarting:** run `docker compose logs server`. An invalid settings file or `property.json` stops startup with a message naming the problem. To restore the property's default settings, delete the settings volume: `docker compose down`, `docker volume rm trelliskit_server-config`, `docker compose up -d` (or run a factory reset).
- **Wrong property:** `docker compose -f compose.yaml -f compose.dev.yaml config | grep -A1 additional_contexts` shows the property folder in use with `dev.sh`. Remember that `--env-file` must be passed to every command for a non-default property.
- **No data in Grafana:** metrics arrive about 15 seconds after requests. Check that `otel-collector` is healthy and that Prometheus shows the `trelliskit` target as up at http://127.0.0.1:9090/targets.
- **Upgrading a running stack to RecallDB wiring:** run `./env-local.sh` (adds `TK_RECALLDB_ADMIN_API_KEY` and `TK_RECALLDB_TOKEN` to `.env.local`), then `docker compose up -d recalldb server` (or `./update.sh`). RecallDB must be recreated to pick up `recalldb/start.sh`; its data stays in the `recalldb` database.
- **RecallDB not reachable:** `docker compose logs recalldb` should show `listening on *:8600` and `recalldb-start: server credential present`. `listening on localhost:8600` means the container still runs the image's default entrypoint; recreate it as above.
- **Upgrading from a stack created before the multi-property milestone:** the database image changed to PostgreSQL with pgvector, the database roles and passwords are now created by the init script from `.env.local`, schema migration 6 adds `propertyid` everywhere, the sample property id changed, and every storage key now starts with the new opaque property id. Recreate the local stack with `factory/reset.sh` (existing local data is not migrated across the id change; there is no production data yet).

## Hosting the stack on one server

The same stack can run on a single Linux host behind a TLS-terminating load balancer. A property's own deployment folder (a `compose.yaml` derived from this one, pinned to the property's published image) carries these in its `.env` (no secrets) and its `.env.local` (secrets):

| Variable | Where | Meaning |
|---|---|---|
| `TK_PUBLIC_BIND` | property env | Host address of the two load-balancer-facing ports (website 8080, Grafana 3000). Default `127.0.0.1`; a hosted stack sets `0.0.0.0` and allows only the load balancer in its firewall or security group. Every other port stays on `127.0.0.1` |
| `TK_PUBLIC_WEB_URL` | property env | Public origin of the website (for example `https://community.example.test`): `web.publicBaseUrl` for links and emails, and the first CORS origin |
| `TK_GRAFANA_ROOT_URL`, `TK_GRAFANA_ANONYMOUS` | property env | Grafana's public URL, and `true` for read-only dashboards without signing in |
| `TK_BOOTSTRAP_ADMIN_EMAIL` | property env | First administrator, created on the first start of an empty database |
| `TK_BOOTSTRAP_ADMIN_PASSWORD`, `TK_ADMIN_TOKEN`, `TK_GRAFANA_ADMIN_PASSWORD` | `.env.local` | The first administrator's password, a random break-glass admin token (replaces the token in the property's settings file), and Grafana's admin password |

Load balancer targets: port 8080 on the server container (website, admin console, API, and WebSockets at `/api/v1/ws`; health check `GET /healthz`) and port 3000 (Grafana; `GET /api/health`). Use a load balancer with WebSocket support and an idle timeout of at least 120 seconds. The website passes the load balancer's `X-Forwarded-Proto` through to the backend, so the server sees `https`.

