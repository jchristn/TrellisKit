import { Platform } from 'react-native';
import { isExpoGo } from './environment';

let cached;

/**
 * Load expo-notifications, or null when push is not usable in this build. Expo Go on Android
 * has no push support (SDK 53 and later), so it returns null there; every caller treats null as
 * "notifications unavailable" and keeps going.
 * @returns {typeof import('expo-notifications')|null}
 */
export function getNotifications() {
  if (cached !== undefined) return cached;
  cached = null;
  if (isExpoGo() && Platform.OS === 'android') return cached;
  try {
    cached = require('expo-notifications');
  } catch {
    cached = null;
  }
  return cached;
}
