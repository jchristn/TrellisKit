import Constants from 'expo-constants';

/**
 * Defensive access to expo-updates and the build's live update settings (app.config.js
 * extra.updates, from scripts/updates.js).
 */

let cached;

/**
 * The expo-updates module, or null when it cannot be loaded.
 * @returns {typeof import('expo-updates')|null}
 */
export function getUpdatesModule() {
  if (cached !== undefined) return cached;
  try {
    cached = require('expo-updates');
  } catch {
    cached = null;
  }
  return cached;
}

/** Forget the cached module (tests). */
export function resetUpdatesModule() {
  cached = undefined;
}

/**
 * The id of the running update (embedded or downloaded), or null (development, Expo Go, or
 * updates disabled).
 * @returns {string|null}
 */
export function getCurrentUpdateId() {
  const updates = getUpdatesModule();
  const id = updates && updates.isEnabled ? updates.updateId : null;
  return typeof id === 'string' && id ? id : null;
}

/**
 * Build-time live update settings.
 * @returns {{ enabled: boolean, channel: string, checkIntervalMinutes: number, url?: string }}
 */
export function getUpdatesSettings() {
  const extra = (Constants.expoConfig && Constants.expoConfig.extra) || {};
  const value = extra.updates && typeof extra.updates === 'object' ? extra.updates : {};
  return { enabled: Boolean(value.enabled), channel: value.channel || 'development', checkIntervalMinutes: value.checkIntervalMinutes || 15, url: value.url };
}
