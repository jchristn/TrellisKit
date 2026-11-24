#!/usr/bin/env bash
# Build the server and website from this engine's source for a property folder (compose.yaml plus compose.dev.yaml)
# and recreate the stack. Non-destructive: named volumes are preserved.
#
#   ./dev.sh                                                # the neutral sample property (docker/.env)
#   ./dev.sh <property folder>/deploy/local.env            # a property's env file (path from your shell)
set -euo pipefail
ENV_ARGS=(--env-file .env)
if [ "$#" -ge 1 ]; then
  ENV_ARGS+=(--env-file "$(cd "$(dirname "$1")" && pwd)/$(basename "$1")")
fi
ENV_ARGS+=(--env-file .env.local)
FILES=(-f compose.yaml -f compose.dev.yaml)
cd "$(dirname "$0")"
./env-local.sh
docker compose "${FILES[@]}" "${ENV_ARGS[@]}" pull --ignore-buildable
docker compose "${FILES[@]}" "${ENV_ARGS[@]}" down --remove-orphans
docker compose "${FILES[@]}" "${ENV_ARGS[@]}" up -d --build
docker ps -a
