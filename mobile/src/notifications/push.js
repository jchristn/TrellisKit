import Constants from 'expo-constants';
import * as Device from 'expo-device';
import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';
import { getAppVersion } from '../config';
import { shouldSuppressPush } from '../messaging/activeConversation';
import { getNotifications } from '../native/notifications';
import strings from '../strings';

/** Secure-store key holding this device's Expo push token. */
export const PushTokenKey = 'tk.pushToken';

/**
 * Push token route (docs/PHASE2_CONTRACT.md section 8.7): Signed in, under /me so a member whose
 * membership lapsed still gets account notices. A server without it answers 404 and registration
 * waits for the next launch.
 */
export const PushTokensPath = '/me/push-tokens';

/** Android notification channel id the server sends every push on (contract 8.7). */
export const DefaultChannelId = 'default';

/**
 * Platform value for the push token body: `Ios` or `Android` (string enum casing, contract 8.7).
 * @param {string} os Platform.OS.
 */
export function pushPlatform(os = Platform.OS) {
  return os === 'android' ? 'Android' : 'Ios';
}

/** Statuses that mean the push token route is not on this server yet. */
const NOT_AVAILABLE = [404, 405, 501];

let registeredKey = null;
let deferred = false;
let handlerConfigured = false;

/** Forget per-launch registration state (tests, and sign-in as a different member). */
export function resetPushRegistrationState() {
  registeredKey = null;
  deferred = false;
}

/**
 * EAS project id from the Expo config (extra.eas.projectId, set from EAS_PROJECT_ID in
 * app.config.js) or the EAS build metadata.
 * @param {typeof Constants} [constants]
 * @returns {string|null}
 */
export function easProjectId(constants = Constants) {
  const extra = constants && constants.expoConfig && constants.expoConfig.extra;
  return (extra && extra.eas && extra.eas.projectId) || (constants && constants.easConfig && constants.easConfig.projectId) || null;
}

/** A device label for the push token list: the model ("iPhone 16"), never the user-chosen device name. */
export function defaultDeviceName() {
  return Device.modelName || (Platform.OS === 'ios' ? 'iPhone or iPad' : 'Android device');
}

function isGranted(Notifications, permission) {
  if (!permission) return false;
  if (permission.granted || permission.status === 'granted') return true;
  const provisional = Notifications.IosAuthorizationStatus && Notifications.IosAuthorizationStatus.PROVISIONAL;
  return Boolean(permission.ios && provisional !== undefined && permission.ios.status === provisional);
}

/**
 * How a push that arrives while the app is open is presented: a banner without sound or badge
 * changes, except a `DirectMessage` for the conversation on screen, which shows nothing
 * (docs/PHASE5_CONTRACT.md section 4.9).
 * @param {any} notification expo-notifications Notification.
 */
export function foregroundPresentation(notification) {
  const content = notification && notification.request && notification.request.content;
  const data = (content && content.data) || null;
  if (shouldSuppressPush(data)) return { shouldShowBanner: false, shouldShowList: false, shouldPlaySound: false, shouldSetBadge: false };
  return { shouldShowBanner: true, shouldShowList: true, shouldPlaySound: false, shouldSetBadge: false };
}

/** Show pushes that arrive while the app is open as a banner, without sound or badge changes. */
export function configureForegroundHandling(Notifications = getNotifications()) {
  if (!Notifications || handlerConfigured || typeof Notifications.setNotificationHandler !== 'function') return;
  handlerConfigured = true;
  Notifications.setNotificationHandler({
    handleNotification: async (notification) => foregroundPresentation(notification)
  });
}

/**
 * Create the Android notification channel. Android 13 and later only shows the permission
 * prompt after a channel exists, so this runs before any permission request.
 * @returns {Promise<boolean>} Whether a channel was set.
 */
