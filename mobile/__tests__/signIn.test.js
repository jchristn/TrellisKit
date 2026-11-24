import { GoogleSignin } from '@react-native-google-signin/google-signin';
import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as AppleAuthentication from 'expo-apple-authentication';
import * as SecureStore from 'expo-secure-store';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { separateDocuments } from '../src/documents/documents';
import { validateRegistration } from '../src/forms';
import { visibleProviders } from '../src/native/social';
import { authResponse, disabledProviders, installFakeServer, makeUser, resetEnvironment } from '../test-utils/fakeServer';
import { property, tokens } from '../test-utils/property';
import { pickDate } from '../test-utils/dates';

const APP_DIR = path.resolve(__dirname, '../app');
const today = new Date(2026, 10, 11);
const MIN_AGE = property.membership.minimumAge;
const adultYear = String(2026 - MIN_AGE);
/** Documents with their own sign-up checkbox in the active property. */
const SEPARATE = separateDocuments(property);
const allSeparate = Object.fromEntries(SEPARATE.map((d) => [d.key, true]));
const enabled = {
  ...disabledProviders,
  apple: { enabled: true, webClientId: 'test.example.web' },
  google: { enabled: true, webClientId: 'web-id.apps.googleusercontent.com', iosClientId: 'ios-id.apps.googleusercontent.com', androidClientId: null },
  facebook: { enabled: true, appId: '123' }
};

describe('registration validation (minimum age from the property)', () => {
  const base = { displayName: 'Alex', email: 'alex@example.com', password: 'long enough pw', acceptTerms: true, acceptedDocuments: allSeparate };

  test('accepts an adult and formats the date as YYYY-MM-DD', () => {
    const result = validateRegistration({ ...base, dob: { month: '11', day: '11', year: adultYear } }, today);
    expect(result.errors).toEqual({});
    expect(result.dateOfBirth).toBe(adultYear + '-11-11');
  });

  test('rejects someone a day short of the minimum age', () => {
    const result = validateRegistration({ ...base, dob: { month: '11', day: '12', year: adultYear } }, today);
    expect(result.errors.dateOfBirth).toBe('You must be ' + MIN_AGE + ' or older to join ' + property.name + '.');
  });

  test('rejects impossible dates, short passwords, a password equal to the email, and unaccepted terms', () => {
    expect(validateRegistration({ ...base, dob: { month: '02', day: '30', year: '1990' } }, today).errors.dateOfBirth).toBe('Enter a valid date of birth.');
    expect(validateRegistration({ ...base, password: 'short', dob: { month: '1', day: '1', year: '1990' } }, today).errors.password).toBe('Use 10 to 128 characters.');
    expect(validateRegistration({ ...base, password: 'Alex@Example.com', dob: { month: '1', day: '1', year: '1990' } }, today).errors.password).toBe('Your password cannot be your email address.');
    expect(validateRegistration({ ...base, acceptTerms: false, dob: { month: '1', day: '1', year: '1990' } }, today).errors.acceptTerms).toBeTruthy();
  });
});

describe('Register screen', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  async function fill({ year }) {
    await fireEvent.changeText(await screen.findByTestId('register-name'), 'New Member');
    await fireEvent.changeText(screen.getByTestId('register-email'), 'new@example.com');
    await fireEvent.changeText(screen.getByTestId('register-password'), 'correct horse battery');
    await pickDate('register-dob', '01', '15', year);
    await fireEvent(screen.getByTestId('register-terms'), 'valueChange', true);
    for (const doc of SEPARATE) await fireEvent(screen.getByTestId('register-document-' + doc.key), 'valueChange', true);
  }

  test('blocks an underage date of birth without calling the server', async () => {
    const server = installFakeServer();
    await renderRouter(APP_DIR, { initialUrl: '/register' });
    await fill({ year: String(new Date().getFullYear() - MIN_AGE + 1) });
    await fireEvent.press(screen.getByTestId('register-submit'));
    expect(await screen.findByText('You must be ' + MIN_AGE + ' or older to join ' + property.name + '.')).toBeOnTheScreen();
    expect(server.called('POST /auth/register')).toHaveLength(0);
  });

  test('registers an adult, stores the session, and continues to profile setup', async () => {
    const created = makeUser({ id: 'usr_new', displayName: 'New Member', handle: null, rank: null, profileComplete: false, membership: { isActive: false } });
    const server = installFakeServer({ 'POST /auth/register': { status: 201, body: { ...authResponse(created, 'r-new'), isNewAccount: true } } });
    const router = await renderRouter(APP_DIR, { initialUrl: '/register' });
    await fill({ year: '1990' });
    await fireEvent.press(screen.getByTestId('register-submit'));
    await waitFor(() => expect(router.getPathname()).toBe('/profile-setup'));
    expect(server.called('POST /auth/register')[0].body).toEqual({
      email: 'new@example.com',
      password: 'correct horse battery',
      displayName: 'New Member',
      dateOfBirth: '1990-01-15',
      acceptTerms: true,
      acceptedDocuments: SEPARATE.map((d) => ({ key: d.key, version: d.version }))
    });
    expect(SecureStore.__store.get('tk.refreshToken')).toBe('r-new');
  });

  test('shows the server message for 403 AgeRequirementNotMet', async () => {
    installFakeServer({ 'POST /auth/register': { status: 403, body: { error: 'AgeRequirementNotMet', message: 'Too young.' } } });
    await renderRouter(APP_DIR, { initialUrl: '/register' });
    await fill({ year: '1990' });
    await fireEvent.press(screen.getByTestId('register-submit'));
    expect(await screen.findByText(tokens.name + ' is for people ' + tokens.minimumAge + ' and older.')).toBeOnTheScreen();
  });
});

