import { collectEnumeration } from './enumeration.js';

/**
 * Client runtime of the content and configuration manifest (docs/LIVE_UPDATES_CONTRACT.md sections 11, 13.2, and 14):
 * a store that fetches `GET /api/v1/app/manifest?since=`, applies deltas (strings and assets) and complete sets
 * (flags, kill switches, banners), re-enumerates on `fullRefresh`, and persists the result; a strings overlay that
 * lets remote overrides replace keys of an app's strings module; and banner and asset helpers. Used by the website
 * and the mobile app. Nothing here throws on network or storage failures: the last good state stays in place.
 */

/** Storage key of the cached manifest state. */
export const APP_CONTENT_STORAGE_KEY = 'tk.appContent.v1';

/** Storage key of dismissed banners (`{ [bannerId]: contentHash }`). */
export const BANNER_DISMISSALS_STORAGE_KEY = 'tk.bannerDismissals.v1';

/** Default foreground refresh interval when the manifest does not say (minutes). */
export const DEFAULT_CHECK_INTERVAL_MINUTES = 15;

const overrides = new Map();
const overrideListeners = new Set();

/**
 * Replace the remote string overrides the overlays read (`{ key: value }`).
 * @param {Record<string, string>|null|undefined} map
 */
export function setRemoteStrings(map) {
  overrides.clear();
  for (const [key, value] of Object.entries(map || {})) if (typeof value === 'string') overrides.set(key, value);
  for (const listener of overrideListeners) {
    try {
      listener();
    } catch {
      // A listener failure never stops the others.
    }
  }
}

/**
 * The remote override for a catalog key, or undefined.
 * @param {string} key For example `web.common.retry`.
 * @returns {string|undefined}
 */
export function remoteStringValue(key) {
  return overrides.has(key) ? overrides.get(key) : undefined;
}

/**
 * Subscribe to override changes (for a re-render). Returns the unsubscribe function.
 * @param {() => void} listener
 */
export function subscribeRemoteStrings(listener) {
  overrideListeners.add(listener);
  return () => overrideListeners.delete(listener);
}

/**
 * A read-through view of a strings tree in which a string leaf at `a.b` is replaced by the remote override of
 * `<prefix>.a.b` (after `fill`, for example the property token substitution) when one is present. Functions,
 * arrays, and non-string values pass through. The original tree is never modified, so a removed override restores
 * the built-in text on the next read.
 * @template T
 * @param {T} tree The strings module object.
 * @param {string} prefix Catalog prefix (`web` or `mobile`).
 * @param {{ lookup?: (key: string) => (string|undefined|null), fill?: (value: string) => string }} [options]
 * @returns {T}
 */
export function remoteStringOverlay(tree, prefix, options = {}) {
  const lookup = options.lookup || remoteStringValue;
  const fill = options.fill || ((value) => value);
  const cache = new Map();
  function read(node, path, prop) {
    const value = node[prop];
    if (typeof prop !== 'string') return value;
    if (typeof value === 'string') {
      const override = lookup(path + '.' + prop);
      return override === undefined || override === null ? value : fill(override);
    }
    if (value && typeof value === 'object' && !Array.isArray(value)) return view(value, path + '.' + prop);
    return value;
  }
  function view(node, path) {
    const cached = cache.get(path);
    if (cached && cached.node === node) return cached.proxy;
    const proxy = new Proxy({}, {
      get(_target, prop) { return read(node, path, prop); },
      has(_target, prop) { return prop in node; },
      ownKeys() { return Reflect.ownKeys(node); },
      getOwnPropertyDescriptor(_target, prop) {
        if (!Object.prototype.hasOwnProperty.call(node, prop)) return undefined;
        return { value: read(node, path, prop), writable: false, enumerable: true, configurable: true };
      },
      set(_target, prop, value) { return Reflect.set(node, prop, value); }
    });
    cache.set(path, { node, proxy });
    return proxy;
  }
  if (!tree || typeof tree !== 'object') return tree;
  return view(tree, prefix);
}

