import Constants from 'expo-constants';
import { Platform } from 'react-native';

/** Default backend origin for the iOS simulator (shares the host's loopback). */
export const IosSimulatorDefaultApiUrl = 'http://127.0.0.1:8000';

/** Default backend origin for the Android emulator (10.0.2.2 is the host's loopback). */
export const AndroidEmulatorDefaultApiUrl = 'http://10.0.2.2:8000';

/**
 * Resolve the backend origin. EXPO_PUBLIC_API_URL (exposed through app.config.js extra.apiUrl)
 * wins; otherwise a platform default for local simulators and emulators is used.
 * @param {object} [options]
 * @param {string} [options.configured] Configured URL; defaults to the Expo config value.
 * @param {string} [options.os] Platform OS; defaults to Platform.OS.
 * @returns {string}
 */
export function resolveApiUrl({ configured, os } = {}) {
  const value = configured !== undefined ? configured : (Constants.expoConfig && Constants.expoConfig.extra && Constants.expoConfig.extra.apiUrl) || process.env.EXPO_PUBLIC_API_URL || '';
  if (value && value.trim()) return value.trim().replace(/\/+$/, '');
  const platform = os || Platform.OS;
  return platform === 'android' ? AndroidEmulatorDefaultApiUrl : IosSimulatorDefaultApiUrl;
}

/** App version from the Expo config. */
export function getAppVersion() {
  return (Constants.expoConfig && Constants.expoConfig.version) || '1.0.0';
}
