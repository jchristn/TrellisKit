import { useSyncExternalStore } from 'react';
import { Appearance } from 'react-native';

/**
 * This device's light or dark mode choice: `system` (follow the device, the default), `light`, or
 * `dark`. It is a device setting, not an account setting, so it is kept only in a small file in
 * the document directory and applied at launch. Applying it overrides the app's color scheme
 * (`Appearance.setColorScheme`), so everything that reads the color scheme (useTheme, navigation,
 * the status bar, native controls, maps) follows it. Every storage failure is harmless.
 */

/** File name in the document directory. */
export const ColorSchemePreferenceFile = 'tk-color-scheme.json';

/** The choices, in display order. */
export const ColorSchemeModes = Object.freeze(['system', 'light', 'dark']);

const listeners = new Set();
let current = 'system';
let loaded = false;
let revision = 0;
let fileFactory = defaultFile;

function defaultFile() {
  const { File, Paths } = require('expo-file-system');
  return new File(Paths.document, ColorSchemePreferenceFile);
}

function valid(value) {
  return ColorSchemeModes.includes(value) ? value : 'system';
}

function apply(mode) {
  try {
    if (typeof Appearance.setColorScheme === 'function') Appearance.setColorScheme(mode === 'system' ? 'auto' : mode);
  } catch {
    // The device setting keeps applying.
  }
}

function emit() {
  for (const listener of listeners) listener();
}

function parse(text) {
  try {
    const parsed = JSON.parse(text);
    return parsed && parsed.version === 1 ? valid(parsed.mode) : 'system';
  } catch {
    return 'system';
  }
}

function adopt(mode) {
  if (mode === current) return;
  current = mode;
  apply(current);
  emit();
}

/**
 * Read and apply the stored choice once (synchronously when the file system allows it, so the
 * first frame already uses it; otherwise as soon as the file is read).
 * @returns {Promise<'system'|'light'|'dark'>}
 */
export function loadColorSchemePreference() {
  if (loaded) return Promise.resolve(current);
  loaded = true;
  const loadingRevision = revision;
  try {
    const file = fileFactory();
    if (!file || file.exists === false) return Promise.resolve(current);
    if (typeof file.textSync === 'function') {
      adopt(parse(file.textSync()));
      return Promise.resolve(current);
    }
    if (typeof file.text !== 'function') return Promise.resolve(current);
    return Promise.resolve(file.text())
      .then((text) => {
        if (revision === loadingRevision) adopt(parse(text));
        return current;
      })
      .catch(() => current);
  } catch {
    return Promise.resolve(current);
  }
}

/** The current choice. */
export function getColorSchemePreference() {
  if (!loaded) loadColorSchemePreference();
  return current;
}

/**
 * Change the choice, apply it at once, persist it, and notify subscribers. Anything that is not
 * `light` or `dark` counts as `system`.
 * @param {'system'|'light'|'dark'} mode
 */
export function setColorSchemePreference(mode) {
  loaded = true;
  revision++;
  const next = valid(mode);
  if (next === current) return;
  current = next;
  apply(current);
  try {
    const file = fileFactory();
    if (file && typeof file.write === 'function') {
      if (file.exists === false && typeof file.create === 'function') file.create();
      Promise.resolve(file.write(JSON.stringify({ version: 1, mode: next }))).catch(() => {});
    }
  } catch {
    // The choice still applies until the app closes.
  }
  emit();
}

function subscribe(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** The current choice as React state. */
export function useColorSchemePreference() {
  return useSyncExternalStore(subscribe, getColorSchemePreference, getColorSchemePreference);
}

/**
 * Test hook: forget the choice and use another file factory (or the default).
 * @param {() => object} [factory]
 */
export function resetColorSchemePreference(factory = defaultFile) {
  revision++;
  current = 'system';
  loaded = false;
  fileFactory = factory;
  listeners.clear();
}
