import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import strings from '../src/strings.js';
import { BUILD_SITE } from '../src/property/buildSite.js';
import AccessModelView from '../src/views/admin/AccessModelView.jsx';
import SignupCodesView from '../src/views/admin/SignupCodesView.jsx';
import { useRuntimeProperty } from '../src/context/PropertyContext.jsx';
import { useAdminAuth } from '../src/context/AdminAuthContext.jsx';
import { onboardingRedirect } from '../src/utils/onboarding.js';
import { SESSION_HINT_KEY } from '../src/context/AuthContext.jsx';
import { authResponse, createFetch, json, makeMe, renderAdmin, renderSite } from './helpers.jsx';
import { enumPage, makePost } from './engagementHelpers.jsx';

const a = strings.access;

const MODELS = {
  Open: { model: 'Open', anonymousBrowsing: true, gatedFeatures: [], defaultAudience: 'SignedIn', purchasesEnabled: false, signupCodesEnabled: false, revision: 2 },
  PartiallyGated: { model: 'PartiallyGated', anonymousBrowsing: true, gatedFeatures: ['forums', 'members', 'directory'], defaultAudience: 'SignedIn', purchasesEnabled: true, signupCodesEnabled: true, revision: 3 },
  FullyGated: { model: 'FullyGated', anonymousBrowsing: false, gatedFeatures: [], defaultAudience: 'Members', purchasesEnabled: true, signupCodesEnabled: true, revision: 1 }
};

function siteFor(model, extra = {}) {
  const base = BUILD_SITE.property;
  const property = {
    ...base,
    access: MODELS[model],
    membership: model === 'Open' ? null : base.membership,
    minimumAge: (base.membership && base.membership.minimumAge) || 18,
    features: { ...(base.features || {}), forums: true, search: true },
    ...extra
  };
  return { ...BUILD_SITE, property };
}

const nonMember = (overrides = {}) => makeMe({ membership: { isActive: false, source: null, store: null, expiresUtc: null }, accessTier: 'SignedIn', membershipRequired: false, ...overrides });

/** The member app with `me` signed in (or signed out when `me` is null) and `handler` for the rest. */
function appFetch(me, handler = () => undefined) {
  if (me) window.localStorage.setItem(SESSION_HINT_KEY, '1');
  else window.localStorage.removeItem(SESSION_HINT_KEY);
  return createFetch((call) => {
    if (call.path === '/api/v1/auth/refresh') return me ? json(authResponse(me)) : json({ error: 'InvalidRefreshToken', message: 'No session.' }, 401);
    if (call.path === '/api/v1/media/grant') return json({ mode: 'Local', expiresUtc: '2099-01-01T00:00:00Z', cookies: [] });
    if (call.path === '/api/v1/notifications/unread-count') return json({ unreadCount: 0 });
    if (call.path === '/api/v1/reactions/set/enumerate') return json({ objects: [], endOfResults: true });
    return handler(call);
  });
}

describe('onboarding follows Me.membershipRequired', () => {
  it('skips the paywall step unless the server requires it', () => {
    expect(onboardingRedirect(nonMember())).toBeNull();
    expect(onboardingRedirect(nonMember({ membershipRequired: true }))).toBe('/app/membership');
    // Older servers without the field behave as FullyGated.
    expect(onboardingRedirect(makeMe({ membership: { isActive: false } }))).toBe('/app/membership');
  });
});

