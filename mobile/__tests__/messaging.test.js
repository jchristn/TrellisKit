import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react-native';
import { createMessagingFake, createFakeWebSocket, welcomeData } from '@trelliskit/shared/testing';
import * as Haptics from 'expo-haptics';
import { router as appRouter } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { AppState, Keyboard } from 'react-native';
import { newestMessageKey } from '../src/components/messaging/ConversationView';
import MessageBubble from '../src/components/messaging/MessageBubble';
import { setActiveConversationId } from '../src/messaging/activeConversation';
import { setRealtimeEnabled, setRealtimeTimers, setWebSocketImpl } from '../src/messaging/realtimeConfig';
import strings from '../src/strings';
import { makeUser, resetEnvironment } from '../test-utils/fakeServer';
import { atWidth, me, phase5Property, serveFakes } from '../test-utils/phase5';
import { likeKey } from '../test-utils/community';

const APP_DIR = path.resolve(__dirname, '../app');
const t = strings.messaging;

function seeded() {
  const fake = createMessagingFake({ me });
  const inbox = fake.addConversation({ otherUserId: 'usr_2', labels: ['friends'] });
  fake.receive(inbox.id, { body: 'Are you coming Thursday?' });
  const request = fake.addConversation({ otherUserId: 'usr_3', folder: 'Requests', requestState: 'Pending' });
  fake.receive(request.id, { body: 'Hi, we have not met' });
  return { fake, inbox, request };
}

function lastBody(fake, pathname) {
  const calls = fake.calls.filter((c) => c.path === pathname);
  return calls.length ? calls[calls.length - 1].body : null;
}

beforeEach(() => {
  resetEnvironment();
  setActiveConversationId(null);
});
afterEach(() => {
  delete global.fetch;
  jest.restoreAllMocks();
});

/** The Messages entry that carries the badge: the bottom shortcut when configured, else the header button. */
async function messagesEntry() {
  let entry = null;
  await waitFor(() => {
    entry = screen.queryByTestId('tab-messages-badge', { includeHiddenElements: true }) ? 'tab-messages'
      : screen.queryByTestId('header-messages-badge', { includeHiddenElements: true }) ? 'header-messages' : null;
    expect(entry).not.toBeNull();
  });
  return entry;
}

describe('conversation list', () => {
  test('shows the inbox with previews and unread badges, the requests folder, and the header badge', async () => {
    const { fake, inbox, request } = seeded();
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/messages' });
    expect(await screen.findByTestId('conversation-' + inbox.id)).toBeOnTheScreen();
    expect(screen.getByText(strings.messages.summary)).toBeOnTheScreen();
    expect(screen.getByTestId('conversation-' + inbox.id + '-preview')).toHaveTextContent('Are you coming Thursday?');
    expect(screen.getByTestId('conversation-' + inbox.id + '-unread')).toHaveTextContent('1');
    expect(screen.queryByTestId('conversation-' + request.id)).toBeNull();
    expect(lastBody(fake, '/conversations/enumerate')).toMatchObject({ maxResults: 25, filters: { folder: 'Inbox' } });

    await fireEvent.press(screen.getByTestId('messages-folder-Requests'));
    expect(await screen.findByTestId('conversation-' + request.id)).toBeOnTheScreen();
    expect(screen.getByTestId('conversation-' + request.id + '-marker')).toHaveTextContent(t.requestLabel);
    expect(lastBody(fake, '/conversations/enumerate').filters).toEqual({ folder: 'Requests' });
  });

  test('the Messages button carries unread conversations plus requests and opens the list', async () => {
    const { fake } = seeded();
    serveFakes([fake], { routes: { 'POST /feed/enumerate': { objects: [], continuationToken: null, endOfResults: true } } });
    const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
    // Messages lives in the header unless the property makes it a bottom shortcut, which then
    // carries the badge alone (the header does not repeat it).
    const entry = await messagesEntry();
    expect(screen.getByTestId(entry + '-badge', { includeHiddenElements: true })).toHaveTextContent('2');
    if (entry === 'tab-messages') {
      expect(screen.getByTestId('tab-messages')).toHaveProp('accessibilityLabel', strings.navigation.unread(strings.messaging.title, '2'));
      expect(screen.queryByTestId('header-messages')).toBeNull();
    } else expect(screen.getByTestId('header-messages')).toHaveProp('accessibilityLabel', strings.header.messagesUnread('2'));
    await fireEvent.press(screen.getByTestId(entry));
    await waitFor(() => expect(router.getPathname()).toBe('/messages'));
  });

  test('label and tag filters and the name search go into the enumeration query', async () => {
    const { fake } = seeded();
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/messages' });
    await screen.findByTestId('messages-list');
    await fireEvent.press(screen.getByTestId('messages-filters-button'));
    await fireEvent.changeText(await screen.findByTestId('messages-filters-panel-search'), 'Alex');
    await fireEvent.press(screen.getByTestId('messages-filters-panel-apply'));
    await waitFor(() => expect(lastBody(fake, '/conversations/enumerate')).toMatchObject({ search: 'Alex', filters: { folder: 'Inbox' } }));
    expect(await screen.findByTestId('messages-filters-chip-search')).toBeOnTheScreen();
  });

  test('the requests path opens the Requests folder', async () => {
    const { fake, request } = seeded();
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/messages/requests' });
    expect(await screen.findByTestId('conversation-' + request.id)).toBeOnTheScreen();
  });
});

