import { createContext, useCallback, useContext, useEffect, useMemo, useReducer, useRef, useState } from 'react';
import { AppState, useColorScheme } from 'react-native';
import { BANNER_DISMISSALS_STORAGE_KEY, dismissBanner, featureDisabledOf, findRemoteAsset, subscribeRemoteStrings } from '@trelliskit/shared';
import { getApiClient } from '../api/client';
import { useOptionalAuth } from '../auth/AuthProvider';
import { createAssetCache, remoteAssetUri } from './assetCache';
import { createMobileAppContentStore } from './manifestClient';
import { appContentStorage } from './storage';

const AppContentContext = createContext(null);

/** How often banner schedules are re-evaluated while the app runs. */
export const BannerClockMs = 60000;

/**
 * What the manifest is evaluated for: anonymous, or a member and whether the membership is
 * active. A change means targeting changed (sign-in, sign-out, membership change), so the
 * manifest is refetched without the throttle. Null while the session is being restored.
 */
export function accountKeyOf(auth) {
  if (!auth || !auth.session) return 'anonymous';
  const { status, user } = auth.session;
  if (status === 'loading') return null;
  if (status !== 'signedIn' || !user) return 'anonymous';
  return 'member|' + user.id + '|' + Boolean(user.membership && user.membership.isActive);
}

/**
 * The content and configuration manifest for the app (docs/LIVE_UPDATES_CONTRACT.md section 11.3):
 * one store (client `mobile`) cached in a JSON file, fetched once the session is known at launch,
 * on return to the foreground (throttled to the manifest's `checkIntervalMinutes` by the store),
 * and always on sign-in, sign-out, and membership change. A `503 FeatureDisabled` from any API
 * call marks that feature killed at once. Remote string overrides re-render the consumers of this
 * context and apply to every other screen on its next render. Published assets are cached by hash.
 * Mount inside AuthProvider.
 * @param {object} props
 * @param {object} [props.store] Injected store (tests).
 * @param {object} [props.api] API client (default: the session's, else the shared client).
 * @param {object} [props.storage] Storage for the dismissals (and the default store).
 * @param {object} [props.assetCache] Injected asset cache (tests).
 */
export function AppContentProvider({ children, store: injected, api: apiProp, storage = appContentStorage, assetCache }) {
  const auth = useOptionalAuth();
  const [api] = useState(() => apiProp || (auth && auth.api) || getApiClient());
  const [store] = useState(() => injected || createMobileAppContentStore({ api, storage }));
  const [cache] = useState(() => assetCache || createAssetCache({ storage }));
  const [state, setState] = useState(() => store.getState());
  const [stringsVersion, bumpStrings] = useReducer((n) => n + 1, 0);
  const [dismissed, setDismissed] = useState({});
  const [cachedHashes, setCachedHashes] = useState([]);
  const [now, setNow] = useState(() => Date.now());
  const lastAccount = useRef(undefined);
  const accountKey = accountKeyOf(auth);

  useEffect(
    () =>
      store.subscribe((next) => {
        setState(next);
        setNow(Date.now());
      }),
    [store]
  );
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), BannerClockMs);
    return () => clearInterval(timer);
  }, []);
  useEffect(() => subscribeRemoteStrings(bumpStrings), []);

  useEffect(() => {
    let alive = true;
    // The cached manifest applies at once, before any network request.
    store.load().then(() => alive && setState(store.getState())).catch(() => {});
    Promise.resolve()
      .then(() => storage.getItem(BANNER_DISMISSALS_STORAGE_KEY))
      .then((text) => {
        const parsed = text ? JSON.parse(text) : null;
        if (alive && parsed && typeof parsed === 'object' && !Array.isArray(parsed)) setDismissed((current) => ({ ...parsed, ...current }));
      })
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, [store, storage]);

  useEffect(() => {
    if (accountKey === null) return;
    const first = lastAccount.current === undefined;
    const changed = !first && lastAccount.current !== accountKey;
    lastAccount.current = accountKey;
    if (first || changed) store.refresh({ force: changed }).catch(() => {});
  }, [store, accountKey]);

  useEffect(() => {
    const sub = AppState.addEventListener('change', (next) => {
      if (next === 'active') store.refresh().catch(() => {});
    });
    return () => sub && typeof sub.remove === 'function' && sub.remove();
  }, [store]);

  useEffect(() => {
    if (!api || typeof api.onFeatureDisabled !== 'function') return undefined;
    return api.onFeatureDisabled((error) => {
      const disabled = featureDisabledOf(error);
      if (disabled && disabled.feature) store.noteFeatureDisabled(disabled.feature, disabled.message);
    });
  }, [api, store]);

  useEffect(() => {
    let alive = true;
    cache.sync(state.assets).then((hashes) => {
      if (alive) setCachedHashes(hashes);
    });
    return () => {
      alive = false;
    };
  }, [cache, state.assets]);

  const dismiss = useCallback(
    (banner) => {
      setDismissed((current) => {
        const next = dismissBanner(current, banner);
        Promise.resolve()
          .then(() => storage.setItem(BANNER_DISMISSALS_STORAGE_KEY, JSON.stringify(next)))
          .catch(() => {});
        return next;
      });
    },
    [storage]
  );

  const cached = useMemo(() => new Set(cachedHashes), [cachedHashes]);
  const assetUri = useCallback((asset, options = {}) => remoteAssetUri(asset, { ...options, cached, uriFor: cache.uriFor }), [cache, cached]);

  const value = useMemo(() => ({ store, state, dismissed, dismiss, assetUri, stringsVersion, now }), [store, state, dismissed, dismiss, assetUri, stringsVersion, now]);
  return <AppContentContext.Provider value={value}>{children}</AppContentContext.Provider>;
}

/**
 * The app content context: `{ store, state, dismissed, dismiss, assetUri, stringsVersion, now }`
 * (`now` is the banner schedule clock), or null outside AppContentProvider.
 */
export function useAppContent() {
  return useContext(AppContentContext);
}

/**
 * A resolved feature flag, or `fallback` when the flag is unknown (or outside the provider).
 * Re-renders when the manifest changes.
 */
export function useFlag(key, fallback = null) {
  const content = useAppContent();
  if (!content) return fallback;
  const flags = content.state.flags || {};
  return Object.prototype.hasOwnProperty.call(flags, key) ? flags[key] : fallback;
}

/**
 * A feature's kill switch for this caller: `{ killed, message }` (section 13.2).
 * @param {string} featureKey Engine feature key, for example `feedComposer`.
 */
export function useKillSwitch(featureKey) {
  const content = useAppContent();
  const entry = content && content.state.killSwitches ? content.state.killSwitches[featureKey] : null;
  return { killed: Boolean(entry && entry.killed), message: (entry && entry.message) || null };
}

/**
 * The URI of a published remote asset by key: the cached file once downloaded, the network URL
 * until then, or null when no published asset has the key. `dark` picks the dark variant when
 * there is one (default: the system color scheme).
 * @param {string} key
 * @param {{ dark?: boolean }} [options]
 * @returns {string|null}
 */
export function useRemoteAsset(key, { dark } = {}) {
  const content = useAppContent();
  const scheme = useColorScheme();
  if (!content) return null;
  const asset = findRemoteAsset(content.state.assets, key);
  return content.assetUri(asset, { dark: dark === undefined ? scheme === 'dark' : Boolean(dark) });
}
