import { useCallback, useSyncExternalStore } from 'react';

/**
 * A small choice remembered on this device (a view mode, for example): one JSON file per key in
 * the document directory, read once and kept in memory. It is a device setting, not an account
 * setting. Every storage failure is harmless: the choice then lasts until the app closes.
 */

const stores = new Map();
let fileFactory = defaultFile;

function defaultFile(name) {
  const { File, Paths } = require('expo-file-system');
  return new File(Paths.document, name);
}

function fileName(key) {
  return 'tk-choice-' + String(key).replace(/[^a-z0-9-]/gi, '-') + '.json';
}

function storeFor(key, fallback) {
  let store = stores.get(key);
  if (store) return store;
  store = { value: fallback, listeners: new Set() };
  stores.set(key, store);
  try {
    const file = fileFactory(fileName(key));
    if (file && file.exists !== false && typeof file.textSync === 'function') {
      const parsed = JSON.parse(file.textSync());
      if (parsed && parsed.version === 1 && typeof parsed.value === 'string') store.value = parsed.value;
    }
  } catch {
    // The default applies.
  }
  return store;
}

/** Change a choice, persist it, and tell its subscribers. */
function writeChoice(key, value) {
  const store = stores.get(key);
  if (!store || store.value === value) return;
  store.value = value;
  try {
    const file = fileFactory(fileName(key));
    if (file && typeof file.write === 'function') {
      if (file.exists === false && typeof file.create === 'function') file.create();
      Promise.resolve(file.write(JSON.stringify({ version: 1, value }))).catch(() => {});
    }
  } catch {
    // The choice still applies until the app closes.
  }
  for (const listener of store.listeners) listener();
}

/**
 * A remembered choice from a fixed list.
 * @param {string} key Name of the choice (letters, digits, hyphens).
 * @param {readonly string[]} options The allowed values.
 * @param {string} fallback The value until one is chosen (and when the stored one is no longer allowed).
 * @returns {[string, (value: string) => void]}
 */
export function useStoredChoice(key, options, fallback) {
  const store = storeFor(key, fallback);
  const subscribe = useCallback(
    (listener) => {
      store.listeners.add(listener);
      return () => store.listeners.delete(listener);
    },
    [store]
  );
  const read = useCallback(() => store.value, [store]);
  const stored = useSyncExternalStore(subscribe, read, read);
  const set = useCallback(
    (value) => {
      if (options.includes(value)) writeChoice(key, value);
    },
    [key, options]
  );
  return [options.includes(stored) ? stored : fallback, set];
}

/**
 * Test hook: forget every choice and use another file factory (or the default).
 * @param {(name: string) => object} [factory]
 */
export function resetStoredChoices(factory = defaultFile) {
  stores.clear();
  fileFactory = factory;
}

export default useStoredChoice;