describe('a conversation', () => {
  test('sends optimistically with haptics, shows the receipt, and marks read', async () => {
    const { fake, inbox } = seeded();
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/messages/' + inbox.id });
    expect(await screen.findByText('Are you coming Thursday?')).toBeOnTheScreen();
    expect(screen.getByTestId('conversation-name')).toHaveTextContent('Alex Chen');
    await waitFor(() => expect(fake.calls.some((c) => c.path === '/conversations/' + inbox.id + '/read' && c.body.seq === 1)).toBe(true));

    await fireEvent.changeText(screen.getByTestId('message-composer-body'), 'See you there');
    await fireEvent.press(screen.getByTestId('message-composer-send'));
    expect(await screen.findByText('See you there')).toBeOnTheScreen();
    const send = fake.calls.find((c) => c.method === 'POST' && c.path === '/conversations/' + inbox.id + '/messages');
    expect(send.body).toMatchObject({ body: 'See you there', mediaIds: [], showLinkPreview: true });
    expect(send.body.clientMessageId).toBeTruthy();
    await waitFor(() => expect(Haptics.impactAsync).toHaveBeenCalled());
    const sent = fake.state.messages.find((m) => m.body === 'See you there');
    expect(await screen.findByTestId('message-' + sent.id + '-receipt')).toHaveTextContent(t.sent);
    expect(screen.getByTestId('message-composer-body').props.value).toBe('');
  });

  test('sending dismisses the keyboard', async () => {
    const dismiss = jest.spyOn(Keyboard, 'dismiss');
    try {
      const { fake, inbox } = seeded();
      serveFakes([fake]);
      await renderRouter(APP_DIR, { initialUrl: '/messages/' + inbox.id });
      await screen.findByText('Are you coming Thursday?');
      await fireEvent.changeText(screen.getByTestId('message-composer-body'), 'On my way');
      await fireEvent.press(screen.getByTestId('message-composer-send'));
      expect(await screen.findByText('On my way')).toBeOnTheScreen();
      expect(dismiss).toHaveBeenCalled();
    } finally {
      dismiss.mockRestore();
    }
  });

  test('the newest message key prefers the client id of an optimistic send', () => {
    expect(newestMessageKey([])).toBeNull();
    expect(newestMessageKey([{ id: 'msg_1' }, { id: 'msg_2' }])).toBe('msg_2');
    expect(newestMessageKey([{ id: 'msg_1' }, { clientMessageId: 'c_1', localId: 'l_1' }])).toBe('c_1');
  });

  test('a rejected send stays with Retry, and Retry sends the same clientMessageId', async () => {
    const { fake, inbox } = seeded();
    fake.fail({ method: 'POST', path: /\/messages$/, status: 403, code: 'RequestLimitReached' });
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/messages/' + inbox.id });
    await screen.findByText('Are you coming Thursday?');
    await fireEvent.changeText(screen.getByTestId('message-composer-body'), 'Third try');
    await fireEvent.press(screen.getByTestId('message-composer-send'));
    expect(await screen.findByText(t.failed)).toBeOnTheScreen();
    expect(await screen.findByTestId('conversation-action-error')).toHaveTextContent(t.errors.RequestLimitReached);
    const retry = screen.getByText(t.retry);
    await fireEvent.press(retry);
    await waitFor(() => expect(fake.state.messages.some((m) => m.body === 'Third try')).toBe(true));
    const sends = fake.calls.filter((c) => c.method === 'POST' && c.path === '/conversations/' + inbox.id + '/messages');
    expect(sends).toHaveLength(2);
    expect(sends[0].body.clientMessageId).toBe(sends[1].body.clientMessageId);
    await waitFor(() => expect(screen.queryByText(t.failed)).toBeNull());
  });

  test('Discard drops a failed message', async () => {
    const { fake, inbox } = seeded();
    fake.fail({ method: 'POST', path: /\/messages$/, status: 400, code: 'ContentFiltered' });
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/messages/' + inbox.id });
    await screen.findByText('Are you coming Thursday?');
    await fireEvent.changeText(screen.getByTestId('message-composer-body'), 'bad words');
    await fireEvent.press(screen.getByTestId('message-composer-send'));
    await fireEvent.press(await screen.findByText(t.discard));
    await waitFor(() => expect(screen.queryByText('bad words')).toBeNull());
  });

  test('edit once shows Edited with the original, then delete for everyone leaves a tombstone', async () => {
    const { fake, inbox } = seeded();
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/messages/' + inbox.id });
    await screen.findByText('Are you coming Thursday?');
    await fireEvent.changeText(screen.getByTestId('message-composer-body'), 'Thursdya works');
    await fireEvent.press(screen.getByTestId('message-composer-send'));
    await screen.findByText('Thursdya works');
    const mine = await waitFor(() => {
      const m = fake.state.messages.find((x) => x.body === 'Thursdya works');
      expect(m).toBeTruthy();
      return m;
    });
    await waitFor(() => expect(screen.getByTestId('message-' + mine.id + '-receipt')).toBeOnTheScreen());

    await fireEvent(screen.getByTestId('message-' + mine.id + '-bubble'), 'longPress');
    await fireEvent.press(await screen.findByTestId('conversation-message-menu-edit'));
    expect(screen.getByTestId('message-composer-context')).toHaveTextContent(t.editing);
    expect(screen.getByTestId('message-composer-body').props.value).toBe('Thursdya works');
    await fireEvent.changeText(screen.getByTestId('message-composer-body'), 'Thursday works');
    await fireEvent.press(screen.getByTestId('message-composer-send'));
    expect(await screen.findByText('Thursday works')).toBeOnTheScreen();
    expect(screen.getByTestId('message-' + mine.id + '-edited')).toHaveTextContent(t.edited);
    expect(fake.calls.find((c) => c.method === 'PUT' && c.path === '/messages/' + mine.id).body).toEqual({ body: 'Thursday works' });

    await fireEvent.press(screen.getByTestId('message-' + mine.id + '-original'));
    expect(await screen.findByTestId('conversation-original-body')).toHaveTextContent('Thursdya works');
    await fireEvent.press(screen.getByLabelText(strings.postMenu.close));

    await fireEvent(screen.getByTestId('message-' + mine.id + '-bubble'), 'longPress');
    expect(screen.queryByTestId('conversation-message-menu-edit')).toBeNull();
    await fireEvent.press(await screen.findByTestId('conversation-message-menu-delete'));
    await fireEvent.press(await screen.findByTestId('conversation-delete-confirm-confirm'));
    expect(await screen.findByTestId('message-' + mine.id + '-body')).toHaveTextContent(t.deleted);
    expect(screen.queryByText('Thursday works')).toBeNull();
  });

  test('reactions from the message menu use the property reaction set', async () => {
    const { fake, inbox } = seeded();
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/messages/' + inbox.id });
    await screen.findByText('Are you coming Thursday?');
    const theirs = fake.state.messages.find((m) => m.body === 'Are you coming Thursday?');
    await fireEvent(screen.getByTestId('message-' + theirs.id + '-bubble'), 'longPress');
    expect(screen.queryByTestId('conversation-message-menu-edit')).toBeNull();
    await fireEvent.press(await screen.findByTestId('conversation-message-menu-react-' + likeKey));
    expect(await screen.findByTestId('message-' + theirs.id + '-reactions-chip-' + likeKey)).toBeOnTheScreen();
    expect(fake.calls.some((c) => c.method === 'PUT' && c.path === '/messages/' + theirs.id + '/reactions/' + likeKey)).toBe(true);
    await fireEvent.press(screen.getByTestId('message-' + theirs.id + '-reactions-chip-' + likeKey));
    await waitFor(() => expect(fake.calls.some((c) => c.method === 'DELETE' && c.path === '/messages/' + theirs.id + '/reactions/' + likeKey)).toBe(true));
  });

  test('a reply quotes the message it answers', async () => {
    const { fake, inbox } = seeded();
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/messages/' + inbox.id });
    await screen.findByText('Are you coming Thursday?');
    const theirs = fake.state.messages.find((m) => m.body === 'Are you coming Thursday?');
    await fireEvent(screen.getByTestId('message-' + theirs.id + '-bubble'), 'longPress');
    await fireEvent.press(await screen.findByTestId('conversation-message-menu-reply'));
    expect(screen.getByTestId('message-composer-context')).toHaveTextContent(t.replyingTo('Alex Chen'));
    await fireEvent.changeText(screen.getByTestId('message-composer-body'), 'Yes');
    await fireEvent.press(screen.getByTestId('message-composer-send'));
    await waitFor(() => expect(fake.calls.find((c) => c.method === 'POST' && c.path === '/conversations/' + inbox.id + '/messages').body.replyToMessageId).toBe(theirs.id));
    const reply = await waitFor(() => {
      const m = fake.state.messages.find((x) => x.body === 'Yes');
      expect(m).toBeTruthy();
      return m;
    });
    expect(await screen.findByTestId('message-' + reply.id + '-reply')).toHaveTextContent(/Are you coming Thursday\?/);
  });

  test('read receipts show Read when the other member read my message', async () => {
    const fake = createMessagingFake({ me });
    const c = fake.addConversation({ otherUserId: 'usr_2' });
    fake.handle({ method: 'POST', path: '/conversations/' + c.id + '/messages', body: { clientMessageId: 'cm-1', body: 'Hello there', mediaIds: [] } });
    fake.conversation(c.id).otherLastReadSeq = 1;
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/messages/' + c.id });
    const m = fake.state.messages[0];
    expect(await screen.findByTestId('message-' + m.id + '-receipt')).toHaveTextContent(t.read);
  });

  test('the request banner accepts a request', async () => {
    const { fake, request } = seeded();
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/messages/' + request.id });
    expect(await screen.findByTestId('request-banner')).toHaveTextContent(new RegExp(t.requestTitle('Jo Park')));
    await fireEvent.press(screen.getByTestId('request-banner-accept'));
    await waitFor(() => expect(screen.queryByTestId('request-banner')).toBeNull());
    expect(screen.getByTestId('conversation-notice')).toHaveTextContent(t.accepted);
    expect(fake.conversation(request.id).folder).toBe('Inbox');
  });

  test('Decline leaves the conversation', async () => {
    const { fake, request } = seeded();
    serveFakes([fake]);
    const router = await renderRouter(APP_DIR, { initialUrl: '/messages' });
    await screen.findByTestId('messages-list');
    await act(async () => appRouter.push('/messages/' + request.id));
    await fireEvent.press(await screen.findByTestId('request-banner-decline'));
    await waitFor(() => expect(router.getPathname()).toBe('/messages'));
    expect(fake.conversation(request.id).folder).toBe('Declined');
  });

  test('Block and report blocks the sender and files a conversation report', async () => {
    const { fake, request } = seeded();
    const server = serveFakes([fake], { routes: { 'POST /users/usr_3/block': { blocked: true } } });
    await renderRouter(APP_DIR, { initialUrl: '/messages/' + request.id });
    await fireEvent.press(await screen.findByTestId('request-banner-block-report'));
    await waitFor(() => expect(server.called('POST /users/usr_3/block')).toHaveLength(1));
    await fireEvent.press(await screen.findByTestId('report-reason-Spam'));
    await fireEvent.press(screen.getByTestId('report-submit'));
    await waitFor(() => expect(fake.state.reports).toHaveLength(1));
    expect(fake.state.reports[0]).toMatchObject({ targetType: 'Conversation', targetId: request.id, reason: 'Spam' });
  });

  test('the conversation menu archives and mutes', async () => {
    const { fake, inbox } = seeded();
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/messages/' + inbox.id });
    await screen.findByText('Are you coming Thursday?');
    await fireEvent.press(screen.getByTestId('conversation-menu'));
    await fireEvent.press(await screen.findByTestId('conversation-conversation-menu-mute'));
    expect(await screen.findByTestId('conversation-muted')).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('conversation-menu'));
    await fireEvent.press(await screen.findByTestId('conversation-conversation-menu-archive'));
    await waitFor(() => expect(fake.conversation(inbox.id).folder).toBe('Archived'));
  });

  test('a conversation the viewer cannot send to shows the reason instead of the composer', async () => {
    const fake = createMessagingFake({ me });
    const c = fake.addConversation({ otherUserId: 'usr_2' });
    const present = fake.presentConversation;
    serveFakes([fake], { routes: { ['GET /conversations/' + c.id]: () => ({ ...present(fake.conversation(c.id)), viewer: { canSend: false, cannotSendReason: 'RecipientUnavailable' } }) } });
    await renderRouter(APP_DIR, { initialUrl: '/messages/' + c.id });
    expect(await screen.findByTestId('conversation-cannot-send')).toHaveTextContent(t.cannotSend.RecipientUnavailable);
    expect(screen.queryByTestId('message-composer')).toBeNull();
  });
});

