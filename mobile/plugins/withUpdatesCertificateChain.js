/**
 * Config plugin: turns on expo-updates' "include manifest response certificate chain" code signing
 * option, which the Expo `updates` config does not expose. With it, a manifest signed by the
 * standby key and sent with its certificate in the `certificate_chain` part is accepted, because
 * that certificate is issued by the embedded one (docs/LIVE_UPDATES_CONTRACT.md decision 14,
 * scripts/updatesCertificates.js).
 *
 * Native keys (expo-updates 58): iOS Expo.plist `EXUpdatesCodeSigningIncludeManifestResponseCertificateChain`
 * (Bool, EXUpdates/UpdatesConfig.swift) and Android meta-data
 * `expo.modules.updates.CODE_SIGNING_INCLUDE_MANIFEST_RESPONSE_CERTIFICATE_CHAIN` (boolean,
 * UpdatesConfiguration.kt). Listed before expo-updates in app.config.js so its mods run last.
 */
const { AndroidConfig, withAndroidManifest, withExpoPlist } = require('expo/config-plugins');

const IosKey = 'EXUpdatesCodeSigningIncludeManifestResponseCertificateChain';
const AndroidKey = 'expo.modules.updates.CODE_SIGNING_INCLUDE_MANIFEST_RESPONSE_CERTIFICATE_CHAIN';

function withUpdatesCertificateChain(config) {
  config = withExpoPlist(config, (cfg) => {
    cfg.modResults[IosKey] = true;
    return cfg;
  });
  config = withAndroidManifest(config, (cfg) => {
    const app = AndroidConfig.Manifest.getMainApplicationOrThrow(cfg.modResults);
    AndroidConfig.Manifest.addMetaDataItemToMainApplication(app, AndroidKey, 'true');
    return cfg;
  });
  return config;
}

module.exports = withUpdatesCertificateChain;
module.exports.IosKey = IosKey;
module.exports.AndroidKey = AndroidKey;
