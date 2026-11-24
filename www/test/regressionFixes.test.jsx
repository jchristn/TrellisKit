import { describe, expect, it } from 'vitest';
import { isForumModerator, isOpeningTicketMessage } from '../src/utils/engagement.js';
import { searchTypesFor } from '../src/views/member/SearchView.jsx';

describe('support tickets show the opening message once', () => {
  it('recognizes the opening member reply stored with the ticket', () => {
    const ticket = { createdUtc: '2026-11-13T10:00:00Z' };
    expect(isOpeningTicketMessage(ticket, { kind: 'Reply', authorRole: 'Member', createdUtc: '2026-11-13T10:00:00Z' })).toBe(true);
    expect(isOpeningTicketMessage(ticket, { kind: 'Reply', authorRole: 'Member', createdUtc: '2026-11-13T10:05:00Z' })).toBe(false);
    expect(isOpeningTicketMessage(ticket, { kind: 'Reply', authorRole: 'Staff', createdUtc: '2026-11-13T10:00:00Z' })).toBe(false);
    expect(isOpeningTicketMessage(ticket, { kind: 'Note', authorRole: 'Member', createdUtc: '2026-11-13T10:00:00Z' })).toBe(false);
    expect(isOpeningTicketMessage(null, { kind: 'Reply' })).toBe(false);
  });
});

describe('messaging moderators are not forum moderators', () => {
  it('ignores Moderator roles on the Messaging resource type', () => {
    expect(isForumModerator({ roles: [{ role: 'Moderator', resourceType: 'Forum', resourceId: '*' }] })).toBe(true);
    expect(isForumModerator({ roles: [{ role: 'Moderator', resourceType: 'Messaging', resourceId: '*' }] })).toBe(false);
    expect(isForumModerator({ roles: [] })).toBe(false);
  });
});

describe('global search filters follow the property features', () => {
  it('offers events, library, directory, and ideas types only when those features are on', () => {
    const all = searchTypesFor({ features: {} });
    expect(all).toEqual(expect.arrayContaining(['Event', 'Series', 'Lesson', 'Venue', 'Instructor', 'Idea']));
    const none = searchTypesFor({ features: { events: false, library: false, directory: false, ideas: false } });
    expect(none).not.toEqual(expect.arrayContaining(['Event']));
    expect(none.some((t) => ['Event', 'Series', 'Lesson', 'Venue', 'Instructor', 'Idea'].includes(t))).toBe(false);
  });
});

describe('directory review shows the member pin', () => {
  it('turns a proposed point into the editable latitude and longitude', async () => {
    const { pinFrom } = await import('../src/views/admin/directory/DirectoryAdminView.jsx');
    expect(pinFrom({ lat: 41.8781, lng: -87.6298 })).toEqual({ lat: '41.8781', lng: '-87.6298' });
    expect(pinFrom(null)).toEqual({ lat: '', lng: '' });
  });
});

describe('tag value suggestions', () => {
  it('ask once per key and text, not after every result (no request loop)', async () => {
    const { render } = await import('@testing-library/react');
    const { default: TagRows } = await import('../src/components/TagRows.jsx');
    let calls = 0;
    const suggestValues = async () => { calls += 1; return ['one', 'two']; };
    const suggestKeys = async () => ['seed'];
    render(<TagRows label="Tags" rows={[{ key: 'seed', value: 'x' }]} onChange={() => {}} suggestKeys={suggestKeys} suggestValues={suggestValues} />);
    await new Promise((r) => setTimeout(r, 1200));
    expect(calls).toBe(1);
  });
});
