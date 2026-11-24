#!/bin/sh
# TrellisKit server entrypoint (POSIX sh).
#
# The writable settings file lives on a volume at /app/config. On first start (the file does not exist yet), it is
# seeded from the property's defaults at $TK_PROPERTY_DIR/server/settings.json. An existing file is never
# overwritten, so settings saved from the admin console survive restarts and image rebuilds. Deleting the volume
# (factory reset) makes the next start seed the defaults again. A property without server/settings.json starts
# with the server's built-in defaults, which the server writes on first boot.
set -eu

SETTINGS_FILE="${TK_SETTINGS_FILE:-/app/config/trelliskit.json}"
DEFAULTS_FILE="${TK_PROPERTY_DIR:-/app/property}/server/settings.json"

if [ ! -f "$SETTINGS_FILE" ]; then
  mkdir -p "$(dirname "$SETTINGS_FILE")"
  if [ -f "$DEFAULTS_FILE" ]; then
    cp "$DEFAULTS_FILE" "$SETTINGS_FILE.seed"
    mv "$SETTINGS_FILE.seed" "$SETTINGS_FILE"
    echo "trelliskit-entrypoint: seeded $SETTINGS_FILE from $DEFAULTS_FILE"
  else
    echo "trelliskit-entrypoint: no $DEFAULTS_FILE; the server will create $SETTINGS_FILE with built-in defaults"
  fi
fi

# Live updates signing keys (local development). When TK_UPDATES_KEYS_DIR (the read-only mount of
# ~/.trelliskit/keys/<property slug>/) holds the private keys written by the mobile updates-certificate script, point
# the server at them. Each variable is set only when its file exists and the variable is not already set, so a
# stack without keys starts normally and an explicit TK_UPDATES_SIGNING_KEY(_FILE) still wins.
KEYS_DIR="${TK_UPDATES_KEYS_DIR:-}"
if [ -n "$KEYS_DIR" ] && [ -d "$KEYS_DIR" ]; then
  if [ -z "${TK_UPDATES_SIGNING_KEY:-}" ] && [ -z "${TK_UPDATES_SIGNING_KEY_FILE:-}" ] && [ -r "$KEYS_DIR/updates-private-key.pem" ]; then
    export TK_UPDATES_SIGNING_KEY_FILE="$KEYS_DIR/updates-private-key.pem"
    echo "trelliskit-entrypoint: updates signing key file $TK_UPDATES_SIGNING_KEY_FILE"
  fi
  if [ -z "${TK_UPDATES_STANDBY_SIGNING_KEY:-}" ] && [ -z "${TK_UPDATES_STANDBY_SIGNING_KEY_FILE:-}" ] && [ -r "$KEYS_DIR/updates-standby-private-key.pem" ]; then
    export TK_UPDATES_STANDBY_SIGNING_KEY_FILE="$KEYS_DIR/updates-standby-private-key.pem"
    echo "trelliskit-entrypoint: updates standby signing key file $TK_UPDATES_STANDBY_SIGNING_KEY_FILE"
  fi
fi

# Combined image (TK_WWW_ENABLED=true): render the website's nginx template (backend origin and embed frame
# ancestors) and start nginx in the background before the server. nginx serves the website on port 8080 and proxies
# /api to the server.
if [ "${TK_WWW_ENABLED:-false}" = "true" ] && [ -f /etc/nginx/templates/default.conf.template ]; then
  envsubst '${TK_WWW_BACKEND} ${TK_EMBED_FRAME_ANCESTORS}' < /etc/nginx/templates/default.conf.template > /etc/nginx/conf.d/default.conf
  nginx
  echo "trelliskit-entrypoint: website on port 8080 (backend $TK_WWW_BACKEND)"
fi

exec "$@"