export async function ensureAndroidChannels({ Notifications = getNotifications(), os = Platform.OS } = {}) {
  if (os !== 'android' || !Notifications) return false;
  try {
    await Notifications.setNotificationChannelAsync(DefaultChannelId, {
      name: strings.push.defaultChannel,
      description: strings.push.defaultChannelDescription,
      importance: Notifications.AndroidImportance ? Notifications.AndroidImportance.DEFAULT : 3,
      vibrationPattern: [0, 250, 250, 250],
      showBadge: true
    });
    return true;
  } catch {
    return false;
  }
}

/**
 * Current permission without prompting.
 * @returns {Promise<'granted'|'denied'|'undetermined'|'unavailable'>}
 */
export async function getPushPermission({ Notifications = getNotifications() } = {}) {
  if (!Notifications) return 'unavailable';
  try {
    const current = await Notifications.getPermissionsAsync();
    if (isGranted(Notifications, current)) return 'granted';
    return current && current.canAskAgain === false ? 'denied' : 'undetermined';
  } catch {
    return 'unavailable';
  }
}

/**
 * Ask for notification permission (the onboarding notification step).
 * @returns {Promise<'granted'|'denied'|'unavailable'>}
 */
export async function requestPushPermission({ Notifications = getNotifications(), os = Platform.OS } = {}) {
  if (!Notifications) return 'unavailable';
  await ensureAndroidChannels({ Notifications, os });
  try {
    const current = await Notifications.getPermissionsAsync();
    if (isGranted(Notifications, current)) return 'granted';
    if (current && current.canAskAgain === false) return 'denied';
    const next = await Notifications.requestPermissionsAsync({ ios: { allowAlert: true, allowBadge: true, allowSound: true } });
    return isGranted(Notifications, next) ? 'granted' : 'denied';
  } catch {
    return 'unavailable';
  }
}

/** @returns {Promise<string|null>} */
export async function getStoredPushToken() {
  try {
    return await SecureStore.getItemAsync(PushTokenKey);
  } catch {
    return null;
  }
}

/**
 * Get this device's Expo push token (permission must already be granted) and keep it in secure
 * storage.
 * @returns {Promise<{ status: 'ok', token: string }|{ status: 'unavailable'|'noProjectId'|'simulator'|'error', error?: any }>}
 */
export async function obtainPushToken({ Notifications = getNotifications(), projectId = easProjectId(), isDevice = Device.isDevice } = {}) {
  if (!Notifications) return { status: 'unavailable' };
  if (!projectId) return { status: 'noProjectId' };
  if (!isDevice) return { status: 'simulator' };
  try {
    const result = await Notifications.getExpoPushTokenAsync({ projectId });
    const token = result && result.data;
    if (!token) return { status: 'error' };
    try {
      await SecureStore.setItemAsync(PushTokenKey, token);
    } catch {
      // The token still registers this launch; it is fetched again next launch.
    }
    return { status: 'ok', token };
  } catch (error) {
    return { status: 'error', error };
  }
}

/**
 * Send the stored push token to POST /api/v1/me/push-tokens as
 * `{ token, platform: 'Ios'|'Android', deviceName, appVersion }`.
 *
 * A 404 (or 405 or 501) means "not on this server yet": nothing else is tried this launch and the
 * next launch tries again. Each token registers once per launch per member.
 *
 * @param {object} api MobileApiClient.
 * @param {{ userId?: string, os?: string, deviceName?: string, token?: string }} [options]
 * @returns {Promise<{ status: 'registered'|'deferred'|'noToken'|'error', token?: string, error?: any }>}
 */
export async function registerPushToken(api, { userId = '', os = Platform.OS, deviceName = defaultDeviceName(), token: given } = {}) {
  const token = given || (await getStoredPushToken());
  if (!token) return { status: 'noToken' };
  if (deferred) return { status: 'deferred' };
  const key = userId + '|' + token;
  if (registeredKey === key) return { status: 'registered', token };
  try {
    await api.post(PushTokensPath, { token, platform: pushPlatform(os), deviceName, appVersion: getAppVersion() });
  } catch (error) {
    if (error && NOT_AVAILABLE.includes(error.status)) {
      deferred = true;
      return { status: 'deferred' };
    }
    return { status: 'error', error };
  }
  registeredKey = key;
  return { status: 'registered', token };
}

