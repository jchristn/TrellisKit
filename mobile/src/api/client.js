import { getAppVersion, resolveApiUrl } from '../config';
import { identityHeaders } from './identity';
import { MobileApiClient } from './MobileApiClient';
import { tokenStore } from './tokenStore';

let client = null;

/**
 * Shared API client for the app. Fetch is looked up on every call so tests can replace
 * globalThis.fetch.
 * @returns {MobileApiClient}
 */
export function getApiClient() {
  if (!client) {
    client = new MobileApiClient({
      baseUrl: resolveApiUrl(),
      appVersion: getAppVersion(),
      tokenStore,
      fetchImpl: (...args) => globalThis.fetch(...args),
      timeoutMs: 15000,
      identityHeaders: () => identityHeaders()
    });
  }
  return client;
}

/** Drop the cached client (used by tests). */
export function resetApiClient() {
  client = null;
}
