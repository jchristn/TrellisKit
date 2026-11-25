import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createDirectoryFake, createEventsFake, makeEvent, makeVenue } from '@trelliskit/shared/testing';
import AccountView from '../src/views/member/AccountView.jsx';
import DirectoryAdminView from '../src/views/admin/directory/DirectoryAdminView.jsx';
import EventEditorView from '../src/views/admin/events/EventEditorView.jsx';
import MemberDetailDrawer from '../src/views/admin/MemberDetailDrawer.jsx';
import { VenueView } from '../src/views/directory/VenueViews.jsx';
import { slugServerError } from '../src/components/PublicPageFields.jsx';
import { createFetch, json, makeMe, renderAdmin } from './helpers.jsx';
import { memberFetch, renderMember } from './engagementHelpers.jsx';
import { fakeResponse, phase4Fetch, renderP4, siteWith } from './phase4Helpers.jsx';

const TAKEN = new Set(['taken-name', 'other-club']);

/** `POST /public-pages/slugs/check`: slugs in TAKEN are in use; everything else is available. */
function slugCheck(call) {
  if (call.method !== 'POST' || call.path !== '/api/v1/public-pages/slugs/check') return undefined;
  const taken = TAKEN.has(call.body.slug);
  return json({ kind: call.body.kind, slug: call.body.slug, valid: true, available: !taken, problem: taken ? 'taken' : null, path: null, url: null });
}

/** A member session serving `/me/public-page` from an in-memory record. */
function publicPageFetch(initial = { enabled: false, slug: null, suggestedSlug: 'sam-rivera', path: null, url: null }) {
  const state = { page: { ...initial } };
  const result = memberFetch((call) => {
    const check = slugCheck(call);
    if (check) return check;
    if (call.path === '/api/v1/me/public-page' && call.method === 'GET') return json(state.page);
    if (call.path === '/api/v1/me/public-page' && call.method === 'PUT') {
      if (call.body.slug === 'other-club') return json({ error: 'SlugInUse', message: 'In use.', fieldErrors: { slug: 'That slug is in use.' } }, 409);
      const slug = call.body.slug || state.page.slug || state.page.suggestedSlug;
      state.page = { ...state.page, enabled: Boolean(call.body.enabled), slug, path: '/profiles/' + slug, url: 'http://localhost/profiles/' + slug };
      return json(state.page);
    }
    return undefined;
  }, makeMe());
  return { ...result, state };
}

