import { requireOptionalNativeModule } from 'expo';
import { isExpoGo } from './environment';

let cached;

/**
 * Load expo-iap, or null when the store is not usable in this build (Expo Go, or a binary built
 * without the module). Callers still handle initConnection failures, for example on a simulator
 * without a StoreKit configuration.
 * @returns {typeof import('expo-iap')|null}
 */
export function getIap() {
  if (cached !== undefined) return cached;
  cached = null;
  if (isExpoGo()) return cached;
  try {
    if (!requireOptionalNativeModule('ExpoIap')) return cached;
    cached = require('expo-iap');
  } catch {
    cached = null;
  }
  return cached;
}
