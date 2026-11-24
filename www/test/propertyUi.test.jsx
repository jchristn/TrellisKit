import { describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { AuthProvider, SESSION_HINT_KEY, useAuth } from '../src/context/AuthContext.jsx';
import { PropertyProvider, useProperty } from '../src/context/PropertyContext.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import ProfileForm, { buildProfileBody, profileFormLayout, validateProfileForm } from '../src/components/ProfileForm.jsx';
import { profileFieldsOf, toFormValues } from '@trelliskit/shared';
import MemberProfileView from '../src/views/member/MemberProfileView.jsx';
import strings from '../src/strings.js';
import { fill } from '../src/property/current.js';
import { authResponse, createFetch, json, loadFixtureSite, makeMe, renderSite } from './helpers.jsx';

const dojo = loadFixtureSite('dojo');
const bookclub = loadFixtureSite('bookclub');

/** Render children once the restored session has a member. */
function WhenSignedIn({ children }) {
  const { me } = useAuth();
  return me ? children : null;
}

function renderProfile(ui, { site, fetchImpl = createFetch(() => undefined).fetchImpl, me = makeMe() } = {}) {
  if (me) window.localStorage.setItem(SESSION_HINT_KEY, '1');
  const { fetchImpl: withSession, calls } = createFetch(async (call) => {
    if (call.path === '/api/v1/auth/refresh') return json(authResponse(me));
    return fetchImpl(call.url, { method: call.method, body: call.body === undefined ? undefined : JSON.stringify(call.body) });
  });
  render(
    <PropertyProvider site={site}>
      <ThemeProvider>
        <MemoryRouter>
          <AuthProvider fetchImpl={withSession}>{ui}</AuthProvider>
        </MemoryRouter>
      </ThemeProvider>
    </PropertyProvider>
  );
  return calls;
}

describe('engine strings and {name} substitution', () => {
  it('keeps property names out of engine strings and fills tokens from the active property', () => {
    const all = JSON.stringify(strings, (key, value) => (typeof value === 'function' ? value('X', 'Y') : value));
    expect(all).toContain('{name}');
    expect(fill(strings.auth.signInMeta, {}, dojo.property)).toBe('Sign in | Fixture Dojo');
    expect(fill(strings.auth.signUpSubtitle, {}, bookclub.property)).toBe('Members must be 13 or older. Membership is $12.00 per year.');
    expect(fill(strings.auth.errors.underage, {}, dojo.property)).toBe('You must be 21 or older to join Fixture Dojo.');
    expect(fill('{slug}-data.json', {}, bookclub.property)).toBe('bookclub-data.json');
    expect(fill(strings.admin.metaTitle('Members'), {}, bookclub.property)).toBe('Members | Fixture Book Club Admin');
  });

  it('applies the active property to page titles', async () => {
    renderSite('/sign-in', { site: bookclub });
    await waitFor(() => expect(document.title).toBe('Sign in | Fixture Book Club'));
  });
});

describe('profile configuration', () => {
  it('derives field definitions from the build-time profile section until the server sends profileFields', () => {
    const fields = profileFieldsOf(dojo.property);
    const editor = profileFormLayout(fields, 'editor');
    expect(editor.editable.map((f) => f.key)).toEqual(['rank', 'affiliation', 'city', 'region', 'country', 'goals', 'rankHistory']);
    expect(editor.editable[0].label).toBe('Tier');
    expect(profileFormLayout(fields, 'onboarding').editable.map((f) => f.key)).toEqual(['rank']);
    expect(profileFormLayout(profileFieldsOf(bookclub.property), 'editor').editable.map((f) => f.key)).toEqual(['city', 'region', 'country']);
    const server = profileFieldsOf({ ...dojo.property, profileFields: [{ key: 'bio', builtIn: true, type: 'LongText', profileOrder: 5, collectAt: 'SignUp' }, { key: 'site', label: 'Site', type: 'Url', profileOrder: 9, collectAt: 'Profile', editableBy: 'Member' }] });
    expect(profileFormLayout(server, 'onboarding').bio).not.toBeNull();
    expect(profileFormLayout(server, 'editor').editable.map((f) => f.key)).toEqual(['site']);
  });

  it('requires a required sign-up field and sends only the profile map of changed fields', () => {
    const fields = profileFieldsOf(dojo.property);
    const layout = profileFormLayout(fields, 'editor');
    const initial = toFormValues(fields, { rank: null, goals: null });
    const base = { displayName: 'Sam', handle: 'sam_r', bio: '' };
    expect(validateProfileForm({ base, values: initial, layout, fields, handleStatus: 'available' })['profile.rank']).toBe('This field is required.');
    const values = { ...initial, rank: { key: 'Gold', level: 3 }, goals: { keys: ['Else'], otherText: 'Kata' } };
    expect(validateProfileForm({ base, values, layout, fields, handleStatus: 'available' })).toEqual({});
    const body = buildProfileBody({ base, values, initial, layout, fields });
    expect(body).toEqual({ displayName: 'Sam', handle: 'sam_r', bio: null, profile: { rank: { key: 'Gold', level: 3 }, goals: { keys: ['Else'], otherText: 'Kata' } } });
    for (const key of ['rank', 'rankLevel', 'affiliation', 'goals', 'goalsOther', 'rankHistory']) expect(body).not.toHaveProperty(key);
  });

  it('renders rank, level, affiliation, goals, and history inputs from the definitions', async () => {
    renderProfile(<ProfileForm me={makeMe({ profile: { rank: { key: 'Gold', level: 1 } } })} onSaved={() => {}} />, { site: dojo });
    const rank = screen.getByLabelText(/^Tier/);
    expect(within(rank).getAllByRole('option').map((o) => o.textContent)).toEqual(['Choose tier', 'Bronze', 'Silver', 'Gold']);
    expect(rank).toHaveValue('Gold');
    expect(within(screen.getByLabelText('Stars')).getAllByRole('option').map((o) => o.textContent)).toEqual(['0', '1', '2', '3']);
    expect(screen.getByLabelText('Stars')).toHaveValue('1');
    expect(screen.getByLabelText('School')).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Focus' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Something else (describe)')).toBeNull();
    await userEvent.click(screen.getByRole('checkbox', { name: 'Something else' }));
    expect(screen.getByLabelText('Something else (describe)')).toBeInTheDocument();
    const history = screen.getByRole('group', { name: 'Tier history' });
    await userEvent.click(within(history).getByRole('button', { name: 'Add entry' }));
    expect(within(history).getByLabelText('Awarded by')).toBeInTheDocument();
    expect(within(history).getByLabelText('School')).toBeInTheDocument();
    expect(within(history).getByText('In display order. Up to 3 entries.')).toBeInTheDocument();
    expect(within(history).getByRole('button', { name: 'Move entry 1 up' })).toBeDisabled();
  });

  it('maps server field errors, including history entries, onto their inputs', async () => {
    const { fetchImpl } = createFetch((call) => (call.path === '/api/v1/me/profile'
      ? json({ error: 'ValidationFailed', message: 'Invalid.', fieldErrors: { 'profile.affiliation': 'Must be 100 characters or fewer.', 'profile.rankHistory[0].dateAwarded': 'That date is in the future.' } }, 400)
      : undefined));
    const me = makeMe({ profile: { rank: { key: 'Gold', level: 0 }, rankHistory: [{ id: 'phe_1', values: { rank: { key: 'Silver' }, dateAwarded: '2025-03-01' } }] } });
    renderProfile(<ProfileForm me={me} onSaved={() => {}} />, { site: dojo, fetchImpl });
    await userEvent.type(screen.getByLabelText('School', { selector: '#profile-affiliation' }), 'North');
    await userEvent.click(screen.getByRole('button', { name: 'Save profile' }));
    expect(await screen.findByText('Must be 100 characters or fewer.')).toBeInTheDocument();
    expect(screen.getByText('That date is in the future.')).toBeInTheDocument();
  });

  it('hides every configurable section the property does not define', () => {
    renderProfile(<ProfileForm me={makeMe({ profile: {} })} onSaved={() => {}} />, { site: bookclub });
    expect(screen.getByLabelText(/^Display name/)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Handle/)).toBeInTheDocument();
    for (const label of ['Tier', 'Stars', 'School', 'Rank', 'Level']) expect(screen.queryByLabelText(label)).toBeNull();
    expect(screen.queryByRole('button', { name: 'Add entry' })).toBeNull();
  });

  it('shows the member profile from the profile map, with formatted rank and history', async () => {
    const me = makeMe({
      profile: {
        rank: { key: 'Gold', level: 2 }, affiliation: 'North School', country: null, goals: { keys: ['Speed', 'Else'], otherText: 'Kata' },
        rankHistory: [{ id: 'phe_1', values: { rank: { key: 'Silver' }, awardedBy: { name: 'Coach Lee', userId: 'usr_9' }, affiliation: 'North School', dateAwarded: '2025-03-01' } }]
      }
    });
    renderProfile(<WhenSignedIn><MemberProfileView /></WhenSignedIn>, { site: dojo, me });
    expect(await screen.findByText('Gold, 2 stars')).toBeInTheDocument();
    expect(screen.getByText('School')).toBeInTheDocument();
    expect(screen.getByText('Speed, Kata')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Tier history' })).toBeInTheDocument();
    expect(screen.getByText('Silver')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Coach Lee' })).toHaveAttribute('href', '/app/members/usr_9');
    expect(screen.getByText('Mar 1, 2025')).toBeInTheDocument();
  });

  it('ignores deprecated mirrors and shows nothing a property does not define', async () => {
    const me = makeMe({ rank: 'Gold', rankLevel: 2, affiliation: 'Ignored', rankHistory: [{ id: 'rh_1', rank: 'Gold' }], profile: { city: 'Springfield' } });
    renderProfile(<WhenSignedIn><MemberProfileView /></WhenSignedIn>, { site: bookclub, me });
    expect(await screen.findByRole('heading', { level: 1, name: 'Sam Rivera' })).toBeInTheDocument();
    expect(screen.queryByText(/Gold/)).toBeNull();
    expect(screen.queryByText('Ignored')).toBeNull();
    expect(screen.getByText('Springfield')).toBeInTheDocument();
  });
});