describe('email sign-in errors', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test.each([
    [401, 'InvalidCredentials', 'x', 'That email and password do not match an account.'],
    [423, 'AccountLocked', 'Try again after 9:15 PM.', 'Try again after 9:15 PM.'],
    [403, 'AccountBanned', 'Banned.', 'This account has been suspended.']
  ])('%s %s', async (status, error, message, expected) => {
    installFakeServer({ 'POST /auth/login': { status, body: { error, message } } });
    await renderRouter(APP_DIR, { initialUrl: '/email-sign-in' });
    await fireEvent.changeText(await screen.findByTestId('email-input'), 'a@example.com');
    await fireEvent.changeText(screen.getByTestId('password-input'), 'whatever password');
    await fireEvent.press(screen.getByTestId('email-sign-in-submit'));
    expect(await screen.findByText(expected)).toBeOnTheScreen();
  });

  test('a successful sign-in of an active member lands on the feed', async () => {
    SecureStore.__store.set('tk.notificationsPrompted.usr_1', '1');
    installFakeServer({ 'POST /auth/login': authResponse(makeUser()) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/email-sign-in' });
    await fireEvent.changeText(await screen.findByTestId('email-input'), 'member@example.com');
    await fireEvent.changeText(screen.getByTestId('password-input'), 'correct horse battery');
    await fireEvent.press(screen.getByTestId('email-sign-in-submit'));
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
  });
});

describe('provider visibility', () => {
  const all = { appleAvailable: true, googleAvailable: true, facebookAvailable: true };

  test('hides providers that /auth/providers disables', () => {
    expect(visibleProviders(disabledProviders, { os: 'ios', ...all })).toEqual({ apple: false, google: false, facebook: false, email: true });
    expect(visibleProviders(enabled, { os: 'ios', ...all })).toEqual({ apple: true, google: true, facebook: true, email: true });
  });

  test('hides providers whose native module is missing (Expo Go)', () => {
    expect(visibleProviders(enabled, { os: 'android', appleAvailable: false, googleAvailable: false, facebookAvailable: false })).toEqual({ apple: false, google: false, facebook: false, email: true });
  });

  test('on iOS, Google and Facebook appear only alongside Sign in with Apple (Guideline 4.8)', () => {
    const noApple = { ...enabled, apple: { enabled: false } };
    expect(visibleProviders(noApple, { os: 'ios', ...all })).toMatchObject({ apple: false, google: false, facebook: false });
    expect(visibleProviders(enabled, { os: 'ios', ...all, appleAvailable: false })).toMatchObject({ google: false, facebook: false });
  });

  test('Apple never appears on Android', () => {
    expect(visibleProviders(enabled, { os: 'android', ...all })).toMatchObject({ apple: false, google: true, facebook: true });
  });
});

describe('sign-in screen', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('shows only email when every provider is disabled', async () => {
    installFakeServer();
    await renderRouter(APP_DIR, { initialUrl: '/sign-in' });
    expect(await screen.findByTestId('sign-in-email')).toBeOnTheScreen();
    expect(screen.queryByTestId('sign-in-apple')).toBeNull();
    expect(screen.queryByTestId('sign-in-google')).toBeNull();
    expect(screen.queryByTestId('sign-in-facebook')).toBeNull();
  });

  test('shows Apple and Google when enabled; Facebook stays hidden without its native module', async () => {
    installFakeServer({ 'GET /auth/providers': enabled });
    await renderRouter(APP_DIR, { initialUrl: '/sign-in' });
    expect(await screen.findByTestId('sign-in-apple')).toBeOnTheScreen();
    expect(screen.getByTestId('sign-in-google')).toBeOnTheScreen();
    expect(screen.queryByTestId('sign-in-facebook')).toBeNull();
  });

  test('Sign in with Apple hashes the nonce for Apple and sends the raw nonce to the server', async () => {
    AppleAuthentication.signInAsync.mockResolvedValueOnce({ identityToken: 'apple-jwt', fullName: { givenName: 'Alex', familyName: 'C' } });
    const server = installFakeServer({ 'GET /auth/providers': enabled, 'POST /auth/apple': authResponse(makeUser({ ageAttested: false, profileComplete: false })) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/sign-in' });
    await fireEvent.press(await screen.findByTestId('sign-in-apple'));
    await waitFor(() => expect(router.getPathname()).toBe('/age'));
    const sentToApple = AppleAuthentication.signInAsync.mock.calls[0][0].nonce;
    const body = server.called('POST /auth/apple')[0].body;
    expect(body).toMatchObject({ identityToken: 'apple-jwt', givenName: 'Alex', familyName: 'C' });
    expect(sentToApple).toBe('sha256(' + body.nonce + ')');
  });

  test('Google uses the client ids from /auth/providers and explains 409 AccountLinkRequired', async () => {
    GoogleSignin.signIn.mockResolvedValueOnce({ type: 'success', data: { idToken: 'google-jwt' } });
    const server = installFakeServer({
      'GET /auth/providers': enabled,
      'POST /auth/google': { status: 409, body: { error: 'AccountLinkRequired', message: 'Link required', existingMethods: ['Email', 'Apple'] } }
    });
    await renderRouter(APP_DIR, { initialUrl: '/sign-in' });
    await fireEvent.press(await screen.findByTestId('sign-in-google'));
    expect(await screen.findByText('An account with this email already exists. Sign in with email and password or Apple, then link Google in Account settings.')).toBeOnTheScreen();
    expect(GoogleSignin.configure).toHaveBeenCalledWith(expect.objectContaining({ webClientId: 'web-id.apps.googleusercontent.com', iosClientId: 'ios-id.apps.googleusercontent.com' }));
    expect(server.called('POST /auth/google')[0].body).toEqual({ idToken: 'google-jwt' });
  });

  test('a cancelled Apple sheet sends nothing', async () => {
    AppleAuthentication.signInAsync.mockRejectedValueOnce(Object.assign(new Error('cancel'), { code: 'ERR_REQUEST_CANCELED' }));
    const server = installFakeServer({ 'GET /auth/providers': enabled });
    await renderRouter(APP_DIR, { initialUrl: '/sign-in' });
    await fireEvent.press(await screen.findByTestId('sign-in-apple'));
    await waitFor(() => expect(AppleAuthentication.signInAsync).toHaveBeenCalled());
    expect(server.called('POST /auth/apple')).toHaveLength(0);
    expect(screen.queryByTestId('sign-in-error')).toBeNull();
  });
});

describe('age attestation', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('403 AgeRequirementNotMet signs out and explains why', async () => {
    const { signedInAs } = require('../test-utils/fakeServer');
    installFakeServer({ ...signedInAs(makeUser({ ageAttested: false, profileComplete: false })), 'POST /me/age-attestation': { status: 403, body: { error: 'AgeRequirementNotMet', message: 'Closed.' } } });
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await pickDate('age-dob', '01', '01', String(new Date().getFullYear() - 12));
    await fireEvent.press(screen.getByTestId('age-submit'));
    await waitFor(() => expect(router.getPathname()).toBe('/welcome'));
    expect(await screen.findByText(tokens.name + ' is for people ' + tokens.minimumAge + ' and older, so this account has been closed.')).toBeOnTheScreen();
    expect(SecureStore.__store.has('tk.refreshToken')).toBe(false);
  });
});

describe('Google Sign-In native module check', () => {
  test('a build without the native module hides Google without requiring the package', () => {
    const { NativeModules } = require('react-native');
    const saved = NativeModules.RNGoogleSignin;
    delete NativeModules.RNGoogleSignin;
    try {
      jest.isolateModules(() => {
        const google = require('../src/native/googleAuth');
        expect(google.hasGoogleNativeModule({ registry: { get: () => null }, modules: {} })).toBe(false);
        expect(google.hasGoogleNativeModule({ registry: { get: () => { throw new Error('no proxy'); } }, modules: {} })).toBe(false);
        expect(google.hasGoogleNativeModule({ registry: { get: () => ({}) }, modules: {} })).toBe(true);
        // Requiring the real package here would throw from TurboModuleRegistry.getEnforcing, which
        // Metro reports as a fatal error; the loader must return null before that.
        expect(google.getGoogleSignInModule()).toBeNull();
        expect(google.isGoogleSignInAvailable()).toBe(false);
      });
    } finally {
      NativeModules.RNGoogleSignin = saved;
    }
  });
});