describe('member public profile page settings', () => {
  it('starts off with the suggested slug, validates live, and saves', async () => {
    const { fetchImpl, calls, state } = publicPageFetch();
    await renderMember(<AccountView />, { fetchImpl, path: '/app/account', routePath: '/app/account' });
    const section = await screen.findByTestId('account-public-page');
    const toggle = await within(section).findByRole('switch', { name: /Show my public profile page/ });
    expect(toggle).not.toBeChecked();
    expect(within(section).getByText(/It shows your public profile information and uploaded photos and videos/)).toBeInTheDocument();
    const input = within(section).getByLabelText('Page address');
    expect(input).toHaveValue('sam-rivera');
    expect(within(section).getByText(window.location.origin + '/profiles/sam-rivera')).toBeInTheDocument();
    expect(within(section).getByRole('button', { name: 'Copy public page link' })).toBeInTheDocument();
    expect(within(section).queryByRole('link', { name: /Preview/ })).toBeNull();

    await userEvent.clear(input);
    await userEvent.type(input, 'ab');
    expect(within(section).getByText('Use 3 to 80 characters.')).toBeInTheDocument();
    await userEvent.clear(input);
    await userEvent.type(input, 'admin');
    expect(within(section).getByText('That address is reserved. Choose another.')).toBeInTheDocument();
    await userEvent.clear(input);
    await userEvent.type(input, 'Taken Name');
    expect(input).toHaveValue('taken-name');
    expect(await within(section).findByText('That address is already in use. Choose another.')).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/v1/public-pages/slugs/check' && c.body.kind === 'Profile' && c.body.slug === 'taken-name' && c.body.id === 'usr_1')).toBe(true);
    await userEvent.click(within(section).getByRole('button', { name: 'Save public page' }));
    expect(within(section).getByText('Fix the page address first.')).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'PUT' && c.path === '/api/v1/me/public-page')).toBe(false);

    await userEvent.clear(input);
    await userEvent.type(input, 'sam-r');
    expect(await within(section).findByText('Available')).toBeInTheDocument();
    await userEvent.click(toggle);
    await userEvent.click(within(section).getByRole('button', { name: 'Save public page' }));
    await waitFor(() => expect(state.page.enabled).toBe(true));
    expect(calls.find((c) => c.method === 'PUT' && c.path === '/api/v1/me/public-page').body).toEqual({ enabled: true, slug: 'sam-r' });
    const preview = await within(section).findByRole('link', { name: /Preview/ });
    expect(preview).toHaveAttribute('href', 'http://localhost/profiles/sam-r');
    expect(preview).toHaveAttribute('target', '_blank');
  }, 20000);

  it('shows a 409 SlugInUse on the slug field', async () => {
    const { fetchImpl } = publicPageFetch({ enabled: true, slug: 'sam', suggestedSlug: 'sam', path: '/profiles/sam', url: 'http://localhost/profiles/sam' });
    await renderMember(<AccountView />, { fetchImpl, path: '/app/account', routePath: '/app/account' });
    const section = await screen.findByTestId('account-public-page');
    const input = await within(section).findByLabelText('Page address');
    expect(within(section).getByText('This is the current address.')).toBeInTheDocument();
    expect(within(section).getByRole('link', { name: /Preview/ })).toHaveAttribute('href', 'http://localhost/profiles/sam');
    await userEvent.clear(input);
    await userEvent.type(input, 'other-club');
    await userEvent.click(within(section).getByRole('button', { name: 'Save public page' }));
    expect(await within(section).findByRole('alert')).toHaveTextContent('That address is already in use. Choose another.');
  }, 20000);

  it('hides the section when the property turns public pages off', async () => {
    const { fetchImpl, calls } = publicPageFetch();
    await renderP4(<AccountView />, { fetchImpl, path: '/app/account', routePath: '/app/account', site: siteWith({ publicPages: { enabled: false } }) });
    expect(await screen.findByRole('heading', { name: 'Account settings' })).toBeInTheDocument();
    expect(screen.queryByTestId('account-public-page')).toBeNull();
    expect(calls.some((c) => c.path === '/api/v1/me/public-page')).toBe(false);
  });

  it('maps server slug errors to field text', () => {
    expect(slugServerError({ code: 'SlugInUse' })).toBe('That address is already in use. Choose another.');
    expect(slugServerError({ code: 'ValidationFailed', fieldErrors: { slug: 'reserved' } })).toBe('That address is reserved. Choose another.');
    expect(slugServerError({ code: 'ValidationFailed', fieldErrors: {} })).toBe('');
  });
});

/** A token-mode admin fetch: the slug check, then the shared fakes. */
function adminFetch(fakes, extra = null) {
  return createFetch((call) => {
    if (call.path === '/api/v1/settings/enumerate') return json({});
    if (call.path === '/api/v1/health') return json({ status: 'Healthy', version: '1.0.0' });
    const check = slugCheck(call);
    if (check) return check;
    if (extra) {
      const answer = extra(call);
      if (answer) return answer;
    }
    for (const fake of fakes) {
      const result = fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body });
      if (result) return fakeResponse(result);
    }
    return undefined;
  });
}

