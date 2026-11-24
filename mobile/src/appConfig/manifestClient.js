import Constants from 'expo-constants';
import { Platform } from 'react-native';
import { appContentHeaders, createAppContentStore, updatesMethods } from '@trelliskit/shared';
import { getAppVersion } from '../config';
import { getInstallId } from '../updates/installId';
import { resolveBuildChannel } from '../updates/requestHeaders';
import { EmbeddedUpdateId } from '../updates/events';
import { getCurrentUpdateId, getUpdatesModule } from '../updates/runtime';
import { appContentStorage } from './storage';
// The published content manifest bundled at build time (docs/LIVE_UPDATES_CONTRACT.md section 11.3), written by
// `npm run manifest:bundle` (shared/scripts/app-manifest.mjs) before a release build; the committed copy is empty.
// The first launch offline starts from it; a cached manifest always wins.
import bundledManifest from '../../assets/app-manifest.json';

/**
 * The manifest and re-enumeration request headers of this install (docs/LIVE_UPDATES_CONTRACT.md
 * section 11.1): client, platform, native app version, running update id (`embedded` when none),
 * install id, and the build channel the updates module resolved. A part that cannot be resolved
 * is left out rather than failing the request.
 * @returns {Promise<Record<string, string>>}
 */
export async function appContentRequestHeaders({ installId = getInstallId, channel = () => resolveBuildChannel({ updates: getUpdatesModule() }) } = {}) {
  const [id, ch] = await Promise.all([installId().catch(() => null), Promise.resolve().then(channel).catch(() => null)]);
  return appContentHeaders({
    client: 'mobile',
    platform: Platform.OS,
    appVersion: Constants.nativeAppVersion || getAppVersion(),
    updateId: getCurrentUpdateId() || EmbeddedUpdateId,
    installId: id || undefined,
    channel: ch || undefined
  });
}

const ENUMERATORS = {
  strings: updatesMethods.enumerateAppStrings,
  banners: updatesMethods.enumerateAppBanners,
  assets: updatesMethods.enumerateAppAssets
};

/**
 * The app's content store: `GET /app/manifest?since=` and the re-enumeration routes through the
 * shared API client (so a signed-in member's token goes with it and targeting applies), cached in
 * a JSON file, starting from the bundled fallback manifest when nothing is cached.
 * @param {object} options
 * @param {object} options.api MobileApiClient.
 * @param {object} [options.storage]
 * @param {() => Promise<Record<string, string>>} [options.headers]
 * @param {object|null} [options.initial] Fallback manifest (default: the bundled assets/app-manifest.json).
 */
export function createMobileAppContentStore({ api, storage = appContentStorage, headers = appContentRequestHeaders, initial = bundledManifest }) {
  let pending = null;
  const getHeaders = () => {
    if (!pending) pending = Promise.resolve().then(headers).catch(() => appContentHeaders({ client: 'mobile', platform: Platform.OS }));
    return pending;
  };
  return createAppContentStore({
    client: 'mobile',
    storage,
    initial,
    fetchManifest: async (since) => updatesMethods.getAppManifest.call(api, { since: since === null ? undefined : since, headers: await getHeaders() }),
    enumerate: async (kind, query) => ENUMERATORS[kind].call(api, query, { headers: await getHeaders() })
  });
}