/**
 * Request headers of the manifest and re-enumeration routes.
 * @param {{ client: string, platform?: string, appVersion?: string, updateId?: string, installId?: string, visitorId?: string, channel?: string }} info
 * @returns {Record<string, string>}
 */
export function appContentHeaders(info = {}) {
  const headers = { 'X-Client': info.client === 'mobile' ? 'mobile' : 'web' };
  if (info.platform) headers['X-Client-Platform'] = info.platform;
  if (info.appVersion) headers['X-Client-Version'] = info.appVersion;
  if (info.updateId) headers['X-Client-Update-Id'] = info.updateId;
  if (info.installId) headers['X-TK-Install-Id'] = info.installId;
  if (info.visitorId) headers['X-TK-Visitor-Id'] = info.visitorId;
  if (info.channel) headers['X-Client-Channel'] = info.channel;
  return headers;
}

function time(value) {
  const ms = value ? Date.parse(value) : NaN;
  return Number.isFinite(ms) ? ms : null;
}

/**
 * The banners to show in one placement now, highest priority first: inside their schedule and not dismissed
 * (a dismissal holds only while the banner's `contentHash` is unchanged; `Critical` and non-dismissible banners
 * ignore dismissals).
 * @param {Array<object>} banners Client banners from the manifest.
 * @param {{ placement?: string, dismissed?: Record<string, string>, now?: number }} [options]
 * @returns {Array<object>}
 */
export function activeBanners(banners, { placement = 'Global', dismissed = {}, now = Date.now() } = {}) {
  return (Array.isArray(banners) ? banners : [])
    .filter((b) => b && b.placement === placement)
    .filter((b) => {
      const starts = time(b.startsUtc);
      const ends = time(b.endsUtc);
      return (starts === null || starts <= now) && (ends === null || now < ends);
    })
    .filter((b) => !(isBannerDismissible(b) && dismissed && dismissed[b.id] && dismissed[b.id] === b.contentHash))
    .sort((a, b) => (Number(b.priority) || 0) - (Number(a.priority) || 0) || String(a.id).localeCompare(String(b.id)));
}

/**
 * Whether a member may dismiss a banner.
 * @param {object} banner
 */
export function isBannerDismissible(banner) {
  return Boolean(banner && banner.dismissible && banner.style !== 'Critical');
}

/**
 * Record a dismissal (returns the new map; the caller persists it).
 * @param {Record<string, string>} dismissed
 * @param {object} banner
 * @returns {Record<string, string>}
 */
export function dismissBanner(dismissed, banner) {
  if (!isBannerDismissible(banner)) return { ...(dismissed || {}) };
  return { ...(dismissed || {}), [banner.id]: banner.contentHash || '' };
}

function assetFiles(assets) {
  const files = [];
  for (const asset of Object.values(assets || {})) {
    if (!asset) continue;
    if (asset.sha256 && asset.url) files.push({ sha256: asset.sha256, url: asset.url, contentType: asset.contentType || 'image/webp' });
    if (asset.darkVariant && asset.darkVariant.sha256 && asset.darkVariant.url) files.push({ sha256: asset.darkVariant.sha256, url: asset.darkVariant.url, contentType: asset.contentType || 'image/webp' });
  }
  return files;
}

/**
 * Files (`{ sha256, url, contentType }`) referenced by the published assets that are not cached yet.
 * @param {Record<string, object>|Array<object>} assets
 * @param {Iterable<string>} cachedHashes
 */
export function assetsToFetch(assets, cachedHashes) {
  const cached = new Set(cachedHashes || []);
  const seen = new Set();
  return assetFiles(assets).filter((f) => !cached.has(f.sha256) && !seen.has(f.sha256) && seen.add(f.sha256));
}

