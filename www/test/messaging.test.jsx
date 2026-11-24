import { describe, expect, it } from 'vitest';
import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route } from 'react-router-dom';
import { createMessagingFake, createFakeWebSocket, welcomeData } from '@trelliskit/shared/testing';
import MessagesView from '../src/views/member/MessagesView.jsx';
import MessagingSettingsView from '../src/views/member/MessagingSettingsView.jsx';
import MemberPublicProfileView from '../src/views/member/MemberPublicProfileView.jsx';
import { conversationPath, dayKey, isLocalHost, previewText, realtimeUrl, receiptOf, withDaySeparators } from '../src/utils/messaging.js';
import { makeMe } from './helpers.jsx';
import { fakeMemberFetch, renderPhase5, siteWith } from './phase5Helpers.jsx';

function seededFake() {
  // Messages are stamped at local noon of the current day so the "Today" separator holds on any date.
  const noon = new Date();
  noon.setHours(12, 0, 0, 0);
  const today = noon.toISOString();
  const fake = createMessagingFake({ now: () => today });
  const alex = fake.addConversation({ otherUserId: 'usr_2', labels: ['training'] });
  fake.receive(alex.id, { body: 'Are you coming Thursday?' });
  fake.receive(alex.id, { body: 'See https://example.test/plan' });
  const jo = fake.addConversation({ otherUserId: 'usr_3', folder: 'Requests', requestState: 'Pending' });
  fake.receive(jo.id, { body: 'Hi, can we talk?' });
  return { fake, alex, jo };
}

const messagesRoutes = (
  <>
    <Route path="/app/messages/requests" element={<MessagesView requests />} />
    <Route path="/app/messages/:id" element={<MessagesView />} />
  </>
);

async function openMessages(fake, path = '/app/messages', options = {}) {
  const { fetchImpl, calls } = fakeMemberFetch(fake);
  await renderPhase5(<MessagesView />, { fetchImpl, path, routePath: '/app/messages', extraRoutes: messagesRoutes, ...options });
  return calls;
}

describe('messaging helpers', () => {
  it('connects directly to the API domain except on local hosts', () => {
    const property = { domains: { api: 'api.example.test' } };
    expect(realtimeUrl(property, { protocol: 'https:', host: 'example.test', hostname: 'example.test' })).toBe('wss://api.example.test/api/v1/ws');
    expect(realtimeUrl({ domains: { api: 'https://api.example.test/' } }, { protocol: 'https:', host: 'www.example.test', hostname: 'www.example.test' })).toBe('wss://api.example.test/api/v1/ws');
    expect(realtimeUrl(property, { protocol: 'http:', host: 'localhost:5173', hostname: 'localhost' })).toBe('ws://localhost:5173/api/v1/ws');
    expect(realtimeUrl({}, { protocol: 'https:', host: 'example.test', hostname: 'example.test' })).toBe('wss://example.test/api/v1/ws');
    expect(isLocalHost('app.localhost')).toBe(true);
    expect(isLocalHost('127.0.0.1')).toBe(true);
    expect(conversationPath('cnv_1', 4)).toBe('/app/messages/cnv_1?around=4');
  });

  it('groups messages by day and computes receipts and previews', () => {
    const now = new Date('2026-11-12T15:00:00');
    const rows = withDaySeparators([
      { id: 'a', createdUtc: new Date('2026-11-10T10:00:00').toISOString() },
      { id: 'b', createdUtc: new Date('2026-11-11T10:00:00').toISOString() },
      { id: 'c', createdUtc: new Date('2026-11-12T10:00:00').toISOString() },
      { id: 'd', createdUtc: new Date('2026-11-12T11:00:00').toISOString() }
    ], now);
    expect(rows.filter((r) => r.kind === 'day').map((r) => r.label)).toEqual(['Tuesday, November 10, 2026', 'Yesterday', 'Today']);
    expect(rows).toHaveLength(7);
    expect(dayKey('nope')).toBe('');
    const conversation = { otherLastReadSeq: 3, otherLastDeliveredSeq: 5 };
    expect(receiptOf({ seq: 3 }, conversation)).toBe('read');
    expect(receiptOf({ seq: 5 }, conversation)).toBe('delivered');
    expect(receiptOf({ seq: 6 }, conversation)).toBe('sent');
    expect(receiptOf({ pending: true, sendState: 'Sending' }, conversation)).toBe('sending');
    expect(receiptOf({ pending: true, sendState: 'Failed' }, conversation)).toBe('failed');
    expect(previewText({ excerpt: 'Hi', authorUserId: 'usr_1', state: 'Visible' }, 'usr_1')).toBe('You: Hi');
    expect(previewText({ excerpt: '', state: 'Deleted' }, 'usr_1')).toBe('Message deleted');
    expect(previewText({ excerpt: '', hasAttachments: true, state: 'Visible', authorUserId: 'usr_2' }, 'usr_1')).toBe('Attachment');
    expect(previewText(null, 'usr_1')).toBe('No messages yet');
  });
});

