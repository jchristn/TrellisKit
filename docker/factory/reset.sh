#!/usr/bin/env bash
# ============================================================================
# TrellisKit factory reset: restores the Docker deployment to factory defaults.
# DESTRUCTIVE: stops the stack and deletes every named volume (database, server settings, uploads, traces, logs,
# metrics, Grafana state), then starts a fresh stack. The server settings volume is seeded again from the property's
# server/settings.json on first start. docker/.env.local (secrets) is kept, so the database passwords and the first
# administrator's password stay the same; delete it before running this to rotate them.
#
#   ./factory/reset.sh                                        # published images (compose.yaml)
#   ./factory/reset.sh <property folder>/deploy/local.env    # build from source for a property (adds compose.dev.yaml)
# ============================================================================
set -euo pipefail
ENV_ARGS=(--env-file .env)
FILES=(-f compose.yaml)
BUILD=()
if [ "$#" -ge 1 ]; then
  ENV_ARGS+=(--env-file "$(cd "$(dirname "$1")" && pwd)/$(basename "$1")")
  FILES+=(-f compose.dev.yaml)
  BUILD=(--build)
fi
ENV_ARGS+=(--env-file .env.local)
cd "$(dirname "$0")/.."
echo "========================================="
echo "  TrellisKit Factory Reset"
echo "========================================="
echo
echo "WARNING: this deletes ALL data in this stack: members, content, uploads, settings, logs, metrics, and traces."
echo
read -r -p "Type RESET to confirm: " CONFIRM
if [ "${CONFIRM}" != "RESET" ]; then
  echo "Reset cancelled."
  exit 1
fi
./env-local.sh
echo "[1/2] Stopping containers and removing volumes..."
docker compose "${FILES[@]}" "${ENV_ARGS[@]}" down --volumes --remove-orphans
echo "[2/2] Starting a fresh stack..."
if [ "${#BUILD[@]}" -eq 0 ]; then docker compose "${FILES[@]}" "${ENV_ARGS[@]}" pull; fi
docker compose "${FILES[@]}" "${ENV_ARGS[@]}" up -d ${BUILD[@]+"${BUILD[@]}"}
docker ps -a
echo
echo "Factory reset complete. Website: http://127.0.0.1:${TK_WWW_PORT:-8080}"
