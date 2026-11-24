import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import ProfileFieldsView, { buildProfileFieldsQuery } from '../src/views/admin/ProfileFieldsView.jsx';
import {
  draftToBody, fieldToDraft, moveInOrder, normalizeDraft, optionKeysInUse, previewFieldsWithDraft, validateFieldDraft
} from '../src/utils/profileFieldForm.js';
import { createFetch, json, renderAdmin } from './helpers.jsx';

const base = {
  helpText: '', placeholder: '', options: [], rank: null, subFields: [], validation: { required: false },
  collectAt: 'Profile', editableBy: 'Member', visibility: 'Public', showOnProfile: true, showOnCard: false,
  searchable: false, filterable: false, enabled: true, valueCount: 0, labels: [], tags: {}, createdUtc: '2026-11-01T00:00:00Z', updatedUtc: '2026-11-01T00:00:00Z'
};
const DISPLAY_NAME = { ...base, id: 'pfd_dn', key: 'displayName', builtIn: true, label: 'Name', type: 'Text', collectAt: 'SignUp', showOnCard: true, profileOrder: 0, validation: { required: true, maxLength: 50 } };
const BIO = { ...base, id: 'pfd_bio', key: 'bio', builtIn: true, label: 'About', type: 'LongText', profileOrder: 5 };
const RANK = {
  ...base, id: 'pfd_rank', key: 'rank', builtIn: false, label: 'Level', type: 'Rank', profileOrder: 10, showOnCard: true, filterable: true, valueCount: 4,
  options: [
    { id: 'pfo_1', key: 'Beginner', label: 'Beginner', color: '#9CA3AF', maxLevel: null, freeText: false, retired: false },
    { id: 'pfo_2', key: 'Advanced', label: 'Advanced', color: '#1F2933', maxLevel: 2, freeText: false, retired: false }
  ],
  rank: { levelLabel: 'Stars', maxLevel: 3 }, validation: { required: true }, collectAt: 'SignUp'
};
const CLUB = { ...base, id: 'pfd_club', key: 'club', builtIn: false, label: 'Club', type: 'Text', profileOrder: 20, searchable: true, filterable: true, validation: { required: false, maxLength: 100 } };
const FIELDS = [DISPLAY_NAME, BIO, RANK, CLUB];
const enumeration = (objects) => ({ success: true, maxResults: 200, skip: 0, continuationToken: null, endOfResults: true, totalRecords: objects.length, recordsRemaining: 0, objects });

function fieldsFetch(handler = () => undefined) {
  return createFetch((call) => {
    const custom = handler(call);
    if (custom) return custom;
    if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
    if (call.path === '/api/v1/admin/profile-fields/enumerate') return json(enumeration(FIELDS));
    if (call.path.endsWith('/enumerate')) return json(enumeration([]));
    return undefined;
  });
}

function renderView(fetchImpl) {
  return renderAdmin(<ProfileFieldsView />, { fetchImpl, path: '/admin/profile-fields', routePath: '/admin/profile-fields' });
}