/**
 * Cached hashes no published asset references any more (clients delete them).
 * @param {Record<string, object>|Array<object>} assets
 * @param {Iterable<string>} cachedHashes
 * @returns {string[]}
 */
export function assetsToEvict(assets, cachedHashes) {
  const used = new Set(assetFiles(assets).map((f) => f.sha256));
  return [...new Set(cachedHashes || [])].filter((hash) => !used.has(hash));
}

/**
 * The published asset with a key, or null.
 * @param {Record<string, object>} assets
 * @param {string} key
 */
export function findRemoteAsset(assets, key) {
  return Object.values(assets || {}).find((a) => a && a.key === key) || null;
}

/**
 * The empty bundled fallback manifest (docs/LIVE_UPDATES_CONTRACT.md section 11.3): no flags, kill switches,
 * strings, banners, or assets, and no content version, so the first online fetch is a full snapshot.
 */
export const EMPTY_FALLBACK_MANIFEST = Object.freeze({ fallback: true, contentVersion: null, flags: {}, killSwitches: {}, strings: { upserts: {}, deletes: [] }, banners: { objects: [] }, assets: { upserts: [], deletes: [] } });

/**
 * Reduce an anonymous `GET /api/v1/app/manifest` body to the fallback manifest bundled into a build for the first
 * launch offline: a full snapshot (no `since`) of the published flags, kill switches, string overrides, banners, and
 * asset descriptors, plus the content version and check interval. Anything else (fullRefresh answers, unknown
 * fields, malformed parts) yields the empty fallback or drops the part. Pure; never throws.
 * @param {any} manifest Manifest body.
 * @returns {object} The fallback manifest (`fallback: true`).
 */
export function fallbackManifestOf(manifest) {
  if (!manifest || typeof manifest !== 'object' || Array.isArray(manifest) || manifest.fullRefresh) return { ...EMPTY_FALLBACK_MANIFEST };
  const plain = (value) => (value && typeof value === 'object' && !Array.isArray(value) ? value : {});
  const strings = plain(manifest.strings);
  const upserts = {};
  for (const [key, value] of Object.entries(plain(strings.upserts))) if (typeof value === 'string') upserts[key] = value;
  const banners = plain(manifest.banners);
  const assets = plain(manifest.assets);
  const out = {
    fallback: true,
    contentVersion: Number.isFinite(Number(manifest.contentVersion)) && manifest.contentVersion !== null ? Number(manifest.contentVersion) : null,
    flags: { ...plain(manifest.flags) },
    killSwitches: { ...plain(manifest.killSwitches) },
    strings: { upserts, deletes: [] },
    banners: { objects: Array.isArray(banners.objects) ? banners.objects.filter((b) => b && typeof b === 'object') : [] },
    assets: { upserts: Array.isArray(assets.upserts) ? assets.upserts.filter((a) => a && typeof a === 'object' && a.id) : [], deletes: [] }
  };
  if (Number.isFinite(Number(manifest.checkIntervalMinutes)) && Number(manifest.checkIntervalMinutes) > 0) out.checkIntervalMinutes = Number(manifest.checkIntervalMinutes);
  return out;
}

function emptyState() {
  return { contentVersion: null, flags: {}, killSwitches: {}, strings: {}, banners: [], assets: {}, binary: null, checkIntervalMinutes: DEFAULT_CHECK_INTERVAL_MINUTES, fetchedAt: 0 };
}

function normalizeState(raw) {
  const s = { ...emptyState(), ...(raw && typeof raw === 'object' ? raw : {}) };
  if (!s.flags || typeof s.flags !== 'object') s.flags = {};
  if (!s.killSwitches || typeof s.killSwitches !== 'object') s.killSwitches = {};
  if (!s.strings || typeof s.strings !== 'object') s.strings = {};
  if (!Array.isArray(s.banners)) s.banners = [];
  if (!s.assets || typeof s.assets !== 'object' || Array.isArray(s.assets)) s.assets = {};
  return s;
}

