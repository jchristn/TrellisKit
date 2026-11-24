#!/usr/bin/env bash
# Create docker/.env.local with random local passwords when it does not exist (never committed; see .gitignore).
# Existing files are kept, because the PostgreSQL volume was initialized with those passwords. To rotate them,
# delete .env.local together with the stack's volumes (factory/reset.sh). Keys added after a file was created
# (the RecallDB admin key and server token) are appended to an existing file without touching the others.
set -euo pipefail
cd "$(dirname "$0")"
secret() { openssl rand -hex 24; }
umask 077
if [ -f .env.local ]; then
  for key in TK_RECALLDB_ADMIN_API_KEY TK_RECALLDB_TOKEN; do
    if ! grep -q "^${key}=" .env.local; then
      echo "${key}=$(secret)" >> .env.local
      echo "Added ${key} to docker/.env.local."
    fi
  done
  exit 0
fi
cat > .env.local <<ENV
# Local stack secrets, generated $(date -u +%Y-%m-%dT%H:%M:%SZ). Never commit this file.
TK_POSTGRES_PASSWORD=$(secret)
TK_DB_PASSWORD=$(secret)
TK_RECALLDB_DB_PASSWORD=$(secret)
TK_RECALLDB_ADMIN_API_KEY=$(secret)
TK_RECALLDB_TOKEN=$(secret)
TK_PLATFORM_BOOTSTRAP_ADMIN_PASSWORD=$(secret)
TK_SECRETS_ENCRYPTION_KEY=$(openssl rand -base64 32)
ENV
echo "Created docker/.env.local with random local passwords."
