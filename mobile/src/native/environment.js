import Constants from 'expo-constants';

/**
 * Whether the app is running inside Expo Go. Expo Go does not contain the native code for
 * Google Sign-In, Facebook Login, or in-app purchases, so those features are hidden there.
 * @returns {boolean}
 */
export function isExpoGo() {
  return Constants.executionEnvironment === 'storeClient';
}
