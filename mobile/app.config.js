/**
 * Expo app configuration for the TrellisKit mobile app.
 *
 * Everything property-specific (name, slug, scheme, bundle id, Android package, colors, icons,
 * splash, EAS project id) comes from the active property folder, selected only by path:
 * TK_PROPERTY_DIR, absolute or relative to the engine root (the folder that contains mobile/).
 * When it is unset the neutral sample property (samples/example) is used. Icons and splash
 * images live in <property>/mobile/ and are produced by scripts/generate-property-assets.js.
 * The public part of property.json is bundled as extra.property, the offline fallback for
 * GET /api/v1/property.
 *
 * The API base URL comes from EXPO_PUBLIC_API_URL when set. Otherwise preview and production
 * builds (APP_VARIANT from eas.json) use https://<property domains.api>, and development picks a
 * platform default at runtime (see src/config.js): http://127.0.0.1:8000 on the iOS simulator and
 * http://10.0.2.2:8000 on the Android emulator.
 *
 * Native sign-in configuration comes from environment variables (EAS environment variables in
 * cloud builds, or your shell locally). Every value defaults to empty so `expo export`,
 * `expo-doctor`, and Expo Go keep working without them:
 *
 * - EXPO_PUBLIC_GOOGLE_IOS_URL_SCHEME: reversed iOS client id, for example
 *   com.googleusercontent.apps.1234-abcd. Enables the Google Sign-In config plugin.
 * - EXPO_PUBLIC_FACEBOOK_APP_ID and EXPO_PUBLIC_FACEBOOK_CLIENT_TOKEN: enable the Facebook SDK
 *   config plugin.
 *
 * - EAS_PROJECT_ID: the EAS project id (from `npx eas-cli@latest init` or expo.dev). Overrides
 *   the property's mobile.easProjectId. Push tokens need it; without it the app skips push
 *   registration.
 * - GOOGLE_SERVICES_JSON: path to google-services.json (an EAS file environment variable in
 *   cloud builds). Android push through FCM needs it; without it Android builds still work but
 *   cannot receive pushes.
 *
 * When a provider's variable is empty its config plugin is skipped and react-native.config.js
 * leaves its native module unlinked, so the app hides that button instead of crashing.
 * The Google client ids themselves come from GET /api/v1/auth/providers at runtime.
 *
 * Live updates (expo-updates against TrellisKit's own GET /api/v1/updates/manifest): see
 * scripts/updates.js. The manifest URL comes from the property's domains.api (TK_UPDATES_URL or
 * EXPO_PUBLIC_API_URL override it), the runtime version policy is `fingerprint`, the channel
 * follows the EAS build profile (APP_VARIANT, or TK_UPDATES_CHANNEL) but only reaches
 * extra.updates.channel (the app sets the expo-channel-name header at runtime, so every channel
 * shares one runtime version), and every update request carries expo-channel-name, X-TK-Property,
 * X-TK-App, and X-TK-Install-Id. Code signing is mandatory: without
 * <property>/mobile/updates/certificate.pem updates are disabled and a warning is printed. The
 * standby certificate (key rotation) is checked too, with a warning when rotation is not ready.
 */
const path = require('path');
const { loadActiveProperty, propertySlug, publicProperty } = require('./scripts/property');
const { updatesConfig } = require('./scripts/updates');

const warned = new Set();

/** Print a build warning once per process (not under Jest, where app.config.js is evaluated per test file). */
function buildWarning(message) {
  if (!message || warned.has(message) || process.env.JEST_WORKER_ID) return;
  warned.add(message);
  console.warn('[trelliskit] Warning: ' + message);
}

/** Read an environment variable, trimmed, or '' when unset. */
function env(name) {
  return (process.env[name] || '').trim();
}

/** Google Sign-In plugin entry, or null when the iOS URL scheme is not configured. */
function googleSignInPlugin(iosUrlScheme) {
  if (!iosUrlScheme.startsWith('com.googleusercontent.apps.')) return null;
  return ['@react-native-google-signin/google-signin', { iosUrlScheme }];
}

/** Facebook SDK plugin entry (Limited Login, no tracking), or null without an app id. */
function facebookPlugin(appID, clientToken, displayName) {
  if (!appID) return null;
  return [
    'react-native-fbsdk-next',
    {
      appID,
      clientToken,
      displayName,
      scheme: 'fb' + appID,
      isAutoInitEnabled: false,
      autoLogAppEventsEnabled: false,
      advertiserIDCollectionEnabled: false
    }
  ];
}