describe('profile field form helpers', () => {
  it('round-trips a definition through the draft and builds the update body', () => {
    const body = draftToBody(fieldToDraft(RANK), RANK);
    // Key and type are left out while members have values.
    expect(body.key).toBeUndefined();
    expect(body.type).toBeUndefined();
    expect(body.options).toEqual([
      { id: 'pfo_1', key: 'Beginner', label: 'Beginner', retired: false, color: '#9CA3AF', maxLevel: null },
      { id: 'pfo_2', key: 'Advanced', label: 'Advanced', retired: false, color: '#1F2933', maxLevel: 2 }
    ]);
    expect(body.rank).toEqual({ levelLabel: 'Stars', maxLevel: 3 });
    expect(body.validation).toMatchObject({ required: true, maxLength: null, pattern: null, notInFuture: false });
    expect(body.editableBy).toBe('Member');
  });

  it('sends only the allowed attributes of built-in fields', () => {
    expect(Object.keys(draftToBody(fieldToDraft(DISPLAY_NAME), DISPLAY_NAME)).sort()).toEqual(['helpText', 'label', 'labels', 'placeholder', 'tags']);
    expect(Object.keys(draftToBody(fieldToDraft(BIO), BIO)).sort()).toEqual(['collectAt', 'enabled', 'helpText', 'label', 'labels', 'placeholder', 'showOnProfile', 'tags', 'visibility']);
  });

  it('couples editableBy, required, and visibility to collectAt, and searchable and filterable to the type', () => {
    const staff = normalizeDraft({ ...fieldToDraft(CLUB), collectAt: 'AdminOnly', visibility: 'Staff', validation: { ...fieldToDraft(CLUB).validation, required: true } });
    expect(staff.editableBy).toBe('Staff');
    expect(staff.validation.required).toBe(false);
    expect(staff.visibility).toBe('Staff');
    const back = normalizeDraft({ ...staff, collectAt: 'Profile' });
    expect(back.editableBy).toBe('Member');
    expect(back.visibility).toBe('Private');
    const url = normalizeDraft({ ...fieldToDraft(CLUB), type: 'Url' });
    expect(url.searchable).toBe(false);
    expect(url.filterable).toBe(false);
  });

  it('checks keys, options, bounds, and the card limit like the server', () => {
    const draft = { ...fieldToDraft(null), key: 'Bad Key', label: '', type: 'SingleSelect', options: [{ ...fieldToDraft(RANK).options[0], key: 'a b' }] };
    const errors = validateFieldDraft(draft, { fields: FIELDS });
    expect(errors.key).toMatch(/camelCase/);
    expect(errors.label).toBe('Use 1 to 60 characters.');
    expect(errors['options[0].key']).toMatch(/letters, digits/);
    expect(validateFieldDraft({ ...fieldToDraft(null), key: 'email', label: 'Email' }, { fields: FIELDS }).key).toBe('That key is reserved.');
    expect(validateFieldDraft({ ...fieldToDraft(null), key: 'club', label: 'Club 2' }, { fields: FIELDS }).key).toBe('Another field uses that key.');
    const bounds = { ...fieldToDraft(null), key: 'nick', label: 'Nick', validation: { ...fieldToDraft(null).validation, minLength: '10', maxLength: '5', pattern: '([' } };
    const found = validateFieldDraft(bounds, { fields: FIELDS });
    expect(found['validation.minLength']).toBe('Must not exceed the maximum.');
    expect(found['validation.pattern']).toBe('Not a valid regular expression.');
    const card = { ...fieldToDraft(null), key: 'extra', label: 'Extra', showOnCard: true };
    const full = [...FIELDS, { ...CLUB, id: 'a', key: 'a', showOnCard: true }, { ...CLUB, id: 'b', key: 'b', showOnCard: true }];
    expect(validateFieldDraft(card, { fields: full, maxCardFields: 3 }).showOnCard).toBe('At most 3 fields can be shown on member cards.');
    const history = { ...fieldToDraft(null), key: 'past', label: 'Past', type: 'History', subFields: [{ ...fieldToDraft({ subFields: [{ key: 'level', label: 'Level', type: 'RankValue' }] }).subFields[0], rankFieldKey: 'missing' }] };
    expect(validateFieldDraft(history, { fields: FIELDS })['subFields[0].rankFieldKey']).toBe('Choose a rank field of this community.');
  });

  it('moves ids, reads OptionInUse keys, and builds preview drafts', () => {
    expect(moveInOrder(['a', 'b', 'c'], 'b', -1)).toEqual(['b', 'a', 'c']);
    expect(moveInOrder(['a', 'b', 'c'], 'c', 1)).toBeNull();
    expect(optionKeysInUse('Members have values for option Beginner, Advanced; set retired instead of removing it.')).toEqual(['Beginner', 'Advanced']);
    const draft = { ...fieldToDraft(CLUB), label: 'Team' };
    const list = previewFieldsWithDraft(FIELDS, CLUB, draft);
    expect(list.map((f) => f.key)).toEqual(['rank', 'club']);
    expect(list[1]).toMatchObject({ id: 'pfd_club', label: 'Team', profileOrder: 20 });
    const added = previewFieldsWithDraft(FIELDS, null, { ...fieldToDraft(null), key: 'nick', label: 'Nick' });
    expect(added.at(-1)).toMatchObject({ key: 'nick', profileOrder: 30 });
  });

  it('queries the list in profile order with filters', () => {
    expect(buildProfileFieldsQuery({ type: 'Rank', enabled: 'false' }, { pageNumber: 1, pageSize: 50 })).toMatchObject({ ordering: 'ProfileOrderAscending', maxResults: 50, filters: { type: 'Rank', enabled: 'false' } });
  });
});

