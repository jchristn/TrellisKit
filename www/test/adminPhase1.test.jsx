import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { AppRoutes } from '../src/App.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import MembersView from '../src/views/admin/MembersView.jsx';
import SignupCodesView, { usesText } from '../src/views/admin/SignupCodesView.jsx';
import SignupCodeDetailView from '../src/views/admin/SignupCodeDetailView.jsx';
import AuditLogView from '../src/views/admin/AuditLogView.jsx';
import { batchCsv, buildBatchBody, buildCodeBody, codeFormValues, validateBatchForm, validateCodeForm } from '../src/views/admin/SignupCodeForms.jsx';
import { rolesBody, validateRoles } from '../src/views/admin/MemberDetailDrawer.jsx';
import { buildMembersQuery } from '../src/utils/adminQuery.js';
import { toEnumerationBody } from '@trelliskit/shared';
import { nextSort } from '../src/components/DataTable.jsx';
import { fillDays } from '../src/components/RedemptionsChart.jsx';
import { browser } from '../src/utils/browser.js';
import { fetchCsvPages } from '../src/utils/csv.js';
import { authResponse, createFetch, json, makeMe, renderAdmin } from './helpers.jsx';

const emptyPage = { pageNumber: 1, pageSize: 50, totalCount: 0, totalPages: 1, items: [] };
const page = (items) => ({ pageNumber: 1, pageSize: 50, totalCount: items.length, totalPages: 1, items });

function adminFetch(handler) {
  return createFetch((call) => {
    if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
    return handler(call);
  });
}

const CODE = {
  id: 'suc_1', code: 'SEMINAR2026', name: 'Spring seminar 2026', description: 'Thanks for coming', notes: 'Paid by sponsor',
  grantType: 'DurationMonths', grantMonths: 12, grantEndUtc: null, grantSummary: 'Free membership for 12 months',
  maxRedemptions: 50, redemptionCount: 37, startsUtc: null, expiresUtc: '2026-12-31T23:59:59Z', isActive: true, status: 'Active',
  batchId: null, tags: ['seminar'], joinUrl: 'https://dojo.test/join?code=SEMINAR2026', lastRedeemedUtc: '2026-11-10T18:00:00Z',
  createdByUserId: 'usr_admin', createdUtc: '2026-10-01T00:00:00Z', updatedUtc: '2026-10-01T00:00:00Z'
};

const RANK_FIELD = {
  id: 'pfd_rank', key: 'rank', builtIn: false, label: 'Level', helpText: '', placeholder: '', type: 'Rank',
  options: [
    { id: 'pfo_1', key: 'Beginner', label: 'Beginner', color: '#9CA3AF', maxLevel: null, freeText: false, retired: false },
    { id: 'pfo_2', key: 'Intermediate', label: 'Intermediate', color: '#1F5FBF', maxLevel: null, freeText: false, retired: false }
  ],
  rank: { levelLabel: 'Stars', maxLevel: 3 }, subFields: [], validation: { required: true },
  collectAt: 'SignUp', editableBy: 'Member', visibility: 'Public', showOnProfile: true, profileOrder: 10, showOnCard: true,
  searchable: false, filterable: true, enabled: true, valueCount: 3, labels: [], tags: {}
};
const STAFF_FIELD = {
  ...RANK_FIELD, id: 'pfd_note', key: 'staffNote', label: 'Staff note', type: 'Text', options: [], rank: null, validation: { required: false },
  collectAt: 'AdminOnly', editableBy: 'Staff', visibility: 'Staff', showOnCard: false, filterable: true, profileOrder: 20
};
const enumeration = (objects) => ({ success: true, maxResults: 50, skip: 0, continuationToken: null, endOfResults: true, totalRecords: objects.length, recordsRemaining: 0, objects });
const MEMBERS = '/api/v1/admin/members/enumerate';