describe('Open access model', () => {
  it('lets a signed-in account in without a membership and hides every purchase surface', async () => {
    const { fetchImpl, calls } = appFetch(nonMember(), (call) => {
      if (call.path === '/api/v1/feed') return json(enumPage([makePost('fpo_1', { audience: 'SignedIn' })]));
      return undefined;
    });
    renderSite('/app/feed', { site: siteFor('Open'), fetchImpl });
    expect(await screen.findByText('Post fpo_1')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /Account menu/ }));
    expect(screen.queryByRole('menuitem', { name: strings.member.menuMembership })).toBeNull();
    expect(screen.queryAllByText(a.lockBadge)).toHaveLength(0);
    expect(calls.some((c) => c.path.startsWith('/api/v1/membership') || c.path.startsWith('/api/v1/signup-codes'))).toBe(false);
  });

  it('sends /join to sign-up with no sign-up code field', async () => {
    const { fetchImpl } = appFetch(null);
    renderSite('/join?code=ABC-123', { site: siteFor('Open'), fetchImpl });
    expect(await screen.findByRole('heading', { name: strings.auth.signUpTitle })).toBeInTheDocument();
    expect(screen.queryByLabelText(strings.auth.signupCode)).toBeNull();
    expect(screen.queryByText(strings.auth.haveCode)).toBeNull();
  });

  it('offers SignedIn and Public to a SystemAdmin poster and sends the audience', async () => {
    const admin = nonMember({ roles: [{ role: 'SystemAdmin', resourceType: null, resourceId: null }], accessTier: 'Staff', flags: { canPostToFeed: true, canPost: true, canPostMedia: true, canUploadVideo: true } });
    const { fetchImpl, calls } = appFetch(admin, (call) => {
      if (call.path === '/api/v1/feed' && call.method === 'GET') return json(enumPage([]));
      if (call.path === '/api/v1/feed' && call.method === 'POST') return json(makePost('fpo_new', { audience: call.body.audience }), 201);
      return undefined;
    });
    renderSite('/app/feed', { site: siteFor('Open'), fetchImpl });
    await userEvent.click(await screen.findByRole('button', { name: strings.feed.write }));
    const picker = screen.getByLabelText(a.audienceLabel);
    expect(within(picker).getAllByRole('option').map((o) => o.value)).toEqual(['SignedIn', 'Public']);
    expect(picker).toHaveValue('SignedIn');
    await userEvent.type(document.getElementById('post-new-body'), 'Hello');
    await userEvent.click(screen.getByRole('button', { name: strings.composer.submitPost }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/feed' && c.method === 'POST')).toBeTruthy());
    const post = calls.find((c) => c.path === '/api/v1/feed' && c.method === 'POST');
    expect(post.body.audience).toBe('SignedIn');
    expect(post.body.visibility).toBeUndefined();
  });
});