describe('profile fields manager', () => {
  it('lists every field in profile order and reorders with every id', async () => {
    const { fetchImpl, calls } = fieldsFetch((call) => (call.path === '/api/v1/admin/profile-fields/reorder' ? json({ order: call.body.order }) : undefined));
    renderView(fetchImpl);
    expect(await screen.findByText('Level')).toBeInTheDocument();
    const rows = screen.getAllByRole('row').slice(1);
    expect(rows.map((r) => within(r).getAllByRole('cell')[2].textContent)).toEqual(['displayName', 'bio', 'rank', 'club']);
    expect(within(rows[0]).getByText('Built-in')).toBeInTheDocument();
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Move Club up' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/profile-fields/reorder').body).toEqual({ order: ['pfd_dn', 'pfd_bio', 'pfd_club', 'pfd_rank'] }));
    expect(screen.getByRole('button', { name: 'Move Name up' })).toBeDisabled();
  });

  it('toggles a field, and the built-in name cannot be disabled', async () => {
    const { fetchImpl, calls } = fieldsFetch((call) => (call.path === '/api/v1/admin/profile-fields/pfd_club' && call.method === 'PUT' ? json({ ...CLUB, enabled: false }) : undefined));
    renderView(fetchImpl);
    expect(await screen.findByRole('switch', { name: 'Enable Club' })).toBeChecked();
    expect(screen.getByRole('switch', { name: 'Enable Name' })).toBeDisabled();
    await userEvent.click(screen.getByRole('switch', { name: 'Enable Club' }));
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT').body).toEqual({ enabled: false }));
  });

  it('creates a select field and shows server field errors inline', async () => {
    let attempt = 0;
    const { fetchImpl, calls } = fieldsFetch((call) => {
      if (call.path === '/api/v1/admin/profile-fields' && call.method === 'POST') {
        attempt += 1;
        if (attempt === 1) return json({ error: 'ValidationFailed', message: 'One or more fields are invalid.', fieldErrors: { 'options[0].label': 'Server says no.' } }, 400);
        return json({ ...CLUB, id: 'pfd_new', key: 'goal', label: 'Goal', type: 'SingleSelect' }, 201);
      }
      return undefined;
    });
    renderView(fetchImpl);
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Add field' }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText('Key'), 'goal');
    await user.type(within(dialog).getByLabelText('Label'), 'Goal');
    await user.selectOptions(within(dialog).getByLabelText('Type'), 'SingleSelect');
    await user.type(within(dialog).getByLabelText('Option 1 key'), 'fit');
    await user.type(within(dialog).getByLabelText('Option 1 label'), 'Fitness');
    await user.click(within(dialog).getByLabelText('Option 1 takes free text'));
    await user.click(within(dialog).getByRole('button', { name: 'Create field' }));
    expect(await within(dialog).findByText('Server says no.')).toBeInTheDocument();
    const post = calls.find((c) => c.method === 'POST' && c.path === '/api/v1/admin/profile-fields');
    expect(post.body).toMatchObject({
      key: 'goal', label: 'Goal', type: 'SingleSelect', collectAt: 'Profile', editableBy: 'Member', visibility: 'Public',
      options: [{ key: 'fit', label: 'Fitness', retired: false, color: null, freeText: true }], rank: null, subFields: []
    });
    await user.click(within(dialog).getByRole('button', { name: 'Create field' }));
    expect(await screen.findByText('Field Goal created.')).toBeInTheDocument();
  });

  it('locks key and type while members have values and surfaces OptionInUse', async () => {
    const { fetchImpl, calls } = fieldsFetch((call) => {
      if (call.path === '/api/v1/admin/profile-fields/pfd_rank/stats/enumerate') return json({ valueCount: 4, fillRate: 0.5, endOfResults: true, objects: [{ key: 'Beginner', count: 3 }, { key: 'Advanced', count: 0 }], updated30d: 1 });
      if (call.path === '/api/v1/admin/profile-fields/pfd_rank' && call.method === 'PUT') return json({ error: 'OptionInUse', message: 'Members have values for option Advanced; set retired instead of removing it.' }, 409);
      return undefined;
    });
    renderView(fetchImpl);
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Actions for Level' }));
    await user.click(screen.getByRole('menuitem', { name: 'Edit' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByLabelText('Key')).toHaveAttribute('readonly');
    expect(within(dialog).getByText(/Locked: 4 members have a value. Create a new field/)).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Type')).toBeDisabled();
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Remove option 1' })).toBeDisabled());
    await user.click(within(dialog).getByRole('button', { name: 'Remove option 2' }));
    await user.click(within(dialog).getByRole('button', { name: 'Save field' }));
    expect(await within(dialog).findByText(/An option you removed is in use/)).toBeInTheDocument();
    const put = calls.find((c) => c.method === 'PUT');
    expect(put.body.options.map((o) => o.key)).toEqual(['Beginner']);
    expect(put.body.key).toBeUndefined();
  });

  it('requires typing the key to delete a field with values', async () => {
    const withValues = { ...CLUB, valueCount: 7 };
    const { fetchImpl, calls } = fieldsFetch((call) => {
      if (call.path === '/api/v1/admin/profile-fields/enumerate') return json(enumeration([DISPLAY_NAME, withValues]));
      if (call.path === '/api/v1/admin/profile-fields/pfd_club' && call.method === 'DELETE') return json(null, 202);
      return undefined;
    });
    renderView(fetchImpl);
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Actions for Club' }));
    await user.click(screen.getByRole('menuitem', { name: 'Delete' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/7 members have a value for this field/)).toBeInTheDocument();
    const confirm = within(dialog).getByRole('button', { name: 'Delete field' });
    expect(confirm).toBeDisabled();
    await user.type(within(dialog).getByLabelText('Type club to confirm'), 'club');
    await user.click(confirm);
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE')).toBe(true));
    const del = calls.find((c) => c.method === 'DELETE');
    expect(del.query).toEqual({ deleteValues: 'true' });
    expect(del.body).toEqual({ confirm: 'club' });
  });

  it('shows stats in the detail view and previews an unsaved draft', async () => {
    const { fetchImpl, calls } = fieldsFetch((call) => {
      if (call.path === '/api/v1/admin/profile-fields/pfd_rank/stats/enumerate') return json({ valueCount: 4, fillRate: 0.5, endOfResults: true, objects: [{ key: 'Beginner', count: 3 }], updated30d: 1 });
      if (call.path === '/api/v1/admin/profile-fields/preview') {
        return json({ target: call.body.target, viewer: call.body.viewer, items: [{ key: 'club', builtIn: false, label: 'Team', type: 'Text', required: false, helpText: '', placeholder: '', control: 'text', display: 'text', value: null, formattedValue: null, options: null }] });
      }
      return undefined;
    });
    renderView(fetchImpl);
    const user = userEvent.setup();
    await user.click(await screen.findByText('Level'));
    const detail = await screen.findByRole('dialog');
    expect(await within(detail).findByText('50%')).toBeInTheDocument();
    expect(within(detail).getByText('Members per option', { selector: 'caption' })).toBeInTheDocument();
    await user.click(within(detail).getByRole('button', { name: 'Close' }));

    await user.click(screen.getByRole('button', { name: 'Actions for Club' }));
    await user.click(screen.getByRole('menuitem', { name: 'Edit' }));
    const dialog = await screen.findByRole('dialog');
    const label = within(dialog).getByLabelText('Label');
    await user.clear(label);
    await user.type(label, 'Team');
    await user.selectOptions(within(dialog).getByLabelText('Surface'), 'Card');
    await user.selectOptions(within(dialog).getByLabelText('Viewer'), 'Anonymous');
    await user.click(within(dialog).getByRole('button', { name: 'Run preview' }));
    expect(await within(dialog).findByText('Team', { selector: 'strong' })).toBeInTheDocument();
    const preview = calls.find((c) => c.path === '/api/v1/admin/profile-fields/preview');
    expect(preview.body.target).toBe('Card');
    expect(preview.body.viewer).toBe('Anonymous');
    expect(preview.body.fields.map((f) => f.key)).toEqual(['rank', 'club']);
    expect(preview.body.fields[1]).toMatchObject({ id: 'pfd_club', label: 'Team' });
  });
});