module.exports = () => {
  const googleIosUrlScheme = env('EXPO_PUBLIC_GOOGLE_IOS_URL_SCHEME');
  const facebookAppId = env('EXPO_PUBLIC_FACEBOOK_APP_ID');
  const facebookClientToken = env('EXPO_PUBLIC_FACEBOOK_CLIENT_TOKEN');
  const googleServicesFile = env('GOOGLE_SERVICES_JSON');
  const { dir: propertyDir, property, mobileAssetsDir } = loadActiveProperty();
  const live = updatesConfig({ property, propertyDir, projectRoot: __dirname });
  buildWarning(live.warning);
  buildWarning(live.rotationWarning && 'Live updates key rotation for property "' + propertySlug(property) + '": ' + live.rotationWarning);
  const easProjectId = env('EAS_PROJECT_ID') || (property.mobile.easProjectId || '').trim();
  const colors = property.brand.colors || {};
  const light = colors.light || {};
  const dark = colors.dark || {};
  const asset = (file) => path.relative(__dirname, path.join(mobileAssetsDir, file)).split(path.sep).join('/');
  const variant = env('APP_VARIANT');
  const apiUrl = env('EXPO_PUBLIC_API_URL') || ((variant === 'production' || variant === 'preview') && property.domains.api ? 'https://' + property.domains.api : '');

  return {
    name: property.name,
    slug: (property.mobile.expoSlug || '').trim() || propertySlug(property),
    ...((property.mobile.expoOwner || '').trim() ? { owner: property.mobile.expoOwner.trim() } : null),
    scheme: property.mobile.scheme,
    version: '1.0.0',
    orientation: 'default',
    icon: asset('icon.png'),
    userInterfaceStyle: 'automatic',
    runtimeVersion: live.runtimeVersion,
    updates: live.updates,
    ios: {
      bundleIdentifier: property.mobile.bundleId,
      supportsTablet: true,
      usesAppleSignIn: true,
      // webcredentials: iOS offers to save and autofill the app's passwords for the website domain. The website
      // serves /.well-known/apple-app-site-association when the property sets mobile.appleTeamId.
      ...(property.domains && property.domains.web && property.mobile.appleTeamId ? { associatedDomains: ['webcredentials:' + property.domains.web] } : null),
      // HTTPS only (exempt encryption), so App Store Connect skips the export compliance prompt.
      config: { usesNonExemptEncryption: false }
    },
    android: {
      package: property.mobile.androidPackage,
      ...(googleServicesFile ? { googleServicesFile } : null),
      adaptiveIcon: {
        backgroundColor: '#FFFFFF',
        foregroundImage: asset('android-icon-foreground.png'),
        backgroundImage: asset('android-icon-background.png'),
        monochromeImage: asset('android-icon-monochrome.png')
      }
    },
    web: { favicon: asset('favicon.png') },
    plugins: [
      'expo-router',
      'expo-font',
      [
        'expo-splash-screen',
        {
          image: asset('splash-icon.png'),
          imageWidth: 200,
          resizeMode: 'contain',
          backgroundColor: '#FFFFFF',
          dark: {
            image: asset('splash-icon-dark.png'),
            backgroundColor: dark.themeColor || '#121619'
          }
        }
      ],
      'expo-dev-client',
      'expo-secure-store',
      'expo-web-browser',
      'expo-apple-authentication',
      [
        'expo-image-picker',
        {
          photosPermission: property.name + ' uses your photo library so you can choose a profile photo and share photos and videos in posts and comments.',
          cameraPermission: property.name + ' uses the camera so you can take photos and videos to share in posts and comments.',
          microphonePermission: property.name + ' uses the microphone to record sound with the videos you share.'
        }
      ],
      'expo-image',
      ['expo-video', { supportsBackgroundPlayback: false, supportsPictureInPicture: false }],
      // Phase 4: "near me" (foreground only), add to calendar, and the directory map
      // (MapLibre, or the platform map with the property's Android Google Maps key).
      ['expo-location', { locationWhenInUsePermission: property.name + ' uses your location only when you ask, to show places near you. It is rounded to about 1 km and never stored.', isIosBackgroundLocationEnabled: false, isAndroidBackgroundLocationEnabled: false }],
      // iOS 17+: add-only (write-only) calendar access; iOS 16 and earlier use NSCalendarsUsageDescription.
      ['expo-calendar', { calendarPermission: property.name + ' adds the events you choose to your calendar.', writeOnlyAccess: true, writeOnlyCalendarPermission: property.name + ' adds the events you choose to your calendar.' }],
      '@maplibre/maplibre-react-native',
      ['react-native-maps', property.mobile.googleMapsApiKey ? { androidGoogleMapsApiKey: property.mobile.googleMapsApiKey } : {}],
      'expo-iap',
      [
        'expo-notifications',
        {
          icon: asset('android-icon-monochrome.png'),
          color: light.primary || '#000000',
          defaultChannel: 'default'
        }
      ],
      // Listed before expo-updates on purpose: mods run in reverse registration order, so this one
      // runs after expo-updates has written the shared request headers and sets X-TK-App per platform.
      live.enabled ? ['./plugins/withUpdatesAppHeader', { bundleId: property.mobile.bundleId, androidPackage: property.mobile.androidPackage }] : null,
      live.includeCertificateChain ? './plugins/withUpdatesCertificateChain' : null,
      'expo-updates',
      googleSignInPlugin(googleIosUrlScheme),
      facebookPlugin(facebookAppId, facebookClientToken, property.name)
    ].filter(Boolean),
    extra: {
      apiUrl,
      property: publicProperty(property),
      updates: live.extra,
      googleSignInConfigured: Boolean(googleSignInPlugin(googleIosUrlScheme)),
      facebookConfigured: Boolean(facebookAppId),
      ...(easProjectId ? { eas: { projectId: easProjectId } } : null)
    }
  };
};