describe('PartiallyGated access model', () => {
  it('renders members-only posts as locked cards and never loads their detail', async () => {
    const stub = makePost('fpo_locked', { title: 'Members briefing', body: '', excerpt: '', audience: 'Members', locked: true, viewer: { canOpen: false, requires: 'Membership', canComment: false, canReact: false } });
    const { fetchImpl, calls } = appFetch(nonMember(), (call) => {
      if (call.path === '/api/v1/feed') return json(enumPage([makePost('fpo_open', { audience: 'SignedIn' }), stub]));
      return undefined;
    });
    renderSite('/app/feed', { site: siteFor('PartiallyGated'), fetchImpl });
    const heading = await screen.findByRole('heading', { name: 'Members briefing' });
    const card = heading.closest('article');
    expect(card).toHaveAttribute('data-locked', 'true');
    expect(within(card).getByRole('link', { name: a.upgradeCta })).toHaveAttribute('href', '/app/membership?feature=feed');
    expect(within(card).queryByRole('link', { name: 'Members briefing' })).toBeNull();
    expect(screen.getByText('Post fpo_open')).toBeInTheDocument();
    expect(calls.some((c) => c.path.includes('fpo_locked'))).toBe(false);
  });

  it('badges gated features in the navigation and shows the upgrade prompt instead of the page', async () => {
    const { fetchImpl, calls } = appFetch(nonMember(), (call) => {
      if (call.path === '/api/v1/feed') return json(enumPage([]));
      return undefined;
    });
    renderSite('/app/forums', { site: siteFor('PartiallyGated'), fetchImpl });
    expect(await screen.findByRole('heading', { name: /Become a/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: a.upgradeCta })).toHaveAttribute('href', '/app/membership?feature=forums');
    const nav = screen.getByRole('navigation', { name: strings.member.navLabel });
    expect(within(nav).getAllByLabelText(a.lockBadgeLabel(a.features.forums)).length).toBeGreaterThan(0);
    expect(within(nav).queryByLabelText(a.lockBadgeLabel(a.features.feed))).toBeNull();
    expect(calls.some((c) => c.path.startsWith('/api/v1/forums'))).toBe(false);
  });

  it('lets members through with no lock badges', async () => {
    const member = makeMe({ accessTier: 'Member', membershipRequired: false });
    const { fetchImpl } = appFetch(member, (call) => {
      if (call.path === '/api/v1/forums' || call.path === '/api/v1/forums/enumerate') return json(enumPage([]));
      return undefined;
    });
    renderSite('/app/forums', { site: siteFor('PartiallyGated'), fetchImpl });
    expect(await screen.findByRole('heading', { name: strings.forums.title, level: 1 })).toBeInTheDocument();
    expect(screen.queryAllByText(a.lockBadge)).toHaveLength(0);
  });

  it('turns a 402 into the upgrade dialog', async () => {
    const { fetchImpl } = appFetch(nonMember(), (call) => {
      if (call.path === '/api/v1/feed') return json(enumPage([makePost('fpo_1', { audience: 'SignedIn' })]));
      if (call.path.startsWith('/api/v1/feed/fpo_1/reactions/')) return json({ error: 'MembershipRequired', message: 'This is for members.', required: 'Membership', feature: 'feed', targetType: 'FeedPost', targetId: 'fpo_1' }, 402);
      return undefined;
    });
    renderSite('/app/feed', { site: siteFor('PartiallyGated'), fetchImpl });
    await screen.findByText('Post fpo_1');
    const card = screen.getByText('Post fpo_1').closest('article');
    const react = within(card).getAllByRole('button').find((b) => /react|like/i.test(b.getAttribute('aria-label') || b.textContent));
    await userEvent.click(react);
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByRole('link', { name: a.upgradeCta })).toBeInTheDocument();
  });

  it('offers SignedIn and Members to a poster, defaulting to the property default', async () => {
    const poster = nonMember({ flags: { canPostToFeed: true, canPost: true, canPostMedia: true, canUploadVideo: true } });
    const { fetchImpl } = appFetch(poster, (call) => {
      if (call.path === '/api/v1/feed') return json(enumPage([]));
      return undefined;
    });
    renderSite('/app/feed', { site: siteFor('PartiallyGated'), fetchImpl });
    await userEvent.click(await screen.findByRole('button', { name: strings.feed.write }));
    const picker = screen.getByLabelText(a.audienceLabel);
    expect(within(picker).getAllByRole('option').map((o) => o.value)).toEqual(['SignedIn', 'Members']);
    expect(picker).toHaveValue('SignedIn');
  });

  it('replaces the composer with the upgrade prompt when the feed is gated', async () => {
    const poster = nonMember({ flags: { canPostToFeed: true, canPost: true, canPostMedia: true, canUploadVideo: true } });
    const { fetchImpl } = appFetch(poster, (call) => {
      if (call.path === '/api/v1/feed') return json(enumPage([]));
      return undefined;
    });
    renderSite('/app/feed', { site: siteFor('PartiallyGated', { access: { ...MODELS.PartiallyGated, gatedFeatures: ['feed'] } }), fetchImpl });
    expect(await screen.findByRole('link', { name: a.upgradeCta })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: strings.feed.write })).toBeNull();
  });

  it('lets signed-out visitors browse the feed with a sign-in prompt', async () => {
    const { fetchImpl, calls } = appFetch(null, (call) => {
      if (call.path === '/api/v1/feed') return json(enumPage([makePost('fpo_pub', { audience: 'Public', viewer: { canComment: false, canReact: false, canReport: false } })]));
      return undefined;
    });
    renderSite('/app/feed', { site: siteFor('PartiallyGated'), fetchImpl });
    expect(await screen.findByText('Post fpo_pub')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: a.joinIn })).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: a.signInCta })[0].getAttribute('href')).toContain('/sign-in?next=');
    const nav = screen.getByRole('navigation', { name: strings.member.navLabel });
    expect(within(nav).getByRole('link', { name: strings.member.nav.feed })).toBeInTheDocument();
    // Forums are gated in this fixture, so they are not offered signed out.
    expect(within(nav).queryByRole('link', { name: strings.member.nav.forums })).toBeNull();
    const feedCall = calls.find((c) => c.path === '/api/v1/feed');
    expect(feedCall.headers.Authorization || feedCall.headers.authorization).toBeUndefined();
  });
});

