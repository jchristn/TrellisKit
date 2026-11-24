import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { parseEnumerationParams, toEnumerationSearch } from '@trelliskit/shared';
import MetadataFilterBar from '../src/components/MetadataFilterBar.jsx';
import MetadataEditor from '../src/components/MetadataEditor.jsx';
import MetadataPanel from '../src/components/MetadataPanel.jsx';
import useEnumerationUrlState, { readMeta, writeMeta } from '../src/hooks/useEnumerationUrlState.js';
import { paged } from '../src/utils/adminQuery.js';
import { json } from './helpers.jsx';
import { useAuth as useAuthHook } from '../src/context/AuthContext.jsx';
import { enumPage, memberFetch, renderMember } from './engagementHelpers.jsx';

function suggestClient(calls = []) {
  return {
    async enumerateLabels(q) { calls.push(['labels', q]); return enumPage([{ label: 'beginners', count: 3 }, { label: 'guides', count: 1 }]); },
    async enumerateTagKeys(q) { calls.push(['keys', q]); return enumPage([{ key: 'level', count: 3 }]); },
    async enumerateTagValues(q) { calls.push(['values', q]); return enumPage([{ value: 'beginner', count: 2 }]); }
  };
}

describe('URL state round-trips', () => {
  it('reads and writes labels, labelMatch, tag.<key>, search, and ordering', () => {
    const meta = { labels: ['beginners', 'staff:featured'], labelMatch: 'Any', tags: { level: 'beginner', has: '*' }, search: 'welcome', ordering: 'CreatedAscending' };
    const params = writeMeta(new URLSearchParams('page=3&state=Published'), meta, { extra: { page: '' } });
    expect(params.toString()).toBe('state=Published&labels=beginners&labels=staff%3Afeatured&labelMatch=Any&tag.level=beginner&tag.has=*&search=welcome&ordering=CreatedAscending');
    expect(readMeta(params)).toEqual(meta);
    expect(readMeta(writeMeta(new URLSearchParams(), meta, { prefix: 'c.' }), 'c.')).toEqual(meta);
    expect(writeMeta(new URLSearchParams(), { labels: ['a'], labelMatch: 'All', tags: {} }).toString()).toBe('labels=a');
    // The URL form is the same as the API query-string form.
    const api = parseEnumerationParams(params, { filterKeys: ['state'] });
    expect(api).toMatchObject({ labels: meta.labels, labelMatch: 'Any', tags: meta.tags, search: 'welcome', ordering: 'CreatedAscending', filters: { state: 'Published' } });
    expect(toEnumerationSearch({ ...api, maxResults: 25 })).toContain('labels=beginners&labels=staff%3Afeatured&labelMatch=Any&tag.level=beginner&tag.has=*');
    expect(paged({ search: 'x', state: 'Hidden', fromUtc: 'f' }, ['search', 'state', 'fromUtc'], { pageNumber: 3, pageSize: 10, sort: '-publishedUtc', meta })).toEqual({
      maxResults: 10, skip: 20, ordering: 'PublishedDescending', search: 'x', createdAfterUtc: 'f', labels: meta.labels, labelMatch: 'Any', 'tag.level': 'beginner', 'tag.has': '*', state: 'Hidden'
    });
  });

  it('useEnumerationUrlState keeps filters in the URL', async () => {
    let seen = '';
    function Probe() {
      const [meta, setMeta] = useEnumerationUrlState();
      seen = useLocation().search;
      return <button type="button" onClick={() => setMeta({ ...meta, labels: [...meta.labels, 'b'] })}>{meta.labels.join(',') || 'none'}</button>;
    }
    render(<MemoryRouter initialEntries={['/x?labels=a']}><Probe /></MemoryRouter>);
    await userEvent.click(screen.getByRole('button', { name: 'a' }));
    expect(screen.getByRole('button', { name: 'a,b' })).toBeInTheDocument();
    expect(seen).toBe('?labels=a&labels=b');
  });
});