describe('admin members query', () => {
  it('builds the server query from filters, sort, paging, and profile filters', () => {
    const body = (q) => toEnumerationBody(q, { maxLimit: 500 });
    expect(body(buildMembersQuery({ search: ' sam ', membership: 'Paid', role: 'Moderator', banned: true }, { pageNumber: 2, pageSize: 25, sort: 'displayName' })))
      .toEqual({ maxResults: 25, skip: 25, ordering: 'DisplayNameAscending', search: 'sam', filters: { membership: 'Paid', role: 'Moderator', banned: true } });
    expect(body(buildMembersQuery({ search: '', banned: false }, { sort: '-createdUtc' }))).toEqual({ maxResults: 50, ordering: 'CreatedDescending' });
    expect(buildMembersQuery({}, { pageSize: 900 }).maxResults).toBe(500);
    expect(body(buildMembersQuery({}, { meta: { labels: ['vip'], labelMatch: 'All', tags: { 'staff:review': 'pending' } } })))
      .toEqual({ maxResults: 50, labels: ['vip'], labelMatch: 'All', tags: { 'staff:review': 'pending' } });
    expect(body(buildMembersQuery({ rank: 'Gold' }, {}, { rank: { minRank: 'Intermediate', minLevel: 1 } })))
      .toEqual({ maxResults: 50, filters: { profile: { rank: { minRank: 'Intermediate', minLevel: 1 } } } });
  });

  it('toggles sort direction on the active column and uses the default for a new one', () => {
    expect(nextSort('-createdUtc', 'createdUtc')).toBe('createdUtc');
    expect(nextSort('createdUtc', 'createdUtc')).toBe('-createdUtc');
    expect(nextSort('-createdUtc', 'displayName')).toBe('displayName');
    expect(nextSort('displayName', 'lastSeenUtc', 'desc')).toBe('-lastSeenUtc');
  });

  it('applies filters, profile filters, and sorting to the enumerate body and resets to page 1', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/profile-fields/enumerate') return json(enumeration([RANK_FIELD, STAFF_FIELD]));
      if (call.path === MEMBERS) {
        return json(enumeration([{ id: 'usr_9', displayName: 'Ana Silva', handle: 'ana', email: 'ana@example.com', profile: { rank: { key: 'Intermediate', level: 1 } }, membership: { isActive: true, source: 'SignupCode' }, roles: [], isBanned: false, isMuted: false, createdUtc: '2026-11-01T00:00:00Z', lastSeenUtc: null }]));
      }
      return undefined;
    });
    renderAdmin(<MembersView />, { fetchImpl, path: '/admin/members?page=3', routePath: '/admin/members' });
    expect(await screen.findByText('Ana Silva')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Profile' })).toBeInTheDocument();
    expect(await screen.findByText('Intermediate, 1 star')).toBeInTheDocument();
    const first = calls.filter((c) => c.path === MEMBERS)[0];
    expect(first.method).toBe('POST');
    expect(first.body).toEqual({ ordering: 'CreatedDescending', skip: 100, maxResults: 50 });

    await userEvent.type(screen.getByLabelText('Name, handle, or email'), 'ana');
    await userEvent.selectOptions(screen.getByLabelText('Membership'), 'Free');
    await userEvent.selectOptions(screen.getByLabelText('Role'), 'Moderator');
    await userEvent.click(screen.getByLabelText('Banned only'));
    await userEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
    await waitFor(() => {
      const last = calls.filter((c) => c.path === MEMBERS).at(-1);
      expect(last.body).toEqual({ search: 'ana', ordering: 'CreatedDescending', maxResults: 50, filters: { membership: 'Free', role: 'Moderator', banned: true } });
    });

    // Profile filters: every filterable field, staff fields included, sent as filters.profile.
    await userEvent.click(screen.getByRole('button', { name: 'Profile filters' }));
    const rankGroup = screen.getByRole('group', { name: 'Level rank' });
    await userEvent.selectOptions(within(rankGroup).getByLabelText('At least'), 'Intermediate');
    const staffGroup = screen.getByRole('group', { name: 'Staff note staffNote' });
    await userEvent.type(within(staffGroup).getByLabelText('Starts with'), 'vip');
    await userEvent.click(screen.getByRole('button', { name: 'Apply profile filters' }));
    await waitFor(() => {
      const last = calls.filter((c) => c.path === MEMBERS).at(-1);
      expect(last.body.filters).toEqual({ membership: 'Free', role: 'Moderator', banned: true, profile: { rank: { minRank: 'Intermediate' }, staffNote: { prefix: 'vip' } } });
    });
    const chips = screen.getByRole('group', { name: 'Active profile filters' });
    expect(within(chips).getByText('Level at least: Intermediate')).toBeInTheDocument();
    await userEvent.click(within(chips).getByRole('button', { name: 'Remove filter Staff note starts with: vip' }));
    await waitFor(() => expect(calls.filter((c) => c.path === MEMBERS).at(-1).body.filters.profile).toEqual({ rank: { minRank: 'Intermediate' } }));

    await userEvent.click(within(screen.getByRole('columnheader', { name: 'Member' })).getByRole('button'));
    await waitFor(() => expect(calls.filter((c) => c.path === MEMBERS).at(-1).body.ordering).toBe('DisplayNameAscending'));
    const header = screen.getByRole('columnheader', { name: 'Member' });
    expect(header).toHaveAttribute('aria-sort', 'ascending');
  });

  it('opens the member drawer, requires a reason to ban, and posts it', async () => {
    const member = {
      ...makeMe({ id: 'usr_9', displayName: 'Ana Silva', email: 'ana@example.com' }),
      isBanned: false, bannedReason: null, lastSeenUtc: null,
      // Nested lists are bounded previews (REST_API.md "Member detail").
      subscriptions: { objects: [], totalRecords: 0, enumerate: '/api/v1/admin/members/usr_9/subscriptions/enumerate' },
      redemptions: { objects: [], totalRecords: 0, enumerate: '/api/v1/admin/members/usr_9/redemptions/enumerate' },
      comps: { objects: [], totalRecords: 0, enumerate: '/api/v1/admin/members/usr_9/comps/enumerate' },
      sessions: { objects: [{ id: 'ses_1', client: 'web', createdUtc: '2026-11-01T00:00:00Z', lastUsedUtc: '2026-11-02T00:00:00Z', sourceIp: '127.0.0.1' }], totalRecords: 2, enumerate: '/api/v1/admin/members/usr_9/sessions/enumerate' }
    };
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === MEMBERS) return json(enumeration([{ ...member, membership: member.membership }]));
      if (call.path === '/api/v1/admin/members/usr_9' && call.method === 'GET') return json(member);
      if (call.path === '/api/v1/admin/members/usr_9/ban') return json(null, 204);
      if (call.path === '/api/v1/admin/members/usr_9/sessions/enumerate') {
        return json({ success: true, maxResults: 25, skip: 0, continuationToken: null, endOfResults: true, totalRecords: 2, recordsRemaining: 0, objects: [
          { id: 'ses_1', client: 'web', createdUtc: '2026-11-01T00:00:00Z', lastUsedUtc: '2026-11-02T00:00:00Z', sourceIp: '127.0.0.1' },
          { id: 'ses_2', client: 'mobile', createdUtc: '2026-10-01T00:00:00Z', lastUsedUtc: null, sourceIp: '10.0.0.9' }
        ] });
      }
      return undefined;
    });
    renderAdmin(<MembersView />, { fetchImpl, path: '/admin/members', routePath: '/admin/members' });
    await userEvent.click(await screen.findByText('Ana Silva'));
    const drawer = await screen.findByRole('dialog');
    expect(await within(drawer).findByRole('heading', { name: 'Sessions' })).toBeInTheDocument();
    expect(within(drawer).getByText('127.0.0.1')).toBeInTheDocument();
    expect(within(drawer).getByText('Showing 1 of 2.')).toBeInTheDocument();
    await userEvent.click(within(drawer).getByRole('button', { name: 'Load more' }));
    expect(await within(drawer).findByText('10.0.0.9')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/members/usr_9/sessions/enumerate').body).toEqual({ maxResults: 25 });
    expect(within(drawer).queryByRole('button', { name: 'Load more' })).toBeNull();
    await userEvent.click(within(drawer).getByRole('button', { name: 'Ban' }));
    const dialogs = await screen.findAllByRole('dialog');
    const banDialog = dialogs.at(-1);
    await userEvent.click(within(banDialog).getByRole('button', { name: 'Ban' }));
    expect(await within(banDialog).findByText('Enter a reason.')).toBeInTheDocument();
    expect(calls.some((c) => c.path.endsWith('/ban'))).toBe(false);
    await userEvent.type(within(banDialog).getByLabelText('Reason'), 'Harassment');
    await userEvent.click(within(banDialog).getByRole('button', { name: 'Ban' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/members/usr_9/ban').body).toEqual({ reason: 'Harassment' }));
  });

  it('validates scoped roles and builds the roles body', () => {
    const rows = [
      { key: 'a', role: 'SystemAdmin', resourceType: 'Global', resourceId: 'ignored' },
      { key: 'b', role: 'Moderator', resourceType: 'Forum', resourceId: ' frm_1 ' },
      { key: 'c', role: 'Moderator', resourceType: 'Forum', resourceId: '' }
    ];
    expect(validateRoles(rows)).toEqual({ c: 'Enter an id or *.' });
    expect(rolesBody(rows.slice(0, 2))).toEqual([
      { role: 'SystemAdmin', resourceType: 'Global', resourceId: '*' },
      { role: 'Moderator', resourceType: 'Forum', resourceId: 'frm_1' }
    ]);
  });
});

