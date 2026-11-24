import { NativeModules, Platform } from 'react-native';
import { createNonce } from './appleAuth';
import { isExpoGo } from './environment';

let cached;

/** Load react-native-fbsdk-next, or null when its native modules are missing (Expo Go). */
export function getFacebookModule() {
  if (cached !== undefined) return cached;
  cached = null;
  if (isExpoGo() || !NativeModules.FBLoginManager) return cached;
  try {
    cached = require('react-native-fbsdk-next');
  } catch {
    cached = null;
  }
  return cached;
}

/** @returns {boolean} */
export function isFacebookAvailable() {
  return Boolean(getFacebookModule());
}

/**
 * Run Facebook Login. iOS uses Limited Login, which returns an OIDC JWT bound to a nonce;
 * Android uses classic login and returns an access token.
 * @returns {Promise<{ token: string, tokenType: 'Jwt'|'AccessToken', nonce?: string }|null>}
 *   Body for POST /auth/facebook, or null when cancelled.
 */
export async function signInWithFacebook() {
  const fb = getFacebookModule();
  if (!fb) throw new Error('Facebook sign-in is not available in this build.');
  const { LoginManager, AccessToken, AuthenticationToken, Settings } = fb;
  if (Settings && Settings.initializeSDK) Settings.initializeSDK();
  LoginManager.logOut();

  if (Platform.OS === 'ios') {
    const { raw } = await createNonce();
    const result = await LoginManager.logInWithPermissions(['public_profile', 'email'], 'limited', raw);
    if (!result || result.isCancelled) return null;
    const auth = await AuthenticationToken.getAuthenticationTokenIOS();
    if (!auth || !auth.authenticationToken) throw new Error('Facebook did not return a token.');
    return { token: auth.authenticationToken, tokenType: 'Jwt', nonce: raw };
  }

  const result = await LoginManager.logInWithPermissions(['public_profile', 'email']);
  if (!result || result.isCancelled) return null;
  const token = await AccessToken.getCurrentAccessToken();
  if (!token || !token.accessToken) throw new Error('Facebook did not return a token.');
  return { token: token.accessToken, tokenType: 'AccessToken' };
}
