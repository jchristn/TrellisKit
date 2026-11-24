import { delegationTimeLeft } from '@trelliskit/shared';

/**
 * Delegated property admin sessions (docs/MULTIPROPERTY_CONTRACT.md section 6.2). A platform
 * administrator starts one from the platform console (`POST /platform/properties/{id}/admin-session`);
 * the admin console then sends its access token with `X-TK-Property: <id>` on the platform host.
 * The session is kept in this tab only (session storage), lasts
 * `platform.delegatedSessionMinutes`, and is never refreshed.
 */

export const DELEGATION_KEY = 'tk.delegation';
export const PLATFORM_TOKEN_KEY = 'tk.platformToken';

/** The stored delegation `{ accessToken, accessTokenExpiresUtc, propertyId, propertyName }`, or null when missing or expired. */
export function readDelegation(now = Date.now()) {
  let value = null;
  try {
    value = JSON.parse(window.sessionStorage.getItem(DELEGATION_KEY) || 'null');
  } catch {
    value = null;
  }
  if (!value || typeof value !== 'object' || !value.accessToken || !value.propertyId) return null;
  if (!delegationTimeLeft(value, now).active) {
    clearDelegation();
    return null;
  }
  return value;
}

/** Store a delegated session returned by `createDelegatedSession`. */
export function writeDelegation(session, propertyName) {
  const value = {
    accessToken: session.accessToken,
    accessTokenExpiresUtc: session.accessTokenExpiresUtc,
    propertyId: session.propertyId,
    propertyName: propertyName || ''
  };
  try {
    window.sessionStorage.setItem(DELEGATION_KEY, JSON.stringify(value));
  } catch {
    // Session storage unavailable: the delegation cannot survive the navigation to /admin.
  }
  return value;
}

/** Forget the delegated session. */
export function clearDelegation() {
  try {
    window.sessionStorage.removeItem(DELEGATION_KEY);
  } catch {
    // Ignore storage failures.
  }
}

/** The stored platform administrator token (tkp_...), or null. */
export function readPlatformToken() {
  try {
    return window.sessionStorage.getItem(PLATFORM_TOKEN_KEY) || null;
  } catch {
    return null;
  }
}

/** Store or clear the platform administrator token. */
export function writePlatformToken(token) {
  try {
    if (token) window.sessionStorage.setItem(PLATFORM_TOKEN_KEY, token);
    else window.sessionStorage.removeItem(PLATFORM_TOKEN_KEY);
  } catch {
    // The token then lives only in memory.
  }
}

