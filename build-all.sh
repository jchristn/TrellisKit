#!/usr/bin/env bash
# Build and push both TrellisKit images (server and website) with the same tags.
#   ./build-all.sh            # latest
#   ./build-all.sh v1.0.0     # v1.0.0 and latest
set -euo pipefail
cd "$(dirname "$0")"
./build-server.sh "${1:-latest}"
./build-www.sh "${1:-latest}"
