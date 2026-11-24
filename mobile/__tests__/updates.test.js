import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import * as Updates from 'expo-updates';
import path from 'path';
import { Alert, AppState, Linking, Text } from 'react-native';
import { getApiClient } from '../src/api/client';
import { auth, account, membership } from '../src/api/endpoints';
import { appIdFor, identityHeaders } from '../src/api/identity';
import Avatar from '../src/components/Avatar';
import { mediaHeaders } from '../src/media/MediaProvider';
import { PushTokensPath } from '../src/notifications/push';
import { PropertyProvider } from '../src/property';
import strings from '../src/strings';
import {
  compareVersions,
  isBinaryBelowMinimum,
  isCriticalManifest,
  minimumBinaryVersionFor,
  normalizeIntervalMinutes,
  storeUrlFor,
  UpdateRequiredGate,
  UpdatesController,
  UpdatesProvider
} from '../src/updates';
import { resetUpdatesModule } from '../src/updates/runtime';
import { authResponse, installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { property } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');
const ENGINE_ROOT = path.resolve(__dirname, '../..');
const MINUTE = 60000;

/** A fake expo-updates module. */
function fakeUpdates(overrides = {}) {
  return {
    isEnabled: true,
    updateId: 'embedded-1',
    checkForUpdateAsync: jest.fn(async () => ({ isAvailable: false, isRollBackToEmbedded: false })),
    fetchUpdateAsync: jest.fn(async () => ({ isNew: false, isRollBackToEmbedded: false })),
    reloadAsync: jest.fn(async () => {}),
    ...overrides
  };
}

function available(id, extra = {}) {
  const manifest = { id, extra, metadata: {} };
  return {
    check: jest.fn(async () => ({ isAvailable: true, isRollBackToEmbedded: false, manifest })),
    fetch: jest.fn(async () => ({ isNew: true, isRollBackToEmbedded: false, manifest }))
  };
}

function clock(start = 1000000) {
  let now = start;
  const fn = () => now;
  fn.advance = (ms) => {
    now += ms;
  };
  return fn;
}

describe('UpdatesController', () => {
  test('checks on the first call, then at most once per interval', async () => {
    const now = clock();
    const updates = fakeUpdates();
    const controller = new UpdatesController({ updates, intervalMinutes: 15, now });
    await controller.check({ reason: 'launch' });
    await controller.check({ reason: 'foreground' });
    expect(updates.checkForUpdateAsync).toHaveBeenCalledTimes(1);
    now.advance(14 * MINUTE);
    await controller.check({ reason: 'foreground' });
    expect(updates.checkForUpdateAsync).toHaveBeenCalledTimes(1);
    now.advance(MINUTE);
    await controller.check({ reason: 'foreground' });
    expect(updates.checkForUpdateAsync).toHaveBeenCalledTimes(2);
    await controller.check({ reason: 'manual', force: true });
    expect(updates.checkForUpdateAsync).toHaveBeenCalledTimes(3);
  });

  test('the interval follows the setting and is clamped', async () => {
    expect(normalizeIntervalMinutes(undefined)).toBe(15);
    expect(normalizeIntervalMinutes('abc')).toBe(15);
    expect(normalizeIntervalMinutes(0.1)).toBe(1);
    expect(normalizeIntervalMinutes(30)).toBe(30);
    const now = clock();
    const updates = fakeUpdates();
    const controller = new UpdatesController({ updates, now });
    controller.setIntervalMinutes(60);
    await controller.check();
    now.advance(30 * MINUTE);
    await controller.check();
    expect(updates.checkForUpdateAsync).toHaveBeenCalledTimes(1);
  });

  test('concurrent checks share one run', async () => {
    let resolve;
    const updates = fakeUpdates({ checkForUpdateAsync: jest.fn(() => new Promise((r) => (resolve = r))) });
    const controller = new UpdatesController({ updates });
    const a = controller.check({ reason: 'launch' });
    const b = controller.check({ reason: 'foreground', force: true });
    expect(controller.state.status).toBe('checking');
    resolve({ isAvailable: false, isRollBackToEmbedded: false });
    await Promise.all([a, b]);
    expect(updates.checkForUpdateAsync).toHaveBeenCalledTimes(1);
    expect(controller.state.status).toBe('idle');
  });

  test('a new update downloads in the background and waits for the next launch', async () => {
    const { check, fetch } = available('upd-2');
    const updates = fakeUpdates({ checkForUpdateAsync: check, fetchUpdateAsync: fetch });
    const events = [];
    const controller = new UpdatesController({ updates, onEvent: (e) => events.push(e.type) });
    const seen = [];
    controller.subscribe((s) => seen.push(s.status));
    const state = await controller.check({ reason: 'launch' });
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(state).toMatchObject({ status: 'ready', pendingUpdateId: 'upd-2', critical: false, error: null });
    expect(seen).toEqual(['checking', 'downloading', 'ready']);
    expect(events).toEqual(['check', 'download', 'ready']);
    // Applied on next launch: nothing reloads the running app.
    expect(updates.reloadAsync).not.toHaveBeenCalled();
  });

  test('the same update is not downloaded twice', async () => {
    const now = clock();
    const { check, fetch } = available('upd-2');
    const controller = new UpdatesController({ updates: fakeUpdates({ checkForUpdateAsync: check, fetchUpdateAsync: fetch }), now });
    await controller.check();
    now.advance(20 * MINUTE);
    await controller.check();
    expect(check).toHaveBeenCalledTimes(2);
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(controller.state.status).toBe('ready');
  });

  test('critical updates are flagged from extra or metadata', async () => {
    expect(isCriticalManifest({ extra: { critical: true } })).toBe(true);
    expect(isCriticalManifest({ metadata: { critical: 'true' } })).toBe(true);
    expect(isCriticalManifest({ extra: { critical: 'yes' } })).toBe(false);
    expect(isCriticalManifest(null)).toBe(false);
    const { check, fetch } = available('upd-3', { critical: true });
    const controller = new UpdatesController({ updates: fakeUpdates({ checkForUpdateAsync: check, fetchUpdateAsync: fetch }) });
    await controller.check();
    expect(controller.state).toMatchObject({ status: 'ready', critical: true, pendingUpdateId: 'upd-3' });
    await expect(controller.restart()).resolves.toBe(true);
  });

  test('a roll back to the embedded update downloads and applies on next launch', async () => {
    const updates = fakeUpdates({
      checkForUpdateAsync: jest.fn(async () => ({ isAvailable: false, isRollBackToEmbedded: true })),
      fetchUpdateAsync: jest.fn(async () => ({ isNew: false, isRollBackToEmbedded: true }))
    });
    const controller = new UpdatesController({ updates });
    await controller.check();
    expect(controller.state).toMatchObject({ status: 'ready', rollBack: true, critical: false });
  });

  test('failures are recorded, never thrown, and the next allowed check retries', async () => {
    const now = clock();
    const updates = fakeUpdates({ checkForUpdateAsync: jest.fn().mockRejectedValueOnce(Object.assign(new Error('Network request failed'), { code: 'ERR_UPDATES_CHECK' })).mockResolvedValue({ isAvailable: false, isRollBackToEmbedded: false }) });
    const events = [];
    const controller = new UpdatesController({ updates, now, onEvent: (e) => events.push(e) });
    await expect(controller.check()).resolves.toMatchObject({ status: 'error', error: 'Network request failed' });
    expect(events.find((e) => e.type === 'failure')).toMatchObject({ code: 'ERR_UPDATES_CHECK' });
    await controller.check();
    expect(updates.checkForUpdateAsync).toHaveBeenCalledTimes(1);
    now.advance(15 * MINUTE);
    await expect(controller.check()).resolves.toMatchObject({ status: 'idle', error: null });
  });

  test('a failed download (for example a rejected signature) is an error, not a crash', async () => {
    const { check } = available('upd-4');
    const updates = fakeUpdates({ checkForUpdateAsync: check, fetchUpdateAsync: jest.fn().mockRejectedValue(new Error('Code signature verification failed')) });
    const controller = new UpdatesController({ updates });
    await controller.check();
    expect(controller.state).toMatchObject({ status: 'error', pendingUpdateId: null });
    await expect(controller.restart()).resolves.toBe(false);
  });

  test('a failed reload keeps the update for the next launch', async () => {
    const { check, fetch } = available('upd-5', { critical: true });
    const updates = fakeUpdates({ checkForUpdateAsync: check, fetchUpdateAsync: fetch, reloadAsync: jest.fn().mockRejectedValue(new Error('no runtime')) });
    const controller = new UpdatesController({ updates });
    await controller.check();
    await expect(controller.restart()).resolves.toBe(false);
    expect(controller.state.pendingUpdateId).toBe('upd-5');
  });

  test('disabled builds never call expo-updates', async () => {
    const updates = fakeUpdates({ isEnabled: false });
    const controller = new UpdatesController({ updates });
    expect(controller.enabled).toBe(false);
    await controller.check({ force: true });
    expect(updates.checkForUpdateAsync).not.toHaveBeenCalled();
    expect(new UpdatesController({ updates: fakeUpdates(), enabled: false }).enabled).toBe(false);
    expect(new UpdatesController({ updates: null }).state.status).toBe('disabled');
  });
});

describe('UpdatesProvider', () => {
  let appStateListeners;
  const appStateListener = (state) => appStateListeners.forEach((fn) => fn(state));
  beforeEach(() => {
    appStateListeners = [];
    jest.spyOn(AppState, 'addEventListener').mockImplementation((type, fn) => {
      if (type === 'change') appStateListeners.push(fn);
      return { remove: jest.fn() };
    });
    jest.spyOn(Alert, 'alert').mockImplementation(() => {});
  });
  afterEach(() => jest.restoreAllMocks());

  function mount(controller) {
    return act(async () => {
      await render(
      <PropertyProvider property={property}>
        <UpdatesProvider controller={controller}>
          <Text>app</Text>
        </UpdatesProvider>
      </PropertyProvider>
      );
    });
  }

  test('checks on launch and on foreground, respecting the interval', async () => {
    const now = clock();
    const updates = fakeUpdates();
    const controller = new UpdatesController({ updates, now });
    await mount(controller);
    await waitFor(() => expect(updates.checkForUpdateAsync).toHaveBeenCalledTimes(1));
    await act(async () => appStateListener('active'));
    expect(updates.checkForUpdateAsync).toHaveBeenCalledTimes(1);
    now.advance(16 * MINUTE);
    await act(async () => appStateListener('background'));
    expect(updates.checkForUpdateAsync).toHaveBeenCalledTimes(1);
    await act(async () => appStateListener('active'));
    expect(updates.checkForUpdateAsync).toHaveBeenCalledTimes(2);
  });

  test('a critical update prompts "Restart to update" once, and Restart reloads', async () => {
    const { check, fetch } = available('upd-9', { critical: true });
    const updates = fakeUpdates({ checkForUpdateAsync: check, fetchUpdateAsync: fetch });
    const controller = new UpdatesController({ updates });
    await mount(controller);
    await waitFor(() => expect(Alert.alert).toHaveBeenCalledTimes(1));
    const [title, message, buttons] = Alert.alert.mock.calls[0];
    expect(title).toBe(strings.updates.readyTitle);
    expect(message).toBe(strings.updates.readyMessage);
    expect(buttons.map((b) => b.text)).toEqual([strings.updates.later, strings.updates.restart]);
    expect(strings.updates.restart).toBe('Restart to update');
    await act(async () => buttons[1].onPress());
    expect(updates.reloadAsync).toHaveBeenCalledTimes(1);
    await act(async () => controller.check({ force: true }));
    expect(Alert.alert).toHaveBeenCalledTimes(1);
  });

  test('a normal update downloads silently', async () => {
    const { check, fetch } = available('upd-10');
    const updates = fakeUpdates({ checkForUpdateAsync: check, fetchUpdateAsync: fetch });
    const controller = new UpdatesController({ updates });
    await mount(controller);
    await waitFor(() => expect(controller.state.status).toBe('ready'));
    expect(Alert.alert).not.toHaveBeenCalled();
    expect(updates.reloadAsync).not.toHaveBeenCalled();
  });

  test('without updates enabled nothing is checked', async () => {
    const updates = fakeUpdates({ isEnabled: false });
    await mount(new UpdatesController({ updates }));
    await act(async () => appStateListener('active'));
    expect(updates.checkForUpdateAsync).not.toHaveBeenCalled();
  });
});

describe('minimum binary version gate', () => {
  const withMobile = (mobile) => ({ ...property, mobile: { ...property.mobile, ...mobile } });

  test('version comparison', () => {
    expect(compareVersions('1.2.0', '1.2')).toBe(0);
    expect(compareVersions('1.10.0', '1.9.9')).toBeGreaterThan(0);
    expect(compareVersions('v1.0.0', '1.0.1')).toBeLessThan(0);
    expect(compareVersions('abc', '1.0.0')).toBeNull();
  });

  test('reads the minimum per platform or as one string', () => {
    const both = withMobile({ minimumBinaryVersion: '2.0.0' });
    const split = withMobile({ minimumBinaryVersion: { ios: '1.5.0', android: '2.1.0' } });
    expect(minimumBinaryVersionFor(both, 'ios')).toBe('2.0.0');
    expect(minimumBinaryVersionFor(split, 'ios')).toBe('1.5.0');
    expect(minimumBinaryVersionFor(split, 'android')).toBe('2.1.0');
    expect(minimumBinaryVersionFor(withMobile({ minimumBinaryVersion: { ios: 'soon' } }), 'ios')).toBeNull();
    expect(minimumBinaryVersionFor(property, 'ios')).toBeNull();
    expect(isBinaryBelowMinimum(split, { os: 'ios', binaryVersion: '1.4.9' })).toBe(true);
    expect(isBinaryBelowMinimum(split, { os: 'ios', binaryVersion: '1.5.0' })).toBe(false);
    expect(isBinaryBelowMinimum(split, { os: 'android', binaryVersion: '2.0.0' })).toBe(true);
    expect(isBinaryBelowMinimum(property, { os: 'ios', binaryVersion: '0.0.1' })).toBe(false);
  });

  test('store links come from the property', () => {
    expect(storeUrlFor(withMobile({ storeUrls: { ios: 'https://apps.apple.com/app/id42' } }), 'ios')).toBe('https://apps.apple.com/app/id42');
    expect(storeUrlFor(withMobile({ appStoreId: '123' }), 'ios')).toBe('https://apps.apple.com/app/id123');
    expect(storeUrlFor(property, 'ios')).toBeNull();
    expect(storeUrlFor(property, 'android')).toBe('https://play.google.com/store/apps/details?id=' + property.mobile.androidPackage);
    expect(storeUrlFor(withMobile({ storeUrls: { android: 'javascript:alert(1)' } }), 'android')).toBe('https://play.google.com/store/apps/details?id=' + property.mobile.androidPackage);
  });

  function gate(prop, os, binaryVersion) {
    return render(
      <PropertyProvider property={prop}>
        <UpdateRequiredGate os={os} binaryVersion={binaryVersion}>
          <Text>the app</Text>
        </UpdateRequiredGate>
      </PropertyProvider>
    );
  }

  test('an older binary sees the blocking screen with the store link', async () => {
    const open = jest.spyOn(Linking, 'openURL').mockResolvedValue(true);
    await gate(withMobile({ minimumBinaryVersion: { ios: '9.0.0' }, appStoreId: '123' }), 'ios', '1.0.0');
    expect(screen.getByText(strings.updates.requiredTitle)).toBeTruthy();
    expect(screen.queryByText('the app')).toBeNull();
    await fireEvent.press(screen.getByTestId('update-required-store'));
    expect(screen.getByText(strings.updates.openAppStore)).toBeTruthy();
    expect(open).toHaveBeenCalledWith('https://apps.apple.com/app/id123');
    open.mockRestore();
  });

  test('Android shows Google Play; no link shows guidance instead', async () => {
    await gate(withMobile({ minimumBinaryVersion: '9.0.0' }), 'android', '1.0.0');
    expect(screen.getByText(strings.updates.openPlayStore)).toBeTruthy();
    await screen.unmount();
    await gate(withMobile({ minimumBinaryVersion: '9.0.0' }), 'ios', '1.0.0');
    expect(screen.queryByTestId('update-required-store')).toBeNull();
    expect(screen.getByText(strings.updates.noStoreLink)).toBeTruthy();
  });

  test('a current binary renders the app', async () => {
    await gate(withMobile({ minimumBinaryVersion: { ios: '1.0.0' } }), 'ios', '1.0.0');
    expect(screen.getByText('the app')).toBeTruthy();
  });

  test('the minimum from GET /api/v1/property blocks the whole app at launch', async () => {
    installFakeServer({ ...signedInAs(makeUser()), 'GET /property': { ...property, mobile: { ...property.mobile, minimumBinaryVersion: '999.0.0' } } });
    await renderRouter(APP_DIR, { initialUrl: '/' });
    expect(await screen.findByTestId('update-required')).toBeTruthy();
    delete global.fetch;
  });
});

describe('identification headers', () => {
  beforeEach(() => {
    resetEnvironment();
    Updates.__reset();
    resetUpdatesModule();
  });
  afterEach(() => {
    Updates.__reset();
    delete global.fetch;
  });

  const expected = () => ({ 'X-TK-Property': property.id, 'X-TK-App': property.mobile.bundleId });

  test('the app id is the iOS bundle id or the Android package', () => {
    expect(appIdFor(property, 'ios')).toBe(property.mobile.bundleId);
    expect(appIdFor(property, 'android')).toBe(property.mobile.androidPackage);
    expect(identityHeaders({ os: 'android', updateId: null })).toEqual({ 'X-TK-Property': property.id, 'X-TK-App': property.mobile.androidPackage });
  });

  test('X-Client-Update-Id is sent only when expo-updates reports a running update', () => {
    expect(identityHeaders()['X-Client-Update-Id']).toBeUndefined();
    Updates.__set({ isEnabled: true, updateId: 'upd-77' });
    expect(identityHeaders()).toEqual({ ...expected(), 'X-Client-Update-Id': 'upd-77' });
  });

  test('every request path of the API client carries the headers', async () => {
    Updates.__set({ isEnabled: true, updateId: 'upd-1' });
    const user = makeUser();
    const server = installFakeServer({
      'POST /auth/refresh': authResponse(user, 'r2'),
      'GET /auth/me': { status: 401, body: { error: 'Unauthorized' } },
      'GET /auth/providers': {},
      'GET /property': {},
      'POST /membership/apple/verify': { ok: true },
      'POST /membership/google/verify': { ok: true },
      [`POST ${PushTokensPath}`]: { status: 201, body: { id: 'ptk_1' } },
      [`DELETE ${PushTokensPath}`]: { status: 204, body: null },
      'PUT /me/avatar': user,
      'POST /media/grant': { cookies: [] },
      'DELETE /me/identities/google': { status: 204, body: null }
    });
    const api = getApiClient();
    await api.setSession(authResponse(user, 'r1'));
    await auth.me(api).catch(() => null); // 401, refresh, retry: three requests
    await auth.providers(api);
    await api.get('/property');
    await membership.verifyApple(api, 'jws');
    await membership.verifyGoogle(api, { purchaseToken: 'pt', productId: 'p' });
    await api.post(PushTokensPath, { token: 't' });
    await api.delete(PushTokensPath, { body: { token: 't' } });
    await account.uploadAvatar(api, new Uint8Array([1]), 'image/jpeg');
    await api.post('/media/grant');
    await account.unlinkIdentity(api, 'google');
    expect(server.calls.map((c) => c.key)).toEqual(expect.arrayContaining(['GET /auth/me', 'POST /auth/refresh', 'POST /membership/apple/verify', 'POST /membership/google/verify', 'POST /me/push-tokens', 'DELETE /me/push-tokens', 'PUT /me/avatar', 'POST /media/grant']));
    for (const call of server.calls) {
      expect({ key: call.key, headers: call.headers }).toEqual({ key: call.key, headers: expect.objectContaining({ ...expected(), 'X-Client-Update-Id': 'upd-1', 'X-Client': 'mobile' }) });
    }
  });

  test('media and avatar sources carry the headers; cookies and the bearer stay scoped', async () => {
    const api = getApiClient();
    api.accessToken = 'tok';
    const remote = mediaHeaders(api, 'https://media.example.test/a.jpg', { cookies: [{ name: 'CloudFront-Policy', value: 'p' }] });
    expect(remote).toEqual(expect.objectContaining(expected()));
    expect(remote.Authorization).toBeUndefined();
    expect(mediaHeaders(api, api.baseUrl + '/media/a.jpg', null)).toEqual({ ...expected(), Authorization: 'Bearer tok' });
    await render(<Avatar uri="https://media.example.test/u.jpg" token="tok" name="A" testID="av" />);
    expect(screen.getByTestId('av').props.source.headers).toEqual({ ...expected(), Authorization: 'Bearer tok' });
  });

  test('the whole launch flow (property, session restore, feed, notifications) sends the headers on every call', async () => {
    const server = installFakeServer(signedInAs(makeUser()));
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
    expect(server.calls.length).toBeGreaterThan(2);
    for (const call of server.calls) expect({ key: call.key, headers: call.headers }).toEqual({ key: call.key, headers: expect.objectContaining(expected()) });
  });
});

describe('build-time updates config', () => {
  const { updatesConfig, resolveChannel, resolveUpdatesUrl } = require('../scripts/updates');
  const { loadActiveProperty } = require('../scripts/property');
  const projectRoot = path.resolve(__dirname, '..');
  const sample = loadActiveProperty(path.join(ENGINE_ROOT, 'samples/example'));
  const plain = loadActiveProperty(path.resolve(__dirname, '../test-utils/fixtures/plain'));

  test('the sample property has updates on with code signing, fingerprint, and its headers', () => {
    const live = updatesConfig({ property: sample.property, propertyDir: sample.dir, projectRoot, env: { APP_VARIANT: 'production' } });
    expect(live.enabled).toBe(true);
    expect(live.runtimeVersion).toEqual({ policy: 'fingerprint' });
    expect(live.updates).toMatchObject({
      enabled: true,
      url: 'https://' + sample.property.domains.api + '/api/v1/updates/manifest',
      codeSigningCertificate: '../samples/example/mobile/updates/certificate.pem',
      codeSigningMetadata: { keyid: 'main', alg: 'rsa-v1_5-sha256' },
      // The channel and install id are placeholders the app replaces at runtime (decision 15).
      requestHeaders: { 'expo-channel-name': 'production', 'X-TK-Property': sample.property.id, 'X-TK-App': sample.property.mobile.bundleId, 'X-TK-Install-Id': '' }
    });
    expect(Object.keys(live.requestHeaders)).toHaveLength(4);
    expect(updatesConfig({ property: sample.property, propertyDir: sample.dir, projectRoot, env: { APP_VARIANT: 'preview' } }).updates.requestHeaders).toEqual(live.requestHeaders);
  });

  test('a property without a certificate builds with updates disabled and a warning', () => {
    const live = updatesConfig({ property: plain.property, propertyDir: plain.dir, projectRoot, env: {} });
    expect(live.enabled).toBe(false);
    expect(live.updates).toEqual({ enabled: false });
    expect(live.warning).toMatch(/no code signing certificate/);
  });

  test('channels follow the build profile, with an override', () => {
    expect(resolveChannel({ APP_VARIANT: 'production' })).toBe('production');
    expect(resolveChannel({ APP_VARIANT: 'preview' })).toBe('preview');
    expect(resolveChannel({ APP_VARIANT: 'development' })).toBe('development');
    expect(resolveChannel({})).toBe('development');
    expect(resolveChannel({ APP_VARIANT: 'production', TK_UPDATES_CHANNEL: 'preview' })).toBe('preview');
    expect(() => resolveChannel({ TK_UPDATES_CHANNEL: 'beta' })).toThrow(/TK_UPDATES_CHANNEL/);
  });

  test('the manifest URL can point at a local backend', () => {
    expect(resolveUpdatesUrl(sample.property, { TK_UPDATES_URL: 'http://127.0.0.1:8000/api/v1/updates/manifest' })).toBe('http://127.0.0.1:8000/api/v1/updates/manifest');
    expect(resolveUpdatesUrl(sample.property, { EXPO_PUBLIC_API_URL: 'http://192.168.1.20:8000/' })).toBe('http://192.168.1.20:8000/api/v1/updates/manifest');
  });
});
