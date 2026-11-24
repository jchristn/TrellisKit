import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { router as appRouter } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { canEnterTabs, isGuest, needsPaywall, nextRoute } from '../src/auth/routing';
import strings from '../src/strings';
import { enumerationResult, likeKey, makePost } from '../test-utils/community';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { property } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');
const inactive = { isActive: false, source: null, store: null, expiresUtc: null, autoRenew: false, willConvertToPaid: false };

const ACCESS = {
  Open: { model: 'Open', anonymousBrowsing: true, gatedFeatures: [], defaultAudience: 'SignedIn', purchasesEnabled: false, signupCodesEnabled: false, revision: 2 },
  PartiallyGated: { model: 'PartiallyGated', anonymousBrowsing: true, gatedFeatures: ['forums', 'members', 'directory'], defaultAudience: 'SignedIn', purchasesEnabled: true, signupCodesEnabled: true, revision: 3 },
  FullyGated: { model: 'FullyGated', anonymousBrowsing: false, gatedFeatures: [], defaultAudience: 'Members', purchasesEnabled: true, signupCodesEnabled: true, revision: 1 }
};

function runtime(model, access = {}) {
  return {
    ...property,
    access: { ...ACCESS[model], ...access },
    membership: model === 'Open' ? null : property.membership,
    minimumAge: (property.membership && property.membership.minimumAge) || 18,
    features: { ...(property.features || {}), forums: true, search: true }
  };
}

const nonMember = (overrides = {}) => makeUser({ membership: inactive, accessTier: 'SignedIn', membershipRequired: false, ...overrides });

describe('routing follows the access model', () => {
  const signedIn = (user) => ({ status: 'signedIn', user, notificationsPrompted: true });

  test('the paywall step applies only when Me.membershipRequired is true', () => {
    expect(needsPaywall(nonMember())).toBe(false);
    expect(nextRoute(signedIn(nonMember()))).toBe('/feed');
    expect(canEnterTabs(signedIn(nonMember()))).toBe(true);
    expect(nextRoute(signedIn(nonMember({ membershipRequired: true })))).toBe('/subscribe');
    expect(canEnterTabs(signedIn(nonMember({ membershipRequired: true })))).toBe(false);
  });

  test('guests browse only after choosing to, and never in FullyGated', () => {
    const browsing = { status: 'signedOut', user: null, browsing: true };
    expect(isGuest(browsing, runtime('PartiallyGated'))).toBe(true);
    expect(isGuest(browsing, runtime('Open'))).toBe(true);
    expect(isGuest(browsing, runtime('FullyGated', { anonymousBrowsing: true }))).toBe(false);
    expect(isGuest({ status: 'signedOut', user: null }, runtime('PartiallyGated'))).toBe(false);
    expect(canEnterTabs(browsing, runtime('PartiallyGated'))).toBe(true);
    expect(canEnterTabs(browsing)).toBe(false);
  });
});