describe('sign-up code forms', () => {
  const today = new Date('2026-11-11T12:00:00Z');

  it('validates a single code: vanity shape, name, grant, max, and date range', () => {
    const values = { ...codeFormValues(null, 'create'), code: 'no', name: '', grantMonths: '0', maxRedemptions: '0', starts: '2027-01-02', expires: '2027-01-01' };
    expect(Object.keys(validateCodeForm(values, { today })).sort()).toEqual(['code', 'expires', 'grantMonths', 'maxRedemptions', 'name']);
    const edit = { ...codeFormValues(CODE, 'edit'), maxRedemptions: '10' };
    expect(validateCodeForm(edit, { mode: 'edit', locked: true, redemptionCount: 37, today }).maxRedemptions).toBe('Cannot be lower than the 37 redemptions so far.');
    expect(validateCodeForm({ ...codeFormValues(null, 'create'), name: 'X', grantType: 'FixedEndDate', grantEnd: '2026-01-01' }, { today }).grantEnd).toBe('Choose a future date.');
  });

  it('omits the code for generated codes and locks code and grant after the first redemption', () => {
    const create = { ...codeFormValues(null, 'create'), name: ' Promo ', grantMonths: '6', maxRedemptions: '', metadata: { labels: ['a', 'b'], tags: {} } };
    expect(buildCodeBody(create)).toEqual({ name: 'Promo', description: null, notes: null, maxRedemptions: null, startsUtc: null, expiresUtc: null, labels: ['a', 'b'], tags: {}, grantType: 'DurationMonths', grantMonths: 6, grantEndUtc: null });
    expect(buildCodeBody({ ...create, code: ' seminar-x ' }).code).toBe('SEMINAR-X');
    const locked = buildCodeBody(codeFormValues(CODE, 'edit'), { mode: 'edit', locked: true });
    expect(locked).not.toHaveProperty('code');
    expect(locked).not.toHaveProperty('grantType');
    expect(locked).toMatchObject({ name: 'Spring seminar 2026', maxRedemptions: 50, isActive: true, labels: ['seminar'] });
  });

  it('validates and builds a batch, and produces CSV of the generated codes', () => {
    expect(Object.keys(validateBatchForm({ name: '', quantity: '1001', grantType: 'DurationMonths', grantMonths: '12' }, today)).sort()).toEqual(['name', 'quantity']);
    expect(buildBatchBody({ name: 'Gym 1', quantity: '3', grantType: 'Lifetime', grantMonths: '12', grantEnd: '', expires: '', tags: 'gym' }))
      .toEqual({ name: 'Gym 1', quantity: 3, grantType: 'Lifetime', grantMonths: null, grantEndUtc: null, expiresUtc: null, labels: ['gym'] });
    const csv = batchCsv([{ code: 'ABCDEFGHJK', joinUrl: 'https://x/join?code=ABCDEFGHJK', grantSummary: 'Free, forever', expiresUtc: null, name: 'Gym 1' }]);
    expect(csv).toBe('Code,Join link,Grant,Expires (UTC),Name\r\nABCDEFGHJK,https://x/join?code=ABCDEFGHJK,"Free, forever",,Gym 1\r\n');
  });

  it('validates and builds discount codes (percent or amount, first year or every renewal)', () => {
    const base = { ...codeFormValues(null, 'create'), name: 'Seminar', grantType: 'Discount' };
    expect(validateCodeForm({ ...base, discountKind: 'percent', discountValue: '101' }, { today }).discountValue).toBe('Enter a whole percent from 1 to 100.');
    expect(validateCodeForm({ ...base, discountKind: 'amount', discountValue: '9.999' }, { today }).discountValue).toBe('Enter a dollar amount such as 10 or 9.99.');
    expect(validateCodeForm({ ...base, discountKind: 'percent', discountValue: '40' }, { today })).toEqual({});
    expect(buildCodeBody({ ...base, discountKind: 'percent', discountValue: '40', discountDuration: 'Once' }))
      .toMatchObject({ grantType: 'Discount', grantMonths: null, grantEndUtc: null, discountPercent: 40, discountAmountCents: null, discountDuration: 'Once' });
    expect(buildCodeBody({ ...base, discountKind: 'amount', discountValue: '9.99', discountDuration: 'Forever' }))
      .toMatchObject({ discountPercent: null, discountAmountCents: 999, discountDuration: 'Forever' });
    const edited = codeFormValues({ ...CODE, grantType: 'Discount', discountAmountCents: 1000, discountDuration: 'Forever' }, 'edit');
    expect(edited).toMatchObject({ grantType: 'Discount', discountKind: 'amount', discountValue: '10.00', discountDuration: 'Forever' });
    expect(buildBatchBody({ name: 'B', quantity: '2', grantType: 'Discount', discountKind: 'percent', discountValue: '25', discountDuration: 'Forever', expires: '', tags: '' }))
      .toMatchObject({ grantType: 'Discount', discountPercent: 25, discountDuration: 'Forever' });
  });

  it('creates a discount code from the list page and shows the discount badge', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/signup-codes' && call.method === 'GET') return json({ ...emptyPage, objects: [{ ...CODE, id: 'suc_d', code: 'SAVE40', grantType: 'Discount', discountPercent: 40, discountDuration: 'Once', grantSummary: '40% off the first year' }], totalRecords: 1 });
      if (call.path === '/api/v1/admin/signup-codes' && call.method === 'POST') return json({ ...CODE, ...call.body, id: 'suc_3', code: 'GENERATED1' }, 201);
      return undefined;
    });
    renderAdmin(<SignupCodesView />, { fetchImpl, path: '/admin/signup-codes', routePath: '/admin/signup-codes' });
    expect(await screen.findByText('40% off the first year')).toBeInTheDocument();
    expect(screen.getAllByText('Discount').length).toBeGreaterThan(0);
    await userEvent.click(screen.getByRole('button', { name: 'Create code' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText('Name'), 'Seminar discount');
    await userEvent.selectOptions(within(dialog).getByLabelText('Grant'), 'Discount');
    await userEvent.selectOptions(within(dialog).getByLabelText('Discount type'), 'amount');
    await userEvent.clear(within(dialog).getByLabelText('Amount off in dollars'));
    await userEvent.type(within(dialog).getByLabelText('Amount off in dollars'), '15');
    await userEvent.selectOptions(within(dialog).getByLabelText('Applies to'), 'Forever');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create code' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/admin/signup-codes')).toBe(true));
    const post = calls.find((c) => c.method === 'POST' && c.path === '/api/v1/admin/signup-codes');
    expect(post.body).toMatchObject({ name: 'Seminar discount', grantType: 'Discount', discountPercent: null, discountAmountCents: 1500, discountDuration: 'Forever' });
  });

  it('shows uses as "37 of 50" or unlimited', () => {
    expect(usesText(CODE)).toBe('37 of 50');
    expect(usesText({ ...CODE, maxRedemptions: null, redemptionCount: 4 })).toBe('4 (unlimited)');
  });

  it('shows the code field on create and generates when it is left blank', () => {
    const values = { ...codeFormValues(null, 'create'), name: 'Promo' };
    expect(validateCodeForm(values).code).toBeUndefined();
    expect(buildCodeBody(values).code).toBeUndefined();
    expect(validateCodeForm({ ...values, code: '' }, { mode: 'edit' }).code).toBeDefined();
  });

  it('deletes an unredeemed code from the list and hides Delete for redeemed codes', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/signup-codes' && call.method === 'GET') return json({ ...emptyPage, objects: [{ ...CODE, id: 'suc_new', code: 'UNUSED01', redemptionCount: 0 }, CODE], totalRecords: 2 });
      if (call.path === '/api/v1/admin/signup-codes/suc_new' && call.method === 'DELETE') return new Response(null, { status: 204 });
      return undefined;
    });
    renderAdmin(<SignupCodesView />, { fetchImpl, path: '/admin/signup-codes', routePath: '/admin/signup-codes' });
    await userEvent.click(await screen.findByRole('button', { name: 'Actions for SEMINAR2026' }));
    expect(screen.queryByRole('menuitem', { name: 'Delete' })).toBeNull();
    await userEvent.keyboard('{Escape}');
    await userEvent.click(screen.getByRole('button', { name: 'Actions for UNUSED01' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Delete' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.path === '/api/v1/admin/signup-codes/suc_new')).toBe(true));
    expect(await screen.findByText('Code deleted.')).toBeInTheDocument();
  });

  it('creates a vanity code from the list page', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/signup-codes' && call.method === 'GET') return json(emptyPage);
      if (call.path === '/api/v1/admin/signup-codes' && call.method === 'POST') return json({ ...CODE, ...call.body, id: 'suc_2' }, 201);
      return undefined;
    });
    renderAdmin(<SignupCodesView />, { fetchImpl, path: '/admin/signup-codes', routePath: '/admin/signup-codes' });
    expect(await screen.findByText(/No sign-up codes yet/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Create code' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText('Code'), 'fall-meetup');
    await userEvent.type(within(dialog).getByLabelText('Name'), 'Fall meetup');
    await userEvent.clear(within(dialog).getByLabelText('Months'));
    await userEvent.type(within(dialog).getByLabelText('Months'), '3');
    await userEvent.type(within(dialog).getByLabelText('Maximum redemptions'), '20');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create code' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/admin/signup-codes')).toBe(true));
    const post = calls.find((c) => c.method === 'POST' && c.path === '/api/v1/admin/signup-codes');
    expect(post.body).toMatchObject({ code: 'FALL-MEETUP', name: 'Fall meetup', grantType: 'DurationMonths', grantMonths: 3, maxRedemptions: 20 });
    expect(await screen.findByText('Created FALL-MEETUP.')).toBeInTheDocument();
  });

  it('creates a batch and downloads the codes as CSV', async () => {
    const download = vi.spyOn(browser, 'download').mockImplementation(() => {});
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/signup-codes' && call.method === 'GET') return json(emptyPage);
      if (call.path === '/api/v1/admin/signup-codes/batches') return json({ batchId: 'scb_1', codes: [{ ...CODE, id: 'suc_a', code: 'ABCDEFGHJK' }, { ...CODE, id: 'suc_b', code: 'MNPQRSTUVW' }] }, 201);
      return undefined;
    });
    renderAdmin(<SignupCodesView />, { fetchImpl, path: '/admin/signup-codes', routePath: '/admin/signup-codes' });
    await userEvent.click(await screen.findByRole('button', { name: 'Create batch' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText('Batch name'), 'Gym partners');
    await userEvent.clear(within(dialog).getByLabelText('Quantity'));
    await userEvent.type(within(dialog).getByLabelText('Quantity'), '2');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create codes' }));
    expect(await screen.findByRole('heading', { name: '2 codes created' })).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/signup-codes/batches').body).toMatchObject({ name: 'Gym partners', quantity: 2, grantType: 'DurationMonths', grantMonths: 12 });
    await userEvent.click(screen.getByRole('button', { name: 'Download CSV' }));
    expect(download).toHaveBeenCalledTimes(1);
    const [text, filename] = download.mock.calls[0];
    expect(filename).toBe('signup-codes-scb_1.csv');
    expect(text).toContain('ABCDEFGHJK');
    expect(text).toContain('MNPQRSTUVW');
  });
});