/** Whether this launch registered a token for the signed-in member. */
export function hasRegisteredToken() {
  return Boolean(registeredKey);
}

/**
 * Remove this device's token from the server (DELETE /api/v1/me/push-tokens with `{ token }`):
 * before sign-out, so a signed-out phone stops getting the member's notifications, and when the
 * device permission was turned off. Uses the token registered this launch, else the stored one.
 * Best effort with a 5 second limit; the server also deletes tokens with their session.
 * @param {object} api MobileApiClient.
 */
export async function unregisterPushToken(api) {
  const token = registeredKey ? registeredKey.slice(registeredKey.indexOf('|') + 1) : await getStoredPushToken();
  registeredKey = null;
  if (!token) return false;
  const controller = typeof AbortController === 'function' ? new AbortController() : null;
  const timer = controller ? setTimeout(() => controller.abort(), 5000) : null;
  try {
    await api.request('DELETE', PushTokensPath, { body: { token }, ...(controller ? { signal: controller.signal } : null) });
    return true;
  } catch {
    return false;
  } finally {
    if (timer) clearTimeout(timer);
  }
}

/**
 * The onboarding "Turn on notifications" action: ask, then get, store, and register the token.
 * @returns {Promise<{ permission: string, token?: string, registration?: object }>}
 */
export async function enablePushNotifications(api, { userId, Notifications = getNotifications(), os = Platform.OS, projectId, isDevice } = {}) {
  const permission = await requestPushPermission({ Notifications, os });
  if (permission !== 'granted') return { permission };
  const options = { Notifications };
  if (projectId !== undefined) options.projectId = projectId;
  if (isDevice !== undefined) options.isDevice = isDevice;
  const obtained = await obtainPushToken(options);
  if (obtained.status !== 'ok') return { permission, tokenStatus: obtained.status };
  const registration = await registerPushToken(api, { userId, os, token: obtained.token });
  return { permission, token: obtained.token, registration };
}

/**
 * Launch-time sync for a signed-in member: when permission was granted earlier, refresh the
 * token (it can change after a reinstall or restore) and register it. Never prompts.
 */
export async function syncPushRegistration(api, { userId, Notifications = getNotifications(), os = Platform.OS, projectId, isDevice } = {}) {
  if (!Notifications) return { status: 'unavailable' };
  configureForegroundHandling(Notifications);
  await ensureAndroidChannels({ Notifications, os });
  if ((await getPushPermission({ Notifications })) !== 'granted') return { status: 'notGranted' };
  const options = { Notifications };
  if (projectId !== undefined) options.projectId = projectId;
  if (isDevice !== undefined) options.isDevice = isDevice;
  const obtained = await obtainPushToken(options);
  const token = obtained.status === 'ok' ? obtained.token : await getStoredPushToken();
  if (!token) return { status: obtained.status };
  return registerPushToken(api, { userId, os, token });
}

/**
 * Foreground check (contract 8.10): re-read the device permission and make the server match it.
 * Granted registers (once per launch); not granted removes a token this device registered.
 * @returns {Promise<{ permission: string, status?: string }>}
 */
export async function reconcilePushPermission(api, { userId, Notifications = getNotifications(), os = Platform.OS, projectId, isDevice } = {}) {
  const permission = await getPushPermission({ Notifications });
  if (permission === 'granted') {
    const result = await syncPushRegistration(api, { userId, Notifications, os, projectId, isDevice });
    return { permission, status: result.status };
  }
  if (permission !== 'unavailable' && (registeredKey || (await getStoredPushToken()))) {
    await unregisterPushToken(api);
    try {
      await SecureStore.deleteItemAsync(PushTokenKey);
    } catch {
      // Nothing stored.
    }
    return { permission, status: 'removed' };
  }
  return { permission };
}
