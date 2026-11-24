#!/usr/bin/env bash
# Pull the pinned published images (compose.yaml) and start or recreate the stack. Non-destructive: named volumes
# are preserved. For a fresh start use factory/reset.sh; to build from source use dev.sh.
#
#   ./update.sh                                   # docker/.env
#   ./update.sh <env file>                        # an extra env file (path from your shell), for example image overrides
set -euo pipefail
ENV_ARGS=(--env-file .env)
if [ "$#" -ge 1 ]; then
  ENV_ARGS+=(--env-file "$(cd "$(dirname "$1")" && pwd)/$(basename "$1")")
fi
ENV_ARGS+=(--env-file .env.local)
cd "$(dirname "$0")"
./env-local.sh
docker compose "${ENV_ARGS[@]}" pull
docker compose "${ENV_ARGS[@]}" up -d --remove-orphans
docker ps -a