describe('FullyGated access model', () => {
  it('sends a non-member to the membership step', async () => {
    const { fetchImpl } = appFetch(nonMember({ membershipRequired: true }), (call) => {
      if (call.path === '/api/v1/membership') return json({ membership: { isActive: false }, stripeEnabled: false, storeProductId: null });
      return undefined;
    });
    renderSite('/app/feed', { site: siteFor('FullyGated'), fetchImpl });
    expect(await screen.findByRole('heading', { name: strings.membershipApp.title, level: 1 })).toBeInTheDocument();
  });

  it('never offers signed-out browsing, even when the stored flag is on', async () => {
    const { fetchImpl, calls } = appFetch(null);
    renderSite('/app/feed', { site: siteFor('FullyGated', { access: { ...MODELS.FullyGated, anonymousBrowsing: true } }), fetchImpl });
    expect(await screen.findByRole('heading', { name: strings.auth.signInTitle })).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/v1/feed')).toBe(false);
  });
});

describe('admin Access model page', () => {
  const stored = { model: 'FullyGated', anonymousBrowsing: false, gatedFeatures: ['library'], defaultAudience: 'Members', configuredDefaultAudience: null, purchasesEnabled: true, signupCodesEnabled: true, revision: 4, updatedByUserId: null, updatedUtc: null };

  it('explains the models, warns about billing before switching to Open, and saves the whole policy', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/property/access' && call.method === 'GET') return json(stored);
      if (call.path === '/api/v1/admin/property/access' && call.method === 'PUT') return json({ ...stored, ...call.body, purchasesEnabled: call.body.model !== 'Open', revision: 5, configuredDefaultAudience: call.body.defaultAudience });
      return undefined;
    });
    renderAdmin(<AccessModelView />, { fetchImpl, path: '/admin/access', routePath: '/admin/access' });
    const m = strings.admin.accessModel;
    expect(await screen.findByText(m.models.Open.summary)).toBeInTheDocument();
    expect(screen.getByText(m.models.PartiallyGated.summary)).toBeInTheDocument();
    expect(screen.getAllByRole('checkbox', { name: /./ }).length).toBeGreaterThanOrEqual(9);
    // Search is the ninth gated feature.
    await userEvent.click(screen.getByRole('checkbox', { name: new RegExp('^' + a.features.search) }));

    await userEvent.click(screen.getByRole('radio', { name: new RegExp(m.models.Open.label) }));
    expect(screen.getAllByText(m.consequences.purchasesOff).length).toBeGreaterThan(0);
    await userEvent.click(screen.getByLabelText(m.anonymousLabel));
    await userEvent.click(screen.getByRole('button', { name: m.save }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(m.consequences.purchasesOff)).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: m.confirmCta }));
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT')).toBeTruthy());
    expect(calls.find((c) => c.method === 'PUT').body).toEqual({ model: 'Open', anonymousBrowsing: true, gatedFeatures: ['library', 'search'], defaultAudience: null });
  });

  it('shows the effective Members default in FullyGated, not a stale saved value, and never saves it', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/property/access' && call.method === 'GET') return json({ ...stored, configuredDefaultAudience: 'SignedIn' });
      if (call.path === '/api/v1/admin/property/access' && call.method === 'PUT') return json({ ...stored, ...call.body, revision: 5, configuredDefaultAudience: null });
      return undefined;
    });
    renderAdmin(<AccessModelView />, { fetchImpl, path: '/admin/access', routePath: '/admin/access' });
    const m = strings.admin.accessModel;
    const select = await screen.findByLabelText(m.audienceLabel);
    expect(select).toBeDisabled();
    expect(select.value).toBe('Members');
    await userEvent.click(screen.getByLabelText(m.anonymousLabel));
    await userEvent.click(screen.getByRole('button', { name: m.save }));
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT')).toBeTruthy());
    expect(calls.find((c) => c.method === 'PUT').body.defaultAudience).toBe(null);
  });

  it('rejects Members as the default audience in Open before saving', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/property/access') return json({ ...stored, model: 'Open', purchasesEnabled: false });
      return undefined;
    });
    renderAdmin(<AccessModelView />, { fetchImpl, path: '/admin/access', routePath: '/admin/access' });
    const select = await screen.findByLabelText(strings.admin.accessModel.audienceLabel);
    expect(within(select).getAllByRole('option').map((o) => o.value)).toEqual(['', 'Public', 'SignedIn']);
    expect(calls.some((c) => c.method === 'PUT')).toBe(false);
  });
});

