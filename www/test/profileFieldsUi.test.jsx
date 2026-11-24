import { describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { SESSION_HINT_KEY } from '../src/context/AuthContext.jsx';
import { PropertyProvider } from '../src/context/PropertyContext.jsx';
import AuthorLine from '../src/components/feed/AuthorLine.jsx';
import { ProfileFieldsEditor } from '../src/components/profile/ProfileFieldInputs.jsx';
import { profileErrorText } from '../src/utils/profileErrors.js';
import { authResponse, createFetch, json, loadFixtureSite, makeMe, renderSite } from './helpers.jsx';

const dojo = loadFixtureSite('dojo');

const fields = [
  { key: 'displayName', builtIn: true, label: 'Name', type: 'Text', profileOrder: 0, collectAt: 'SignUp' },
  { key: 'handle', builtIn: true, label: 'Handle', type: 'Text', profileOrder: 1, collectAt: 'SignUp' },
  {
    key: 'level', label: 'Level', type: 'Rank', profileOrder: 10, collectAt: 'SignUp', editableBy: 'Member', visibility: 'Public', required: true,
    validation: { required: true }, showOnProfile: true, showOnCard: true, filterable: true,
    options: [{ key: 'Low', label: 'Low', color: '#111111' }, { key: 'High', label: 'High', color: '#EEEEEE' }], rank: { levelLabel: 'Stars', maxLevel: 2 }
  },
  { key: 'club', label: 'Club', type: 'Text', profileOrder: 20, collectAt: 'Profile', editableBy: 'Member', visibility: 'Public', showOnProfile: true, showOnCard: true, filterable: true },
  { key: 'country', label: 'Country', type: 'Country', profileOrder: 30, collectAt: 'Profile', editableBy: 'Member', visibility: 'Public', showOnProfile: true, filterable: true },
  {
    key: 'focus', label: 'Focus', type: 'MultiSelect', profileOrder: 40, collectAt: 'Profile', editableBy: 'Member', visibility: 'Members', showOnProfile: true, filterable: true,
    options: [{ key: 'A', label: 'Alpha' }, { key: 'B', label: 'Beta' }]
  },
  { key: 'site', label: 'Website', type: 'Url', profileOrder: 50, collectAt: 'Profile', editableBy: 'Member', visibility: 'Public', showOnProfile: true, helpText: 'Your own page.' },
  { key: 'future', label: 'Future', type: 'Hologram', profileOrder: 60 }
];
const site = { ...dojo, property: { ...dojo.property, profileFields: fields } };

describe('profile field inputs', () => {
  it('renders a control per type, skips unknown types, and shows help and errors', async () => {
    const changes = [];
    render(
      <MemoryRouter>
        <ProfileFieldsEditor
          fields={fields.filter((f) => !f.builtIn)} values={{ level: { key: 'High', level: 0 }, focus: { keys: [], otherText: '' } }}
          onChange={(key, value) => changes.push([key, value])} errors={{ 'profile.site': 'Use a full web address starting with https://.' }}
        />
      </MemoryRouter>
    );
    expect(screen.getByLabelText(/^Level/)).toHaveValue('High');
    expect(screen.getByLabelText('Stars')).toBeInTheDocument();
    expect(screen.getByLabelText('Club')).toBeInTheDocument();
    expect(within(screen.getByLabelText('Country')).getAllByRole('option').length).toBeGreaterThan(200);
    expect(screen.getByRole('group', { name: 'Focus' })).toBeInTheDocument();
    expect(screen.getByLabelText('Website')).toHaveAttribute('type', 'url');
    expect(screen.getByText('Your own page.')).toBeInTheDocument();
    expect(screen.getByText('Use a full web address starting with https://.')).toBeInTheDocument();
    expect(screen.queryByText('Future')).toBeNull();
    await userEvent.selectOptions(screen.getByLabelText('Stars'), '2');
    expect(changes.at(-1)).toEqual(['level', { key: 'High', level: 2 }]);
    await userEvent.click(screen.getByRole('checkbox', { name: 'Beta' }));
    expect(changes.at(-1)).toEqual(['focus', { keys: ['B'], otherText: '' }]);
  });

  it('maps shared validation codes to member-facing text', () => {
    expect(profileErrorText({ code: 'tooLong', params: { max: 5 } })).toBe('Must be 5 characters or fewer.');
    expect(profileErrorText({ code: 'pattern', params: { message: 'Capitals only.' } })).toBe('Capitals only.');
    expect(profileErrorText({ code: 'nope' })).toBe('Check this value.');
    expect(profileErrorText('Server text.')).toBe('Server text.');
  });
});

describe('member cards', () => {
  it('shows card fields from UserSummary.profile in profile order with the rank color bar', () => {
    const user = { id: 'usr_2', displayName: 'Alex Chen', handle: 'alex', profile: { club: 'North', level: { key: 'High', level: 1 }, focus: { keys: ['A'] } } };
    render(<PropertyProvider site={site}><MemoryRouter><AuthorLine user={user} time="2026-11-11T20:00:00Z" /></MemoryRouter></PropertyProvider>);
    const card = document.querySelector('.card-fields');
    expect(card).not.toBeNull();
    expect(Array.from(card.querySelectorAll('[data-field]')).map((n) => n.getAttribute('data-field'))).toEqual(['level', 'club']);
    expect(within(card).getByText('High, 1 star')).toBeInTheDocument();
    expect(card.querySelector('.rank-bar')).toHaveStyle({ backgroundColor: '#EEEEEE' });
  });
});

describe('member directory', () => {
  it('sends profile, label, and search filters to the directory route and shows removable chips', async () => {
    window.localStorage.setItem(SESSION_HINT_KEY, '1');
    const me = makeMe();
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/auth/refresh') return json(authResponse(me));
      if (call.path === '/api/v1/property') return json({ ...site.property, web: undefined });
      if (call.path === '/api/v1/members/directory/enumerate') {
        return json({ success: true, maxResults: 24, endOfResults: true, totalRecords: 1, recordsRemaining: 0, continuationToken: null, objects: [
          { id: 'usr_2', displayName: 'Alex Chen', handle: 'alex', avatarUrl: null, profile: { level: { key: 'High', level: 0 } }, labels: [], tags: {} }
        ] });
      }
      return undefined;
    });
    renderSite('/app/members?profile.level.minRank=High&profile.focus.any=A&labels=coaches&search=al', { site, fetchImpl });
    expect(await screen.findByRole('heading', { level: 1, name: 'Members' })).toBeInTheDocument();
    expect(await screen.findByText('Alex Chen')).toBeInTheDocument();
    expect(screen.getByText('1 member')).toBeInTheDocument();
    const request = calls.filter((c) => c.path === '/api/v1/members/directory/enumerate').at(-1);
    expect(request.method).toBe('POST');
    expect(request.body).toMatchObject({ search: 'al', labels: ['coaches'], ordering: 'DisplayNameAscending', filters: { profile: { level: { minRank: 'High' }, focus: { any: ['A'] } } } });
    expect(screen.getByRole('link', { name: /Alex Chen/ })).toHaveAttribute('href', '/app/members/usr_2');
    // Filterable fields get controls; the chip removes one filter and the request repeats without it.
    expect(screen.getByRole('group', { name: 'Club' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Remove filter Level' }));
    await waitFor(() => {
      const last = calls.filter((c) => c.path === '/api/v1/members/directory/enumerate').at(-1);
      expect(last.body.filters).toEqual({ profile: { focus: { any: ['A'] } } });
    });
  });

  it('drops a filter the server rejects and says so', async () => {
    window.localStorage.setItem(SESSION_HINT_KEY, '1');
    const me = makeMe();
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/auth/refresh') return json(authResponse(me));
      if (call.path === '/api/v1/members/directory/enumerate') {
        if (call.body.filters && call.body.filters.profile) return json({ error: 'ValidationFailed', message: 'Invalid.', fieldErrors: { 'filters.profile.club': 'Unknown filter.' } }, 400);
        return json({ success: true, maxResults: 24, endOfResults: true, totalRecords: 0, recordsRemaining: 0, continuationToken: null, objects: [] });
      }
      return undefined;
    });
    renderSite('/app/members?profile.club.prefix=no', { site, fetchImpl });
    expect(await screen.findByText('One of the filters is not available. It was removed.')).toBeInTheDocument();
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/v1/members/directory/enumerate').at(-1).body.filters).toBeUndefined());
    expect(await screen.findByText('No members match.')).toBeInTheDocument();
  });
});
