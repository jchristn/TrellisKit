import { createMessagingFake } from '@trelliskit/shared/testing';
import { MobileApiClient } from '../src/api/MobileApiClient';
import { configuredStatuses, ideaCollectionFilters, membersSubmitIdeas, withVote } from '../src/ideas/ideas';
import { getActiveConversationId, onActiveConversationChange, setActiveConversationId, shouldSuppressPush } from '../src/messaging/activeConversation';
import {
  cannotSendText,
  conversationName,
  dayLabel,
  describeMessagingError,
  displayBody,
  hasOriginal,
  isEdited,
  lastOwnSeq,
  messageRows,
  messagesBadgeCount,
  newestIncomingSeq,
  previewText,
  receiptState,
  receiptText
} from '../src/messaging/format';
import { realtimeUrl } from '../src/messaging/realtimeConfig';
import { resolveNotificationPath, pushTarget } from '../src/notifications/deepLinks';
import { CategoryOrder, orderedCategories } from '../src/notifications/preferences';
import { foregroundPresentation } from '../src/notifications/push';
import strings from '../src/strings';
import { deviceDiagnostics, supportEmailOf, supportEnabled, ticketProblems } from '../src/support/support';
import { property } from '../test-utils/property';

const t = strings.messaging;
const alex = { id: 'usr_2', displayName: 'Alex Chen', handle: 'alexchen' };

describe('message display helpers', () => {
  test('names and previews', () => {
    const c = { otherMember: alex, lastMessage: { authorUserId: 'usr_1', excerpt: 'Hi', state: 'Visible', hasAttachments: false } };
    expect(conversationName(c, 'usr_1')).toBe('Alex Chen');
    expect(conversationName({ members: [{ user: { id: 'usr_1' } }, { user: alex }] }, 'usr_1')).toBe('Alex Chen');
    expect(conversationName({ otherMember: { ...alex, isDeleted: true } }, 'usr_1')).toBe(t.deletedMember);
    expect(previewText(c, 'usr_1')).toBe(t.youPrefix('Hi'));
    expect(previewText({ lastMessage: { authorUserId: 'usr_2', excerpt: '', state: 'Visible', hasAttachments: true } }, 'usr_1')).toBe(t.attachment);
    expect(previewText({ lastMessage: { authorUserId: 'usr_2', excerpt: '', state: 'Deleted' } }, 'usr_1')).toBe(t.deletedPreview);
    expect(previewText({ lastMessage: { authorUserId: 'usr_2', state: 'Removed' } }, 'usr_1')).toBe(t.removedPreview);
    expect(previewText({ lastMessage: null }, 'usr_1')).toBe(t.noMessagesYet);
  });

  test('tombstones, edits, and the original text', () => {
    expect(displayBody({ state: 'Deleted', body: '' })).toBe(t.deleted);
    expect(displayBody({ state: 'Removed', body: 'x' })).toBe(t.removed);
    expect(displayBody({ state: 'Visible', body: 'Hello' })).toBe('Hello');
    const edited = { state: 'Visible', body: 'New', isEdited: true, originalBody: 'Old' };
    expect(isEdited(edited)).toBe(true);
    expect(hasOriginal(edited)).toBe(true);
    expect(isEdited({ ...edited, state: 'Deleted' })).toBe(false);
    expect(hasOriginal({ state: 'Visible', editedUtc: '2026-11-12T00:00:00Z', originalBody: null })).toBe(false);
  });

  test('receipts: sending, failed, read, delivered, sent', () => {
    expect(receiptState({ pending: true, sendState: 'Sending' })).toBe('Sending');
    expect(receiptState({ pending: true, sendState: 'Failed' })).toBe('Failed');
    expect(receiptState({ seq: 4 }, { otherLastReadSeq: 4 })).toBe('Read');
    expect(receiptState({ seq: 5 }, { otherLastReadSeq: 4, otherLastDeliveredSeq: 5 })).toBe('Delivered');
    expect(receiptState({ seq: 5 }, { otherLastReadSeq: null })).toBe('Sent');
    expect(['Sending', 'Failed', 'Read', 'Delivered', 'Sent'].map(receiptText)).toEqual([t.sending, t.failed, t.read, t.delivered, t.sent]);
  });

  test('sequence helpers and day separators in the inverted list', () => {
    const list = [
      { id: 'm1', seq: 1, author: alex, createdUtc: '2026-11-10T10:00:00Z' },
      { id: 'm2', seq: 2, author: { id: 'usr_1' }, createdUtc: '2026-11-12T10:00:00Z' },
      { id: 'm3', seq: 3, author: alex, createdUtc: '2026-11-12T11:00:00Z' },
      { localId: 'local:x', pending: true, author: { id: 'usr_1' }, createdUtc: '2026-11-12T12:00:00Z' }
    ];
    expect(lastOwnSeq(list, 'usr_1')).toBe(2);
    expect(newestIncomingSeq(list, 'usr_1')).toBe(3);
    const rows = messageRows(list, new Date('2026-11-12T13:00:00Z'));
    expect(rows.map((r) => r.type)).toEqual(['message', 'message', 'message', 'day', 'message', 'day']);
    expect(rows[0].key).toBe('local:x');
    expect(rows[3].label).toBe(t.today);
    expect(dayLabel('2026-11-11T10:00:00Z', new Date('2026-11-12T13:00:00Z'))).toBe(t.yesterday);
  });

  test('errors, cannot-send reasons, and the badge count', () => {
    expect(describeMessagingError({ code: 'RequestLimitReached' })).toBe(t.errors.RequestLimitReached);
    expect(describeMessagingError({ code: 'NotFound', status: 404 })).toBe(strings.errors.notFound);
    expect(cannotSendText({ viewer: { canSend: true } })).toBeNull();
    expect(cannotSendText({ viewer: { canSend: false, cannotSendReason: 'Blocked' } })).toBe(t.cannotSend.Blocked);
    expect(cannotSendText({ viewer: { canSend: false, cannotSendReason: null } })).toBe(t.cannotSend.Unavailable);
    expect(messagesBadgeCount({ conversations: 3, requests: 1 })).toBe(4);
    expect(messagesBadgeCount(null)).toBe(0);
  });
});

