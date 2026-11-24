import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import Constants from 'expo-constants';
import * as Notifications from 'expo-notifications';
import * as SecureStore from 'expo-secure-store';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import {
  easProjectId,
  enablePushNotifications,
  ensureAndroidChannels,
  obtainPushToken,
  PushTokenKey,
  registerPushToken,
  requestPushPermission,
  resetPushRegistrationState
} from '../src/notifications/push';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';

const APP_DIR = path.resolve(__dirname, '../app');
const TOKEN = 'ExponentPushToken[test-device]';
const granted = { granted: true, status: 'granted', canAskAgain: true };
const denied = { granted: false, status: 'denied', canAskAgain: false };
const undetermined = { granted: false, status: 'undetermined', canAskAgain: true };

function apiError(status) {
  const error = new Error('HTTP ' + status);
  error.status = status;
  return error;
}

describe('push registration', () => {
  beforeEach(() => {
    resetEnvironment();
    jest.clearAllMocks();
    Notifications.getPermissionsAsync.mockResolvedValue(undetermined);
    Notifications.requestPermissionsAsync.mockResolvedValue(granted);
    Notifications.getExpoPushTokenAsync.mockResolvedValue({ type: 'expo', data: TOKEN });
  });

  test('a denied permission stops before asking for a token or calling the server', async () => {
    Notifications.requestPermissionsAsync.mockResolvedValue(denied);
    const api = { post: jest.fn() };
    const result = await enablePushNotifications(api, { userId: 'usr_1', projectId: 'proj', isDevice: true });
    expect(result).toEqual({ permission: 'denied' });
    expect(Notifications.getExpoPushTokenAsync).not.toHaveBeenCalled();
    expect(api.post).not.toHaveBeenCalled();
    expect(SecureStore.__store.has(PushTokenKey)).toBe(false);
  });

  test('a permission already refused in Settings is not requested again', async () => {
    Notifications.getPermissionsAsync.mockResolvedValue(denied);
    expect(await requestPushPermission({ Notifications, os: 'ios' })).toBe('denied');
    expect(Notifications.requestPermissionsAsync).not.toHaveBeenCalled();
  });

  test('the token is obtained with the EAS project id and kept in secure storage', async () => {
    const result = await obtainPushToken({ Notifications, projectId: 'proj-123', isDevice: true });
    expect(result).toEqual({ status: 'ok', token: TOKEN });
    expect(Notifications.getExpoPushTokenAsync).toHaveBeenCalledWith({ projectId: 'proj-123' });
    expect(SecureStore.__store.get(PushTokenKey)).toBe(TOKEN);
  });

  test('no project id or a simulator means no token', async () => {
    expect(await obtainPushToken({ Notifications, projectId: null, isDevice: true })).toEqual({ status: 'noProjectId' });
    expect(await obtainPushToken({ Notifications, projectId: 'p', isDevice: false })).toEqual({ status: 'simulator' });
    expect(await obtainPushToken({ Notifications: null, projectId: 'p', isDevice: true })).toEqual({ status: 'unavailable' });
    expect(Notifications.getExpoPushTokenAsync).not.toHaveBeenCalled();
    expect(easProjectId({ expoConfig: { extra: { eas: { projectId: 'from-config' } } } })).toBe('from-config');
    expect(easProjectId({ expoConfig: { extra: {} }, easConfig: { projectId: 'from-eas' } })).toBe('from-eas');
  });

  test('registration posts token, platform, device name, and app version to /me/push-tokens', async () => {
    SecureStore.__store.set(PushTokenKey, TOKEN);
    const api = { post: jest.fn(async () => ({ id: 'ptk_1' })) };
    expect(await registerPushToken(api, { userId: 'usr_1', os: 'ios' })).toEqual({ status: 'registered', token: TOKEN });
    expect(api.post).toHaveBeenCalledWith('/me/push-tokens', { token: TOKEN, platform: 'Ios', deviceName: 'iPhone 16', appVersion: '1.0.0' });
    // Once per launch and member.
    await registerPushToken(api, { userId: 'usr_1', os: 'ios' });
    expect(api.post).toHaveBeenCalledTimes(1);
  });

  test('a 404 defers registration for this launch and the next launch tries again', async () => {
    SecureStore.__store.set(PushTokenKey, TOKEN);
    const api = { post: jest.fn(async () => Promise.reject(apiError(404))) };
    expect(await registerPushToken(api, { userId: 'usr_1', os: 'android' })).toEqual({ status: 'deferred' });
    expect(await registerPushToken(api, { userId: 'usr_1', os: 'android' })).toEqual({ status: 'deferred' });
    expect(api.post).toHaveBeenCalledTimes(1);
    // The token is kept for later.
    expect(SecureStore.__store.get(PushTokenKey)).toBe(TOKEN);

    resetPushRegistrationState(); // next launch
    api.post.mockResolvedValue({ id: 'ptk_1' });
    expect((await registerPushToken(api, { userId: 'usr_1', os: 'android' })).status).toBe('registered');
    expect(api.post).toHaveBeenCalledTimes(2);
  });

  test('other server errors are not a deferral', async () => {
    SecureStore.__store.set(PushTokenKey, TOKEN);
    const api = { post: jest.fn(async () => Promise.reject(apiError(500))) };
    expect((await registerPushToken(api, { userId: 'usr_1' })).status).toBe('error');
    expect((await registerPushToken(api, { userId: 'usr_1' })).status).toBe('error');
    expect(api.post).toHaveBeenCalledTimes(2);
  });

  test('without a stored token nothing is sent', async () => {
    const api = { post: jest.fn() };
    expect(await registerPushToken(api, { userId: 'usr_1' })).toEqual({ status: 'noToken' });
    expect(api.post).not.toHaveBeenCalled();
  });

  test('Android gets its notification channel before the permission prompt', async () => {
    const order = [];
    Notifications.setNotificationChannelAsync.mockImplementation(async (id) => order.push('channel:' + id));
    Notifications.requestPermissionsAsync.mockImplementation(async () => (order.push('request'), granted));
    expect(await requestPushPermission({ Notifications, os: 'android' })).toBe('granted');
    expect(order).toEqual(['channel:default', 'request']);
    expect(Notifications.setNotificationChannelAsync.mock.calls[0][1]).toMatchObject({ name: 'General', importance: 3 });
    expect(await ensureAndroidChannels({ Notifications, os: 'ios' })).toBe(false);
  });
});