describe('guest home at /app', () => {
  it('shows signed-out visitors a feed preview with sign-in prompts when anonymous browsing is on', async () => {
    const { fetchImpl, calls } = appFetch(null, (call) => {
      if (call.path === '/api/v1/feed') return json(enumPage([makePost('fpo_pub', { audience: 'Public', viewer: { canComment: false, canReact: false, canReport: false } })]));
      return undefined;
    });
    renderSite('/app', { site: siteFor('PartiallyGated'), fetchImpl });
    expect(await screen.findByText('Post fpo_pub')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: strings.member.guestFeedTitle })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: a.joinIn })).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: a.signInCta })[0].getAttribute('href')).toContain('/sign-in?next=');
    expect(screen.getByRole('link', { name: strings.member.guestSeeFeed })).toHaveAttribute('href', '/app/feed');
    expect(screen.queryByRole('heading', { name: strings.auth.signInTitle })).toBeNull();
    const feedCall = calls.find((c) => c.path === '/api/v1/feed');
    expect(feedCall.query.maxResults).toBe('5');
    expect(feedCall.headers.Authorization || feedCall.headers.authorization).toBeUndefined();
  });

  it('offers guests sign-in instead of "no forums yet" when every forum needs an account', async () => {
    const { fetchImpl } = appFetch(null, (call) => {
      if (call.path === '/api/v1/forums' || call.path === '/api/v1/forums/enumerate') return json(enumPage([]));
      return undefined;
    });
    renderSite('/app/forums', { site: siteFor('Open'), fetchImpl });
    expect(await screen.findByRole('heading', { name: strings.forums.guestEmptyTitle })).toBeInTheDocument();
    expect(screen.queryByText(strings.forums.empty)).toBeNull();
    expect(screen.getAllByRole('link', { name: a.signInCta }).some((l) => l.getAttribute('href').startsWith('/sign-in?next='))).toBe(true);
  });

  it('sends signed-out visitors to sign in when anonymous browsing is off', async () => {
    const { fetchImpl, calls } = appFetch(null);
    renderSite('/app', { site: siteFor('FullyGated'), fetchImpl });
    expect(await screen.findByRole('heading', { name: strings.auth.signInTitle })).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/v1/feed')).toBe(false);
  });

  it('sends a member with an active membership from /app to the feed', async () => {
    const member = makeMe({ membership: { isActive: true, source: 'AdminComp', store: null, expiresUtc: null }, accessTier: 'Member', membershipRequired: false });
    const { fetchImpl } = appFetch(member, (call) => (call.path === '/api/v1/feed' ? json(enumPage([])) : undefined));
    renderSite('/app', { site: siteFor('PartiallyGated'), fetchImpl });
    expect(await screen.findByRole('heading', { name: strings.feed.title })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: /Welcome, / })).toBeNull();
  });

  it.each(['Open', 'PartiallyGated'])('opens the feed for an unpaid account with read access in %s', async (model) => {
    const { fetchImpl } = appFetch(nonMember(), (call) => (call.path === '/api/v1/feed' ? json(enumPage([])) : undefined));
    renderSite('/app', { site: siteFor(model), fetchImpl });
    expect(await screen.findByRole('heading', { name: strings.feed.title })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: /Welcome, / })).toBeNull();
  });

  it('still shows the member home when the feed requires membership', async () => {
    const { fetchImpl } = appFetch(nonMember());
    renderSite('/app', { site: siteFor('FullyGated'), fetchImpl });
    expect(await screen.findByRole('heading', { name: /Welcome, / })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: strings.member.guestFeedTitle })).toBeNull();
  });
});

