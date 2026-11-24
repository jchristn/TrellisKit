import { GoogleSignin } from '@react-native-google-signin/google-signin';
import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as SecureStore from 'expo-secure-store';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import * as WebBrowser from 'expo-web-browser';
import * as Sharing from 'expo-sharing';
import * as FileSystem from 'expo-file-system';
import { Linking } from 'react-native';
import { getApiClient } from '../src/api/client';
import { ExportPolling, runAccountExport } from '../src/api/endpoints';
import strings from '../src/strings';
import { rankHasLevel, rankMaxLevel } from '@trelliskit/shared';
import { disabledProviders, installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { defaultProfile, rankField, rankKeys } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');
const inactive = { isActive: false, source: null, store: null, expiresUtc: null, autoRenew: false, willConvertToPaid: false };

describe('account', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => {
    delete global.fetch;
    jest.restoreAllMocks();
  });

  test('delete account needs the typed confirmation, then shows the store note and signs out', async () => {
    const server = installFakeServer({
      ...signedInAs(makeUser()),
      'DELETE /me': { status: 202, body: { message: 'Your account has been deleted.', storeSubscriptionNote: 'Cancel your App Store subscription in iPhone Settings.' } }
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/account/delete' });
    const submit = await screen.findByTestId('delete-submit');
    expect(submit).toBeDisabled();

    await fireEvent.changeText(screen.getByTestId('delete-confirm'), 'delete me');
    expect(screen.getByTestId('delete-submit')).toBeDisabled();
    await fireEvent.press(screen.getByTestId('delete-submit'));
    expect(server.called('DELETE /me')).toHaveLength(0);

    await fireEvent.changeText(screen.getByTestId('delete-confirm'), 'DELETE');
    expect(screen.getByTestId('delete-submit')).toBeEnabled();
    await fireEvent.press(screen.getByTestId('delete-submit'));

    expect(await screen.findByText('Cancel your App Store subscription in iPhone Settings.')).toBeOnTheScreen();
    expect(server.called('DELETE /me')[0].body).toEqual({ confirm: 'DELETE' });

    await fireEvent.press(screen.getByTestId('delete-finish'));
    await waitFor(() => expect(router.getPathname()).toBe('/welcome'));
    expect(SecureStore.__store.has('tk.refreshToken')).toBe(false);
    expect(server.called('POST /auth/logout')).toHaveLength(0);
  });

  test('a member without a membership can still reach account deletion from the paywall', async () => {
    installFakeServer(signedInAs(makeUser({ membership: inactive })));
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/subscribe'));
    await fireEvent.press(await screen.findByRole('button', { name: 'Account settings' }));
    await fireEvent.press(await screen.findByTestId('account-delete'));
    expect(await screen.findByTestId('screen-delete')).toBeOnTheScreen();
  });

  test('App Store subscriptions on iOS offer Manage subscription', async () => {
    const open = jest.spyOn(Linking, 'openURL').mockResolvedValue(true);
    installFakeServer({ ...signedInAs(makeUser()), 'GET /auth/me': makeUser() });
    await renderRouter(APP_DIR, { initialUrl: '/account/membership' });
    expect(await screen.findByTestId('membership-type')).toHaveTextContent('Annual subscription, App Store');
    await fireEvent.press(screen.getByTestId('membership-manage'));
    expect(open).toHaveBeenCalledWith('https://apps.apple.com/account/subscriptions');
  });

  test.each([
    [{ isActive: true, source: 'Subscription', store: 'Stripe', expiresUtc: '2027-11-11T00:00:00Z', autoRenew: true }, 'Annual subscription'],
    [{ isActive: true, source: 'SignupCode', store: null, expiresUtc: '2027-03-01T00:00:00Z', autoRenew: false }, 'Complimentary membership'],
    [{ isActive: true, source: 'AdminComp', store: null, expiresUtc: null, autoRenew: false }, 'Complimentary membership']
  ])('%o shows status only, with no links out', async (membership, label) => {
    const user = makeUser({ membership });
    installFakeServer({ ...signedInAs(user), 'GET /auth/me': user });
    await renderRouter(APP_DIR, { initialUrl: '/account/membership' });
    expect(await screen.findByTestId('membership-type')).toHaveTextContent(label);
    expect(screen.getByTestId('membership-status')).toHaveTextContent('Active');
    expect(screen.queryByTestId('membership-manage')).toBeNull();
    expect(screen.queryByText(/website|web|code|stripe/i)).toBeNull();
  });

  test('email verification status with resend', async () => {
    const server = installFakeServer({ ...signedInAs(makeUser({ emailVerified: false })), 'POST /auth/email/resend': { status: 202, body: null } });
    await renderRouter(APP_DIR, { initialUrl: '/account' });
    expect(await screen.findByTestId('account-email-status')).toHaveTextContent('Not verified');
    await fireEvent.press(screen.getByTestId('account-resend'));
    expect(await screen.findByText(/Verification email sent/)).toBeOnTheScreen();
    expect(server.called('POST /auth/email/resend')).toHaveLength(1);
  });

  test('export data queues a job then shares a local download without a browser', async () => {
    const server = installFakeServer({
      ...signedInAs(makeUser()),
      'GET /me/export': { status: 410, body: { error: 'ExportMoved', message: 'Use the export job.' } },
      'POST /me/export': { status: 202, body: { exportId: 'aex_1', status: 'Pending' } },
      'GET /me/export/aex_1': { id: 'aex_1', status: 'Ready', downloadUrl: '/api/v1/me/export/aex_1/download?exp=1&sig=s', sizeBytes: 10 }
    });
    await renderRouter(APP_DIR, { initialUrl: '/account' });
    await fireEvent.press(await screen.findByTestId('account-export'));
    await waitFor(() => expect(Sharing.shareAsync).toHaveBeenCalledWith(expect.stringMatching(/account-export\.zip$/), { mimeType: 'application/zip' }));
    expect(FileSystem.__downloads.at(-1).url).toMatch(/\/api\/v1\/me\/export\/aex_1\/download\?exp=1&sig=s$/);
    expect(WebBrowser.openBrowserAsync).not.toHaveBeenCalled();
    expect(server.called('GET /me/export')).toHaveLength(0);
    expect(server.called('POST /me/export')).toHaveLength(1);
    expect(screen.getByTestId('account-message')).toHaveTextContent(strings.account.exportReady);
  });

  test('runAccountExport polls until Ready, stops at Failed, and reports a timeout as still pending', async () => {
    let reads = 0;
    const server = installFakeServer({
      'POST /me/export': { status: 202, body: { exportId: 'aex_2', status: 'Pending' } },
      'GET /me/export/aex_2': () => {
        reads += 1;
        return { id: 'aex_2', status: reads < 3 ? 'Running' : 'Ready', downloadUrl: reads < 3 ? null : '/x' };
      },
      'GET /me/export/aex_4': { id: 'aex_4', status: 'Failed' },
      'GET /me/export/aex_3': { id: 'aex_3', status: 'Pending' }
    });
    const api = getApiClient();
    const sleep = jest.fn(async () => {});
    const onQueued = jest.fn();
    expect((await runAccountExport(api, { sleep, onQueued })).status).toBe('Ready');
    expect(onQueued).toHaveBeenCalledWith('aex_2');
    expect(sleep).toHaveBeenCalledTimes(2);
    expect(sleep).toHaveBeenCalledWith(ExportPolling.intervalMs);
    expect(server.called('GET /me/export/aex_2')).toHaveLength(3);
    expect((await runAccountExport(api, { exportId: 'aex_4', sleep })).status).toBe('Failed');
    expect(server.called('POST /me/export')).toHaveLength(1);
    let clock = 0;
    const result = await runAccountExport(api, { exportId: 'aex_3', sleep: async (ms) => { clock += ms; }, now: () => clock, intervalMs: 10, timeoutMs: 30 });
    expect(result.status).toBe('Pending');
  });

  test('sign-in methods: link Google and explain LastSignInMethod on unlink', async () => {
    GoogleSignin.signIn.mockResolvedValueOnce({ type: 'success', data: { idToken: 'google-jwt' } });
    const providers = { ...disabledProviders, apple: { enabled: true }, google: { enabled: true, webClientId: 'w', iosClientId: 'i' } };
    const linked = makeUser({ signInMethods: ['Email', 'Google'] });
    const server = installFakeServer({
      ...signedInAs(makeUser({ signInMethods: ['Apple'] })),
      'GET /auth/providers': providers,
      'POST /me/identities/google': linked,
      'DELETE /me/identities/apple': { status: 409, body: { error: 'LastSignInMethod', message: 'x' } }
    });
    await renderRouter(APP_DIR, { initialUrl: '/account/sign-in-methods' });
    expect(await screen.findByRole('button', { name: 'Set a password' })).toBeOnTheScreen();

    await fireEvent.press(await screen.findByTestId('unlink-apple'));
    expect(await screen.findByText(/cannot unlink your only sign-in method/)).toBeOnTheScreen();

    await fireEvent.press(screen.getByTestId('link-google'));
    expect(await screen.findByText('Google is now linked.')).toBeOnTheScreen();
    expect(server.called('POST /me/identities/google')[0].body).toEqual({ idToken: 'google-jwt' });
    expect(screen.getByTestId('unlink-google')).toBeOnTheScreen();
  });

  test('set a password for a social-only account', async () => {
    const server = installFakeServer({ ...signedInAs(makeUser({ signInMethods: ['Apple'] })), 'PUT /me/password': { status: 204, body: null }, 'GET /auth/me': makeUser() });
    await renderRouter(APP_DIR, { initialUrl: '/account/password' });
    expect(screen.queryByTestId('password-current')).toBeNull();
    await fireEvent.changeText(await screen.findByTestId('password-new'), 'short');
    await fireEvent.press(screen.getByTestId('password-submit'));
    expect(await screen.findByText('Use 10 to 128 characters.')).toBeOnTheScreen();
    await fireEvent.changeText(screen.getByTestId('password-new'), 'a much longer password');
    await fireEvent.press(screen.getByTestId('password-submit'));
    expect(await screen.findByText(/Your password was saved/)).toBeOnTheScreen();
    expect(server.called('PUT /me/password')[0].body).toEqual({ newPassword: 'a much longer password' });
  });
});

describe('profile setup', () => {
  beforeEach(() => {
    resetEnvironment();
    jest.useFakeTimers();
  });
  afterEach(() => {
    jest.useRealTimers();
    delete global.fetch;
  });

  test('checks the handle live, saves the profile, and moves on to the paywall', async () => {
    const unset = rankField ? { [rankField.key]: null } : {};
    const start = makeUser({ handle: null, profile: defaultProfile(unset), profileComplete: false, profileMissing: ['handle'], membership: inactive });
    const server = installFakeServer({
      ...signedInAs(start),
      'GET /handles/taken/availability': { available: false },
      'GET /handles/alex.c/availability': { available: true },
      'PUT /me/profile': (call) => makeUser({ ...call.body, profile: defaultProfile(call.body.profile), profileComplete: true, membership: inactive })
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/profile-setup'));
    expect(screen.getByText('Set up your profile')).toBeOnTheScreen();

    await fireEvent.changeText(await screen.findByTestId('profile-handle'), 'taken');
    await act(async () => jest.advanceTimersByTime(500));
    expect(await screen.findByText('That handle is taken.')).toBeOnTheScreen();

    await fireEvent.changeText(screen.getByTestId('profile-handle'), 'alex.c');
    expect(screen.getByTestId('profile-handle-status')).toHaveTextContent('Checking availability');
    await act(async () => jest.advanceTimersByTime(500));
    expect(await screen.findByText('Available')).toBeOnTheScreen();

    let expected = {};
    if (rankField && rankField.collectAt === 'SignUp') {
      const rank = rankKeys[rankKeys.length - 1];
      await fireEvent.press(screen.getByTestId('profile-field-' + rankField.key + '-' + rank));
      let level = 0;
      if (rankHasLevel(rankField, rank)) {
        level = rankMaxLevel(rankField, rank);
        for (let i = 0; i < level + 1; i += 1) await fireEvent.press(screen.getByTestId('profile-field-' + rankField.key + '-level-inc'));
      }
      expected = { profile: { [rankField.key]: { key: rank, level } } };
    }
    await fireEvent.press(screen.getByTestId('profile-submit'));

    await waitFor(() => expect(router.getPathname()).toBe('/subscribe'));
    const body = server.called('PUT /me/profile')[0].body;
    expect(body).toMatchObject({ displayName: 'Test Member', handle: 'alex.c', ...expected });
    for (const key of ['rank', 'rankLevel', 'affiliation', 'city', 'region', 'country', 'goals', 'goalsOther', 'rankHistory']) expect(body).not.toHaveProperty(key);
  });

  test('a required sign-up field without a value is not sent', async () => {
    if (!rankField || !rankField.required) return;
    const start = makeUser({ handle: 'okhandle', profile: defaultProfile({ [rankField.key]: null }), profileComplete: false, profileMissing: [rankField.key], membership: inactive });
    const server = installFakeServer(signedInAs(start));
    await renderRouter(APP_DIR, { initialUrl: '/' });
    await fireEvent.press(await screen.findByTestId('profile-submit'));
    expect(await screen.findByTestId('profile-field-' + rankField.key + '-field-error')).toHaveTextContent(strings.profileFields.errors.required);
    expect(screen.getByText(strings.profileSetup.completeHeading)).toBeOnTheScreen();
    expect(server.called('PUT /me/profile')).toHaveLength(0);
  });
});