describe('notification step and launch registration', () => {
  const extra = Constants.expoConfig.extra;
  beforeEach(() => {
    resetEnvironment();
    jest.clearAllMocks();
    Notifications.getPermissionsAsync.mockResolvedValue(undetermined);
    Notifications.requestPermissionsAsync.mockResolvedValue(granted);
    Notifications.getExpoPushTokenAsync.mockResolvedValue({ type: 'expo', data: TOKEN });
    Constants.expoConfig.extra = { ...extra, eas: { projectId: 'test-project' } };
  });
  afterEach(() => {
    Constants.expoConfig.extra = extra;
    delete global.fetch;
  });

  test('Turn on notifications asks, stores the token, registers it, and continues to the feed', async () => {
    const server = installFakeServer({ ...signedInAs(makeUser(), { notificationsPrompted: false }), 'POST /me/push-tokens': { status: 201, body: { id: 'ptk_1' } } });
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/allow-notifications'));
    expect(screen.queryByText(/coming update/)).toBeNull();
    await fireEvent.press(await screen.findByTestId('allow-notifications-enable'));
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
    expect(Notifications.requestPermissionsAsync).toHaveBeenCalled();
    expect(SecureStore.__store.get(PushTokenKey)).toBe(TOKEN);
    expect(server.called('POST /me/push-tokens').map((c) => c.body)).toEqual([{ token: TOKEN, platform: 'Ios', deviceName: 'iPhone 16', appVersion: '1.0.0' }]);
  });

  test('a denied prompt still continues to the feed', async () => {
    Notifications.requestPermissionsAsync.mockResolvedValue(denied);
    const server = installFakeServer(signedInAs(makeUser(), { notificationsPrompted: false }));
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await fireEvent.press(await screen.findByTestId('allow-notifications-enable'));
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
    expect(server.called('POST /me/push-tokens')).toHaveLength(0);
    expect(SecureStore.__store.has(PushTokenKey)).toBe(false);
  });

  test('before the backend has the route, the 404 is tolerated and the app carries on', async () => {
    const server = installFakeServer(signedInAs(makeUser(), { notificationsPrompted: false }));
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await fireEvent.press(await screen.findByTestId('allow-notifications-enable'));
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
    expect(server.called('POST /me/push-tokens')).toHaveLength(1);
    expect(SecureStore.__store.get(PushTokenKey)).toBe(TOKEN);
  });

  test('on a later launch with permission granted the token registers without a prompt', async () => {
    Notifications.getPermissionsAsync.mockResolvedValue(granted);
    const server = installFakeServer({ ...signedInAs(makeUser()), 'POST /me/push-tokens': { status: 201, body: { id: 'ptk_1' } } });
    await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(server.called('POST /me/push-tokens')).toHaveLength(1));
    expect(Notifications.requestPermissionsAsync).not.toHaveBeenCalled();
  });

  test('without permission nothing is registered at launch', async () => {
    const server = installFakeServer(signedInAs(makeUser()));
    await renderRouter(APP_DIR, { initialUrl: '/' });
    expect(await screen.findByTestId('screen-feed')).toBeOnTheScreen();
    expect(server.called('POST /me/push-tokens')).toHaveLength(0);
    expect(Notifications.getExpoPushTokenAsync).not.toHaveBeenCalled();
  });

  test('signing out removes the registered token from the server', async () => {
    Notifications.getPermissionsAsync.mockResolvedValue(granted);
    const server = installFakeServer({
      ...signedInAs(makeUser()),
      'POST /me/push-tokens': { status: 201, body: { id: 'ptk_1' } },
      'DELETE /me/push-tokens': { status: 204, body: null },
      'POST /auth/logout': { status: 204, body: null }
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/profile' });
    await waitFor(() => expect(server.called('POST /me/push-tokens')).toHaveLength(1));
    await fireEvent.press(await screen.findByTestId('profile-sign-out'));
    await waitFor(() => expect(router.getPathname()).toBe('/welcome'));
    const deletes = server.called('DELETE /me/push-tokens');
    expect(deletes).toHaveLength(1);
    expect(deletes[0].body).toEqual({ token: TOKEN });
    // The DELETE runs before the session is revoked, so it still carries the access token.
    expect(deletes[0].headers.Authorization).toMatch(/^Bearer /);
    expect(server.calls.findIndex((c) => c.key === 'DELETE /me/push-tokens')).toBeLessThan(server.calls.findIndex((c) => c.key === 'POST /auth/logout'));
  });
});
