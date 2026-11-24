/**
 * Small JSON key-value storage in the app's private document directory for the content manifest
 * cache, banner dismissals, and the remote asset index (docs/LIVE_UPDATES_CONTRACT.md section
 * 11.3). One file per key. Every failure is swallowed: a read that fails returns null and a write
 * that fails is dropped, so the in-memory state still applies for the session.
 */

/** Folder under the document directory. */
export const AppContentFolder = 'tk-app-content';

function fileFor(folder, key) {
  const { Directory, File, Paths } = require('expo-file-system');
  const dir = new Directory(Paths.document, folder);
  try {
    if (dir.exists === false && typeof dir.create === 'function') dir.create({ intermediates: true });
  } catch {
    // Created concurrently, or no directory API (tests).
  }
  return new File(dir, String(key).replace(/[^A-Za-z0-9._-]/g, '_') + '.json');
}

/**
 * Create a storage over JSON files (`{ getItem, setItem, removeItem }`, async, never throws).
 * @param {string} [folder]
 * @param {(folder: string, key: string) => object} [fileFactory] Returns an expo-file-system `File`.
 */
export function createJsonFileStorage(folder = AppContentFolder, fileFactory = fileFor) {
  return {
    async getItem(key) {
      try {
        const file = fileFactory(folder, key);
        if (!file || file.exists === false || typeof file.text !== 'function') return null;
        return await file.text();
      } catch {
        return null;
      }
    },
    async setItem(key, value) {
      try {
        const file = fileFactory(folder, key);
        if (!file || typeof file.write !== 'function') return;
        if (file.exists === false && typeof file.create === 'function') file.create({ overwrite: true });
        await file.write(String(value));
      } catch {
        // Not saved: the in-memory state still applies.
      }
    },
    async removeItem(key) {
      try {
        const file = fileFactory(folder, key);
        if (file && file.exists && typeof file.delete === 'function') file.delete();
      } catch {
        // Already gone.
      }
    }
  };
}

/** The app's content storage. */
export const appContentStorage = createJsonFileStorage();

/** In-memory storage with the same interface (tests and fallbacks). */
export function createMemoryStorage(initial = {}) {
  const map = new Map(Object.entries(initial));
  return {
    map,
    async getItem(key) {
      return map.has(key) ? map.get(key) : null;
    },
    async setItem(key, value) {
      map.set(key, String(value));
    },
    async removeItem(key) {
      map.delete(key);
    }
  };
}
