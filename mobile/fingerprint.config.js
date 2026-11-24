/**
 * Fingerprint (runtime version) settings. `extra` (the bundled property, API URL, and update
 * settings, including the build's update channel) is read by JavaScript only, so changing it must
 * not change the runtime version and strand installed binaries without updates. Everything native
 * (plugins, ids, permissions, update URL and headers, code signing settings) still counts.
 *
 * The update channel is deliberately not native: the embedded update request headers are the same
 * for every channel (scripts/updates.js) and the app sets the real `expo-channel-name` at runtime
 * (src/updates/requestHeaders.js). So preview and production binaries of one commit share one
 * runtime version and the server can promote an update between channels
 * (docs/LIVE_UPDATES_CONTRACT.md decision 15). __tests__/fingerprint.test.js proves it by
 * resolving the runtime version for each channel.
 *
 * The `android` and `ios` package.json scripts are skipped while they do not use `expo run`: EAS
 * Build runs prebuild, which rewrites them to `expo run:android` and `expo run:ios`, so without the
 * skip the runtime version computed on EAS differs from the one computed locally and the build
 * fails in its Configure expo-updates phase.
 * @type {import('expo/fingerprint').Config}
 */
const { SourceSkips } = require('expo/fingerprint');

module.exports = {
  sourceSkips: SourceSkips.ExpoConfigExtraSection | SourceSkips.PackageJsonAndroidAndIosScriptsIfNotContainRun
};
