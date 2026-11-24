import { Directory, File, Paths } from 'expo-file-system';

/**
 * A small key-value store in the app's private document directory (never shared storage), for
 * the library's offline progress queue and the download index. Values are strings; each key is
 * one JSON file. Everything here is per device and is cleared on sign-out.
 */

const FOLDER = 'tk-library';

function folder() {
  const dir = new Directory(Paths.document, FOLDER);
  try {
    if (!dir.exists) dir.create({ intermediates: true });
  } catch {
    // Created concurrently, or the platform has no directory API (tests).
  }
  return dir;
}

function fileFor(key) {
  return new File(folder(), String(key).replace(/[^A-Za-z0-9._-]/g, '_') + '.json');
}

/** `getItem`, `setItem`, and `removeItem` over private files (async, like AsyncStorage). */
export const fileStorage = {
  async getItem(key) {
    try {
      const file = fileFor(key);
      return file.exists ? await file.text() : null;
    } catch {
      return null;
    }
  },
  async setItem(key, value) {
    const file = fileFor(key);
    if (!file.exists) file.create({ overwrite: true });
    await file.write(String(value));
  },
  async removeItem(key) {
    try {
      const file = fileFor(key);
      if (file.exists) file.delete();
    } catch {
      // Already gone.
    }
  }
};

/** The private folder that holds downloaded lesson files. */
export function downloadsFolder() {
  const dir = new Directory(Paths.document, FOLDER, 'downloads');
  try {
    if (!dir.exists) dir.create({ intermediates: true });
  } catch {
    // Created concurrently.
  }
  return dir;
}