describe('message search and settings', () => {
  test('search needs 2 characters and opens the conversation of a hit', async () => {
    const { fake, inbox } = seeded();
    serveFakes([fake]);
    const router = await renderRouter(APP_DIR, { initialUrl: '/messages/search' });
    expect(await screen.findByTestId('message-search-minimum')).toBeOnTheScreen();
    await fireEvent.changeText(screen.getByTestId('message-search-field'), 'thursday');
    await fireEvent.press(screen.getByTestId('message-search-submit'));
    const theirs = fake.state.messages.find((m) => m.body === 'Are you coming Thursday?');
    // The match is highlighted with the shared headlineParts helper, never shown with raw `**` markers.
    const hitText = await screen.findByTestId('message-hit-text-' + theirs.id);
    expect(hitText).toHaveTextContent('Are you coming Thursday?');
    expect(hitText).not.toHaveTextContent(/\*\*/);
    expect(within(hitText).getByText('Thursday')).toBeOnTheScreen();
    await fireEvent.press(await screen.findByTestId('message-hit-' + theirs.id));
    await waitFor(() => expect(router.getPathname()).toBe('/messages/' + inbox.id));
    expect(fake.calls.find((c) => c.path === '/messages/search/enumerate').body).toMatchObject({ search: 'thursday', maxResults: 25 });
  });

  test('messaging settings save DM policy, receipts, typing, push preview, and the hours exemption', async () => {
    const { fake } = seeded();
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/account/messaging' });
    await fireEvent.press(await screen.findByTestId('messaging-dm-policy-Known'));
    await waitFor(() => expect(fake.state.settings.dmPolicy).toBe('Known'));
    expect(screen.getByTestId('messaging-dm-policy-hint')).toHaveTextContent(t.dmPolicies.Known.hint);
    await fireEvent(screen.getByTestId('messaging-read-receipts'), 'valueChange', false);
    await waitFor(() => expect(fake.state.settings.readReceipts).toBe(false));
    await fireEvent(screen.getByTestId('messaging-typing'), 'valueChange', false);
    await waitFor(() => expect(fake.state.settings.typingIndicators).toBe(false));
    await fireEvent.press(screen.getByTestId('messaging-push-preview-Hidden'));
    await waitFor(() => expect(fake.state.settings.pushPreview).toBe('Hidden'));
    await fireEvent(screen.getByTestId('messaging-exempt-hours'), 'valueChange', true);
    await waitFor(() => expect(fake.calls.some((c) => c.method === 'PUT' && c.path === '/me/messaging/settings' && c.body.exemptFromNotificationHours === true)).toBe(true));
  });

  test('a failed save rolls back with a message', async () => {
    const { fake } = seeded();
    fake.fail({ method: 'PUT', path: '/me/messaging/settings', status: 500 });
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/account/messaging' });
    await fireEvent.press(await screen.findByTestId('messaging-dm-policy-Nobody'));
    expect(await screen.findByTestId('messaging-settings-save-error')).toHaveTextContent(t.saveFailed);
    expect(screen.getByTestId('messaging-dm-policy-hint')).toHaveTextContent(t.dmPolicies.Everyone.hint);
  });
});

