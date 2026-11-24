import { assetsToEvict, assetsToFetch } from '@trelliskit/shared';
import { appContentStorage } from './storage';

/**
 * Remote asset cache (docs/LIVE_UPDATES_CONTRACT.md sections 11.3 and 14.2): every published
 * asset file (and dark variant) is downloaded once per sha256 into the cache directory and kept
 * until no published descriptor references that hash. The cached hashes are kept in a small
 * index (the OS may purge the cache directory; a missing file is dropped from the index and
 * downloaded again). Nothing here throws: a failed download is retried on the next sync, and
 * until then the asset is shown from its network URL.
 */

/** Storage key of the cached hash index. */
export const AssetIndexKey = 'tk.appAssets.v1';

/** Folder under the cache directory. */
export const AssetFolder = 'tk-app-assets';

const HASH = /^[0-9a-f]{16,128}$/i;

function defaultFolder() {
  const { Directory, Paths } = require('expo-file-system');
  const dir = new Directory(Paths.cache, AssetFolder);
  try {
    if (dir.exists === false && typeof dir.create === 'function') dir.create({ intermediates: true });
  } catch {
    // Created concurrently, or no directory API (tests).
  }
  return dir;
}

function defaultFileFor(hash) {
  const { File } = require('expo-file-system');
  return new File(defaultFolder(), String(hash).toLowerCase());
}

async function defaultDownload(url, file) {
  const { File } = require('expo-file-system');
  return File.downloadFileAsync(url, file, { idempotent: true });
}

/**
 * Create the asset cache.
 * @param {object} [options]
 * @param {{ getItem: Function, setItem: Function }} [options.storage] Where the hash index lives.
 * @param {(hash: string) => object} [options.fileFor] The cache `File` of a hash.
 * @param {(url: string, file: object) => Promise<any>} [options.download]
 */
export function createAssetCache({ storage = appContentStorage, fileFor = defaultFileFor, download = defaultDownload } = {}) {
  let hashes = null;
  let chain = Promise.resolve();

  async function load() {
    if (hashes) return hashes;
    hashes = new Set();
    try {
      const parsed = JSON.parse((await storage.getItem(AssetIndexKey)) || '[]');
      if (Array.isArray(parsed)) for (const h of parsed) if (typeof h === 'string' && HASH.test(h)) hashes.add(h.toLowerCase());
    } catch {
      // Unreadable index: start empty (files are downloaded again).
    }
    return hashes;
  }

  async function save() {
    try {
      await storage.setItem(AssetIndexKey, JSON.stringify([...hashes]));
    } catch {
      // Kept in memory for this session.
    }
  }

  function exists(hash) {
    try {
      return fileFor(hash).exists !== false;
    } catch {
      return false;
    }
  }

  async function syncOnce(assets) {
    await load();
    for (const hash of [...hashes]) if (!exists(hash)) hashes.delete(hash);
    for (const hash of assetsToEvict(assets, hashes)) {
      try {
        const file = fileFor(hash);
        if (file && file.exists && typeof file.delete === 'function') file.delete();
      } catch {
        // Already gone.
      }
      hashes.delete(hash);
    }
    for (const item of assetsToFetch(assets, hashes)) {
      const hash = String(item.sha256).toLowerCase();
      if (!HASH.test(hash)) continue;
      try {
        const file = fileFor(hash);
        await download(item.url, file);
        // Content addressed: a file that does not hash to its name is not kept.
        if (file && typeof file.digest === 'function') {
          const digest = String(await file.digest('SHA-256')).toLowerCase();
          if (digest !== hash) {
            try {
              file.delete();
            } catch {
              // Nothing to remove.
            }
            continue;
          }
        }
        hashes.add(hash);
      } catch {
        // Retried on the next sync; the network URL is used meanwhile.
      }
    }
    await save();
    return [...hashes];
  }

  return {
    /** The cached hashes (loads the index). */
    async cachedHashes() {
      return [...(await load())];
    },
    /**
     * Download what the published assets reference and evict what they no longer do. Syncs run
     * one after another; resolves the cached hashes.
     * @param {Record<string, object>} assets
     * @returns {Promise<string[]>}
     */
    sync(assets) {
      const run = chain.then(() => syncOnce(assets || {}));
      chain = run.catch(() => undefined);
      return run.catch(() => (hashes ? [...hashes] : []));
    },
    /** The local file URI of a cached hash, or null. */
    uriFor(hash) {
      if (!hash || !hashes || !hashes.has(String(hash).toLowerCase())) return null;
      try {
        return fileFor(String(hash).toLowerCase()).uri || null;
      } catch {
        return null;
      }
    }
  };
}

/**
 * The URI to show for an asset: the cached file when the hash is cached, else the network URL.
 * The dark variant is used when `dark` is set and the asset has one.
 * @param {object|null} asset ClientAsset.
 * @param {{ dark?: boolean, cached?: Set<string>|string[], uriFor?: (hash: string) => string|null }} [options]
 * @returns {string|null}
 */
export function remoteAssetUri(asset, { dark = false, cached = [], uriFor = null } = {}) {
  if (!asset) return null;
  const file = dark && asset.darkVariant && asset.darkVariant.sha256 && asset.darkVariant.url ? asset.darkVariant : asset;
  if (!file.sha256 || !file.url) return null;
  const set = cached instanceof Set ? cached : new Set(cached || []);
  const hash = String(file.sha256).toLowerCase();
  if (set.has(hash) && uriFor) return uriFor(hash) || file.url;
  return file.url;
}