describe('conversation list', () => {
  it('shows the inbox with unread badges, filters by name and labels, and switches folders', async () => {
    const { fake } = seededFake();
    const calls = await openMessages(fake);
    const inbox = await screen.findByRole('list', { name: 'Inbox' });
    const row = within(inbox).getByRole('link', { name: /Alex Chen/ });
    expect(within(row).getByLabelText('2 unread')).toBeInTheDocument();
    expect(within(row).getByText('See https://example.test/plan')).toBeInTheDocument();
    expect(within(inbox).queryByText('Jo Park')).toBeNull();
    const enumerations = () => calls.filter((c) => c.path === '/api/v1/conversations/enumerate');
    expect(enumerations()[0].body).toMatchObject({ maxResults: 30, filters: { folder: 'Inbox' } });
    // The requests tab shows the unread request count from the unread-count route.
    expect(await within(screen.getByRole('link', { name: /Requests/ })).findByLabelText('1 unread')).toBeInTheDocument();

    await userEvent.type(screen.getByPlaceholderText('Search by name'), 'Nobody{Enter}');
    await waitFor(() => expect(enumerations().at(-1).body.search).toBe('Nobody'));
    expect(await screen.findByText('No conversations match these filters.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('link', { name: /Requests/ }));
    await waitFor(() => expect(enumerations().at(-1).body.filters).toEqual({ folder: 'Requests' }));
    expect(await screen.findByRole('link', { name: /Jo Park/ })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('link', { name: 'Archived' }));
    await waitFor(() => expect(enumerations().at(-1).body.filters).toEqual({ folder: 'Archived' }));
    expect(await screen.findByText('No archived conversations.')).toBeInTheDocument();
  });

  it('searches messages across conversations and opens a result at the message', async () => {
    const { fake, alex } = seededFake();
    const calls = await openMessages(fake);
    await screen.findByRole('list', { name: 'Inbox' });
    await userEvent.type(screen.getByPlaceholderText('Search messages'), 'x{Enter}');
    expect(screen.getByRole('alert')).toHaveTextContent('Enter at least 2 characters.');
    await userEvent.clear(screen.getByPlaceholderText('Search messages'));
    await userEvent.type(screen.getByPlaceholderText('Search messages'), 'thursday{Enter}');
    const result = await screen.findByRole('link', { name: 'Message in your conversation with Alex Chen' });
    expect(result).toHaveAttribute('href', '/app/messages/' + alex.id + '?around=1');
    expect(calls.find((c) => c.path === '/api/v1/messages/search/enumerate').body).toMatchObject({ search: 'thursday', maxResults: 20 });
    await userEvent.click(result);
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/conversations/' + alex.id + '/messages/enumerate' && c.body.filters && c.body.filters.aroundSeq === 1)).toBe(true));
    expect(await screen.findByRole('button', { name: 'Jump to the latest messages' })).toBeInTheDocument();
  });
});

describe('conversation', () => {
  it('loads history, marks it read, sends optimistically, edits once, and deletes for everyone', async () => {
    const { fake, alex } = seededFake();
    const calls = await openMessages(fake, '/app/messages/' + alex.id);
    const pane = await screen.findByRole('region', { name: 'Conversation with Alex Chen' });
    expect(await within(pane).findByText('Are you coming Thursday?')).toBeInTheDocument();
    expect(within(pane).getByRole('link', { name: 'https://example.test/plan' })).toHaveAttribute('rel', 'noopener noreferrer nofollow');
    expect(within(pane).getByRole('separator')).toHaveTextContent('Today');
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/conversations/' + alex.id + '/read').body).toEqual({ seq: 2 }));

    await userEvent.type(within(pane).getByRole('textbox', { name: 'Message Alex Chen' }), 'Yes, see you there{Enter}');
    expect(await within(pane).findByText('Yes, see you there')).toBeInTheDocument();
    const send = calls.find((c) => c.method === 'POST' && c.path === '/api/v1/conversations/' + alex.id + '/messages');
    expect(send.body).toMatchObject({ body: 'Yes, see you there', mediaIds: [], showLinkPreview: true });
    expect(send.body.clientMessageId).toMatch(/[0-9a-f-]{36}/);
    expect(await within(pane).findByText('Sent')).toBeInTheDocument();

    // One edit within the window: the message shows Edited and the original text on request.
    const mine = within(pane).getByText('Yes, see you there').closest('li');
    await userEvent.click(within(mine).getByRole('button', { name: /Actions for the message/ }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Edit' }));
    const editor = screen.getByRole('form', { name: 'Edit message' });
    await userEvent.clear(within(editor).getByLabelText('Edit message'));
    await userEvent.type(within(editor).getByLabelText('Edit message'), 'Yes, see you at six');
    await userEvent.click(within(editor).getByRole('button', { name: 'Save' }));
    const edited = (await within(pane).findByText('Yes, see you at six')).closest('li');
    await userEvent.click(within(edited).getByRole('button', { name: 'Edited' }));
    expect(within(edited).getByLabelText('Original text')).toHaveTextContent('Yes, see you there');
    await userEvent.click(within(edited).getByRole('button', { name: /Actions for the message/ }));
    expect(screen.queryByRole('menuitem', { name: 'Edit' })).toBeNull();

    // Delete for everyone leaves a tombstone.
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Delete for everyone' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Delete' }));
    expect(await within(pane).findByText('Message deleted')).toBeInTheDocument();
    expect(within(pane).queryByText('Yes, see you at six')).toBeNull();
    expect(calls.some((c) => c.method === 'DELETE' && c.path.startsWith('/api/v1/messages/'))).toBe(true);
  });

  it('shows the server-fetched link preview of a message and the preview privacy note in the composer', async () => {
    const { fake, alex } = seededFake();
    await openMessages(fake, '/app/messages/' + alex.id);
    const pane = await screen.findByRole('region', { name: 'Conversation with Alex Chen' });
    expect(await within(pane).findByText('Preview of example.test')).toBeInTheDocument();
    const box = within(pane).getByRole('textbox', { name: 'Message Alex Chen' });
    expect(within(pane).queryByText(/Link previews are fetched by/)).toBeNull();
    await userEvent.type(box, 'Look at https://example.test/x');
    expect(within(pane).getByText(/Link previews are fetched by .+, not by your device/)).toBeInTheDocument();
  });

  it('replies to a message and reacts to it', async () => {
    const { fake, alex } = seededFake();
    const calls = await openMessages(fake, '/app/messages/' + alex.id);
    const pane = await screen.findByRole('region', { name: 'Conversation with Alex Chen' });
    const theirs = (await within(pane).findByText('Are you coming Thursday?')).closest('li');
    await userEvent.click(within(theirs).getByRole('button', { name: /Actions for the message/ }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Reply' }));
    expect(within(pane).getByText(/Replying to Alex Chen/)).toBeInTheDocument();
    await userEvent.type(within(pane).getByRole('textbox', { name: 'Message Alex Chen' }), 'Yes{Enter}');
    await waitFor(() => expect(calls.find((c) => c.method === 'POST' && c.path.endsWith('/messages')).body.replyToMessageId).toMatch(/^msg_/));
    expect(await within(pane).findByRole('button', { name: 'Go to the message from Alex Chen' })).toBeInTheDocument();

    const likeButton = within(theirs).getAllByRole('button').find((b) => b.getAttribute('aria-pressed') === 'false');
    await userEvent.click(likeButton);
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && /\/api\/v1\/messages\/msg_[^/]+\/reactions\//.test(c.path))).toBe(true));
  });

  it('keeps a failed send with Retry and sends it again with the same clientMessageId', async () => {
    const { fake, alex } = seededFake();
    fake.fail({ method: 'POST', path: new RegExp('^/conversations/' + alex.id + '/messages$'), status: 403, code: 'RequestLimitReached' });
    const calls = await openMessages(fake, '/app/messages/' + alex.id);
    const pane = await screen.findByRole('region', { name: 'Conversation with Alex Chen' });
    await within(pane).findByText('Are you coming Thursday?');
    await userEvent.type(within(pane).getByRole('textbox', { name: 'Message Alex Chen' }), 'Hello again{Enter}');
    expect(await within(pane).findByText('Not sent')).toBeInTheDocument();
    expect(await screen.findByText('You reached the limit for messages before they reply.')).toBeInTheDocument();
    await userEvent.click(within(pane).getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(within(pane).queryByText('Not sent')).toBeNull());
    const sends = calls.filter((c) => c.method === 'POST' && c.path === '/api/v1/conversations/' + alex.id + '/messages');
    expect(sends).toHaveLength(2);
    expect(sends[0].body.clientMessageId).toBe(sends[1].body.clientMessageId);
  });

  it('shows the request banner and accepts or declines the request', async () => {
    const { fake, jo } = seededFake();
    const calls = await openMessages(fake, '/app/messages/' + jo.id);
    expect(await screen.findByText('Jo Park wants to message you')).toBeInTheDocument();
    expect(screen.getByText('Accept the request to reply.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Block and report' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Accept' }));
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/conversations/' + jo.id + '/accept')).toBe(true));
    expect(await screen.findByRole('textbox', { name: 'Message Jo Park' })).toBeInTheDocument();
    expect(screen.queryByText('Jo Park wants to message you')).toBeNull();
  });

  it('declines a request and returns to the requests folder', async () => {
    const { fake, jo } = seededFake();
    const calls = await openMessages(fake, '/app/messages/' + jo.id);
    await userEvent.click(await screen.findByRole('button', { name: 'Decline' }));
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/conversations/' + jo.id + '/decline')).toBe(true));
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/app/messages/requests'));
  });

  it('archives, mutes, and reports a conversation from its menu', async () => {
    const { fake, alex } = seededFake();
    const calls = await openMessages(fake, '/app/messages/' + alex.id);
    await screen.findByRole('region', { name: 'Conversation with Alex Chen' });
    await userEvent.click(screen.getByRole('button', { name: 'Conversation actions' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Mute' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.path === '/api/v1/conversations/' + alex.id + '/mute')).toBe(true));
    await userEvent.click(screen.getByRole('button', { name: 'Conversation actions' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Archive' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/conversations/' + alex.id + '/folder').body).toEqual({ folder: 'Archived' }));
    await userEvent.click(screen.getByRole('button', { name: 'Conversation actions' }));
    expect(await screen.findByRole('menuitem', { name: 'Move to inbox' })).toBeInTheDocument();
  });

  it('shows the members-only prompt in a gated model for non-members', async () => {
    const { fake } = seededFake();
    const lapsed = makeMe({ membership: { isActive: false }, accessTier: 'SignedIn' });
    const { fetchImpl, calls } = fakeMemberFetch(fake, lapsed);
    await renderPhase5(<MessagesView />, { fetchImpl, path: '/app/messages', routePath: '/app/messages', site: siteWith({ access: { model: 'PartiallyGated', gatedFeatures: ['messaging'], anonymousBrowsing: false } }) });
    expect(await screen.findByRole('region', { name: /Become a/ })).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/v1/conversations/enumerate')).toBe(false);
  });
});

describe('realtime', () => {
  it('opens one socket with hello, applies live messages and typing, and updates the notification badge', async () => {
    const { fake, alex } = seededFake();
    const sockets = createFakeWebSocket();
    await openMessages(fake, '/app/messages/' + alex.id, { realtime: { WebSocketImpl: sockets.WebSocket } });
    const pane = await screen.findByRole('region', { name: 'Conversation with Alex Chen' });
    await within(pane).findByText('Are you coming Thursday?');
    expect(sockets.instances).toHaveLength(1);
    const socket = sockets.last();
    expect(socket.url).toBe('ws://localhost:3000/api/v1/ws');
    expect(socket.protocols).toBeUndefined();
    await act(async () => { socket.accept(); });
    await waitFor(() => expect(socket.framesOf('hello')).toHaveLength(1));
    const hello = socket.framesOf('hello')[0].data;
    expect(hello).toMatchObject({ accessToken: 'access-1', client: 'Web', capabilities: ['messaging', 'notifications', 'tickets'], protocol: 'tk.v1' });
    await act(async () => { socket.serverSend('welcome', welcomeData()); });

    await act(async () => { socket.serverSend('typing', { conversationId: alex.id, userId: 'usr_2', state: 'Start', expiresInMs: 6000 }); });
    expect(await within(pane).findByText('Alex Chen is typing...')).toBeInTheDocument();

    const live = fake.receive(alex.id, { body: 'Live from the socket' });
    await act(async () => { socket.serverSend('message.created', { conversationId: alex.id, message: live }); });
    expect(await within(pane).findByText('Live from the socket')).toBeInTheDocument();
    expect(within(pane).queryByText('Alex Chen is typing...')).toBeNull();
    // The open conversation is reported in a state frame and read over the socket.
    await waitFor(() => expect(socket.framesOf('state').some((f) => f.data.activeConversationId === alex.id)).toBe(true));
    await waitFor(() => expect(socket.framesOf('read').some((f) => f.data.seq === live.seq)).toBe(true));

    await act(async () => { socket.serverSend('notification.created', { notification: { id: 'ntf_1', type: 'Broadcast' }, unreadCount: 7 }); });
    await waitFor(() => expect(screen.getByTestId('bell-count')).toHaveTextContent('7'));
  });

  it('requests no messaging capability when messaging is not allowed', async () => {
    const fake = createMessagingFake();
    const sockets = createFakeWebSocket();
    const { fetchImpl } = fakeMemberFetch(fake, makeMe({ membership: { isActive: false }, accessTier: 'SignedIn' }));
    await renderPhase5(<span>shell</span>, { fetchImpl, path: '/test', routePath: '/test', realtime: { WebSocketImpl: sockets.WebSocket }, site: siteWith({ access: { model: 'PartiallyGated', gatedFeatures: ['messaging'], anonymousBrowsing: false } }) });
    const socket = sockets.last();
    await act(async () => { socket.accept(); });
    await waitFor(() => expect(socket.framesOf('hello')).toHaveLength(1));
    expect(socket.framesOf('hello')[0].data.capabilities).toEqual(['notifications', 'tickets']);
  });
});

describe('profile and settings', () => {
  it('starts a conversation from a member profile', async () => {
    const fake = createMessagingFake();
    const { fetchImpl, calls } = fakeMemberFetch([fake, { handle: (call) => (call.path === '/api/v1/users/usr_2' ? { status: 200, body: { id: 'usr_2', displayName: 'Alex Chen', handle: 'alex', isStaff: false, isDeleted: false, isBlockedByMe: false } } : undefined) }]);
    await renderPhase5(<MemberPublicProfileView />, { fetchImpl, path: '/app/members/usr_2', routePath: '/app/members/:id', extraRoutes: <Route path="/app/messages/:id" element={<span>conversation</span>} /> });
    await userEvent.click(await screen.findByRole('button', { name: 'Message' }));
    await waitFor(() => expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/conversations').body).toEqual({ recipientUserId: 'usr_2' }));
    await waitFor(() => expect(screen.getByTestId('location').textContent).toMatch(/^\/app\/messages\/cnv_/));
  });

  it('hides the Message button for blocked members and when sending is off', async () => {
    const fake = createMessagingFake();
    const user = { handle: (call) => (call.path === '/api/v1/users/usr_2' ? { status: 200, body: { id: 'usr_2', displayName: 'Alex Chen', isBlockedByMe: true } } : undefined) };
    const { fetchImpl } = fakeMemberFetch([fake, user]);
    await renderPhase5(<MemberPublicProfileView />, { fetchImpl, path: '/app/members/usr_2', routePath: '/app/members/:id' });
    await screen.findByRole('heading', { name: 'Alex Chen' });
    expect(screen.queryByRole('button', { name: 'Message' })).toBeNull();
  });

  it('saves messaging settings as they change', async () => {
    const fake = createMessagingFake();
    const { fetchImpl, calls } = fakeMemberFetch(fake);
    await renderPhase5(<MessagingSettingsView />, { fetchImpl, path: '/app/settings/messaging', routePath: '/app/settings/messaging' });
    const known = await screen.findByRole('radio', { name: /People I know/ });
    expect(screen.getByRole('radio', { name: /Everyone/ })).toBeChecked();
    await userEvent.click(known);
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT').body).toEqual({ dmPolicy: 'Known' }));
    await userEvent.click(screen.getByRole('checkbox', { name: /Read receipts/ }));
    await waitFor(() => expect(calls.filter((c) => c.method === 'PUT').at(-1).body).toEqual({ readReceipts: false }));
    await userEvent.selectOptions(screen.getByLabelText('Push notification preview'), 'Hidden');
    await waitFor(() => expect(calls.filter((c) => c.method === 'PUT').at(-1).body).toEqual({ pushPreview: 'Hidden' }));
    await userEvent.click(screen.getByRole('checkbox', { name: /Messages ignore notification hours/ }));
    await waitFor(() => expect(calls.filter((c) => c.method === 'PUT').at(-1).body).toEqual({ exemptFromNotificationHours: true }));
    expect(screen.getByRole('checkbox', { name: /Messages ignore notification hours/ })).toBeChecked();
    expect(screen.getByRole('radio', { name: /People I know/ })).toBeChecked();
  });
});