describe('access', () => {
  test('a non-member sees the upgrade prompt where messaging is gated, and no messaging request is made', async () => {
    const { fake } = seeded();
    const lapsed = makeUser({ membership: { isActive: false, source: null, store: null, expiresUtc: null }, membershipRequired: false, accessTier: 'SignedIn' });
    serveFakes([fake], { user: lapsed, propertyDoc: phase5Property({ access: { model: 'PartiallyGated', gatedFeatures: ['messaging'] } }) });
    await renderRouter(APP_DIR, { initialUrl: '/messages' });
    expect(await screen.findByTestId('messaging-locked-prompt')).toBeOnTheScreen();
    expect(fake.calls.some((c) => c.path.startsWith('/conversations'))).toBe(false);
  });

  test('features.messaging off hides the Messages button and explains the screen', async () => {
    const { fake } = seeded();
    serveFakes([fake], { propertyDoc: phase5Property({ features: { messaging: false } }), routes: { 'POST /feed/enumerate': { objects: [], continuationToken: null, endOfResults: true } } });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await waitFor(() => expect(screen.getByTestId('header-notifications')).toBeOnTheScreen());
    await waitFor(() => expect(screen.queryByTestId('header-messages')).toBeNull());
    await act(async () => appRouter.push('/messages'));
    expect(await screen.findByTestId('messaging-off')).toBeOnTheScreen();
  });
});