describe('sign-up code detail', () => {
  function detailFetch() {
    return adminFetch((call) => {
      if (call.path === '/api/v1/admin/signup-codes/suc_1' && call.method === 'GET') {
        return json({ ...CODE, redemptionsByDay: [{ date: '2026-11-10', count: 3 }], outcomes: { active: 30, convertedToPaid: 4, lapsed: 2, revoked: 1 } });
      }
      if (call.path === '/api/v1/admin/signup-codes/suc_1/redemptions') {
        return json(page([{ id: 'scr_1', userId: 'usr_5', memberDisplayName: 'Bea', memberEmail: 'bea@example.com', redeemedUtc: '2026-11-10T00:00:00Z', membershipEndsUtc: '2027-11-10T00:00:00Z', isNewAccount: true, status: 'Active', convertedToPaid: false, revokedUtc: null, revokeReason: null }]));
      }
      if (call.path === '/api/v1/admin/signup-code-redemptions/scr_1/revoke') return json(null, 204);
      if (call.path === '/api/v1/admin/signup-codes/suc_1/revoke-all') return json({ revokedCount: 30 });
      return undefined;
    });
  }

  it('requires a reason to revoke one redemption', async () => {
    const { fetchImpl, calls } = detailFetch();
    renderAdmin(<SignupCodeDetailView />, { fetchImpl, path: '/admin/signup-codes/suc_1', routePath: '/admin/signup-codes/:id' });
    expect(await screen.findByText('Bea')).toBeInTheDocument();
    expect(screen.getByText('37 of 50')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /3 in total/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Actions for Bea' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Revoke' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Revoke' }));
    expect(await within(dialog).findByText('Enter a reason.')).toBeInTheDocument();
    expect(calls.some((c) => c.path.endsWith('/revoke'))).toBe(false);
    await userEvent.type(within(dialog).getByLabelText('Reason'), 'Shared publicly');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Revoke' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/signup-code-redemptions/scr_1/revoke').body).toEqual({ reason: 'Shared publicly' }));
  });

  it('requires a reason to revoke all redemptions', async () => {
    const { fetchImpl, calls } = detailFetch();
    renderAdmin(<SignupCodeDetailView />, { fetchImpl, path: '/admin/signup-codes/suc_1', routePath: '/admin/signup-codes/:id' });
    await userEvent.click(await screen.findByRole('button', { name: 'Revoke all' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/all 30 active redemptions/)).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Revoke all' }));
    expect(await within(dialog).findByText('Enter a reason.')).toBeInTheDocument();
    await userEvent.type(within(dialog).getByLabelText('Reason'), 'Code leaked');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Revoke all' }));
    expect(await screen.findByText('Revoked 30 redemptions.')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/signup-codes/suc_1/revoke-all').body).toEqual({ reason: 'Code leaked' });
  });

  it('fills 90 days of chart data with zeros', () => {
    const days = fillDays([{ date: '2026-11-10', count: 2 }, { date: '2026-11-10T00:00:00Z', count: 1 }], 90, new Date('2026-11-11T12:00:00Z'));
    expect(days).toHaveLength(90);
    expect(days.at(-1)).toEqual({ date: '2026-11-11', count: 0 });
    expect(days.at(-2)).toEqual({ date: '2026-11-10', count: 3 });
  });
});

