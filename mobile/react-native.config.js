/**
 * React Native autolinking overrides (read by Expo's autolinking during prebuild and EAS Build).
 *
 * Google Sign-In on iOS and the Facebook SDK on both platforms crash at runtime when their
 * native configuration is missing. When the matching environment variable is empty (see
 * app.config.js) the native module is left out of the build, so the JavaScript loader in
 * src/native finds no module and the app hides that sign-in button.
 */
const googleConfigured = (process.env.EXPO_PUBLIC_GOOGLE_IOS_URL_SCHEME || '').trim().startsWith('com.googleusercontent.apps.');
const facebookConfigured = Boolean((process.env.EXPO_PUBLIC_FACEBOOK_APP_ID || '').trim());

const dependencies = {};
if (!googleConfigured) {
  dependencies['@react-native-google-signin/google-signin'] = { platforms: { ios: null } };
}
if (!facebookConfigured) {
  dependencies['react-native-fbsdk-next'] = { platforms: { ios: null, android: null } };
}

module.exports = { dependencies };