describe('venue slug field', () => {
  it('previews the derived slug on create and sends a typed one', async () => {
    const fake = createDirectoryFake({ venues: [] });
    const { fetchImpl } = adminFetch([fake]);
    renderAdmin(<DirectoryAdminView />, { fetchImpl, path: '/admin/directory', routePath: '/admin/directory' });
    await userEvent.click(await screen.findByRole('button', { name: /^New / }));
    const drawer = await screen.findByRole('dialog');
    await userEvent.type(within(drawer).getByLabelText('Name'), 'Example Club');
    await userEvent.type(within(drawer).getByLabelText('City or town'), 'Exampleville');
    await userEvent.type(within(drawer).getByLabelText('Region'), 'EX');
    const slug = within(drawer).getByLabelText('Page address');
    expect(slug).toHaveAttribute('placeholder', 'example-club-exampleville-ex');
    expect(within(drawer).getByText('Leave empty to use example-club-exampleville-ex.')).toBeInTheDocument();
    expect(within(drawer).getByText(window.location.origin + '/venues/example-club-exampleville-ex')).toBeInTheDocument();
    await userEvent.type(slug, 'other-club');
    expect(await within(drawer).findByText('That address is already in use. Choose another.')).toBeInTheDocument();
    await userEvent.clear(slug);
    await userEvent.type(slug, 'example-club');
    expect(await within(drawer).findByText('Available')).toBeInTheDocument();
    expect(within(drawer).getByText(window.location.origin + '/venues/example-club')).toBeInTheDocument();
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(fake.calls.some((c) => c.method === 'POST' && c.path === '/admin/venues')).toBe(true));
    expect(fake.calls.find((c) => c.method === 'POST' && c.path === '/admin/venues').body).toMatchObject({ name: 'Example Club', slug: 'example-club' });
  }, 20000);

  it('checks the edited slug with the venue id and links the live page', async () => {
    const fake = createDirectoryFake({ venues: [makeVenue('ven_1', { name: 'Example Club', slug: 'example-club', publicUrl: 'http://localhost/venues/example-club' })] });
    const { fetchImpl, calls } = adminFetch([fake]);
    renderAdmin(<DirectoryAdminView />, { fetchImpl, path: '/admin/directory', routePath: '/admin/directory' });
    await userEvent.click(await screen.findByText('Example Club'));
    const drawer = await screen.findByRole('dialog');
    const slug = within(drawer).getByLabelText('Page address');
    expect(slug).toHaveValue('example-club');
    expect(within(drawer).getByText('This is the current address.')).toBeInTheDocument();
    expect(within(drawer).getByRole('link', { name: /Preview/ })).toHaveAttribute('href', 'http://localhost/venues/example-club');
    await userEvent.clear(slug);
    expect(within(drawer).getByText('Enter a page address.')).toBeInTheDocument();
    await userEvent.type(slug, 'example-club-two');
    expect(await within(drawer).findByText('Available')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/public-pages/slugs/check').body).toEqual({ kind: 'Venue', slug: 'example-club-two', id: 'ven_1' });
  }, 20000);
});

