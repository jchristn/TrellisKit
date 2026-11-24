import { useSyncExternalStore } from 'react';

/**
 * The signed-in member's palette preset preference (docs/TRELLISKIT.md section 2.1): an engine
 * preset id, or null for the property's brand palette. The account copy (`GET`/`PUT
 * /api/v1/me/appearance`) is the source; this module keeps the last value in localStorage so the
 * next page load applies it at once, and notifies the runtime palette when it changes. Whether the
 * preset is still allowed is decided against `GET /api/v1/property` `brand.presets` when it applies.
 */

/** localStorage key of the preference. */
export const PALETTE_PRESET_STORAGE_KEY = 'tk.palettePreset';

const ID = /^[a-z0-9]+(-[a-z0-9]+)*$/;
const listeners = new Set();
let current;

function storage() {
  try {
    return typeof window !== 'undefined' ? window.localStorage : null;
  } catch {
    return null;
  }
}

function read() {
  try {
    const store = storage();
    const value = store ? store.getItem(PALETTE_PRESET_STORAGE_KEY) : null;
    return value && ID.test(value) ? value : null;
  } catch {
    return null;
  }
}

/** The stored preference (a preset id or null). */
export function getPalettePreference() {
  if (current === undefined) current = read();
  return current;
}

/**
 * Change the preference (null clears it) and notify subscribers. Invalid ids count as null.
 * @param {string|null|undefined} presetId
 */
export function setPalettePreference(presetId) {
  const next = typeof presetId === 'string' && ID.test(presetId) ? presetId : null;
  if (next === getPalettePreference()) return;
  current = next;
  try {
    const store = storage();
    if (store) {
      if (next) store.setItem(PALETTE_PRESET_STORAGE_KEY, next);
      else store.removeItem(PALETTE_PRESET_STORAGE_KEY);
    }
  } catch {
    // Storage can be unavailable (private mode); the preference still applies for this page view.
  }
  for (const listener of listeners) listener();
}

/** Subscribe to preference changes; returns the unsubscribe function. */
export function subscribePalettePreference(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** Test hook: forget the in-memory copy (the next read uses storage again). */
export function resetPalettePreference() {
  current = undefined;
}

/** React hook: the current preference, re-rendering when it changes. */
export function usePalettePreference() {
  return useSyncExternalStore(subscribePalettePreference, getPalettePreference, () => null);
}
