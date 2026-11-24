import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import { useLocation } from 'react-router-dom';
import { BANNER_DISMISSALS_STORAGE_KEY, appContentHeaders, assetsToEvict, createAppContentStore, dismissBanner, subscribeRemoteStrings } from '@trelliskit/shared';
import { CLIENT_HEADERS, useAuth } from './AuthContext.jsx';
import { setFeatureDisabledStore } from '../utils/appContentErrors.js';
// The published content manifest inlined at build time (section 11.3): the first visit offline, or before the first
// fetch answers, starts from it instead of empty. A cached manifest in localStorage always wins.
import fallbackManifest from 'virtual:trelliskit/app-manifest';

/** localStorage key of the anonymous visitor id sent as `X-TK-Visitor-Id` (stable rollout buckets for signed-out visitors). */
export const VISITOR_ID_KEY = 'tk.visitorId';

/** Cache API cache name of remote asset files. */
export const APP_CONTENT_CACHE = 'tk-app-content';

const EMPTY_STATE = Object.freeze({ contentVersion: null, flags: {}, killSwitches: {}, strings: {}, banners: [], assets: {}, binary: null, fetchedAt: 0 });

function randomId() {
  try {
    if (typeof crypto !== 'undefined' && crypto.randomUUID) return crypto.randomUUID();
  } catch {
    // Fall through to the non-cryptographic id; it only spreads rollout buckets.
  }
  return 'v' + Math.random().toString(36).slice(2) + Date.now().toString(36);
}

/** The visitor id kept in localStorage; a fresh random id every call when storage is unavailable. */
export function visitorId() {
  try {
    let value = window.localStorage.getItem(VISITOR_ID_KEY);
    if (!value) {
      value = randomId();
      window.localStorage.setItem(VISITOR_ID_KEY, value);
    }
    return value;
  } catch {
    return randomId();
  }
}

/** localStorage wrapper for the store (every access in try and catch). */
export const browserStorage = Object.freeze({
  getItem(key) {
    try {
      return window.localStorage.getItem(key);
    } catch {
      return null;
    }
  },
  setItem(key, value) {
    try {
      window.localStorage.setItem(key, value);
    } catch {
      // Storage full or blocked: the in-memory state still applies.
    }
  }
});

function readDismissals() {
  try {
    const parsed = JSON.parse(window.localStorage.getItem(BANNER_DISMISSALS_STORAGE_KEY) || '{}');
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : {};
  } catch {
    return {};
  }
}

function writeDismissals(map) {
  try {
    window.localStorage.setItem(BANNER_DISMISSALS_STORAGE_KEY, JSON.stringify(map));
  } catch {
    // The dismissal then lasts for this page view only.
  }
}

/** The hash a content asset URL ends with (`.../content-assets/{propertyId}/{sha256}`). */
export function assetUrlHash(url) {
  const path = String(url || '').split('?')[0].split('#')[0];
  return path.slice(path.lastIndexOf('/') + 1);
}

/** Delete cached asset files that no published asset references any more. Never throws. */
export async function evictRemoteAssets(assets) {
  if (typeof caches === 'undefined') return [];
  try {
    const cache = await caches.open(APP_CONTENT_CACHE);
    const requests = await cache.keys();
    const byHash = new Map(requests.map((request) => [assetUrlHash(request.url), request]));
    const evict = assetsToEvict(assets, byHash.keys());
    for (const hash of evict) await cache.delete(byHash.get(hash));
    return evict;
  } catch {
    return [];
  }
}

// Strings revision: bumps only when the override map really changes, so the app shell re-renders
// on new overrides (section 11.3: strings apply on the next render) and not on every manifest check.
let stringsRevision = 0;
let stringsSignature = '{}';
let revisionStore = null;
const revisionListeners = new Set();
subscribeRemoteStrings(() => {
  const next = JSON.stringify((revisionStore && revisionStore.getState().strings) || {});
  if (next === stringsSignature) return;
  stringsSignature = next;
  stringsRevision += 1;
  for (const listener of revisionListeners) listener();
});
function subscribeRevision(listener) {
  revisionListeners.add(listener);
  return () => revisionListeners.delete(listener);
}
const readRevision = () => stringsRevision;

/** Re-render the calling component (the app shell) whenever the remote string overrides change. */
export function useRemoteStringsRevision() {
  return useSyncExternalStore(subscribeRevision, readRevision, readRevision);
}

const AppContentContext = createContext(null);

const NOOP_CONTEXT = Object.freeze({ store: null, state: EMPTY_STATE, dismissed: {}, dismiss: () => {}, refresh: () => Promise.resolve(EMPTY_STATE) });

