import { useEffect } from 'react';
import { setMediaGrantRefresher } from '../utils/mediaGrant.js';

/** Refresh the grant when it is this close to expiring (contract section 4.7). */
export const GRANT_REFRESH_MARGIN_MS = 10 * 60 * 1000;
const RETRY_MS = 5 * 60 * 1000;
const STORAGE_PREFIX = 'tk.mediaGrant.';

function readStoredExpiry(cacheKey) {
  if (!cacheKey) return NaN;
  try {
    const value = window.localStorage.getItem(STORAGE_PREFIX + cacheKey);
    return value ? new Date(value).getTime() : NaN;
  } catch {
    return NaN;
  }
}

function writeStoredExpiry(cacheKey, expiresUtc) {
  if (!cacheKey) return;
  try {
    if (expiresUtc) window.localStorage.setItem(STORAGE_PREFIX + cacheKey, expiresUtc);
    else window.localStorage.removeItem(STORAGE_PREFIX + cacheKey);
  } catch {
    /* storage unavailable: every load asks for a grant */
  }
}

/**
 * Keep the media grant cookie fresh: call `POST /media/grant` on mount (sign-in and app load) and
 * again when `expiresUtc` is less than 10 minutes away. The response sets the cookies; the page
 * only needs the expiry. Failures retry after a few minutes. With a `cacheKey` (the signed-in
 * account), a grant that is still valid for longer than the margin is reused across page loads and
 * tabs instead of asking again, so reloads do not spend `rateLimits.mediaGrantsPerHour`; a 403 on
 * media still renews at once through `refreshMediaGrant()`.
 * @param {{ getMediaGrant: (signal?: AbortSignal) => Promise<{ expiresUtc: string }> }|null} client
 * @param {{ enabled?: boolean, cacheKey?: string|null }} [options]
 */
export function useMediaGrant(client, { enabled = true, cacheKey = null } = {}) {
  useEffect(() => {
    if (!enabled || !client || typeof client.getMediaGrant !== 'function') return undefined;
    let timer = null;
    let cancelled = false;
    const controller = new AbortController();
    const schedule = (ms) => {
      if (!cancelled) timer = setTimeout(run, Math.max(1000, ms));
    };
    async function run() {
      try {
        const grant = await client.getMediaGrant(controller.signal);
        const expires = grant && grant.expiresUtc ? new Date(grant.expiresUtc).getTime() : NaN;
        writeStoredExpiry(cacheKey, Number.isFinite(expires) ? grant.expiresUtc : null);
        schedule(Number.isFinite(expires) ? expires - Date.now() - GRANT_REFRESH_MARGIN_MS : RETRY_MS);
      } catch (err) {
        writeStoredExpiry(cacheKey, null);
        schedule(RETRY_MS);
        throw err;
      }
    }
    let inFlight = null;
    const refreshNow = () => {
      if (!inFlight) {
        if (timer) clearTimeout(timer);
        inFlight = run().finally(() => { inFlight = null; });
      }
      return inFlight;
    };
    const unregister = setMediaGrantRefresher(refreshNow);
    const stored = readStoredExpiry(cacheKey);
    if (Number.isFinite(stored) && stored - Date.now() > GRANT_REFRESH_MARGIN_MS) schedule(stored - Date.now() - GRANT_REFRESH_MARGIN_MS);
    else refreshNow().catch(() => {});
    return () => {
      unregister();
      cancelled = true;
      controller.abort();
      if (timer) clearTimeout(timer);
    };
  }, [client, enabled, cacheKey]);
}

export default useMediaGrant;