/**
 * Apply one manifest response to a state (pure). `enumerated` carries the re-enumerated sets after a full refresh.
 * @param {object} state Current state.
 * @param {object} manifest Manifest body.
 * @param {{ sentSince: number|null, enumerated?: { strings?: Array<{ key: string, value: string }>, banners?: Array<object>, assets?: Array<object> }, now?: number }} options
 * @returns {object} The next state.
 */
export function applyAppManifest(state, manifest, { sentSince = null, enumerated = null, now = Date.now() } = {}) {
  const next = normalizeState(state);
  if (!manifest || typeof manifest !== 'object') return next;
  if (manifest.flags && typeof manifest.flags === 'object') next.flags = { ...manifest.flags };
  if (manifest.killSwitches && typeof manifest.killSwitches === 'object') next.killSwitches = { ...manifest.killSwitches };
  if (manifest.fullRefresh) {
    if (enumerated && Array.isArray(enumerated.strings)) next.strings = Object.fromEntries(enumerated.strings.filter((s) => s && s.key).map((s) => [s.key, s.value]));
    if (enumerated && Array.isArray(enumerated.banners)) next.banners = enumerated.banners;
    if (enumerated && Array.isArray(enumerated.assets)) next.assets = Object.fromEntries(enumerated.assets.filter((a) => a && a.id).map((a) => [a.id, a]));
  } else {
    const full = sentSince === null || sentSince === undefined;
    const strings = manifest.strings || {};
    const base = full ? {} : { ...next.strings };
    for (const key of strings.deletes || []) delete base[key];
    for (const [key, value] of Object.entries(strings.upserts || {})) base[key] = value;
    next.strings = base;
    if (manifest.banners && Array.isArray(manifest.banners.objects)) next.banners = manifest.banners.objects;
    const assets = manifest.assets || {};
    const byId = full ? {} : { ...next.assets };
    for (const id of assets.deletes || []) delete byId[id];
    for (const asset of assets.upserts || []) if (asset && asset.id) byId[asset.id] = asset;
    next.assets = byId;
  }
  if (Object.prototype.hasOwnProperty.call(manifest, 'binary')) next.binary = manifest.binary || null;
  if (Number.isFinite(Number(manifest.checkIntervalMinutes)) && Number(manifest.checkIntervalMinutes) > 0) next.checkIntervalMinutes = Number(manifest.checkIntervalMinutes);
  if (manifest.contentVersion !== undefined && manifest.contentVersion !== null) next.contentVersion = Number(manifest.contentVersion);
  next.fetchedAt = now;
  return next;
}

async function enumerateAll(fetchPage) {
  return (await collectEnumeration(fetchPage, { maxResults: 100 })).objects;
}

/**
 * Create the app content store.
 * @param {object} options
 * @param {'web'|'mobile'} options.client
 * @param {(since: number|null) => Promise<object>} options.fetchManifest Fetches `GET /app/manifest?since=`.
 * @param {(kind: 'strings'|'banners'|'assets', query: object) => Promise<object>} [options.enumerate] Fetches one re-enumeration page.
 * @param {{ getItem: (key: string) => any, setItem: (key: string, value: string) => any }} [options.storage] Sync or async storage.
 * @param {object} [options.initial] Bundled fallback manifest (applied when nothing is cached).
 * @param {boolean} [options.applyStrings] Push string overrides to the overlays (default true).
 * @param {() => number} [options.now]
 */