describe('push and deep links', () => {
  afterEach(() => setActiveConversationId(null));

  test('Phase 5 notification paths open their screens', () => {
    for (const p of ['/messages', '/messages/requests', '/messages/cnv_1', '/support', '/support/tickets/tkt_9', '/ideas', '/ideas/ida_3', '/announcements', '/announcements/bct_2']) expect(resolveNotificationPath(p)).toBe(p);
    expect(resolveNotificationPath('/messages/usr_1')).toBeNull();
    expect(pushTarget({ notification: { request: { content: { data: { type: 'DirectMessage', conversationId: 'cnv_1', seq: 4, path: '/messages/cnv_1' } } } } })).toEqual({ notificationId: null, path: '/messages/cnv_1' });
  });

  test('a DirectMessage push for the open conversation is not shown in the foreground', () => {
    const listener = jest.fn();
    const off = onActiveConversationChange(listener);
    setActiveConversationId('cnv_1');
    expect(getActiveConversationId()).toBe('cnv_1');
    expect(listener).toHaveBeenCalledWith('cnv_1');
    off();
    expect(shouldSuppressPush({ type: 'DirectMessage', conversationId: 'cnv_1' }, { active: true })).toBe(true);
    expect(shouldSuppressPush({ type: 'DirectMessage', conversationId: 'cnv_2' }, { active: true })).toBe(false);
    expect(shouldSuppressPush({ type: 'DirectMessage', conversationId: 'cnv_1' }, { active: false })).toBe(false);
    expect(shouldSuppressPush({ type: 'Reaction', conversationId: 'cnv_1' }, { active: true })).toBe(false);
    const push = (data) => ({ request: { content: { data } } });
    expect(foregroundPresentation(push({ type: 'Reaction' })).shouldShowBanner).toBe(true);
    expect(foregroundPresentation(push({ type: 'DirectMessage', conversationId: 'cnv_2' })).shouldShowBanner).toBe(true);
  });

  test('the realtime URL is on the API origin', () => {
    expect(realtimeUrl('https://api.example.test')).toBe('wss://api.example.test/api/v1/ws');
    expect(realtimeUrl('http://127.0.0.1:8000/')).toBe('ws://127.0.0.1:8000/api/v1/ws');
    expect(realtimeUrl('')).toBeNull();
  });

  test('notification categories include Messages, Ideas, and Announcements with labels', () => {
    expect(CategoryOrder).toEqual(expect.arrayContaining(['Messages', 'Ideas', 'Announcements']));
    const ordered = orderedCategories({ categories: [{ category: 'Account' }, { category: 'Announcements' }, { category: 'Messages' }, { category: 'Ideas' }] });
    expect(ordered.map((c) => c.category)).toEqual(['Messages', 'Ideas', 'Announcements', 'Account']);
    for (const key of ['Messages', 'Ideas', 'Announcements']) expect(strings.notificationSettings.categories[key].label).toBeTruthy();
  });
});

