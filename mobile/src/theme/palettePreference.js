import { useSyncExternalStore } from 'react';

/**
 * The signed-in member's palette preset preference (docs/TRELLISKIT.md section 2.1): an engine
 * preset id, or null for the property's brand palette. The account copy (`GET`/`PUT
 * /api/v1/me/appearance`) is the source; the last value is kept in a small file in the document
 * directory so the next launch applies it before the network answers. useTheme resolves it against
 * the live property's `brand.presets` (cached with its ETag), so a preset the administrator removes
 * falls back to the property palette without a new build. Every storage failure is harmless.
 */

/** File name in the document directory. */
export const PalettePreferenceFile = 'tk-palette-preset.json';

const ID = /^[a-z0-9]+(-[a-z0-9]+)*$/;
const listeners = new Set();
let current = null;
let loaded = false;
let fileFactory = defaultFile;

function defaultFile() {
  const { File, Paths } = require('expo-file-system');
  return new File(Paths.document, PalettePreferenceFile);
}

function valid(value) {
  return typeof value === 'string' && ID.test(value) ? value : null;
}

function emit() {
  for (const listener of listeners) listener();
}

function parse(text) {
  try {
    const parsed = JSON.parse(text);
    return parsed && parsed.version === 1 ? valid(parsed.presetId) : null;
  } catch {
    return null;
  }
}

/**
 * Read the stored preference once (synchronously when the file system allows it, so the first
 * frame already uses it; otherwise asynchronously, notifying subscribers when it arrives).
 */
export function loadPalettePreference() {
  if (loaded) return Promise.resolve(current);
  loaded = true;
  try {
    const file = fileFactory();
    if (!file || file.exists === false) return Promise.resolve(current);
    if (typeof file.textSync === 'function') {
      current = parse(file.textSync());
      return Promise.resolve(current);
    }
    if (typeof file.text !== 'function') return Promise.resolve(current);
    return Promise.resolve(file.text())
      .then((text) => {
        const value = parse(text);
        if (value !== current) {
          current = value;
          emit();
        }
        return current;
      })
      .catch(() => current);
  } catch {
    return Promise.resolve(current);
  }
}

/** The current preference (a preset id or null). */
export function getPalettePreference() {
  if (!loaded) loadPalettePreference();
  return current;
}

/**
 * Change the preference (null clears it), persist it, and notify subscribers. Invalid ids count as null.
 * @param {string|null|undefined} presetId
 */
export function setPalettePreference(presetId) {
  loaded = true;
  const next = valid(presetId);
  if (next === current) return;
  current = next;
  try {
    const file = fileFactory();
    if (file && typeof file.write === 'function') {
      if (file.exists === false && typeof file.create === 'function') file.create();
      Promise.resolve(file.write(JSON.stringify({ version: 1, presetId: next }))).catch(() => {});
    }
  } catch {
    // The preference still applies until the app closes.
  }
  emit();
}

/** Subscribe to preference changes; returns the unsubscribe function. */
export function subscribePalettePreference(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/**
 * Test hook: forget the preference and use another file factory (or the default).
 * @param {() => object} [factory]
 */
export function resetPalettePreference(factory = defaultFile) {
  current = null;
  loaded = false;
  fileFactory = factory;
}

/** React hook: the current preference, re-rendering when it changes. */
export function usePalettePreference() {
  return useSyncExternalStore(subscribePalettePreference, getPalettePreference, getPalettePreference);
}