describe('audit log', () => {
  it('sends audit filters to the server', async () => {
    const { fetchImpl, calls } = adminFetch((call) => (call.path === '/api/v1/admin/audit' ? json(emptyPage) : undefined));
    renderAdmin(<AuditLogView />, { fetchImpl, path: '/admin/audit', routePath: '/admin/audit' });
    expect(await screen.findByText(/No audit records yet/)).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Action'), 'member.ban');
    await userEvent.type(screen.getByLabelText('Target id'), 'usr_9');
    await userEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/v1/admin/audit').at(-1).query).toEqual({ action: 'member.ban', targetId: 'usr_9', ordering: 'CreatedDescending', maxResults: '50' }));
  });
});

describe('admin console sign-in', () => {
  function renderConsole(handler) {
    const { fetchImpl, calls } = createFetch(handler);
    render(
      <ThemeProvider>
        <MemoryRouter initialEntries={['/admin/login']}>
          <AppRoutes fetchImpl={fetchImpl} />
        </MemoryRouter>
      </ThemeProvider>
    );
    return calls;
  }

  it('tells a signed-in non-admin that the account is not an administrator', async () => {
    renderConsole((call) => (call.path === '/api/v1/auth/login' ? json(authResponse(makeMe())) : undefined));
    await userEvent.type(await screen.findByLabelText('Email'), 'member@example.com');
    await userEvent.type(screen.getByLabelText('Password'), 'correct horse battery');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByText('This account is not an administrator.')).toBeInTheDocument();
  });

  it('opens the console for a SystemAdmin using the member session', async () => {
    const admin = makeMe({ displayName: 'Admin User', roles: [{ role: 'SystemAdmin', resourceType: 'Global', resourceId: '*' }] });
    const calls = renderConsole((call) => {
      if (call.path === '/api/v1/auth/login') return json(authResponse(admin, { accessToken: 'adm' }));
      if (call.path === '/api/v1/health') return json({ status: 'Healthy', version: '1.0.0', startedUtc: '2026-11-11T00:00:00Z', uptimeSeconds: 5, database: 'Sqlite' });
      if (call.path === '/api/v1/request-history/summary') return json({ totalCount: 0, totalSuccess: 0, totalFailure: 0, averageDurationMs: 0, buckets: [] });
      if (call.path === '/api/v1/admin/metrics/membership') return json({ totalMembers: 12, activeMembers: 10, paidActive: 7, freeActive: 3, byStore: { Apple: 2, Google: 1, Stripe: 4 }, bySource: { Subscription: 7, SignupCode: 2, AdminComp: 1 }, newMembers30d: 4, estimatedArrUsd: 209.93, lapsed30d: 1 });
      if (call.path === '/api/v1/admin/signup-codes/summary') return json({ activeCodes: 3, totalRedemptions: 40, redemptions30d: 9, activeFreeMembers: 30, convertedToPaid: 4, conversionRate: 0.1 });
      return undefined;
    });
    await userEvent.type(await screen.findByLabelText('Email'), 'admin@example.com');
    await userEvent.type(screen.getByLabelText('Password'), 'correct horse battery');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Home' })).toBeInTheDocument();
    expect(await screen.findByText('$209.93')).toBeInTheDocument();
    expect(screen.getByText('10.0%')).toBeInTheDocument();
    expect(screen.getAllByText('Admin User').length).toBeGreaterThan(0);
    const metrics = calls.find((c) => c.path === '/api/v1/admin/metrics/membership');
    expect(metrics.headers.Authorization).toBe('Bearer adm');
    expect(metrics.headers['X-Client']).toBe('web');
  });
});

