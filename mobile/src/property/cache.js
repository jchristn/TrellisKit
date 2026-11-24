/**
 * Persisted copy of the last runtime property (GET /api/v1/property) and its `ETag`, so the app
 * starts from it before the network answers and revalidates with `If-None-Match`. Stored as one
 * JSON file in the document directory with expo-file-system. Every failure (no file system,
 * unreadable or corrupt file, full disk) is harmless: reads resolve null and writes are dropped.
 */

/** File name in the document directory. */
export const PropertyCacheFile = 'tk-property-cache.json';

/** Cache format version; a different version is ignored. */
const VERSION = 1;

function defaultFile() {
  const { File, Paths } = require('expo-file-system');
  return new File(Paths.document, PropertyCacheFile);
}

/**
 * A property cache over one file. `fileFactory` returns an expo-file-system `File` (or anything
 * with `exists`, `text()`, `write()`, and optionally `create()`).
 * @param {() => object} [fileFactory]
 * @returns {{ load: () => Promise<{ property: object, etag: string|null }|null>, save: (property: object, etag: string|null) => Promise<boolean> }}
 */
export function createFileCache(fileFactory = defaultFile) {
  async function load() {
    try {
      const file = fileFactory();
      if (!file || typeof file.text !== 'function' || file.exists === false) return null;
      const parsed = JSON.parse(await file.text());
      if (!parsed || parsed.version !== VERSION || !parsed.property || typeof parsed.property !== 'object') return null;
      return { property: parsed.property, etag: typeof parsed.etag === 'string' && parsed.etag ? parsed.etag : null };
    } catch {
      return null;
    }
  }

  async function save(property, etag) {
    try {
      const file = fileFactory();
      if (!file || typeof file.write !== 'function') return false;
      if (file.exists === false && typeof file.create === 'function') file.create();
      await file.write(JSON.stringify({ version: VERSION, etag: etag || null, property }));
      return true;
    } catch {
      return false;
    }
  }

  return Object.freeze({ load, save });
}

/** The default cache used by PropertyProvider (the document directory). */
export const fileCache = createFileCache();

/** Read the cached runtime property from the default cache. */
export function loadCachedProperty() {
  return fileCache.load();
}

/** Store the runtime property and its `ETag` in the default cache. */
export function saveCachedProperty(property, etag) {
  return fileCache.save(property, etag);
}
