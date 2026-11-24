import { describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import MembersView from '../src/views/admin/MembersView.jsx';
import FeedAdminView from '../src/views/admin/FeedAdminView.jsx';
import AdminFilterPanel, { fieldChipText } from '../src/components/AdminFilterPanel.jsx';
import BulkMetadataDialog, { BULK_METADATA_MAX, bulkMetadataBody } from '../src/components/BulkMetadataDialog.jsx';
import useTableState from '../src/hooks/useTableState.js';
import { AdminAuthProvider, ADMIN_TOKEN_KEY } from '../src/context/AdminAuthContext.jsx';
import { AuthProvider } from '../src/context/AuthContext.jsx';
import { ToastProvider } from '../src/context/ToastContext.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { createFetch, json, renderAdmin } from './helpers.jsx';

const member = (id, name) => ({ id, displayName: name, handle: name.toLowerCase(), email: name.toLowerCase() + '@example.com', rank: null, rankLevel: 0, membership: { isActive: true, source: 'SignupCode' }, roles: [], isBanned: false, isMuted: false, createdUtc: '2026-11-01T00:00:00Z', lastSeenUtc: null });

function adminFetch(handler) {
  return createFetch((call) => {
    if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
    const custom = handler(call);
    if (custom) return custom;
    if (call.path.endsWith('/enumerate')) return json({ success: true, maxResults: 10, skip: 0, continuationToken: null, endOfResults: true, totalRecords: 0, recordsRemaining: 0, timestampUtc: '2026-11-11T00:00:00Z', objects: [] });
    return undefined;
  });
}

const MEMBERS = '/api/v1/admin/members/enumerate';
const memberCalls = (calls) => calls.filter((c) => c.path === MEMBERS);
const result = (items) => ({ success: true, maxResults: 50, skip: 0, continuationToken: null, endOfResults: true, totalRecords: items.length, recordsRemaining: 0, timestampUtc: '2026-11-11T00:00:00Z', objects: items });

describe('unified admin filter panel', () => {
  it('shows chips for form filters and label and tag filters, removes one, and clears all', async () => {
    const { fetchImpl, calls } = adminFetch((call) => (call.path === MEMBERS ? json(result([member('usr_1', 'Ana')])) : undefined));
    renderAdmin(<MembersView />, { fetchImpl, path: '/admin/members?membership=Free&banned=true&labels=vip&tag.level=beginner', routePath: '/admin/members' });
    expect(await screen.findByText('Ana')).toBeInTheDocument();
    const chips = screen.getByRole('group', { name: 'Active filters' });
    expect(within(chips).getByText('Filters (4)')).toBeInTheDocument();
    expect(within(chips).getByText('Membership: Free')).toBeInTheDocument();
    expect(within(chips).getByText('Banned only')).toBeInTheDocument();
    expect(within(chips).getByText('Label: vip')).toBeInTheDocument();
    expect(within(chips).getByText('level = beginner')).toBeInTheDocument();

    await userEvent.click(within(chips).getByRole('button', { name: 'Remove filter Membership: Free' }));
    await waitFor(() => expect(memberCalls(calls).at(-1).body).toEqual({ maxResults: 50, ordering: 'CreatedDescending', labels: ['vip'], labelMatch: 'All', tags: { level: 'beginner' }, filters: { banned: true } }));
    expect(screen.queryByText('Membership: Free')).toBeNull();

    await userEvent.click(within(screen.getByRole('group', { name: 'Active filters' })).getByRole('button', { name: 'Clear all' }));
    await waitFor(() => expect(memberCalls(calls).at(-1).body).toEqual({ maxResults: 50, ordering: 'CreatedDescending' }));
    expect(screen.queryByRole('group', { name: 'Active filters' })).toBeNull();
  });

  it('applies form filters and label and tag filters together with one Apply, keeping a search filter', async () => {
    const { fetchImpl, calls } = adminFetch((call) => (call.path === MEMBERS ? json(result([member('usr_1', 'Ana')])) : undefined));
    renderAdmin(<MembersView />, { fetchImpl, path: '/admin/members', routePath: '/admin/members' });
    expect(await screen.findByText('Ana')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Apply filters' })).toHaveLength(1);
    expect(screen.queryByRole('button', { name: 'Apply' })).toBeNull();

    await userEvent.type(screen.getByLabelText('Name, handle, or email'), 'ana');
    await userEvent.selectOptions(screen.getByLabelText('Role'), 'Moderator');
    await userEvent.click(screen.getByRole('button', { name: 'Labels and tags' }));
    await userEvent.type(screen.getByRole('combobox', { name: 'Labels' }), 'vip{enter}');
    await userEvent.click(screen.getByRole('button', { name: 'Add tag' }));
    await userEvent.type(screen.getByLabelText('Tag 1 key'), 'cohort');
    await userEvent.type(screen.getByLabelText('Tag 1 value'), 'spring');
    const before = memberCalls(calls).length;
    await userEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
    await waitFor(() => expect(memberCalls(calls).length).toBe(before + 1));
    const last = memberCalls(calls).at(-1);
    expect(last.method).toBe('POST');
    expect(last.body).toMatchObject({ search: 'ana', labels: ['vip'], tags: { cohort: 'spring' }, filters: { role: 'Moderator' } });
    const chips = screen.getByRole('group', { name: 'Active filters' });
    expect(within(chips).getByText('Name, handle, or email: ana')).toBeInTheDocument();
    expect(within(chips).getByText('Role: Forum moderator')).toBeInTheDocument();
    expect(within(chips).getByText('Label: vip')).toBeInTheDocument();
    expect(within(chips).getByText('cohort = spring')).toBeInTheDocument();
  });

  it('keeps every filter in the URL', async () => {
    let search = '';
    function Harness() {
      const table = useTableState({ storageKey: 'tk.test', filterKeys: ['status', 'search'], defaultSort: '' });
      search = useLocation().search;
      return <AdminFilterPanel fields={[{ key: 'search', label: 'Search' }, { key: 'status', label: 'Status', type: 'select', options: [{ value: 'Open', label: 'Open' }] }]} table={table} client={null} targetType="Report" />;
    }
    render(<MemoryRouter initialEntries={['/x?page=3']}><Routes><Route path="/x" element={<Harness />} /></Routes></MemoryRouter>);
    await userEvent.type(screen.getByLabelText('Search'), 'spam');
    await userEvent.selectOptions(screen.getByLabelText('Status'), 'Open');
    await userEvent.click(screen.getByRole('button', { name: 'Labels and tags' }));
    await userEvent.type(screen.getByRole('combobox', { name: 'Labels' }), 'urgent{enter}');
    await userEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
    const params = new URLSearchParams(search);
    expect(params.get('search')).toBe('spam');
    expect(params.get('status')).toBe('Open');
    expect(params.getAll('labels')).toEqual(['urgent']);
    expect(params.get('page')).toBeNull();
    expect(fieldChipText({ key: 'on', label: 'Only open', type: 'checkbox' }, 'false')).toBe('');
  });
});

function renderDialog(ui, fetchImpl) {
  window.sessionStorage.setItem(ADMIN_TOKEN_KEY, 'test-token');
  return render(
    <ThemeProvider>
      <MemoryRouter>
        <AuthProvider fetchImpl={fetchImpl} restoreOnLoad={false}>
          <AdminAuthProvider fetchImpl={fetchImpl}>
            <ToastProvider>{ui}</ToastProvider>
          </AdminAuthProvider>
        </AuthProvider>
      </MemoryRouter>
    </ThemeProvider>
  );
}

describe('bulk labels and tags', () => {
  it('builds the bulk body, omits empty changes, and caps the ids at 500', () => {
    const ids = Array.from({ length: 600 }, (_, i) => 'id_' + i);
    const body = bulkMetadataBody('Media', ids, { addLabels: ['a'], removeLabels: [], tagRows: [{ key: ' level ', value: 'x', hasKey: false }], removeTagKeys: ['draft'] });
    expect(body.targetIds).toHaveLength(BULK_METADATA_MAX);
    expect(body).toMatchObject({ targetType: 'Media', addLabels: ['a'], setTags: { level: 'x' }, removeTagKeys: ['draft'] });
    expect(body.removeLabels).toBeUndefined();
    expect(bulkMetadataBody('Media', ['m1'], { addLabels: [], removeLabels: [], tagRows: [], removeTagKeys: [] })).toBeNull();
  });

  it('refuses more than 500 selected items', () => {
    const { fetchImpl } = adminFetch(() => undefined);
    renderDialog(<BulkMetadataDialog open targetType="User" ids={Array.from({ length: 501 }, (_, i) => 'u' + i)} onClose={() => {}} />, fetchImpl);
    expect(screen.getAllByText(/Select at most 500 items/).length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: 'Apply changes' })).toBeDisabled();
  });

  it('selects members, applies a bulk change, and shows the result summary', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === MEMBERS) return json(result([member('usr_1', 'Ana'), member('usr_2', 'Ben')]));
      if (call.path === '/api/v1/admin/metadata/bulk') return json({ updated: 1, notFound: ['usr_2'] });
      return undefined;
    });
    renderAdmin(<MembersView />, { fetchImpl, path: '/admin/members', routePath: '/admin/members' });
    expect(await screen.findByText('Ana')).toBeInTheDocument();
    await userEvent.click(screen.getByLabelText('Select all on this page'));
    const bar = screen.getByRole('region', { name: '2 selected' });
    await userEvent.click(within(bar).getByRole('button', { name: 'Labels and tags' }));
    const dialog = screen.getByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Apply changes' }));
    expect(within(dialog).getByText('Add at least one label or tag change.')).toBeInTheDocument();
    await userEvent.type(within(dialog).getByRole('combobox', { name: 'Add labels' }), 'vip{enter}');
    await userEvent.type(within(dialog).getByRole('combobox', { name: 'Remove tag keys' }), 'draft{enter}');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add tag' }));
    await userEvent.type(within(dialog).getByLabelText('Tag 1 key'), 'cohort');
    await userEvent.type(within(dialog).getByLabelText('Tag 1 value'), 'spring');
    const listsBefore = memberCalls(calls).length;
    await userEvent.click(within(dialog).getByRole('button', { name: 'Apply changes' }));
    expect(await screen.findByText('1 item updated.')).toBeInTheDocument();
    expect(screen.getByText('usr_2')).toBeInTheDocument();
    const bulk = calls.find((c) => c.path === '/api/v1/admin/metadata/bulk');
    expect(bulk.method).toBe('POST');
    expect(bulk.body).toEqual({ targetType: 'User', targetIds: ['usr_1', 'usr_2'], addLabels: ['vip'], setTags: { cohort: 'spring' }, removeTagKeys: ['draft'] });
    await waitFor(() => expect(memberCalls(calls).length).toBeGreaterThan(listsBefore));
    expect(screen.queryByRole('region', { name: '2 selected' })).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Done' }));
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('feed posts are selectable for bulk labels and tags', async () => {
    const post = { id: 'fpo_1', title: 'Hello', excerpt: 'x', author: null, state: 'Published', visibility: 'Members', createdUtc: '2026-11-01T00:00:00Z', commentCount: 0, reactionTotal: 0 };
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/feed') return json({ success: true, maxResults: 50, skip: 0, continuationToken: null, endOfResults: true, totalRecords: 1, recordsRemaining: 0, timestampUtc: '2026-11-11T00:00:00Z', objects: [post] });
      if (call.path === '/api/v1/admin/metadata/bulk') return json({ updated: 1, notFound: [] });
      return undefined;
    });
    renderAdmin(<FeedAdminView />, { fetchImpl, path: '/admin/feed', routePath: '/admin/feed' });
    await userEvent.click(await screen.findByLabelText('Select Hello'));
    await userEvent.click(within(screen.getByRole('region', { name: '1 selected' })).getByRole('button', { name: 'Labels and tags' }));
    const dialog = screen.getByRole('dialog');
    await userEvent.type(within(dialog).getByRole('combobox', { name: 'Remove labels' }), 'old{enter}');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Apply changes' }));
    expect(await screen.findByText('1 item updated.')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/metadata/bulk').body).toEqual({ targetType: 'FeedPost', targetIds: ['fpo_1'], removeLabels: ['old'] });
  });
});
