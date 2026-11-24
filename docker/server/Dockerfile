# TrellisKit backend server.
# Build context: the engine root (see docker/compose.yaml). The property is a separate named build context
# called "property"; without one, the neutral sample property (samples/example) is baked in.
#   docker build -f docker/server/Dockerfile --build-context property=<property folder> -t trelliskit/server .
# Targets: runtime (default, production on ECS), local (adds ffmpeg), and combined (local plus the website served by
# nginx on port 8080, which proxies /api to the server on 127.0.0.1:8000; the published Docker images use this one).

# ---- Property (overridden by --build-context property=<folder> or compose additional_contexts) ----
FROM scratch AS property
COPY samples/example/ /

# ---- Website build (combined target only) ----
FROM node:22-alpine AS web
WORKDIR /repo
COPY package.json package-lock.json ./
COPY shared/package.json shared/package.json
COPY www/package.json www/package.json
RUN npm ci --no-audit --no-fund
COPY shared/ shared/
COPY www/ www/
COPY --from=property / /property/
ENV TK_PROPERTY_DIR=/property
RUN npm run build --workspace www

# ---- CLI agent harnesses (worker targets only) ----
# Pinned npm releases from the reviewed upstream snapshot. Bump them deliberately: a harness's flags and
# event stream are parsed by PolyPrompt, so a new CLI version is tested before it reaches a worker.
FROM node:22-bookworm-slim AS harness-clis
ARG CLAUDE_CODE_VERSION=2.1.330
ARG CODEX_VERSION=0.171.0
ARG GEMINI_CLI_VERSION=0.70.0
RUN npm install --global --prefix /opt/harness --no-audit --no-fund \
        "@anthropic-ai/claude-code@${CLAUDE_CODE_VERSION}" \
        "@openai/codex@${CODEX_VERSION}" \
        "@google/gemini-cli@${GEMINI_CLI_VERSION}" \
    && npm cache clean --force

# ---- Mux harness (optional; worker targets only) ----
# Mux.Cli is not published on nuget.org yet, so it is skipped unless MUX_CLI_VERSION names a version and
# MUX_NUGET_SOURCE (default nuget.org) serves it.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS harness-mux
ARG MUX_CLI_VERSION=
ARG MUX_NUGET_SOURCE=https://api.nuget.org/v3/index.json
RUN mkdir -p /opt/mux \
    && if [ -n "${MUX_CLI_VERSION}" ]; then \
        dotnet tool install Mux.Cli --tool-path /opt/mux --version "${MUX_CLI_VERSION}" --add-source "${MUX_NUGET_SOURCE}"; \
    fi

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/src/Directory.Build.props ./
COPY backend/src/TrellisKit.Core/TrellisKit.Core.csproj TrellisKit.Core/
COPY backend/src/TrellisKit.Server/TrellisKit.Server.csproj TrellisKit.Server/
RUN dotnet restore TrellisKit.Server/TrellisKit.Server.csproj
COPY backend/src/TrellisKit.Core/ TrellisKit.Core/
COPY backend/src/TrellisKit.Server/ TrellisKit.Server/
RUN dotnet publish TrellisKit.Server/TrellisKit.Server.csproj -c Release -o /app --no-restore

# ---- Runtime base (shared by the production and local targets) ----
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime-base
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
# The property is baked in so the image runs on its own (for example on ECS). Docker Compose also mounts the
# property folder read-only over /app/property, so local edits apply without a rebuild.
COPY --from=property / /app/property/
COPY --chmod=755 docker/server/entrypoint.sh /usr/local/bin/trelliskit-entrypoint.sh
ENV TK_SETTINGS_FILE=/app/config/trelliskit.json \
    TK_PROPERTY_DIR=/app/property
EXPOSE 8000
HEALTHCHECK --interval=5s --timeout=2s --retries=2 --start-period=30s \
    CMD curl -fsS http://127.0.0.1:8000/api/v1/health || exit 1
# The entrypoint seeds /app/config/trelliskit.json from the property's server/settings.json on first start.
ENTRYPOINT ["/usr/local/bin/trelliskit-entrypoint.sh"]
CMD ["dotnet", "TrellisKit.Server.dll"]

# ---- Local target (docker/compose.yaml): adds ffmpeg for media.videoProcessor = Ffmpeg ----
# Production never transcodes in the server (MediaConvert does), so ffmpeg is only in this target.
#   docker build -f docker/server/Dockerfile --target local -t trelliskit/server:local .
FROM runtime-base AS local
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*
ENV TK_FFMPEG_PATH=/usr/bin/ffmpeg

# ---- Combined target: the local target plus the website on port 8080 (nginx), in one container ----
# The entrypoint renders the nginx template, starts nginx in the background, and then runs the server. The website
# proxies /api (and /api/v1/ws) to the server on 127.0.0.1:8000.
#   docker build -f docker/server/Dockerfile --target combined --build-context property=<folder> -t <image> .
FROM local AS combined
RUN apt-get update \
    && apt-get install -y --no-install-recommends nginx gettext-base \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/* /etc/nginx/sites-enabled/* /etc/nginx/conf.d/* \
    && ln -sf /dev/stdout /var/log/nginx/access.log \
    && ln -sf /dev/stderr /var/log/nginx/error.log
COPY docker/www/nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=web /repo/www/dist /usr/share/nginx/html
ENV TK_WWW_ENABLED=true \
    TK_WWW_BACKEND=http://127.0.0.1:8000 \
    TK_EMBED_FRAME_ANCESTORS="'self'"
EXPOSE 8080
HEALTHCHECK --interval=5s --timeout=2s --retries=2 --start-period=30s \
    CMD curl -fsS http://127.0.0.1:8000/api/v1/health && curl -fsS http://127.0.0.1:8080/healthz || exit 1

# ---- Production worker target: runtime plus the CLI agent harnesses ----
# Runs the same server with jobs.runHarnesses on (TK_JOBS_RUN_HARNESSES=true); harness calls happen only inside job
# items. Harness credentials are vendor API keys in the environment (ANTHROPIC_API_KEY, CODEX_API_KEY for codex exec,
# GEMINI_API_KEY), or credentials explicitly configured in a harness profile.
#   docker build -f docker/server/Dockerfile --target worker-runtime --build-context property=<folder> -t <image> .
FROM runtime-base AS worker-runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends ripgrep \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*
COPY --from=harness-clis /usr/local/bin/node /usr/local/bin/node
COPY --from=harness-clis /opt/harness /opt/harness
COPY --from=harness-mux /opt/mux /opt/mux
ENV PATH=/opt/harness/bin:/opt/mux:$PATH \
    TK_JOBS_RUN_HARNESSES=true \
    DISABLE_AUTOUPDATER=1
RUN node --version && claude --version && codex --version && gemini --version

# ---- Local worker target (docker/compose.yaml): the local target plus the CLI agent harnesses ----
FROM local AS worker
RUN apt-get update \
    && apt-get install -y --no-install-recommends ripgrep \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*
COPY --from=harness-clis /usr/local/bin/node /usr/local/bin/node
COPY --from=harness-clis /opt/harness /opt/harness
COPY --from=harness-mux /opt/mux /opt/mux
ENV PATH=/opt/harness/bin:/opt/mux:$PATH \
    TK_JOBS_RUN_HARNESSES=true \
    DISABLE_AUTOUPDATER=1
RUN node --version && claude --version && codex --version && gemini --version

# ---- Production target (the default, last stage): no ffmpeg ----
FROM runtime-base AS runtime