describe('MetadataFilterBar', () => {
  it('adds labels with autocomplete, toggles Any, adds a has-key tag, and shows removable chips', async () => {
    const calls = [];
    const onChange = vi.fn();
    const value = { labels: [], labelMatch: 'All', tags: {}, search: '', ordering: '' };
    render(<MetadataFilterBar value={value} onChange={onChange} client={suggestClient(calls)} targetType="FeedPost" showSearch orderings={[{ value: '', label: 'Newest' }, { value: 'CreatedAscending', label: 'Oldest' }]} />);
    await userEvent.click(screen.getByRole('button', { name: 'Labels and tags' }));
    const labelInput = screen.getByRole('combobox', { name: 'Labels' });
    await userEvent.type(labelInput, 'be');
    const option = await screen.findByRole('option', { name: 'beginners' });
    expect(calls.find((c) => c[0] === 'labels')[1]).toMatchObject({ prefix: 'be', targetType: 'FeedPost', maxResults: 10 });
    await userEvent.click(option);
    await userEvent.type(labelInput, 'custom{enter}');
    await userEvent.click(screen.getByRole('radio', { name: 'Any label' }));
    await userEvent.click(screen.getByRole('button', { name: 'Add tag' }));
    await userEvent.type(screen.getByLabelText('Tag 1 key'), 'level');
    await userEvent.click(screen.getByRole('checkbox', { name: 'Has key' }));
    await userEvent.type(screen.getByLabelText('Search'), 'welcome');
    await userEvent.click(screen.getByRole('button', { name: 'Apply' }));
    expect(onChange).toHaveBeenLastCalledWith({ labels: ['beginners', 'custom'], labelMatch: 'Any', tags: { level: '*' }, search: 'welcome', ordering: '' });

    onChange.mockClear();
    const active = { labels: ['beginners', 'custom'], labelMatch: 'Any', tags: { level: '*', color: 'blue' }, search: 'welcome', ordering: '' };
    render(<MetadataFilterBar value={active} onChange={onChange} client={suggestClient()} />);
    const chips = screen.getAllByRole('group', { name: 'Active filters' }).at(-1);
    expect(within(chips).getByText('Has level')).toBeInTheDocument();
    expect(within(chips).getByText('color = blue')).toBeInTheDocument();
    expect(within(chips).getByText('matching any label')).toBeInTheDocument();
    await userEvent.click(within(chips).getByRole('button', { name: 'Remove filter Label: beginners' }));
    expect(onChange).toHaveBeenLastCalledWith({ ...active, labels: ['custom'] });
    await userEvent.click(within(chips).getByRole('button', { name: 'Clear all' }));
    expect(onChange).toHaveBeenLastCalledWith({ ...active, labels: [], labelMatch: 'All', tags: {}, search: '' });
  });
});

describe('MetadataEditor', () => {
  it('members edit public metadata only and never see or send staff: entries', async () => {
    const onChange = vi.fn();
    render(<MetadataEditor value={{ labels: ['beginners', 'staff:featured'], tags: { level: 'beginner', 'staff:review': 'pending' } }} onChange={onChange} client={suggestClient()} targetType="FeedPost" />);
    expect(screen.getByText('beginners')).toBeInTheDocument();
    expect(screen.queryByText('staff:featured')).toBeNull();
    expect(screen.queryByDisplayValue('staff:review')).toBeNull();
    expect(screen.queryByText('Staff-only labels')).toBeNull();
    await userEvent.type(screen.getByRole('combobox', { name: 'Labels' }), 'staff:sneaky{enter}');
    const [next, problems] = onChange.mock.calls.at(-1);
    // The staff: label is refused with a message but never kept: it would be hidden from the member
    // and block saving with no way to remove it.
    expect(next.labels).toEqual(['beginners']);
    expect(problems).toEqual([]);
    expect(screen.getByText('"staff:sneaky" is reserved for staff.')).toBeInTheDocument();
    await userEvent.type(screen.getByRole('combobox', { name: 'Labels' }), 'drills{enter}');
    expect(onChange.mock.calls.at(-1)[0].labels).toEqual(['beginners', 'drills']);
    expect(screen.queryByText('"staff:sneaky" is reserved for staff.')).toBeNull();
  });

  it('staff see a staff-only group and their entries are prefixed', async () => {
    const onChange = vi.fn();
    render(<MetadataEditor value={{ labels: ['beginners', 'staff:featured'], tags: { 'staff:review': 'pending' } }} onChange={onChange} isStaff client={suggestClient()} />);
    expect(screen.getByText('staff:featured')).toBeInTheDocument();
    expect(screen.getByDisplayValue('staff:review')).toBeInTheDocument();
    await userEvent.type(screen.getByRole('combobox', { name: 'Staff-only labels' }), 'priority{enter}');
    expect(onChange.mock.calls.at(-1)[0].labels).toEqual(['beginners', 'staff:featured', 'staff:priority']);
    await userEvent.click(screen.getAllByRole('button', { name: 'Add tag' })[0]);
    await userEvent.type(screen.getAllByLabelText('Tag 1 key')[0], 'bad key!');
    expect(onChange.mock.calls.at(-1)[1]).toContain('tagKeyInvalid:bad key!');
  });

  it('MetadataPanel saves through PUT /metadata with replace semantics', async () => {
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path === '/api/v1/metadata/User/usr_1' && call.method === 'PUT') return json({ targetType: 'User', targetId: 'usr_1', ...call.body });
      return undefined;
    });
    const Harness = () => {
      const auth = useAuthHook();
      return <MetadataPanel client={auth.client} targetType="User" targetId="usr_1" initial={{ labels: ['a'], tags: {} }} />;
    };
    await renderMember(<Harness />, { fetchImpl });
    const save = screen.getByRole('button', { name: 'Save labels and tags' });
    expect(save).toBeDisabled();
    await userEvent.type(screen.getByRole('combobox', { name: 'Labels' }), 'b{enter}');
    await userEvent.click(save);
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT').body).toEqual({ labels: ['a', 'b'], tags: {} }));
  });
});

