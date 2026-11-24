import { Platform } from 'react-native';
import { signInWithApple } from './appleAuth';
import { isFacebookAvailable, signInWithFacebook } from './facebookAuth';
import { isGoogleSignInAvailable, signInWithGoogle } from './googleAuth';

/** Providers in display order. */
export const SocialProviders = Object.freeze(['apple', 'google', 'facebook']);

/**
 * Decide which sign-in buttons to show.
 *
 * A provider appears only when GET /auth/providers enables it and the native module is present.
 * On iOS, Google and Facebook are shown only when Sign in with Apple is also shown (App Store
 * Guideline 4.8).
 *
 * @param {object} providers GET /auth/providers response.
 * @param {{ os?: string, appleAvailable: boolean, googleAvailable?: boolean, facebookAvailable?: boolean }} native
 * @returns {{ apple: boolean, google: boolean, facebook: boolean, email: boolean }}
 */
export function visibleProviders(providers, native) {
  const os = native.os || Platform.OS;
  const p = providers || {};
  const apple = os === 'ios' && Boolean(p.apple && p.apple.enabled) && Boolean(native.appleAvailable);
  let google = Boolean(p.google && p.google.enabled) && Boolean(native.googleAvailable);
  let facebook = Boolean(p.facebook && p.facebook.enabled) && Boolean(native.facebookAvailable);
  if (os === 'ios' && !apple) {
    google = false;
    facebook = false;
  }
  return { apple, google, facebook, email: p.email !== false };
}

/** Native availability of Google and Facebook (Apple is async; see isAppleSignInAvailable). */
export function nativeSocialAvailability() {
  return { googleAvailable: isGoogleSignInAvailable(), facebookAvailable: isFacebookAvailable() };
}

/**
 * Run the native flow for a provider and return the request body for /auth/{provider} or
 * /me/identities/{provider}, or null when the user cancelled.
 * @param {'apple'|'google'|'facebook'} provider
 * @param {object} providers GET /auth/providers response.
 */
export function getSocialCredential(provider, providers) {
  if (provider === 'apple') return signInWithApple();
  if (provider === 'google') return signInWithGoogle(providers && providers.google);
  if (provider === 'facebook') return signInWithFacebook();
  return Promise.reject(new Error('Unknown provider ' + provider));
}
