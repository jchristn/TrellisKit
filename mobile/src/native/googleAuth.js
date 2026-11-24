import { NativeModules, Platform, TurboModuleRegistry } from 'react-native';
import { isExpoGo } from './environment';

let cached;

/** Name the package registers its native module under. */
export const GoogleNativeModuleName = 'RNGoogleSignin';

/**
 * Whether the Google Sign-In native module is linked into this binary (react-native.config.js
 * leaves it out on iOS when EXPO_PUBLIC_GOOGLE_IOS_URL_SCHEME is empty).
 * @returns {boolean}
 */
export function hasGoogleNativeModule({ registry = TurboModuleRegistry, modules = NativeModules } = {}) {
  try {
    return Boolean((registry && registry.get && registry.get(GoogleNativeModuleName)) || (modules && modules[GoogleNativeModuleName]));
  } catch {
    return false;
  }
}

/**
 * Load @react-native-google-signin/google-signin, or null when its native module is missing
 * (Expo Go, or a build made without the config plugin). The package throws at import time when
 * the native module is absent, and Metro reports a throwing module factory as a fatal error
 * before a surrounding try block sees it, so the native module is checked before requiring.
 */
export function getGoogleSignInModule() {
  if (cached !== undefined) return cached;
  cached = null;
  if (isExpoGo() || !hasGoogleNativeModule()) return cached;
  try {
    cached = require('@react-native-google-signin/google-signin');
  } catch {
    cached = null;
  }
  return cached;
}

/** @returns {boolean} */
export function isGoogleSignInAvailable() {
  return Boolean(getGoogleSignInModule());
}

/**
 * Run Google Sign-In.
 * @param {{ webClientId?: string|null, iosClientId?: string|null }} config From GET /auth/providers.
 * @returns {Promise<{ idToken: string }|null>} Body for POST /auth/google, or null when cancelled.
 */
export async function signInWithGoogle(config) {
  const mod = getGoogleSignInModule();
  if (!mod) throw new Error('Google sign-in is not available in this build.');
  const { GoogleSignin, statusCodes } = mod;
  const options = { offlineAccess: false };
  if (config && config.webClientId) options.webClientId = config.webClientId;
  if (config && config.iosClientId) options.iosClientId = config.iosClientId;
  GoogleSignin.configure(options);
  try {
    if (Platform.OS === 'android') await GoogleSignin.hasPlayServices({ showPlayServicesUpdateDialog: true });
    const response = await GoogleSignin.signIn();
    if (!response || response.type === 'cancelled') return null;
    const idToken = response.data && response.data.idToken;
    if (!idToken) throw new Error('Google did not return an ID token.');
    return { idToken };
  } catch (error) {
    if (error && statusCodes && (error.code === statusCodes.SIGN_IN_CANCELLED || error.code === statusCodes.IN_PROGRESS)) return null;
    throw error;
  } finally {
    // Clear Google's cached account so the next sign-in shows the account chooser again.
    GoogleSignin.signOut().catch(() => {});
  }
}