describe('on a tablet', () => {
  test('the conversation list and the conversation sit side by side with the filters in a side panel', async () => {
    const { fake, inbox } = seeded();
    serveFakes([fake]);
    await atWidth(1180, async () => {
      await renderRouter(APP_DIR, { initialUrl: '/messages' });
      expect(await screen.findByTestId('messages-select')).toBeOnTheScreen();
      expect(screen.getByTestId('messages-filters-side-panel')).toBeOnTheScreen();
      await fireEvent.press(await screen.findByTestId('conversation-' + inbox.id));
      const pane = await screen.findByTestId('split-detail-pane');
      expect(await within(pane).findByText('Are you coming Thursday?')).toBeOnTheScreen();
      expect(screen.getByTestId('conversation-' + inbox.id)).toBeOnTheScreen();
      await waitFor(() => expect(screen.queryByTestId('conversation-' + inbox.id + '-unread')).toBeNull());
    });
  });
});

describe('realtime', () => {
  let ws;
  let appStateListeners;

  beforeEach(() => {
    ws = createFakeWebSocket();
    setWebSocketImpl(ws.WebSocket);
    setRealtimeEnabled(true);
    appStateListeners = [];
    jest.spyOn(AppState, 'addEventListener').mockImplementation((type, fn) => {
      appStateListeners.push(fn);
      return { remove: () => (appStateListeners = appStateListeners.filter((x) => x !== fn)) };
    });
  });
  afterEach(() => {
    setRealtimeEnabled(false);
    setWebSocketImpl(undefined);
    setRealtimeTimers(null);
  });

  async function openSocket() {
    await waitFor(() => expect(ws.last()).toBeTruthy());
    const socket = ws.last();
    await act(async () => {
      socket.accept();
    });
    await waitFor(() => expect(socket.framesOf('hello')).toHaveLength(1));
    await act(async () => {
      socket.serverSend('welcome', welcomeData());
    });
    return socket;
  }

  test('connects with the identification headers, reports the open conversation, and shows live messages and typing', async () => {
    const { fake, inbox } = seeded();
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/messages/' + inbox.id });
    await screen.findByText('Are you coming Thursday?');
    const socket = await openSocket();
    expect(socket.url).toBe('ws://127.0.0.1:8000/api/v1/ws');
    expect(socket.protocols).toBeUndefined();
    expect(socket.options.headers).toMatchObject({ 'X-TK-Property': phase5Property().id, 'X-TK-App': expect.any(String) });
    expect(socket.framesOf('hello')[0].data).toMatchObject({ client: 'Mobile', accessToken: expect.any(String), propertyId: phase5Property().id });
    await waitFor(() => expect(socket.framesOf('state').some((f) => f.data.activeConversationId === inbox.id)).toBe(true));

    await act(async () => {
      socket.serverSend('typing', { conversationId: inbox.id, userId: 'usr_2', state: 'Start', expiresInMs: 6000 });
    });
    expect(await screen.findByTestId('conversation-typing')).toHaveTextContent(t.typing('Alex Chen'));

    const message = fake.receive(inbox.id, { body: 'Running late' });
    await act(async () => {
      socket.serverSend('message.created', { conversationId: inbox.id, message });
    });
    expect(await screen.findByText('Running late')).toBeOnTheScreen();
    await waitFor(() => expect(screen.queryByTestId('conversation-typing')).toBeNull());

    await fireEvent.changeText(screen.getByTestId('message-composer-body'), 'No problem');
    await waitFor(() => expect(socket.framesOf('typing').some((f) => f.data.conversationId === inbox.id && f.data.state === 'Start')).toBe(true));
    await waitFor(() => expect(socket.framesOf('read').some((f) => f.data.seq === message.seq)).toBe(true));
  });

  test('the waiting banner of the requester clears live when the recipient accepts (conversation.updated over the socket)', async () => {
    const fake = createMessagingFake({ me });
    const sent = fake.addConversation({ otherUserId: 'usr_2', requestState: 'AwaitingAcceptance' });
    fake.receive(sent.id, { body: 'Hello from me' });
    serveFakes([fake]);
    await renderRouter(APP_DIR, { initialUrl: '/messages/' + sent.id });
    expect(await screen.findByTestId('request-banner-awaiting')).toHaveTextContent(t.awaitingBanner);
    const socket = await openSocket();
    const accepted = { ...fake.presentConversation(fake.conversation(sent.id)), requestState: null };
    await act(async () => {
      socket.serverSend('conversation.updated', { conversation: accepted });
    });
    await waitFor(() => expect(screen.queryByTestId('request-banner-awaiting')).toBeNull());
  });

  test('notification.created updates the notification badge and unread.counts the Messages badge', async () => {
    const { fake } = seeded();
    serveFakes([fake], { routes: { 'POST /feed/enumerate': { objects: [], continuationToken: null, endOfResults: true } } });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    const socket = await openSocket();
    await act(async () => {
      socket.serverSend('notification.created', { notification: { id: 'ntf_1' }, unreadCount: 7 });
      socket.serverSend('unread.counts', { notifications: 7, conversations: 4, messages: 9, requests: 1 });
    });
    expect(await screen.findByTestId('header-notifications-badge', { includeHiddenElements: true })).toHaveTextContent('7');
    const entry = await messagesEntry();
    await waitFor(() => expect(screen.getByTestId(entry + '-badge', { includeHiddenElements: true })).toHaveTextContent('5'));
  });

  test('closes 30 seconds after the app goes to the background and reconnects in the foreground', async () => {
    const { fake } = seeded();
    const timers = [];
    setRealtimeTimers({ setTimeout: (fn, ms) => timers.push({ fn, ms }) - 1, clearTimeout: (id) => { if (timers[id]) timers[id].cancelled = true; }, now: () => Date.now() });
    serveFakes([fake], { routes: { 'POST /feed/enumerate': { objects: [], continuationToken: null, endOfResults: true } } });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    const socket = await openSocket();
    await act(async () => appStateListeners.forEach((fn) => fn('background')));
    expect(socket.framesOf('state').some((f) => f.data.appState === 'Background')).toBe(true);
    const close = timers.find((x) => x.ms === 30000 && !x.cancelled);
    expect(close).toBeTruthy();
    expect(socket.closeCalls).toHaveLength(0);
    await act(async () => close.fn());
    expect(socket.closeCalls[0]).toMatchObject({ code: 1000 });
    const before = ws.instances.length;
    await act(async () => appStateListeners.forEach((fn) => fn('active')));
    await waitFor(() => expect(ws.instances.length).toBe(before + 1));
  });

  test('without WebSocket support the client polls the messaging counts', async () => {
    setWebSocketImpl(null);
    const { fake } = seeded();
    serveFakes([fake], { routes: { 'POST /feed/enumerate': { objects: [], continuationToken: null, endOfResults: true } } });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await waitFor(() => expect(fake.calls.some((c) => c.path === '/conversations/changes/enumerate')).toBe(true));
    expect(fake.calls.some((c) => c.path === '/me/messaging/unread-count')).toBe(true);
  });
});