function NameProbe() {
  const { property } = useProperty();
  return <p data-testid="property-name">{property.name}</p>;
}

describe('runtime property', () => {
  it('loads GET /api/v1/property in the member app and replaces the build-time copy', async () => {
    window.localStorage.setItem(SESSION_HINT_KEY, '1');
    const runtime = { ...dojo.property, name: 'Dojo Live' };
    delete runtime.web;
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/auth/refresh') return json(authResponse(makeMe()));
      if (call.path === '/api/v1/property') return json(runtime);
      return undefined;
    });
    renderSite('/app/forums', { fetchImpl, extra: <NameProbe /> });
    expect(screen.getByTestId('property-name')).toHaveTextContent('Fixture Dojo');
    await waitFor(() => expect(screen.getByTestId('property-name')).toHaveTextContent('Dojo Live'));
    expect(calls.filter((c) => c.path === '/api/v1/property')).toHaveLength(1);
    await waitFor(() => expect(document.title).toBe('Forums | Dojo Live'));
  });

  it('keeps the build-time copy when the server has no property route or returns an invalid one', async () => {
    const { fetchImpl } = createFetch((call) => (call.path === '/api/v1/property' ? json({ schemaVersion: 99 }) : undefined));
    renderSite('/admin/login', { fetchImpl, extra: <NameProbe /> });
    expect(await screen.findByRole('heading', { name: 'Fixture Dojo Admin' }, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.getByTestId('property-name')).toHaveTextContent('Fixture Dojo');
  });
});