export function createAppContentStore({ client = 'web', fetchManifest, enumerate = null, storage = null, initial = null, applyStrings = true, now = () => Date.now() } = {}) {
  if (typeof fetchManifest !== 'function') throw new TypeError('fetchManifest is required');
  let state = emptyState();
  const listeners = new Set();
  let inflight = null;
  let loaded = false;

  function publish(next) {
    state = next;
    if (applyStrings) setRemoteStrings(state.strings);
    for (const listener of listeners) {
      try {
        listener(state);
      } catch {
        // A listener failure never stops the others.
      }
    }
  }

  async function persist() {
    if (!storage) return;
    try {
      await storage.setItem(APP_CONTENT_STORAGE_KEY, JSON.stringify(state));
    } catch {
      // Storage full or unavailable: the in-memory state still applies.
    }
  }

  async function load() {
    if (loaded) return state;
    loaded = true;
    let cached = null;
    if (storage) {
      try {
        const text = await storage.getItem(APP_CONTENT_STORAGE_KEY);
        cached = text ? JSON.parse(text) : null;
      } catch {
        cached = null;
      }
    }
    if (cached) publish(normalizeState(cached));
    else if (initial) publish(applyAppManifest(emptyState(), initial, { sentSince: null, now: 0 }));
    return state;
  }

  async function run() {
    await load();
    const since = state.contentVersion === null || state.contentVersion === undefined ? null : state.contentVersion;
    let manifest;
    try {
      manifest = await fetchManifest(since);
    } catch {
      return state;
    }
    if (!manifest) {
      publish({ ...state, fetchedAt: now() });
      return state;
    }
    let enumerated = null;
    if (manifest.fullRefresh && enumerate) {
      try {
        enumerated = {
          strings: await enumerateAll((q) => enumerate('strings', { ...q, filters: { client } })),
          banners: await enumerateAll((q) => enumerate('banners', q)),
          assets: await enumerateAll((q) => enumerate('assets', q))
        };
      } catch {
        enumerated = null;
      }
      if (!enumerated) return state;
    }
    publish(applyAppManifest(state, manifest, { sentSince: since, enumerated, now: now() }));
    await persist();
    return state;
  }

  return {
    client,
    /** Load the cached state (or the bundled fallback) without the network. */
    load,
    /** Current state. */
    getState() { return state; },
    /**
     * Fetch the manifest unless the last fetch is younger than `checkIntervalMinutes` (`force` skips the throttle:
     * sign-in, sign-out, and membership changes always refetch).
     */
    async refresh({ force = false } = {}) {
      await load();
      const intervalMs = (Number(state.checkIntervalMinutes) || DEFAULT_CHECK_INTERVAL_MINUTES) * 60000;
      if (!force && state.fetchedAt && now() - state.fetchedAt < intervalMs) return state;
      if (inflight) return inflight;
      inflight = run().finally(() => { inflight = null; });
      return inflight;
    },
    /** Subscribe to state changes; returns the unsubscribe function. */
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    /** A resolved flag value, or the fallback when the flag is unknown. */
    getFlag(key, fallback = null) {
      return Object.prototype.hasOwnProperty.call(state.flags, key) ? state.flags[key] : fallback;
    },
    /** Whether a feature's kill switch is on for this caller. */
    isKilled(featureKey) {
      return Boolean(state.killSwitches[featureKey] && state.killSwitches[featureKey].killed);
    },
    /** The kill switch message of a feature, or null. */
    killMessage(featureKey) {
      return (state.killSwitches[featureKey] && state.killSwitches[featureKey].message) || null;
    },
    /** A `503 FeatureDisabled` answer: mark the feature killed at once (section 11.3). */
    noteFeatureDisabled(featureKey, message) {
      if (!featureKey) return;
      publish({ ...state, killSwitches: { ...state.killSwitches, [featureKey]: { killed: true, message: message || null } } });
      persist();
    }
  };
}

/**
 * The feature key and message of an ApiError that is a `503 FeatureDisabled`, or null.
 * @param {any} error
 * @returns {{ feature: string|null, message: string|null }|null}
 */
export function featureDisabledOf(error) {
  if (!error || error.status !== 503) return null;
  const body = error.body || {};
  if ((error.code || body.error) !== 'FeatureDisabled') return null;
  return { feature: body.feature || null, message: body.message || error.message || null };
}