describe('direct message link previews (as-built note E)', () => {
  const preview = { url: 'https://example.com/a', canonicalUrl: 'https://example.com/a', siteName: 'Example', title: 'Article title', description: 'About it', imageUrl: null };

  test('a message shows its server-fetched link preview; a tombstone never does', async () => {
    const message = { id: 'msg_1', body: 'Look https://example.com/a', state: 'Visible', createdUtc: '2026-11-10T18:00:00Z', attachments: [], linkPreview: preview, viewer: {} };
    await render(<MessageBubble message={message} mine={false} />);
    expect(screen.getByTestId('message-msg_1-preview')).toBeOnTheScreen();
    expect(screen.getByText('Article title')).toBeOnTheScreen();
    await render(<MessageBubble message={{ ...message, id: 'msg_2', state: 'Deleted', body: '' }} mine={false} />);
    expect(screen.queryByTestId('message-msg_2-preview')).toBeNull();
  });

  test('the composer previews the first link with the privacy note, and removing it sends showLinkPreview false', async () => {
    jest.useFakeTimers();
    try {
      const { fake, inbox } = seeded();
      const server = serveFakes([fake], { routes: { 'POST /link-previews': preview } });
      await renderRouter(APP_DIR, { initialUrl: '/messages/' + inbox.id });
      await screen.findByText('Are you coming Thursday?');
      await fireEvent.changeText(screen.getByTestId('message-composer-body'), 'Read https://example.com/a');
      await act(async () => jest.advanceTimersByTime(700));
      expect(await screen.findByTestId('message-composer-preview')).toBeOnTheScreen();
      expect(screen.getByTestId('message-composer-preview-privacy')).toHaveTextContent(t.linkPreviewPrivacy);
      expect(server.called('POST /link-previews')[0].body).toEqual({ url: 'https://example.com/a' });
      await fireEvent.press(screen.getByLabelText(strings.composer.removePreview));
      expect(await screen.findByTestId('message-composer-show-preview')).toBeOnTheScreen();
      await fireEvent.press(screen.getByTestId('message-composer-send'));
      await waitFor(() => expect(lastBody(fake, '/conversations/' + inbox.id + '/messages')).toMatchObject({ body: 'Read https://example.com/a', showLinkPreview: false }));
    } finally {
      jest.useRealTimers();
    }
  });
});

