/**
 * Config plugin: sets the X-TK-App update request header per platform (docs/TRELLISKIT.md
 * section 10.1). The expo `updates.requestHeaders` map is shared by iOS and Android, so it carries
 * the iOS bundle id; this plugin rewrites the native copies so iOS sends the bundle id and Android
 * sends the package. app.config.js lists it before expo-updates so its mods run after the expo-updates mods (mods run in reverse registration order) and these values win.
 */
const { AndroidConfig, withAndroidManifest, withExpoPlist } = require('expo/config-plugins');

const AndroidHeadersKey = 'expo.modules.updates.UPDATES_CONFIGURATION_REQUEST_HEADERS_KEY';

function headersFor(config, appId) {
  const base = (config.updates && config.updates.requestHeaders) || null;
  return base ? { ...base, 'X-TK-App': appId } : null;
}

module.exports = function withUpdatesAppHeader(config, { bundleId, androidPackage } = {}) {
  config = withExpoPlist(config, (cfg) => {
    const headers = headersFor(cfg, bundleId || (cfg.ios && cfg.ios.bundleIdentifier));
    if (headers) cfg.modResults.EXUpdatesRequestHeaders = headers;
    return cfg;
  });
  config = withAndroidManifest(config, (cfg) => {
    const headers = headersFor(cfg, androidPackage || (cfg.android && cfg.android.package));
    if (headers) {
      const app = AndroidConfig.Manifest.getMainApplicationOrThrow(cfg.modResults);
      AndroidConfig.Manifest.addMetaDataItemToMainApplication(app, AndroidHeadersKey, JSON.stringify(headers));
    }
    return cfg;
  });
  return config;
};
