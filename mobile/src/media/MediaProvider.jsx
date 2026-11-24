import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { AppState } from 'react-native';
import { media as mediaApi } from '../api/community';
import { useAuth } from '../auth/AuthProvider';
import { canEnterTabs } from '../auth/routing';

/** Refresh the grant when it expires within this many milliseconds (contract 4.7). */
export const GrantRefreshMarginMs = 10 * 60 * 1000;

const MediaContext = createContext(null);

/** `Cookie` header value for a grant: `name=value; name=value`. */
export function cookieHeader(grant) {
  const cookies = grant && Array.isArray(grant.cookies) ? grant.cookies : [];
  return cookies
    .filter((c) => c && c.name)
    .map((c) => c.name + '=' + (c.value || ''))
    .join('; ');
}

/** Whether a grant needs refreshing now. */
export function grantNeedsRefresh(grant, now = Date.now()) {
  if (!grant || !grant.expiresUtc) return true;
  const expires = Date.parse(grant.expiresUtc);
  return !Number.isFinite(expires) || expires - now < GrantRefreshMarginMs;
}

/**
 * Absolute URL for a media URL. Absolute URLs (production, `media.publicBaseUrl` set) pass
 * through; root-relative ones (local mode) resolve against the API origin, like avatars.
 */
export function resolveMediaUrl(api, url) {
  if (!url || typeof url !== 'string') return null;
  if (/^https?:\/\//i.test(url)) return url;
  if (url.startsWith('/')) return api.baseUrl + url;
  return null;
}

/**
 * Request headers for a media URL: the grant cookies as a `Cookie` header (contract 4.7, mobile
 * delivery). For URLs on the API origin (local mode) the bearer token is added too, which the
 * local file route also accepts; it is never sent to another host. Every media request also
 * carries the identification headers (X-TK-Property, X-TK-App, X-Client-Update-Id) so a
 * multi-property media route resolves the property.
 */
export function mediaHeaders(api, uri, grant) {
  const headers = typeof api.identityHeaders === 'function' ? api.identityHeaders() : {};
  const cookie = cookieHeader(grant);
  if (cookie) headers.Cookie = cookie;
  if (uri && api.accessToken && api.baseUrl && uri.startsWith(api.baseUrl + '/')) headers.Authorization = 'Bearer ' + api.accessToken;
  return headers;
}

/**
 * Keeps a media grant for the signed-in member: requested after sign-in, on foreground, and
 * whenever it is within 10 minutes of expiring (a timer). Cookies stay in memory only. Media
 * components read `source(url)` for `{ uri, headers }` and call `refreshAfterForbidden()` once
 * when a media request fails, then retry.
 */
export function MediaProvider({ children }) {
  const { api, session } = useAuth();
  const member = canEnterTabs(session);
  const userId = member ? session.user.id : null;
  const [grant, setGrant] = useState(null);
  const [version, setVersion] = useState(0);
  const inFlight = useRef(null);
  const grantRef = useRef(null);

  const requestGrant = useCallback(() => {
    if (!inFlight.current) {
      inFlight.current = mediaApi
        .grant(api)
        .then((next) => {
          grantRef.current = next;
          setGrant(next);
          setVersion((v) => v + 1);
          return next;
        })
        .catch(() => null)
        .finally(() => {
          inFlight.current = null;
        });
    }
    return inFlight.current;
  }, [api]);

  useEffect(() => {
    if (!userId) {
      grantRef.current = null;
      // Clearing the previous member's cookies on sign-out.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setGrant(null);
      return undefined;
    }
    requestGrant();
    const sub = AppState.addEventListener('change', (state) => {
      if (state === 'active' && grantNeedsRefresh(grantRef.current)) requestGrant();
    });
    return () => sub.remove();
  }, [userId, requestGrant]);

  useEffect(() => {
    if (!grant || !grant.expiresUtc) return undefined;
    const due = Date.parse(grant.expiresUtc) - GrantRefreshMarginMs - Date.now();
    if (!Number.isFinite(due)) return undefined;
    const timer = setTimeout(() => requestGrant(), Math.max(5000, Math.min(due, 2147483000)));
    return () => clearTimeout(timer);
  }, [grant, requestGrant]);

  const value = useMemo(
    () => ({
      grant,
      version,
      source(url) {
        const uri = resolveMediaUrl(api, url);
        return uri ? { uri, headers: mediaHeaders(api, uri, grant) } : null;
      },
      refreshAfterForbidden: requestGrant
    }),
    [api, grant, version, requestGrant]
  );

  return <MediaContext.Provider value={value}>{children}</MediaContext.Provider>;
}

/** Media grant context; a no-grant fallback outside the provider (tests rendering one component). */
export function useMedia() {
  const value = useContext(MediaContext);
  const { api } = useAuth();
  return (
    value || {
      grant: null,
      version: 0,
      source(url) {
        const uri = resolveMediaUrl(api, url);
        return uri ? { uri, headers: mediaHeaders(api, uri, null) } : null;
      },
      refreshAfterForbidden: async () => null
    }
  );
}
