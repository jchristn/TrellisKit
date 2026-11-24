import { loadScript, randomHex, sha256Hex } from './browser.js';

export const GOOGLE_SCRIPT = 'https://accounts.google.com/gsi/client';
export const APPLE_SCRIPT = 'https://appleid.cdn-apple.com/appleauth/static/jsapi/appleid/1/en_US/appleid.auth.js';
export const FACEBOOK_SCRIPT = 'https://connect.facebook.net/en_US/sdk.js';
export const FACEBOOK_API_VERSION = 'v21.0';

/** Thrown when the member closes a provider popup; callers treat it as a quiet cancel. */
export class SocialCancelled extends Error {
  constructor() {
    super('cancelled');
    this.name = 'SocialCancelled';
  }
}

/**
 * Sign in with Apple JS in popup mode. A random raw nonce is generated; Apple receives its
 * SHA-256 hex (embedded in the identity token) and the backend receives the raw value, which it
 * hashes and compares.
 * @param {string} clientId Apple Services ID (`apple.webClientId`).
 * @returns {Promise<{ identityToken: string, nonce: string, givenName?: string, familyName?: string }>}
 */
export async function appleCredential(clientId) {
  await loadScript(APPLE_SCRIPT);
  const rawNonce = randomHex(32);
  const hashedNonce = await sha256Hex(rawNonce);
  window.AppleID.auth.init({
    clientId,
    scope: 'name email',
    redirectURI: window.location.origin + '/auth/apple/callback',
    state: randomHex(16),
    nonce: hashedNonce,
    usePopup: true
  });
  let result;
  try {
    result = await window.AppleID.auth.signIn();
  } catch (err) {
    const code = err && (err.error || err.message);
    if (code === 'popup_closed_by_user' || code === 'user_cancelled_authorize') throw new SocialCancelled();
    throw err instanceof Error ? err : new Error(String(code || 'Apple sign-in failed'));
  }
  const name = (result && result.user && result.user.name) || {};
  const body = { identityToken: result.authorization.id_token, nonce: rawNonce };
  if (name.firstName) body.givenName = name.firstName;
  if (name.lastName) body.familyName = name.lastName;
  return body;
}

let facebookAppId = null;

/**
 * Facebook Login with the JS SDK. Returns the access token body for `/auth/facebook`.
 * @param {string} appId
 * @returns {Promise<{ token: string, tokenType: 'AccessToken' }>}
 */
export async function facebookCredential(appId) {
  await loadScript(FACEBOOK_SCRIPT);
  if (facebookAppId !== appId) {
    window.FB.init({ appId, version: FACEBOOK_API_VERSION, cookie: false, xfbml: false });
    facebookAppId = appId;
  }
  const response = await new Promise((resolve) => window.FB.login(resolve, { scope: 'public_profile,email' }));
  if (!response || !response.authResponse || !response.authResponse.accessToken) throw new SocialCancelled();
  return { token: response.authResponse.accessToken, tokenType: 'AccessToken' };
}

/** Google Identity Services accepts button widths from 200 to 400 pixels. */
export const GOOGLE_BUTTON_MIN_WIDTH = 200;
export const GOOGLE_BUTTON_MAX_WIDTH = 400;

/** Clamp a container width to what Google's button accepts (300 when the width is unknown). */
export function googleButtonWidth(containerWidth) {
  const width = Math.round(Number(containerWidth) || 0);
  if (width <= 0) return 300;
  return Math.min(GOOGLE_BUTTON_MAX_WIDTH, Math.max(GOOGLE_BUTTON_MIN_WIDTH, width));
}

/**
 * Render the Google Identity Services button into `element`. `onCredential` receives the ID
 * token (JWT) for `/auth/google` as `{ idToken }`. `width` (pixels, clamped to Google's 200 to 400)
 * lets the caller match the other sign-in buttons; calling again re-renders at a new width.
 * @returns {Promise<void>}
 */
export async function renderGoogleButton(element, { clientId, onCredential, text = 'continue_with', theme = 'outline', width }) {
  await loadScript(GOOGLE_SCRIPT);
  if (!element || !window.google || !window.google.accounts) return;
  window.google.accounts.id.initialize({
    client_id: clientId,
    callback: (response) => { if (response && response.credential) onCredential({ idToken: response.credential }); },
    ux_mode: 'popup',
    auto_select: false,
    cancel_on_tap_outside: true
  });
  window.google.accounts.id.renderButton(element, { type: 'standard', theme, size: 'large', text, shape: 'rectangular', width: googleButtonWidth(width), logo_alignment: 'left' });
}
