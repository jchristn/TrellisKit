import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as Network from 'expo-network';
import * as SecureStore from 'expo-secure-store';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { MobileApiClient } from '../src/api/MobileApiClient';
import { describeError } from '../src/auth/errors';
import { isOfflineState } from '../src/hooks/useNetworkStatus';
import { getIap } from '../src/native/iap';
import { withTimeout } from '../src/utils/timeout';
import { authResponse, disabledProviders, installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { property } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');
const PRODUCT = property.membership.storeProductId;
const inactive = { isActive: false, source: null, store: null, expiresUtc: null, autoRenew: false, willConvertToPaid: false };

/** Signed in at launch with refresh-1; later refreshes answer with `later`. */
function sessionThenRefresh(user, later) {
  let first = true;
  return {
    ...signedInAs(user),
    'POST /auth/refresh': () => {
      if (first) {
        first = false;
        return authResponse(user);
      }
      return later();
    }
  };
}

describe('revoked session during a request', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('a 401 whose refresh is rejected returns to Sign in with a notice', async () => {
    const server = installFakeServer({
      ...sessionThenRefresh(makeUser(), () => ({ status: 401, body: { error: 'InvalidRefreshToken', message: 'Revoked' } })),
      'POST /me/export': { status: 401, body: { error: 'Unauthorized', message: 'Unauthorized' } }
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/account' });
    await fireEvent.press(await screen.findByTestId('account-export'));
    await waitFor(() => expect(router.getPathname()).toBe('/sign-in'));
    expect(await screen.findByText('You were signed out. Please sign in again.')).toBeOnTheScreen();
    expect(SecureStore.__store.has('tk.refreshToken')).toBe(false);
    expect(server.called('POST /auth/refresh')).toHaveLength(2);
  });

  test('a route that still answers 401 after a successful refresh ends the session', async () => {
    installFakeServer({
      ...sessionThenRefresh(makeUser(), () => authResponse(makeUser(), 'refresh-3')),
      'POST /me/export': { status: 401, body: { error: 'Unauthorized', message: 'Unauthorized' } }
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/account' });
    await fireEvent.press(await screen.findByTestId('account-export'));
    await waitFor(() => expect(router.getPathname()).toBe('/sign-in'));
    expect(await screen.findByTestId('sign-in-notice')).toHaveTextContent('You were signed out. Please sign in again.');
  });

  test('a suspended account says so', async () => {
    installFakeServer({
      ...sessionThenRefresh(makeUser(), () => ({ status: 403, body: { error: 'AccountBanned', message: 'Banned' } })),
      'POST /me/export': { status: 401, body: { error: 'Unauthorized', message: 'Unauthorized' } }
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/account' });
    await fireEvent.press(await screen.findByTestId('account-export'));
    await waitFor(() => expect(router.getPathname()).toBe('/sign-in'));
    expect(await screen.findByText('This account has been suspended.')).toBeOnTheScreen();
  });

  test('a session revoked on a screen outside the tabs (Messages) also leaves for Sign in', async () => {
    installFakeServer({
      ...sessionThenRefresh(makeUser(), () => ({ status: 401, body: { error: 'InvalidRefreshToken' } })),
      'GET /auth/me': { status: 401, body: { error: 'Unauthorized' } }
    });
    const { getApiClient } = require('../src/api/client');
    const router = await renderRouter(APP_DIR, { initialUrl: '/messages' });
    expect(await screen.findByText('Private conversations with other members.')).toBeOnTheScreen();
    await act(async () => {
      await getApiClient()
        .get('/auth/me')
        .catch(() => null);
    });
    await waitFor(() => expect(router.getPathname()).toBe('/sign-in'));
  });

  test('a 401 with an access token but no stored refresh token expires the session', async () => {
    const tokenStore = { getRefreshToken: jest.fn(async () => null), setRefreshToken: jest.fn(), clear: jest.fn(async () => {}) };
    const fetchImpl = jest.fn(async () => ({ ok: false, status: 401, headers: { get: () => 'application/json' }, json: async () => ({ error: 'Unauthorized' }) }));
    const api = new MobileApiClient({ baseUrl: 'http://x', appVersion: '1', tokenStore, fetchImpl });
    api.accessToken = 'access';
    const expired = jest.fn();
    api.onSessionExpired(expired);
    await expect(api.get('/me/export')).rejects.toMatchObject({ status: 401 });
    expect(expired).toHaveBeenCalledTimes(1);
    expect(api.accessToken).toBeNull();
  });
});

describe('timeouts', () => {
  // renderRouter in earlier tests switches to fake timers.
  beforeEach(() => jest.useRealTimers());
  test('a request that never answers times out with its own message', async () => {
    const tokenStore = { getRefreshToken: async () => null, setRefreshToken: async () => {}, clear: async () => {} };
    const fetchImpl = jest.fn((url, { signal }) => new Promise((_, reject) => signal.addEventListener('abort', () => reject(Object.assign(new Error('aborted'), { name: 'AbortError' })))));
    const api = new MobileApiClient({ baseUrl: 'http://x', appVersion: '1', tokenStore, fetchImpl, timeoutMs: 20 });
    const error = await api.get('/health').catch((e) => e);
    expect(error).toMatchObject({ status: 0, code: 'Timeout' });
    expect(describeError(error)).toBe('The server took too long to respond. Please try again.');
    expect(describeError({ status: 0, code: 'NetworkError' })).toBe('Cannot reach the server. Check your connection and try again.');
  });

  test('withTimeout rejects slow store calls', async () => {
    await expect(withTimeout(new Promise(() => {}), 10)).rejects.toMatchObject({ code: 'Timeout', status: 0 });
    await expect(withTimeout(Promise.resolve(7), 50)).resolves.toBe(7);
  });
});

describe('offline banner', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('isOfflineState treats unknown reachability as online', () => {
    expect(isOfflineState({ isConnected: true, isInternetReachable: null })).toBe(false);
    expect(isOfflineState({ isConnected: false, isInternetReachable: false })).toBe(true);
    expect(isOfflineState({ isConnected: true, isInternetReachable: false })).toBe(true);
    expect(isOfflineState(null)).toBe(false);
  });

  test('appears while offline and goes away when the connection returns', async () => {
    installFakeServer(signedInAs(makeUser()));
    await renderRouter(APP_DIR, { initialUrl: '/' });
    expect(await screen.findByTestId('screen-feed')).toBeOnTheScreen();
    expect(screen.queryByTestId('offline-banner')).toBeNull();

    await act(async () => Network.__setState({ isConnected: false, isInternetReachable: false, type: 'NONE' }));
    const banner = await screen.findByTestId('offline-banner');
    expect(banner).toHaveTextContent(/You are offline\. Some actions will not work until you reconnect\./);
    expect(banner.props.accessibilityRole).toBe('alert');

    await act(async () => Network.__setState({ isConnected: true, isInternetReachable: true, type: 'WIFI' }));
    await waitFor(() => expect(screen.queryByTestId('offline-banner')).toBeNull());
  });

  test('shows at launch when the device starts offline, on the signed-out screens too', async () => {
    Network.__setState({ isConnected: false, isInternetReachable: false });
    installFakeServer({});
    await renderRouter(APP_DIR, { initialUrl: '/' });
    expect(await screen.findByTestId('offline-banner')).toBeOnTheScreen();
    expect(await screen.findByTestId('welcome-logo')).toBeOnTheScreen();
  });
});

describe('error and retry states', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => {
    delete global.fetch;
    getIap.mockReturnValue(null);
  });

  test('sign-in options that fail to load offer Retry, and email stays available', async () => {
    let fail = true;
    installFakeServer({ 'GET /auth/providers': () => (fail ? { __network: true } : { ...disabledProviders, apple: { enabled: true } }) });
    await renderRouter(APP_DIR, { initialUrl: '/sign-in' });
    expect(await screen.findByTestId('sign-in-providers-error')).toBeOnTheScreen();
    expect(screen.getByTestId('sign-in-email')).toBeOnTheScreen();
    fail = false;
    await fireEvent.press(screen.getByTestId('sign-in-providers-error-retry'));
    await waitFor(() => expect(screen.queryByTestId('sign-in-providers-error')).toBeNull());
  });

  test('the paywall offers Retry when the server cannot be reached, then loads', async () => {
    const iap = {
      initConnection: jest.fn(async () => true),
      endConnection: jest.fn(async () => true),
      fetchProducts: jest.fn(async () => [{ id: PRODUCT, displayPrice: '$29.99', displayName: 'Annual membership' }]),
      purchaseUpdatedListener: jest.fn(() => ({ remove: jest.fn() })),
      purchaseErrorListener: jest.fn(() => ({ remove: jest.fn() })),
      getAvailablePurchases: jest.fn(async () => [])
    };
    getIap.mockReturnValue(iap);
    let offline = true;
    installFakeServer({
      ...signedInAs(makeUser({ membership: inactive })),
      'GET /auth/providers': { ...disabledProviders, appleIap: { enabled: true, productId: PRODUCT } },
      'GET /membership': () => (offline ? { __network: true } : { membership: inactive, storeProductId: PRODUCT, storeAccountToken: 'acct', history: [] })
    });
    await renderRouter(APP_DIR, { initialUrl: '/' });
    expect(await screen.findByText('Could not load the membership')).toBeOnTheScreen();
    expect(screen.getByText('Cannot reach the server. Check your connection and try again.')).toBeOnTheScreen();
    offline = false;
    await fireEvent.press(screen.getByTestId('paywall-retry'));
    expect(await screen.findByTestId('paywall-subscribe')).toBeOnTheScreen();
  });

  test('Membership shows the cached status with Retry when the refresh fails', async () => {
    let fail = true;
    installFakeServer({ ...signedInAs(makeUser()), 'GET /auth/me': () => (fail ? { __network: true } : makeUser()) });
    await renderRouter(APP_DIR, { initialUrl: '/account/membership' });
    expect(await screen.findByTestId('membership-refresh-error')).toBeOnTheScreen();
    expect(screen.getByTestId('membership-status')).toHaveTextContent('Active');
    fail = false;
    await fireEvent.press(screen.getByTestId('membership-refresh-error-retry'));
    await waitFor(() => expect(screen.queryByTestId('membership-refresh-error')).toBeNull());
  });
});