describe('Open', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('a guest who sees no forums gets the sign-in prompt, not "no forums yet"', async () => {
    installFakeServer({ 'GET /property': runtime('Open'), 'POST /forums/enumerate': enumerationResult([]), 'GET /forums': enumerationResult([]) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/welcome'));
    await fireEvent.press(await screen.findByTestId('welcome-browse'));
    await act(async () => appRouter.push('/forums'));
    expect(await screen.findByTestId('forum-list-guest')).toBeOnTheScreen();
    expect(screen.getByText(strings.forums.guestEmptyTitle)).toBeOnTheScreen();
    expect(screen.queryByText(strings.forums.empty)).toBeNull();
    expect(screen.getByTestId('forum-list-guest-sign-in')).toBeOnTheScreen();
  });

  test('a signed-in account without a membership reaches the feed, and Account has no membership section', async () => {
    const server = installFakeServer({ ...signedInAs(nonMember()), 'GET /property': runtime('Open'), 'POST /feed/enumerate': enumerationResult([makePost('fpo_1', { audience: 'SignedIn' })]) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
    expect(await screen.findByTestId('post-fpo_1')).toBeOnTheScreen();
    await waitFor(() => expect(server.called('GET /property').length).toBeGreaterThan(0));
    await act(async () => appRouter.push('/account'));
    expect(await screen.findByTestId('account-sign-in-methods')).toBeOnTheScreen();
    await waitFor(() => expect(screen.queryByTestId('account-membership')).toBeNull());
    expect(server.calls.some((c) => c.path.startsWith('/membership') || c.path.startsWith('/signup-codes'))).toBe(false);
  });
});

describe('PartiallyGated', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('members-only posts are locked cards that open the in-app purchase and never load their detail', async () => {
    const stub = makePost('fpo_l', { title: 'Members briefing', body: '', excerpt: '', audience: 'Members', locked: true, viewer: { canOpen: false, requires: 'Membership', canComment: false, canReact: false } });
    const server = installFakeServer({ ...signedInAs(nonMember()), 'GET /property': runtime('PartiallyGated'), 'POST /feed/enumerate': enumerationResult([makePost('fpo_o'), stub]) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
    expect(await screen.findByTestId('post-fpo_l-locked')).toBeOnTheScreen();
    expect(screen.getByText('Members briefing')).toBeOnTheScreen();
    expect(screen.getByText(strings.access.lockedPostBody)).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('post-fpo_l-access-upgrade'));
    await waitFor(() => expect(router.getPathname()).toBe('/subscribe'));
    expect(await screen.findByTestId('paywall-not-now')).toBeOnTheScreen();
    expect(server.calls.some((c) => c.path.includes('fpo_l'))).toBe(false);
  });

  test('a gated forum shows the upgrade prompt and makes no forum calls', async () => {
    const server = installFakeServer({ ...signedInAs(nonMember()), 'GET /property': runtime('PartiallyGated'), 'POST /feed/enumerate': enumerationResult([]) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await screen.findByTestId('screen-feed');
    await waitFor(() => expect(server.called('GET /property').length).toBeGreaterThan(0));
    await act(async () => appRouter.push('/forums'));
    expect(await screen.findByTestId('feature-locked-forums-prompt')).toBeOnTheScreen();
    expect(screen.getByTestId('feature-locked-forums-prompt-upgrade')).toBeOnTheScreen();
    expect(server.calls.some((c) => c.path.startsWith('/forums'))).toBe(false);
  });

  test('members open gated forums', async () => {
    installFakeServer({ ...signedInAs(makeUser({ accessTier: 'Member', membershipRequired: false })), 'GET /property': runtime('PartiallyGated'), 'POST /forums/enumerate': enumerationResult([]), 'GET /forums': enumerationResult([]) });
    await renderRouter(APP_DIR, { initialUrl: '/forums' });
    expect(await screen.findByTestId('screen-forums')).toBeOnTheScreen();
    expect(screen.queryByTestId('feature-locked-forums-prompt')).toBeNull();
  });

  test('a 402 on a reaction opens the upgrade sheet', async () => {
    const denied = { status: 402, body: { error: 'MembershipRequired', message: 'This is for members.', required: 'Membership', feature: 'feed', targetType: 'FeedPost', targetId: 'fpo_1' } };
    installFakeServer(
      { ...signedInAs(nonMember()), 'GET /property': runtime('PartiallyGated'), 'POST /feed/enumerate': enumerationResult([makePost('fpo_1', { reactionCounts: { [likeKey]: 1 }, reactionTotal: 1 })]) },
      { fallback: (call) => (call.path.startsWith('/feed/fpo_1/reactions/') ? denied : undefined) }
    );
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await fireEvent.press(await screen.findByTestId('post-fpo_1-reactions-chip-' + likeKey));
    expect(await screen.findByTestId('access-sheet-upgrade')).toBeOnTheScreen();
  });

  test('guests browse the feed from Welcome with a sign-in prompt', async () => {
    const server = installFakeServer({ 'GET /property': runtime('PartiallyGated'), 'POST /feed/enumerate': enumerationResult([makePost('fpo_p', { audience: 'Public', viewer: { canComment: false, canReact: false, canReport: false } })]) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/welcome'));
    await fireEvent.press(await screen.findByTestId('welcome-browse'));
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
    expect(await screen.findByTestId('post-fpo_p')).toBeOnTheScreen();
    expect(screen.getByTestId('feed-join-in')).toBeOnTheScreen();
    expect(screen.queryByTestId('feed-new-post')).toBeNull();
    const call = server.called('POST /feed/enumerate')[0];
    expect(call.headers.Authorization || call.headers.authorization).toBeUndefined();
  });

  test('posters pick SignedIn or Members, defaulting to the property default', async () => {
    const poster = nonMember({ flags: { canPostToFeed: true } });
    const server = installFakeServer({ ...signedInAs(poster), 'GET /property': runtime('PartiallyGated'), 'POST /feed': (c) => ({ status: 201, body: makePost('fpo_new', { audience: c.body.audience }) }) });
    await renderRouter(APP_DIR, { initialUrl: '/compose' });
    expect(await screen.findByTestId('compose-audience')).toBeOnTheScreen();
    expect(screen.getByText(strings.access.audiences.SignedIn)).toBeOnTheScreen();
    expect(screen.getByText(strings.access.audiences.Members)).toBeOnTheScreen();
    expect(screen.queryByText(strings.access.audiences.Public)).toBeNull();
    await fireEvent.changeText(await screen.findByTestId('compose-body'), 'Hello');
    await fireEvent.press(screen.getByText(strings.access.audiences.Members));
    await fireEvent.press(screen.getByTestId('compose-submit'));
    await waitFor(() => expect(server.called('POST /feed')).toHaveLength(1));
    expect(server.called('POST /feed')[0].body.audience).toBe('Members');
  });
});

describe('FullyGated', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('a non-member goes to the paywall with no Not now', async () => {
    installFakeServer({ ...signedInAs(nonMember({ membershipRequired: true })), 'GET /property': runtime('FullyGated') });
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/subscribe'));
    expect(screen.queryByTestId('paywall-not-now')).toBeNull();
  });

  test('Welcome offers no guest browsing, even when the stored flag is on', async () => {
    const server = installFakeServer({ 'GET /property': runtime('FullyGated', { anonymousBrowsing: true }) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/welcome'));
    await waitFor(() => expect(server.called('GET /property').length).toBeGreaterThan(0));
    await waitFor(() => expect(screen.queryByTestId('welcome-browse')).toBeNull());
  });
});

describe('search gating', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('uses the search key, not the forums', async () => {
    const server = installFakeServer({ ...signedInAs(nonMember()), 'GET /property': runtime('PartiallyGated', { gatedFeatures: ['search'] }) });
    await renderRouter(APP_DIR, { initialUrl: '/forums/search' });
    expect(await screen.findByTestId('feature-locked-search-prompt')).toBeOnTheScreen();
    expect(server.calls.some((c) => c.path.startsWith('/search'))).toBe(false);
  });

  test('stays open when only the forums are gated', async () => {
    installFakeServer({ ...signedInAs(nonMember()), 'GET /property': runtime('PartiallyGated', { gatedFeatures: ['forums'] }) });
    await renderRouter(APP_DIR, { initialUrl: '/forums/search' });
    expect(await screen.findByTestId('screen-search')).toBeOnTheScreen();
    expect(screen.queryByTestId('feature-locked-forums-prompt')).toBeNull();
  });

  test('hides the search entry when features.search is false', async () => {
    const member = makeUser({ accessTier: 'Member', membershipRequired: false });
    const off = runtime('PartiallyGated', { gatedFeatures: [] });
    installFakeServer({ ...signedInAs(member), 'GET /property': { ...off, features: { ...off.features, search: false } }, 'POST /forums/enumerate': enumerationResult([]), 'GET /forums': enumerationResult([]) });
    await renderRouter(APP_DIR, { initialUrl: '/forums' });
    expect(await screen.findByTestId('screen-forums')).toBeOnTheScreen();
    await waitFor(() => expect(screen.queryByTestId('forums-header-search')).toBeNull());
    expect(screen.getByTestId('forums-header-following')).toBeOnTheScreen();
  });

  test('opens search when neither is gated', async () => {
    installFakeServer({ ...signedInAs(nonMember()), 'GET /property': runtime('PartiallyGated', { gatedFeatures: ['library'] }) });
    await renderRouter(APP_DIR, { initialUrl: '/forums/search' });
    expect(await screen.findByTestId('screen-search')).toBeOnTheScreen();
  });
});