describe('starting a conversation from a member profile', () => {
  const profile = (extra = {}) => ({ id: 'usr_2', displayName: 'Alex Chen', handle: 'alexchen', avatarUrl: null, bio: null, profile: {}, createdUtc: '2026-01-01T00:00:00Z', isBlockedByMe: false, ...extra });

  test('Message opens the conversation with that member', async () => {
    const fake = createMessagingFake({ me });
    serveFakes([fake], { routes: { 'GET /users/usr_2': profile() } });
    const router = await renderRouter(APP_DIR, { initialUrl: '/members/usr_2' });
    await fireEvent.press(await screen.findByTestId('member-send-message'));
    expect(lastBody(fake, '/conversations')).toEqual({ recipientUserId: 'usr_2' });
    await waitFor(() => expect(router.getPathname()).toMatch(/^\/messages\/.+/));
  });

  test('is hidden for a blocked member, when the profile refuses messages, and when messaging is off', async () => {
    const fake = createMessagingFake({ me });
    serveFakes([fake], { routes: { 'GET /users/usr_2': profile({ isBlockedByMe: true }), 'GET /users/usr_3': profile({ id: 'usr_3', viewer: { canMessage: false } }) } });
    await renderRouter(APP_DIR, { initialUrl: '/members/usr_2' });
    expect(await screen.findByTestId('member-block')).toBeOnTheScreen();
    expect(screen.queryByTestId('member-send-message')).toBeNull();
    await act(async () => appRouter.push('/members/usr_3'));
    expect(await screen.findByText('Alex Chen')).toBeOnTheScreen();
    expect(screen.queryByTestId('member-send-message')).toBeNull();
  });

  test('is hidden when the property turns messaging off', async () => {
    const fake = createMessagingFake({ me });
    serveFakes([fake], { routes: { 'GET /users/usr_2': profile() }, propertyDoc: phase5Property({ features: { messaging: false } }) });
    await renderRouter(APP_DIR, { initialUrl: '/members/usr_2' });
    expect(await screen.findByTestId('member-block')).toBeOnTheScreen();
    expect(screen.queryByTestId('member-send-message')).toBeNull();
  });
});
