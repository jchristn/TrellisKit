import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { createFetch, json, renderSite } from './helpers.jsx';
import { BUILD_SITE } from '../src/property/buildSite.js';
import { applyContentConditions, contentCondition, stripPurchaseSentences } from '../src/property/content.js';
import { siteMetaFor } from '../src/views/marketing/siteMeta.js';

const base = BUILD_SITE.property;
const openProperty = { ...base, membership: null, access: { model: 'Open', anonymousBrowsing: false, gatedFeatures: [], defaultAudience: 'SignedIn', purchasesEnabled: false, signupCodesEnabled: false, revision: 3 } };
const gatedProperty = { ...base, access: { model: 'FullyGated', purchasesEnabled: true, signupCodesEnabled: true } };

const THREAD = {
  id: 'thr_1',
  forum: { id: 'frm_1', name: 'General', slug: 'general' },
  kind: 'Original',
  type: 'Question',
  author: { id: 'usr_9', displayName: 'Ana Admin', handle: 'ana' },
  title: 'How do you warm up?',
  body: 'Share your **routine** here.',
  excerpt: 'Share your routine here.',
  audience: 'Public',
  state: 'Published',
  isPinned: false,
  isLocked: false,
  hasAcceptedAnswer: false,
  poll: null,
  commentCount: 4,
  reactionTotal: 2,
  attachmentPreview: [],
  attachmentCount: 0,
  labels: [],
  tags: {},
  publishedUtc: '2026-11-10T10:00:00Z',
  createdUtc: '2026-11-10T10:00:00Z'
};

describe('access-dependent marketing copy', () => {
  it('evaluates conditions against the access policy', () => {
    expect(contentCondition('purchases', gatedProperty)).toBe(true);
    expect(contentCondition('purchases', openProperty)).toBe(false);
    expect(contentCondition('!purchases', openProperty)).toBe(true);
    expect(contentCondition('membersOnly', gatedProperty)).toBe(true);
    expect(contentCondition('Open, !anonymousBrowsing', openProperty)).toBe(true);
    expect(contentCondition('unknownThing', gatedProperty)).toBe(false);
  });

  it('drops when= lines and directives and purchase-token lines when nothing is sold', () => {
    const body = [
      'Members only {.eyebrow when=membersOnly}',
      'Free for everyone {when=Open}',
      '- {price} per {interval}',
      '- Members {minimumAge} and older',
      '::: div when=purchases',
      'Buy now',
      ':::: div',
      'nested',
      '::::',
      ':::',
      'After'
    ].join('\n');
    const open = applyContentConditions(body, openProperty);
    expect(open).toBe(['Free for everyone', '- Members {minimumAge} and older', 'After'].join('\n'));
    const gated = applyContentConditions(body, gatedProperty);
    expect(gated).toContain('Members only {.eyebrow}');
    expect(gated).not.toContain('Free for everyone');
    expect(gated).toContain('- {price} per {interval}');
    expect(gated).toContain('Buy now');
    expect(gated).toContain('nested');
  });

  it('strips price sentences from meta descriptions only when nothing is sold', () => {
    expect(stripPurchaseSentences('A great place. Join for {price} per {interval}.', openProperty)).toBe('A great place.');
    expect(stripPurchaseSentences('A great place. Join for {price} per {interval}.', gatedProperty)).toBe('A great place. Join for {price} per {interval}.');
    const meta = siteMetaFor({ ...BUILD_SITE, property: openProperty });
    expect(meta.contentRoutes.find((r) => r.path === '/').description).not.toMatch(/\$|per month/);
  });

  it('replaces the prerendered price with the live access policy after load', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/property') return json(openProperty);
      return undefined;
    });
    renderSite('/membership', { fetchImpl });
    // First render is the build-time copy (what the prerendered HTML contains).
    expect(screen.getByText('$45.00')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText('$45.00')).not.toBeInTheDocument());
    expect(screen.queryByText('per month')).not.toBeInTheDocument();
    expect(screen.queryByText(/Membership is/)).not.toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/v1/property')).toBe(true);
  });

  it('keeps the price when the live policy still sells memberships', async () => {
    const { fetchImpl, calls } = createFetch((call) => (call.path === '/api/v1/property' ? json(base) : undefined));
    renderSite('/membership', { fetchImpl });
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/property')).toBe(true));
    expect(screen.getByText('$45.00')).toBeInTheDocument();
  });
});

describe('public sample threads', () => {
  const withForums = { ...base, features: { ...(base.features || {}), forums: true, publicSamples: true } };

  it('lists public threads on the home page with links to the public thread page', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/property') return json(withForums);
      if (call.path === '/api/v1/public/threads/enumerate') return json({ objects: [THREAD], totalRecords: 1, endOfResults: true });
      if (call.path === '/api/v1/public/feed') return json({ objects: [], totalRecords: 0, endOfResults: true });
      return undefined;
    });
    renderSite('/', { fetchImpl });
    const section = await screen.findByRole('region', { name: 'From the forums' });
    expect(within(section).getByRole('link', { name: 'How do you warm up?' })).toHaveAttribute('href', '/threads/thr_1');
    expect(within(section).getByRole('link', { name: 'Read the thread: How do you warm up?' })).toHaveAttribute('href', '/threads/thr_1');
    expect(within(section).getByText('4 replies', { exact: false })).toBeInTheDocument();
    const call = calls.find((c) => c.path === '/api/v1/public/threads/enumerate');
    expect(call.body.maxResults).toBeLessThanOrEqual(20);
  });

  it('shows nothing when the server has no forums', async () => {
    const { fetchImpl, calls } = createFetch((call) => (call.path === '/api/v1/property' ? json({ ...base, features: { ...(base.features || {}), forums: false } }) : undefined));
    renderSite('/', { fetchImpl });
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/property')).toBe(true));
    expect(screen.queryByRole('region', { name: 'From the forums' })).not.toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/v1/public/threads/enumerate')).toBe(false);
  });

  it('renders the public thread page with a join prompt', async () => {
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/public/threads/thr_1') return json(THREAD);
      if (call.path === '/api/v1/property') return json(withForums);
      return undefined;
    });
    renderSite('/threads/thr_1', { fetchImpl });
    expect(await screen.findByRole('heading', { level: 1, name: 'How do you warm up?' })).toBeInTheDocument();
    expect(screen.getByText('In General')).toBeInTheDocument();
    expect(screen.getByText('routine')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Join to read the replies' })).toHaveAttribute('href', '/sign-up');
    expect(within(screen.getByRole('main')).getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/sign-in?next=' + encodeURIComponent('/app/threads/thr_1'));
    await waitFor(() => expect(document.title).toContain('How do you warm up?'));
  });

  it('shows not found for a thread that is not public', async () => {
    const { fetchImpl } = createFetch((call) => (call.path === '/api/v1/public/threads/thr_x' ? json({ error: 'NotFound' }, 404) : undefined));
    renderSite('/threads/thr_x', { fetchImpl });
    expect(await screen.findByText('Thread not found')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to home' })).toHaveAttribute('href', '/');
  });
});
