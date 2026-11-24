import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { canEnterTabs, hasMemberAccess, nextRoute } from '../src/auth/routing';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';

const APP_DIR = path.resolve(__dirname, '../app');
const inactive = { isActive: false, source: null, store: null, expiresUtc: null, autoRenew: false, willConvertToPaid: false };
const admin = [{ role: 'SystemAdmin', resourceType: 'Global', resourceId: '*' }];

describe('nextRoute', () => {
  const signedIn = (user, notificationsPrompted = true) => ({ status: 'signedIn', user, notificationsPrompted });

  test('walks the onboarding states in order', () => {
    expect(nextRoute({ status: 'signedOut', user: null })).toBe('/welcome');
    expect(nextRoute(signedIn(makeUser({ ageAttested: false, profileComplete: false, membership: inactive })))).toBe('/age');
    expect(nextRoute(signedIn(makeUser({ profileComplete: false, membership: inactive })))).toBe('/profile-setup');
    expect(nextRoute(signedIn(makeUser({ membership: inactive })))).toBe('/subscribe');
    expect(nextRoute(signedIn(makeUser(), false))).toBe('/allow-notifications');
    expect(nextRoute(signedIn(makeUser()))).toBe('/feed');
  });

  test('an incomplete profile keeps an existing member out of the tabs and sends them to profile setup', () => {
    const user = makeUser({ profileComplete: false, profileMissing: ['newField'] });
    expect(canEnterTabs(signedIn(user))).toBe(false);
    expect(nextRoute(signedIn(user))).toBe('/profile-setup');
    expect(canEnterTabs(signedIn(makeUser()))).toBe(true);
  });

  test('SystemAdmin passes the membership check but not age attestation', () => {
    expect(hasMemberAccess(makeUser({ membership: inactive, roles: admin }))).toBe(true);
    expect(nextRoute(signedIn(makeUser({ membership: inactive, roles: admin })))).toBe('/feed');
    expect(canEnterTabs(signedIn(makeUser({ membership: inactive, roles: admin, ageAttested: false })))).toBe(false);
  });

  test('a membership from any source (including one started on the website) lets the member in', () => {
    for (const source of ['Subscription', 'SignupCode', 'AdminComp']) {
      expect(nextRoute(signedIn(makeUser({ membership: { isActive: true, source, store: source === 'Subscription' ? 'Stripe' : null, expiresUtc: null } })))).toBe('/feed');
    }
  });
});

describe('session restore at launch routes by state', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test.each([
    ['age attestation', makeUser({ ageAttested: false, profileComplete: false, membership: inactive }), '/age', 'Confirm your age'],
    ['profile setup', makeUser({ profileComplete: false, membership: inactive }), '/profile-setup', 'Set up your profile'],
    ['paywall', makeUser({ membership: inactive }), '/subscribe', 'Become a member'],
    ['feed', makeUser(), '/feed', null]
  ])('%s', async (_name, user, expectedPath, heading) => {
    installFakeServer(signedInAs(user));
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe(expectedPath));
    if (heading) expect(await screen.findByText(heading)).toBeOnTheScreen();
    else expect(await screen.findByTestId('screen-feed')).toBeOnTheScreen();
  });

  test('the notification prompt shows once, then the tabs', async () => {
    installFakeServer(signedInAs(makeUser(), { notificationsPrompted: false }));
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/allow-notifications'));
    await fireEvent.press(await screen.findByTestId('allow-notifications-continue'));
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
  });

  test('a revoked refresh token signs out to Welcome', async () => {
    installFakeServer({ ...signedInAs(makeUser()), 'POST /auth/refresh': { status: 401, body: { error: 'InvalidRefreshToken' } } });
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/welcome'));
  });

  test('an unreachable server keeps the session and offers Retry', async () => {
    const server = installFakeServer({ ...signedInAs(makeUser()), 'POST /auth/refresh': { __network: true } });
    await renderRouter(APP_DIR, { initialUrl: '/' });
    expect(await screen.findByTestId('launch-offline')).toBeOnTheScreen();
    server.handlers['POST /auth/refresh'] = signedInAs(makeUser())['POST /auth/refresh'];
    await fireEvent.press(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByTestId('screen-feed')).toBeOnTheScreen();
  });
});

describe('tab gating', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('signed-out visitors cannot open the tabs', async () => {
    installFakeServer();
    const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await waitFor(() => expect(router.getPathname()).toBe('/welcome'));
    expect(screen.queryByTestId('screen-feed')).toBeNull();
  });

  test('members without a membership are sent to the paywall', async () => {
    installFakeServer(signedInAs(makeUser({ membership: inactive })));
    const router = await renderRouter(APP_DIR, { initialUrl: '/forums' });
    await waitFor(() => expect(router.getPathname()).toBe('/subscribe'));
    expect(screen.queryByTestId('screen-forums')).toBeNull();
  });

  test('members whose profile is incomplete are sent to profile setup from the tabs', async () => {
    installFakeServer(signedInAs(makeUser({ profileComplete: false, profileMissing: ['handle'] })));
    const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await waitFor(() => expect(router.getPathname()).toBe('/profile-setup'));
    expect(screen.queryByTestId('screen-feed')).toBeNull();
  });

  test('accounts that have not attested their age are sent to age attestation', async () => {
    installFakeServer(signedInAs(makeUser({ ageAttested: false })));
    const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await waitFor(() => expect(router.getPathname()).toBe('/age'));
  });

  test('a SystemAdmin without a membership can open the tabs', async () => {
    installFakeServer(signedInAs(makeUser({ membership: inactive, roles: admin })));
    const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
    expect(await screen.findByTestId('screen-feed')).toBeOnTheScreen();
    expect(router.getPathname()).toBe('/feed');
  });
});
