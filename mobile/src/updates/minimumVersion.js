import Constants from 'expo-constants';
import { Platform } from 'react-native';
import { getAppVersion } from '../config';

/**
 * Minimum binary version gate (docs/TRELLISKIT.md section 8.1). Native changes cannot ship as
 * code updates, so the property can require a newer store binary per platform:
 *
 *   "mobile": {
 *     "minimumBinaryVersion": { "ios": "1.2.0", "android": "1.2.0" },   // or one string for both
 *     "storeUrls": { "ios": "https://apps.apple.com/app/id123", "android": "https://play.google.com/..." },
 *     "appStoreId": "123"                                                // optional, builds the iOS link
 *   }
 *
 * The value comes from GET /api/v1/property (merged over the bundled copy by PropertyProvider; the
 * app manifest feeds the same property once it exists). Older binaries see a blocking
 * "Update required" screen with the store link.
 *
 * `mobile.latestBinaryVersion` (same shape, optional) only drives the dismissible "A new version is
 * available" hint (NewVersionHint.jsx).
 */

/**
 * Parse a dotted version ("1.2.3", "1.2", "v2") into numbers, or null.
 * @param {unknown} value
 * @returns {number[]|null}
 */
export function parseVersion(value) {
  if (typeof value !== 'string') return null;
  const match = value.trim().replace(/^v/i, '').match(/^\d+(\.\d+)*/);
  if (!match) return null;
  return match[0].split('.').map((part) => Number(part));
}

/**
 * Compare two versions: negative when a < b, 0 when equal, positive when a > b. Missing parts
 * count as 0 ("1.2" equals "1.2.0").
 * @returns {number|null} null when either is not a version.
 */
export function compareVersions(a, b) {
  const left = parseVersion(a);
  const right = parseVersion(b);
  if (!left || !right) return null;
  for (let i = 0; i < Math.max(left.length, right.length); i += 1) {
    const diff = (left[i] || 0) - (right[i] || 0);
    if (diff) return diff;
  }
  return 0;
}

/** A per-platform version field of property.mobile (one string, or { ios, android }), or null. */
function platformVersion(property, field, os) {
  const value = property && property.mobile ? property.mobile[field] : null;
  if (typeof value === 'string') return parseVersion(value) ? value.trim() : null;
  if (value && typeof value === 'object') {
    const entry = value[os];
    return typeof entry === 'string' && parseVersion(entry) ? entry.trim() : null;
  }
  return null;
}

/**
 * The minimum binary version for a platform, or null.
 * @param {object} property
 * @param {string} [os]
 */
export function minimumBinaryVersionFor(property, os = Platform.OS) {
  return platformVersion(property, 'minimumBinaryVersion', os);
}

/**
 * The latest store binary version for a platform (`mobile.latestBinaryVersion`, same shape as the
 * minimum), or null. Binaries below it get a dismissible "new version available" hint, never the
 * blocking screen.
 * @param {object} property
 * @param {string} [os]
 */
export function latestBinaryVersionFor(property, os = Platform.OS) {
  return platformVersion(property, 'latestBinaryVersion', os);
}

/**
 * The store page for this app, or null when the property does not give enough to build one.
 * @param {object} property
 * @param {string} [os]
 */
export function storeUrlFor(property, os = Platform.OS) {
  const mobile = (property && property.mobile) || {};
  const urls = mobile.storeUrls && typeof mobile.storeUrls === 'object' ? mobile.storeUrls : {};
  const explicit = typeof urls[os] === 'string' ? urls[os].trim() : '';
  if (/^https:\/\//i.test(explicit)) return explicit;
  if (os === 'ios') {
    const id = mobile.appStoreId !== undefined && mobile.appStoreId !== null ? String(mobile.appStoreId).trim() : '';
    return /^\d+$/.test(id) ? 'https://apps.apple.com/app/id' + id : null;
  }
  if (os === 'android' && mobile.androidPackage) return 'https://play.google.com/store/apps/details?id=' + encodeURIComponent(mobile.androidPackage);
  return null;
}

/** The installed binary's version (the store version, not the update). */
export function getBinaryVersion() {
  return Constants.nativeAppVersion || getAppVersion();
}

/**
 * Whether this binary is older than the property's minimum for the platform.
 * @param {object} property
 * @param {object} [options]
 * @param {string} [options.os]
 * @param {string} [options.binaryVersion]
 * @returns {boolean}
 */
export function isBinaryBelowMinimum(property, { os = Platform.OS, binaryVersion = getBinaryVersion() } = {}) {
  const minimum = minimumBinaryVersionFor(property, os);
  if (!minimum) return false;
  const diff = compareVersions(binaryVersion, minimum);
  return diff !== null && diff < 0;
}

/**
 * Whether a newer store binary than this one exists for the platform.
 * @param {object} property
 * @param {object} [options]
 * @param {string} [options.os]
 * @param {string} [options.binaryVersion]
 * @returns {boolean}
 */
export function isBinaryBelowLatest(property, { os = Platform.OS, binaryVersion = getBinaryVersion() } = {}) {
  const latest = latestBinaryVersionFor(property, os);
  if (!latest) return false;
  const diff = compareVersions(binaryVersion, latest);
  return diff !== null && diff < 0;
}
