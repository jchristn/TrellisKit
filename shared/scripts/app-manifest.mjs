/**
 * Build the fallback content manifest a web or mobile build bundles for the first launch offline
 * (docs/LIVE_UPDATES_CONTRACT.md section 11.3). The published content comes from, in order:
 *
 * - `TK_APP_MANIFEST_FILE`: a JSON file holding an anonymous `GET /api/v1/app/manifest` body (for example saved by a
 *   release pipeline), or
 * - `TK_APP_MANIFEST_URL`: the property's API origin; the anonymous manifest is fetched for the client (5 second
 *   timeout).
 *
 * Without either, or when the source cannot be read, the empty fallback is used, so a build never fails or blocks on the
 * network. The result is reduced by `fallbackManifestOf` (shared/src/appContent.js).
 *
 * Usage: node shared/scripts/app-manifest.mjs --client web|mobile [--out <file>]
 * (without --out the manifest is printed to stdout).
 */

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { EMPTY_FALLBACK_MANIFEST, fallbackManifestOf } from '../src/appContent.js';

/** How long the manifest fetch may take before the empty fallback is used. */
export const FETCH_TIMEOUT_MS = 5000;

/**
 * The fallback manifest for a client.
 * @param {{ client?: 'web'|'mobile', env?: Record<string, string|undefined>, fetchImpl?: typeof fetch, warn?: (message: string) => void }} [options]
 * @returns {Promise<object>}
 */
export async function buildFallbackManifest({ client = 'web', env = process.env, fetchImpl = globalThis.fetch, warn = (m) => process.stderr.write(m + '\n') } = {}) {
  const file = (env.TK_APP_MANIFEST_FILE || '').trim();
  const url = (env.TK_APP_MANIFEST_URL || '').trim();
  try {
    if (file) return fallbackManifestOf(JSON.parse(readFileSync(resolve(file), 'utf8')));
    if (url && typeof fetchImpl === 'function') {
      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), FETCH_TIMEOUT_MS);
      try {
        const response = await fetchImpl(url.replace(/\/+$/, '') + '/api/v1/app/manifest', {
          headers: { Accept: 'application/json', 'X-Client': client, 'X-Client-Platform': client === 'web' ? 'web' : 'ios' },
          signal: controller.signal
        });
        if (!response.ok) throw new Error('HTTP ' + response.status);
        return fallbackManifestOf(await response.json());
      } finally {
        clearTimeout(timer);
      }
    }
  } catch (error) {
    warn('[app-manifest] using the empty fallback manifest: ' + (error && error.message ? error.message : String(error)));
  }
  return { ...EMPTY_FALLBACK_MANIFEST };
}

function argument(argv, name) {
  const index = argv.indexOf(name);
  return index >= 0 && index + 1 < argv.length ? argv[index + 1] : null;
}

async function main(argv) {
  const client = argument(argv, '--client') === 'mobile' ? 'mobile' : 'web';
  const out = argument(argv, '--out');
  const manifest = await buildFallbackManifest({ client });
  const text = JSON.stringify(manifest, null, 2) + '\n';
  if (!out) {
    process.stdout.write(text);
    return;
  }
  mkdirSync(dirname(resolve(out)), { recursive: true });
  writeFileSync(resolve(out), text);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main(process.argv.slice(2)).catch((error) => {
    process.stderr.write(String(error && error.stack ? error.stack : error) + '\n');
    process.exit(1);
  });
}
