import { Platform } from 'react-native';

let cached;

/**
 * Load expo-apple-authentication on iOS, or null when it is not usable (Android, or a build
 * without the native module).
 * @returns {typeof import('expo-apple-authentication')|null}
 */
export function getAppleAuthentication() {
  if (cached !== undefined) return cached;
  cached = null;
  if (Platform.OS !== 'ios') return cached;
  try {
    cached = require('expo-apple-authentication');
  } catch {
    cached = null;
  }
  return cached;
}

/** @returns {Promise<boolean>} Whether Sign in with Apple can be offered on this device. */
export async function isAppleSignInAvailable() {
  const apple = getAppleAuthentication();
  if (!apple) return false;
  try {
    return await apple.isAvailableAsync();
  } catch {
    return false;
  }
}

/**
 * Create a nonce pair. Apple receives the SHA-256 hex digest; the server receives the raw value
 * and checks that its digest matches the `nonce` claim in the identity token.
 * @returns {Promise<{ raw: string, hashed: string }>}
 */
export async function createNonce() {
  const Crypto = require('expo-crypto');
  const raw = Crypto.randomUUID() + Crypto.randomUUID();
  const hashed = await Crypto.digestStringAsync(Crypto.CryptoDigestAlgorithm.SHA256, raw);
  return { raw, hashed };
}

/**
 * Run Sign in with Apple.
 * @returns {Promise<{ identityToken: string, nonce: string, givenName?: string, familyName?: string }|null>}
 *   The body for POST /auth/apple, or null when the user cancelled.
 */
export async function signInWithApple() {
  const apple = getAppleAuthentication();
  if (!apple) throw new Error('Sign in with Apple is not available.');
  const nonce = await createNonce();
  let credential;
  try {
    credential = await apple.signInAsync({
      requestedScopes: [apple.AppleAuthenticationScope.FULL_NAME, apple.AppleAuthenticationScope.EMAIL],
      nonce: nonce.hashed
    });
  } catch (error) {
    if (error && error.code === 'ERR_REQUEST_CANCELED') return null;
    throw error;
  }
  if (!credential || !credential.identityToken) throw new Error('Apple did not return an identity token.');
  const body = { identityToken: credential.identityToken, nonce: nonce.raw };
  const name = credential.fullName || {};
  if (name.givenName) body.givenName = name.givenName;
  if (name.familyName) body.familyName = name.familyName;
  return body;
}