describe('bounded CSV exports', () => {
  const pageOf = (text, token) => ({ text, headers: new Headers(token ? { 'X-Continuation-Token': token } : {}) });

  it('follows X-Continuation-Token with the same filters and drops repeated header rows', async () => {
    const seen = [];
    const fetchPage = async (query) => {
      seen.push(query);
      if (!query.continuationToken) return pageOf('code,name\r\nA,One\r\n', 't1');
      if (query.continuationToken === 't1') return pageOf('code,name\r\nB,Two\r\n', 't2');
      return pageOf('code,name\r\nC,Three\r\n', null);
    };
    const result = await fetchCsvPages(fetchPage, { status: 'Active' });
    expect(result).toEqual({ text: 'code,name\r\nA,One\r\nB,Two\r\nC,Three\r\n', pages: 3, complete: true });
    expect(seen).toEqual([{ status: 'Active' }, { status: 'Active', continuationToken: 't1' }, { status: 'Active', continuationToken: 't2' }]);
  });

  it('stops at the page limit and reports the export as incomplete', async () => {
    const result = await fetchCsvPages(async () => pageOf('h\r\nrow\r\n', 'again'), {}, { maxPages: 2 });
    expect(result.pages).toBe(2);
    expect(result.complete).toBe(false);
  });
});