describe('support and ideas helpers', () => {
  test('device diagnostics, the support email, and form checks', () => {
    expect(deviceDiagnostics({ os: 'ios', version: '18.1', model: 'iPhone 16' })).toEqual({ platform: 'Ios', osVersion: '18.1', deviceModel: 'iPhone 16' });
    expect(deviceDiagnostics({ os: 'android', version: 35, model: null })).toEqual({ platform: 'Android', osVersion: '35', deviceModel: null });
    expect(supportEmailOf(property)).toBe(property.contact.supportEmail);
    expect(supportEnabled({ features: { support: false } })).toBe(false);
    expect(supportEnabled({ features: {} })).toBe(true);
    expect(ticketProblems({ type: null, title: 'Hi', body: 'short' })).toEqual({ type: 'typeRequired', title: 'subjectTooShort', body: 'bodyTooShort' });
    expect(ticketProblems({ type: 'Billing', title: 'Charged twice', body: 'I was charged twice this month.' })).toEqual({});
  });

  test('idea statuses, submissions, filters, and optimistic votes', () => {
    expect(configuredStatuses({}).map((s) => s.key)).toEqual(['UnderReview', 'Planned', 'InProgress', 'Shipped', 'Declined']);
    expect(configuredStatuses({ ideas: { statuses: [{ key: 'Open', label: 'Open' }] } })).toEqual([{ key: 'Open', label: 'Open', color: null, isClosed: false }]);
    expect(membersSubmitIdeas({ ideas: { memberSubmissions: 'Direct' } })).toBe(true);
    expect(membersSubmitIdeas({})).toBe(false);
    expect(ideaCollectionFilters({})).toBeNull();
    expect(ideaCollectionFilters({ statuses: ['Planned'], openOnly: true, votedByMe: true })).toEqual({ status: ['Planned'], open: true, votedByMe: true });
    expect(withVote({ hasVoted: false, voteCount: 2 }, true)).toEqual({ hasVoted: true, voteCount: 3 });
    expect(withVote({ hasVoted: true, voteCount: 0 }, false)).toEqual({ hasVoted: false, voteCount: 0 });
  });
});

describe('MobileApiClient Phase 5 methods', () => {
  test('mixes in messaging, support, ideas, and announcements without touching the assistant chat names', async () => {
    const fake = createMessagingFake();
    fake.addConversation();
    const api = new MobileApiClient({ baseUrl: 'http://x', appVersion: '1', tokenStore: { getRefreshToken: async () => null, setRefreshToken: async () => {}, clear: async () => {} }, fetchImpl: fake.fetchImpl });
    for (const name of ['enumerateDirectConversations', 'sendMessage', 'createTicket', 'enumerateIdeas', 'enumerateAnnouncements', 'getUnreadCount']) expect(typeof api[name]).toBe('function');
    expect(api.messaging).toBe(api.messaging);
    const page = await api.messaging.enumerateConversations({ maxResults: 5 });
    expect(page.objects).toHaveLength(1);
    expect(fake.calls[0].path).toBe('/conversations/enumerate');
    expect(fake.calls[0].headers['x-client']).toBe('mobile');
  });
});
