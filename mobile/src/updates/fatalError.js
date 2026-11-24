import { getCurrentUpdateId } from './runtime';

/**
 * The last fatal JavaScript error, for symbolication of launch failures (live updates spec
 * section 7). A global error handler (chained in front of the previous one, which still shows the
 * red box in development and crashes the app in release) writes `{ message, stackTrace, updateId }`
 * synchronously to a small file before the crash. When the next launch reports `LaunchFailed` for
 * that update (src/updates/events.js), the message and stack go with the event and the record is
 * cleared. Only the error text and the minified stack are kept: no member data, no breadcrumbs.
 */

/** Record file name in the document directory. */
export const FatalErrorFile = 'tk-last-fatal-error.json';

/** Longest message sent with an event. */
export const MaxFailureMessageLength = 500;

/** Longest stack trace sent with an event. */
export const MaxStackTraceLength = 16384;

function clip(value, max) {
  if (value === null || value === undefined) return null;
  const text = String(value);
  return text ? text.slice(0, max) : null;
}

/**
 * The record of an error: message and stack clipped to what the server accepts.
 * @param {unknown} error
 * @param {string|null} updateId The running update.
 */
export function fatalRecordOf(error, updateId, now = Date.now()) {
  const message = error && typeof error === 'object' ? error.message || error.name || String(error) : String(error);
  return {
    message: clip(message, MaxFailureMessageLength),
    stackTrace: clip(error && typeof error === 'object' ? error.stack : null, MaxStackTraceLength),
    updateId: typeof updateId === 'string' && updateId ? updateId.toLowerCase() : null,
    occurredUtc: new Date(now).toISOString()
  };
}

function defaultFile() {
  const { File, Paths } = require('expo-file-system');
  return new File(Paths.document, FatalErrorFile);
}

/**
 * The record's storage: one JSON file (memory when the file system is unavailable). `save` is
 * synchronous where the platform allows it, because the app is about to terminate.
 * @param {() => object|null} [fileFactory] Returns an expo-file-system `File`.
 */
export function createFatalErrorStore(fileFactory = defaultFile) {
  let memory;
  return {
    save(record) {
      memory = record;
      try {
        const file = fileFactory();
        if (!file) return;
        if (file.exists === false && typeof file.create === 'function') file.create({ overwrite: true });
        const text = JSON.stringify(record);
        if (typeof file.writeSync === 'function') file.writeSync(text);
        else if (typeof file.write === 'function') file.write(text);
      } catch {
        // Kept in memory only.
      }
    },
    async read() {
      if (memory !== undefined) return memory;
      memory = null;
      try {
        const file = fileFactory();
        if (file && file.exists !== false && typeof file.text === 'function') {
          const parsed = JSON.parse(await file.text());
          if (parsed && typeof parsed === 'object') memory = parsed;
        }
      } catch {
        // Missing or unreadable: no record.
      }
      return memory;
    },
    clear() {
      memory = null;
      try {
        const file = fileFactory();
        if (file && file.exists && typeof file.delete === 'function') file.delete();
      } catch {
        // Already gone.
      }
    }
  };
}

/** The app's record store. */
export const fatalErrorStore = createFatalErrorStore();

const INSTALLED = Symbol.for('tk.fatalErrorRecorder');

/**
 * Install the global handler once (React Native `ErrorUtils`). Returns whether it is installed.
 * @param {object} [options]
 * @param {object} [options.errorUtils]
 * @param {{ save: (record: object) => void }} [options.store]
 * @param {() => string|null} [options.updateId]
 */
export function installFatalErrorRecorder({ errorUtils = globalThis.ErrorUtils, store = fatalErrorStore, updateId = getCurrentUpdateId } = {}) {
  if (!errorUtils || typeof errorUtils.setGlobalHandler !== 'function') return false;
  if (errorUtils[INSTALLED]) return true;
  const previous = typeof errorUtils.getGlobalHandler === 'function' ? errorUtils.getGlobalHandler() : null;
  errorUtils.setGlobalHandler((error, isFatal) => {
    if (isFatal) {
      try {
        store.save(fatalRecordOf(error, updateId()));
      } catch {
        // Never stand between the error and the previous handler.
      }
    }
    if (typeof previous === 'function') previous(error, isFatal);
  });
  try {
    errorUtils[INSTALLED] = true;
  } catch {
    // Frozen object: a second install chains once more, which is harmless.
  }
  return true;
}