describe('event slug field', () => {
  it('edits the slug and notes the Public audience rule', async () => {
    const fake = createEventsFake({ events: [makeEvent('evt_1', { title: 'Open session', slug: 'open-session', status: 'Published', publicUrl: 'http://localhost/events/open-session', startUtc: '2026-12-01T18:00:00Z', endUtc: '2026-12-01T19:00:00Z', timeZone: 'UTC' })] });
    const { fetchImpl, calls } = adminFetch([fake]);
    renderAdmin(<EventEditorView />, { fetchImpl, path: '/admin/events/evt_1', routePath: '/admin/events/:id' });
    const slug = await screen.findByLabelText('Page address');
    expect(slug).toHaveValue('open-session');
    expect(screen.getByText(/The public page exists only while the event is published with a Public audience/)).toBeInTheDocument();
    expect(screen.getByTestId('public-page-link')).toBeInTheDocument();
    await userEvent.clear(slug);
    await userEvent.type(slug, 'open-session-december');
    expect(await screen.findByText('Available')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/public-pages/slugs/check').body).toMatchObject({ kind: 'Event', id: 'evt_1' });
    expect(screen.getByText(window.location.origin + '/events/open-session-december')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(fake.calls.some((c) => c.method === 'PUT' && c.path === '/admin/events/evt_1')).toBe(true));
    expect(fake.calls.find((c) => c.method === 'PUT' && c.path === '/admin/events/evt_1').body).toMatchObject({ slug: 'open-session-december' });
  }, 20000);

  it('leaves the slug out of a new event when it is empty', async () => {
    const fake = createEventsFake();
    const { fetchImpl } = adminFetch([fake]);
    renderAdmin(<EventEditorView />, { fetchImpl, path: '/admin/events/new', routePath: '/admin/events/new' });
    expect(screen.getByText('Leave empty to generate one automatically.')).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Title'), 'Weekly session');
    await userEvent.type(screen.getByLabelText('Starts'), '2026-12-01T18:00');
    await userEvent.type(screen.getByLabelText('Ends'), '2026-12-01T19:00');
    await userEvent.type(screen.getByLabelText('Place name'), 'Example Hall');
    await userEvent.click(screen.getByRole('button', { name: 'Create draft' }));
    await waitFor(() => expect(fake.calls.some((c) => c.method === 'POST' && c.path === '/admin/events')).toBe(true));
    expect(fake.calls.find((c) => c.method === 'POST' && c.path === '/admin/events').body.slug).toBeUndefined();
  }, 20000);
});

describe('admin member public page', () => {
  it('shows and saves the member page through the admin routes', async () => {
    const member = { ...makeMe({ id: 'usr_2', displayName: 'Alex Chen' }), sessions: [] };
    let page = { enabled: false, slug: null, suggestedSlug: 'alex-chen', path: null, url: null };
    const { fetchImpl, calls } = adminFetch([], (call) => {
      if (call.path === '/api/v1/admin/members/usr_2' && call.method === 'GET') return json(member);
      if (call.path === '/api/v1/admin/members/usr_2/public-page' && call.method === 'GET') return json(page);
      if (call.path === '/api/v1/admin/members/usr_2/public-page' && call.method === 'PUT') {
        page = { ...page, ...call.body, url: 'http://localhost/profiles/' + call.body.slug };
        return json(page);
      }
      return undefined;
    });
    renderAdmin(<MemberDetailDrawer id="usr_2" onClose={() => {}} onChanged={() => {}} />, { fetchImpl });
    const section = await screen.findByTestId('member-public-page', {}, { timeout: 5000 });
    const toggle = await within(section).findByRole('switch', { name: /Public page on/ });
    expect(within(section).getByLabelText('Page address')).toHaveValue('alex-chen');
    await userEvent.click(toggle);
    await userEvent.click(within(section).getByRole('button', { name: 'Save public page' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.path === '/api/v1/admin/members/usr_2/public-page')).toBe(true));
    expect(calls.find((c) => c.method === 'PUT').body).toEqual({ enabled: true, slug: 'alex-chen' });
    expect(await within(section).findByRole('link', { name: /Preview/ })).toHaveAttribute('href', 'http://localhost/profiles/alex-chen');
  }, 20000);
});

describe('view public page links', () => {
  it('links a venue to its landing page when the payload has publicUrl', async () => {
    const fake = createDirectoryFake({ venues: [makeVenue('ven_1', { name: 'Example Club', publicUrl: 'http://localhost/venues/example-club' })] });
    const fetchImpl = phase4Fetch([fake]).fetchImpl;
    await renderP4(<VenueView />, { fetchImpl, path: '/app/venues/ven_1', routePath: '/app/venues/:id' });
    const link = await screen.findByRole('link', { name: /View public page/ });
    expect(link).toHaveAttribute('href', 'http://localhost/venues/example-club');
    expect(screen.getByRole('button', { name: 'Copy public page link' })).toBeInTheDocument();
  });
});
