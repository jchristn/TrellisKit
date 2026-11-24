import { Platform } from 'react-native';
import { getBundledProperty } from '../property/property';
import { getCurrentUpdateId } from '../updates/runtime';

/**
 * Request identification for multi-property deployments (docs/TRELLISKIT.md sections 8.1 and
 * 10.1). Every API call, media request, push token registration, store verification, and update
 * check carries:
 *
 * - `X-TK-Property`: the bundled property id (fixed per build).
 * - `X-TK-App`: the iOS bundle id or the Android package of this binary.
 * - `X-Client-Update-Id`: the running update id (embedded or downloaded), when expo-updates
 *   reports one, for update adoption telemetry.
 */

/**
 * The app id the backend matches against the property's registered apps.
 * @param {object} [property]
 * @param {string} [os]
 * @returns {string}
 */
export function appIdFor(property = getBundledProperty(), os = Platform.OS) {
  const mobile = (property && property.mobile) || {};
  return (os === 'android' ? mobile.androidPackage : mobile.bundleId) || '';
}

/**
 * Identification headers for one request.
 * @param {object} [options]
 * @param {object} [options.property] Defaults to the bundled property.
 * @param {string} [options.os] Defaults to Platform.OS.
 * @param {string|null} [options.updateId] Defaults to the running update id.
 * @returns {Record<string, string>}
 */
export function identityHeaders({ property = getBundledProperty(), os = Platform.OS, updateId = getCurrentUpdateId() } = {}) {
  const headers = {};
  if (property && property.id) headers['X-TK-Property'] = property.id;
  const app = appIdFor(property, os);
  if (app) headers['X-TK-App'] = app;
  if (updateId) headers['X-Client-Update-Id'] = updateId;
  return headers;
}