describe('search gating', () => {
  const searchFetch = () => appFetch(nonMember(), (call) => {
    if (call.path === '/api/v1/feed') return json(enumPage([]));
    return undefined;
  });

  it('gates search on its own key when the server lists it', async () => {
    const { fetchImpl } = searchFetch();
    renderSite('/app/search', { site: siteFor('PartiallyGated', { access: { ...MODELS.PartiallyGated, gatedFeatures: ['search'] } }), fetchImpl });
    expect(await screen.findByRole('link', { name: a.upgradeCta })).toHaveAttribute('href', '/app/membership?feature=search');
    const nav = screen.getByRole('navigation', { name: strings.member.navLabel });
    expect(within(nav).getAllByLabelText(a.lockBadgeLabel(a.features.search)).length).toBeGreaterThan(0);
    expect(within(nav).queryByLabelText(a.lockBadgeLabel(a.features.forums))).toBeNull();
  });

  it('opens search when only the forums are gated', async () => {
    const { fetchImpl } = searchFetch();
    renderSite('/app/search', { site: siteFor('PartiallyGated', { access: { ...MODELS.PartiallyGated, gatedFeatures: ['forums'] } }), fetchImpl });
    expect(await screen.findByRole('heading', { name: strings.search.title, level: 1 })).toBeInTheDocument();
    const nav = screen.getByRole('navigation', { name: strings.member.navLabel });
    expect(within(nav).queryByLabelText(a.lockBadgeLabel(a.features.search))).toBeNull();
  });

  it('hides search from the navigation when features.search is false', async () => {
    const { fetchImpl } = searchFetch();
    renderSite('/app/feed', { site: siteFor('PartiallyGated', { features: { ...(BUILD_SITE.property.features || {}), forums: true, search: false } }), fetchImpl });
    const nav = await screen.findByRole('navigation', { name: strings.member.navLabel });
    expect(within(nav).getAllByRole('link', { name: /^Forums/ }).length).toBeGreaterThan(0);
    expect(within(nav).queryByRole('link', { name: /^Search/ })).toBeNull();
  });

  it('opens search for non-members when neither search nor forums is gated', async () => {
    const { fetchImpl } = searchFetch();
    renderSite('/app/search', { site: siteFor('PartiallyGated', { access: { ...MODELS.PartiallyGated, gatedFeatures: ['library'] } }), fetchImpl });
    expect(await screen.findByRole('heading', { name: strings.search.title, level: 1 })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: a.upgradeCta })).toBeNull();
  });
});

describe('sign-up codes wait for the live access settings', () => {
  it('renders neither the create buttons nor the read-only notice until GET /property settles', async () => {
    let release;
    const live = new Promise((resolve) => { release = resolve; });
    const { fetchImpl } = createFetch(async (call) => {
      if (call.path === '/api/v1/property') {
        await live;
        return json({ ...siteFor('Open').property });
      }
      if (call.path === '/api/v1/admin/signup-codes') return json(enumPage([]));
      return undefined;
    });
    // The build-time copy still says purchases are on (PartiallyGated); the live settings say Open.
    const Harness = () => {
      useRuntimeProperty(useAdminAuth().client);
      return <SignupCodesView />;
    };
    renderAdmin(<Harness />, { fetchImpl, path: '/admin/signup-codes', routePath: '/admin/signup-codes', site: siteFor('PartiallyGated') });
    expect(await screen.findByRole('heading', { name: strings.admin.routes['signup-codes'].label })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: strings.admin.codes.create })).toBeNull();
    expect(screen.queryByText(strings.admin.accessModel.readOnlyOpen)).toBeNull();
    release();
    expect(await screen.findByText(strings.admin.accessModel.readOnlyOpen)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: strings.admin.codes.create })).toBeNull();
    expect(screen.queryByRole('button', { name: strings.admin.codes.createBatch })).toBeNull();
  });
});