/**
 * The website runtime of the content and configuration manifest (docs/LIVE_UPDATES_CONTRACT.md
 * section 11.3, Web column): one app content store fed by `GET /api/v1/app/manifest` through the
 * member ApiClient (so targeting sees the signed-in caller), cached in localStorage, refreshed on
 * load and tab focus (throttled to `checkIntervalMinutes`) and forced on sign-in, sign-out, and
 * membership change. Exposes flags, kill switches, banners with dismissals, and assets. The admin and
 * platform consoles make no manifest calls (they show no member content), so their pages stay
 * limited to their own API calls.
 *
 * @param {{ children: React.ReactNode, store?: object }} props `store` replaces the created store (tests).
 */
export function AppContentProvider({ children, store: storeOverride = null }) {
  const auth = useAuth();
  const client = auth.client;
  const [store] = useState(() => storeOverride || createAppContentStore({
    client: 'web',
    storage: browserStorage,
    initial: fallbackManifest,
    fetchManifest: (since) => client.getAppManifest({ since: since === null ? undefined : since, headers: contentHeaders() }),
    enumerate: (kind, query) => {
      const headers = contentHeaders();
      if (kind === 'strings') return client.enumerateAppStrings(query, { headers });
      if (kind === 'banners') return client.enumerateAppBanners(query, { headers });
      return client.enumerateAppAssets(query, { headers });
    }
  }));
  const state = useSyncExternalStore(store.subscribe, store.getState, store.getState);
  const [dismissed, setDismissed] = useState(() => (typeof window === 'undefined' ? {} : readDismissals()));
  const consoleRoute = /^\/(admin|platform)(\/|$)/.test(useLocation().pathname);
  const consoleRef = useRef(consoleRoute);
  useEffect(() => {
    consoleRef.current = consoleRoute;
  }, [consoleRoute]);

  useEffect(() => {
    revisionStore = store;
    const unregister = setFeatureDisabledStore(store);
    return () => {
      unregister();
      if (revisionStore === store) revisionStore = null;
    };
  }, [store]);

  // Load and account events: the first settled auth state fetches (throttled); every later change
  // of the signed-in account or its membership forces a refetch because evaluation changes.
  const me = auth.me;
  const accountKey = auth.status === 'restoring' ? null : [auth.status, me ? me.id : '', me && me.membership && me.membership.isActive ? 'm' : '', me && me.accessTier ? me.accessTier : ''].join('|');
  const [lastAccountKey, setLastAccountKey] = useState(null);
  useEffect(() => {
    if (consoleRoute || accountKey === null || accountKey === lastAccountKey) return;
    const first = lastAccountKey === null;
    setLastAccountKey(accountKey);
    store.refresh({ force: !first });
  }, [accountKey, lastAccountKey, store, consoleRoute]);

  useEffect(() => {
    if (typeof document === 'undefined') return undefined;
    const onVisible = () => {
      if (document.visibilityState === 'visible' && !consoleRef.current) store.refresh();
    };
    document.addEventListener('visibilitychange', onVisible);
    return () => document.removeEventListener('visibilitychange', onVisible);
  }, [store]);

  // Remote asset cache: drop files no published asset references (only after a real fetch).
  useEffect(() => {
    if (state.fetchedAt) evictRemoteAssets(state.assets);
  }, [state.assets, state.fetchedAt]);

  const dismiss = useCallback((banner) => {
    setDismissed((current) => {
      const next = dismissBanner(current, banner);
      writeDismissals(next);
      return next;
    });
  }, []);
  const refresh = useCallback((options) => store.refresh(options), [store]);

  const value = useMemo(() => ({ store, state, dismissed, dismiss, refresh }), [store, state, dismissed, dismiss, refresh]);
  return <AppContentContext.Provider value={value}>{children}</AppContentContext.Provider>;
}

function contentHeaders() {
  return appContentHeaders({ client: 'web', platform: 'web', appVersion: CLIENT_HEADERS['X-Client-Version'], visitorId: visitorId() });
}

/** The app content state (`{ store, state, dismissed, dismiss, refresh }`); an empty, inert value outside the provider. */
export function useAppContent() {
  return useContext(AppContentContext) || NOOP_CONTEXT;
}

/** A resolved feature flag value from the manifest, or `fallback` when the flag is unknown. */
export function useFlag(key, fallback = null) {
  const { state } = useAppContent();
  return state.flags && Object.prototype.hasOwnProperty.call(state.flags, key) ? state.flags[key] : fallback;
}

/** Whether a feature's kill switch is on for this caller, with its message: `{ killed, message }`. */
export function useKillSwitch(featureKey) {
  const { state } = useAppContent();
  const entry = state.killSwitches ? state.killSwitches[featureKey] : null;
  return { killed: Boolean(entry && entry.killed), message: (entry && entry.message) || null };
}
