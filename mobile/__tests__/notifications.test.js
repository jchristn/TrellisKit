import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import * as Notifications from 'expo-notifications';
import * as WebBrowser from 'expo-web-browser';
import * as SecureStore from 'expo-secure-store';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { Linking } from 'react-native';
import { badgeText, FallbackPath, pushTarget, resolveNotificationPath, staffWebPath } from '../src/notifications/deepLinks';
import { deviceTimeZone, displayTime, isWithinHours, stepTime, toggleCategory } from '../src/notifications/preferences';
import { PushTokenKey, pushPlatform, reconcilePushPermission, registerPushToken, resetPushRegistrationState } from '../src/notifications/push';
import strings from '../src/strings';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { defaultPreferences, enumerationResult, makeComment, makeNotification, makePost } from '../test-utils/community';

const APP_DIR = path.resolve(__dirname, '../app');
const TOKEN = 'ExponentPushToken[test-device]';
const granted = { granted: true, status: 'granted', canAskAgain: true };
const denied = { granted: false, status: 'denied', canAskAgain: false };
const undetermined = { granted: false, status: 'undetermined', canAskAgain: true };

function pushResponse(data, identifier = 'push-1') {
  return { notification: { request: { identifier, content: { data } } } };
}

describe('deep links', () => {
  test('known app paths are followed unchanged; anything else falls back to the center', () => {
    expect(resolveNotificationPath('/feed/fpo_1')).toBe('/feed/fpo_1');
    expect(resolveNotificationPath('/feed/fpo_1?comment=cmt_2')).toBe('/feed/fpo_1?comment=cmt_2');
    expect(resolveNotificationPath('/notifications')).toBe('/notifications');
    expect(staffWebPath('/admin/content-review')).toBe('/admin/content-review');
    expect(staffWebPath('/admin/metrics/health')).toBe('/admin/metrics/health');
    expect(staffWebPath('//evil.test/admin/metrics')).toBeNull();
    expect(resolveNotificationPath('/account/membership')).toBe('/account/membership');
    expect(resolveNotificationPath('/threads/thr_1')).toBe('/threads/thr_1');
    expect(resolveNotificationPath('/lessons/les_1')).toBe('/lessons/les_1');
    expect(resolveNotificationPath('/directory')).toBe('/map');
    expect(resolveNotificationPath('/me/directory/submissions')).toBe('/directory/submissions');
    expect(resolveNotificationPath('/courses/crs_1')).toBeNull();
    expect(resolveNotificationPath('//evil.test/feed')).toBeNull();
    expect(resolveNotificationPath('/feed/fpo_1?comment=cmt_2&x=1')).toBeNull();
    expect(pushTarget(pushResponse({ notificationId: 'ntf_1', path: '/feed/fpo_1' }))).toEqual({ notificationId: 'ntf_1', path: '/feed/fpo_1' });
    expect(pushTarget(pushResponse({ notificationId: 'ntf_2', path: '/messages/msg_1' }))).toEqual({ notificationId: 'ntf_2', path: FallbackPath });
    expect(badgeText(0)).toBeNull();
    expect(badgeText(7)).toBe('7');
    expect(badgeText(150)).toBe('99+');
  });
});

describe('push token route', () => {
  beforeEach(() => {
    resetEnvironment();
    jest.clearAllMocks();
  });

  test('platform casing is Ios and Android', () => {
    expect(pushPlatform('ios')).toBe('Ios');
    expect(pushPlatform('android')).toBe('Android');
  });

  test('Android registers with platform Android at POST /me/push-tokens', async () => {
    SecureStore.__store.set(PushTokenKey, TOKEN);
    const api = { post: jest.fn(async () => ({ id: 'ptk_1' })) };
    await registerPushToken(api, { userId: 'usr_1', os: 'android', deviceName: 'Pixel 9' });
    expect(api.post).toHaveBeenCalledWith('/me/push-tokens', { token: TOKEN, platform: 'Android', deviceName: 'Pixel 9', appVersion: '1.0.0' });
  });

  test('a permission turned off in Settings removes the token on the next foreground check', async () => {
    SecureStore.__store.set(PushTokenKey, TOKEN);
    Notifications.getPermissionsAsync.mockResolvedValue(denied);
    const api = { request: jest.fn(async () => null), post: jest.fn() };
    expect(await reconcilePushPermission(api, { userId: 'usr_1', Notifications })).toEqual({ permission: 'denied', status: 'removed' });
    expect(api.request).toHaveBeenCalledWith('DELETE', '/me/push-tokens', expect.objectContaining({ body: { token: TOKEN } }));
    expect(SecureStore.__store.has(PushTokenKey)).toBe(false);
    resetPushRegistrationState();
  });
});

describe('notification center and badge', () => {
  beforeEach(() => {
    resetEnvironment();
    jest.clearAllMocks();
    Notifications.getPermissionsAsync.mockResolvedValue(undetermined);
    Notifications.getLastNotificationResponseAsync.mockResolvedValue(null);
  });
  afterEach(() => delete global.fetch);

  test('the header badge shows the unread count (99+ above 99)', async () => {
    installFakeServer({ ...signedInAs(makeUser()), 'GET /notifications/unread-count': { unreadCount: 120, latestUpdatedUtc: null }, 'POST /feed/enumerate': enumerationResult([]) });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    const badge = await screen.findByTestId('header-notifications-badge', { includeHiddenElements: true });
    expect(badge).toHaveTextContent('99+');
    expect(screen.getByTestId('header-notifications')).toHaveProp('accessibilityLabel', strings.header.notificationsUnread('99+'));
    await waitFor(() => expect(Notifications.setBadgeCountAsync).toHaveBeenCalledWith(120));
  });

  test('opening a notification marks it read, then follows its path to the comment in context', async () => {
    const server = installFakeServer({
      ...signedInAs(makeUser()),
      'GET /notifications/unread-count': { unreadCount: 2 },
      'POST /notifications/enumerate': enumerationResult([makeNotification('ntf_1'), makeNotification('ntf_2', { path: '/courses/crs_1', type: 'CoursePublished', isRead: true, title: 'Something new' })]),
      'POST /notifications/read': { unreadCount: 1 },
      'GET /feed/fpo_1': makePost('fpo_1'),
      'GET /comments/cmt_1': { target: { type: 'FeedPost', id: 'fpo_1' }, ancestors: [], comment: makeComment('cmt_1') }
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/notifications' });
    expect(await screen.findByTestId('notification-ntf_1-unread')).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('notification-ntf_2-open'));
    expect(router.getPathname()).toBe('/notifications');
    await fireEvent.press(screen.getByTestId('notification-ntf_1-open'));
    await waitFor(() => expect(router.getPathname()).toBe('/feed/fpo_1'));
    expect(server.called('POST /notifications/read')[0].body).toEqual({ ids: ['ntf_1'] });
    expect(await screen.findByTestId('comment-cmt_1')).toBeOnTheScreen();
    expect(screen.getByText(strings.comments.inContext)).toBeOnTheScreen();
  });

  test('mark all as read, and toggle one back to unread', async () => {
    const server = installFakeServer({
      ...signedInAs(makeUser()),
      'GET /notifications/unread-count': { unreadCount: 1 },
      'POST /notifications/enumerate': enumerationResult([makeNotification('ntf_1')]),
      'POST /notifications/read-all': { unreadCount: 0 },
      'POST /notifications/unread': { unreadCount: 1 }
    });
    await renderRouter(APP_DIR, { initialUrl: '/notifications' });
    await screen.findByTestId('notification-ntf_1-unread');
    await fireEvent.press(screen.getByTestId('notifications-mark-all'));
    await waitFor(() => expect(screen.queryByTestId('notification-ntf_1-unread')).toBeNull());
    expect(server.called('POST /notifications/read-all')).toHaveLength(1);
    await fireEvent.press(screen.getByTestId('notification-ntf_1-toggle'));
    expect(await screen.findByTestId('notification-ntf_1-unread')).toBeOnTheScreen();
    expect(server.called('POST /notifications/unread')[0].body).toEqual({ ids: ['ntf_1'] });
  });

  test('tapping a push marks it read and opens data.path', async () => {
    let onResponse;
    Notifications.addNotificationResponseReceivedListener.mockImplementation((fn) => {
      onResponse = fn;
      return { remove: jest.fn() };
    });
    const server = installFakeServer({
      ...signedInAs(makeUser()),
      'POST /feed/enumerate': enumerationResult([]),
      'POST /notifications/read': { unreadCount: 0 },
      'GET /feed/fpo_9': makePost('fpo_9'),
      'POST /feed/fpo_9/comments/enumerate': enumerationResult([])
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await screen.findByTestId('feed-empty');
    await act(async () => onResponse(pushResponse({ notificationId: 'ntf_9', type: 'NewFeedPost', path: '/feed/fpo_9' })));
    await waitFor(() => expect(router.getPathname()).toBe('/feed/fpo_9'));
    expect(server.called('POST /notifications/read')[0].body).toEqual({ ids: ['ntf_9'] });
  });

  test.each(['ContentFlagged', 'MetricAlert'])('opens a %s staff push on the property website', async (type) => {
    let onResponse;
    Notifications.addNotificationResponseReceivedListener.mockImplementation((fn) => { onResponse = fn; return { remove: jest.fn() }; });
    const server = installFakeServer({ ...signedInAs(makeUser()), 'POST /feed/enumerate': enumerationResult([]), 'POST /notifications/read': { unreadCount: 0 } });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await screen.findByTestId('feed-empty');
    const path = type === 'ContentFlagged' ? '/admin/content-review' : '/admin/metrics/health';
    await act(async () => onResponse(pushResponse({ notificationId: 'ntf_staff', type, path })));
    await waitFor(() => expect(WebBrowser.openBrowserAsync).toHaveBeenCalledWith(expect.stringMatching(new RegExp(path + '$'))));
    expect(server.called('POST /notifications/read')[0].body).toEqual({ ids: ['ntf_staff'] });
  });

  test('a push that cold-started the app opens once the session is restored', async () => {
    Notifications.getLastNotificationResponseAsync.mockResolvedValue(pushResponse({ notificationId: 'ntf_3', path: '/account/membership' }, 'cold'));
    installFakeServer({ ...signedInAs(makeUser()), 'POST /notifications/read': { unreadCount: 0 }, 'GET /membership': { membership: makeUser().membership, history: [] } });
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/account/membership'));
  });

  test('a push received in the foreground refreshes the count', async () => {
    let onReceived;
    Notifications.addNotificationReceivedListener.mockImplementation((fn) => {
      onReceived = fn;
      return { remove: jest.fn() };
    });
    let count = 0;
    const server = installFakeServer({ ...signedInAs(makeUser()), 'POST /feed/enumerate': enumerationResult([]), 'GET /notifications/unread-count': () => ({ unreadCount: (count += 1) }) });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await waitFor(() => expect(server.called('GET /notifications/unread-count')).toHaveLength(1));
    await act(async () => onReceived({}));
    await waitFor(() => expect(server.called('GET /notifications/unread-count')).toHaveLength(2));
    expect(await screen.findByTestId('header-notifications-badge', { includeHiddenElements: true })).toHaveTextContent('2');
  });
});

describe('notification preferences and hours', () => {
  test('hours: crossing midnight, all day, 12-hour display, 30 minute steps that wrap', () => {
    expect(isWithinHours({ enabled: true, start: '07:00', end: '22:00' }, 7 * 60)).toBe(true);
    expect(isWithinHours({ enabled: true, start: '07:00', end: '22:00' }, 22 * 60)).toBe(false);
    expect(isWithinHours({ enabled: true, start: '22:00', end: '06:00' }, 23 * 60)).toBe(true);
    expect(isWithinHours({ enabled: true, start: '22:00', end: '06:00' }, 12 * 60)).toBe(false);
    expect(isWithinHours({ enabled: true, start: '08:00', end: '08:00' }, 3)).toBe(true);
    expect(isWithinHours({ enabled: false, start: '07:00', end: '22:00' }, 3)).toBe(true);
    expect(displayTime('07:00')).toBe('7:00 AM');
    expect(displayTime('22:30')).toBe('10:30 PM');
    expect(displayTime('00:00')).toBe('12:00 AM');
    expect(stepTime('07:00', 1)).toBe('07:30');
    expect(stepTime('23:30', 1)).toBe('00:00');
    expect(stepTime('00:00', -1)).toBe('23:30');
  });

  test('turning in-app off also turns push off; turning push on turns in-app back on', () => {
    const prefs = defaultPreferences();
    const off = toggleCategory(prefs, 'Replies', 'inApp', false);
    expect(off.change).toEqual({ category: 'Replies', inApp: false, push: false });
    expect(off.prefs.categories[0]).toMatchObject({ inApp: false, push: false });
    expect(toggleCategory(off.prefs, 'Replies', 'push', true).change).toEqual({ category: 'Replies', push: true, inApp: true });
  });

  describe('settings screen', () => {
    beforeEach(() => {
      resetEnvironment();
      jest.clearAllMocks();
      Notifications.getPermissionsAsync.mockResolvedValue(granted);
    });
    afterEach(() => delete global.fetch);

    function server(prefs = defaultPreferences({ notificationHours: { enabled: true, start: '07:00', end: '22:00', timeZone: deviceTimeZone() } }), extra = {}) {
      let doc = prefs;
      return installFakeServer({
        ...signedInAs(makeUser()),
        'POST /me/notification-preferences/enumerate': () => ({ ...doc, objects: doc.categories, endOfResults: true }),
        'PUT /me/notification-preferences': (call) => {
          const body = call.body;
          doc = {
            ...doc,
            pushPaused: body.pushPaused !== undefined ? body.pushPaused : doc.pushPaused,
            notificationHours: { ...doc.notificationHours, ...(body.notificationHours || {}) },
            categories: doc.categories.map((c) => ({ ...c, ...((body.categories || []).find((x) => x.category === c.category) || {}) }))
          };
          return doc;
        },
        ...extra
      });
    }

    test('every category and channel defaults on; locked cells are disabled; default hours are 7:00 AM to 10:00 PM', async () => {
      server();
      await renderRouter(APP_DIR, { initialUrl: '/account/notifications' });
      for (const category of ['Replies', 'Mentions', 'Reactions', 'FeedPosts', 'Moderation', 'Account']) {
        expect(await screen.findByTestId('category-' + category)).toHaveTextContent(new RegExp(strings.notificationSettings.categories[category].label));
        expect(screen.getByTestId('pref-' + category + '-inApp').props.value).toBe(true);
        expect(screen.getByTestId('pref-' + category + '-push').props.value).toBe(true);
      }
      expect(screen.getByTestId('pref-Moderation-inApp').props.disabled).toBe(true);
      expect(screen.getByTestId('pref-Account-inApp').props.disabled).toBe(true);
      expect(screen.getByTestId('pref-Replies-email').props.disabled).toBe(true);
      expect(screen.getByTestId('pref-FeedPosts-email').props.value).toBe(true);
      expect(screen.getByTestId('notification-hours-start-value')).toHaveTextContent('7:00 AM');
      expect(screen.getByTestId('notification-hours-end-value')).toHaveTextContent('10:00 PM');
      expect(screen.queryByTestId('notification-permission-banner')).toBeNull();
    });

    test('Following and Forums offer an opt-in email digest when the server allows it', async () => {
      const base = defaultPreferences();
      const s = server(defaultPreferences({
        categories: [
          ...base.categories,
          { category: 'Following', inApp: true, push: true, email: false, editable: { inApp: true, push: true, email: true } },
          { category: 'Forums', inApp: true, push: true, email: false, editable: { inApp: true, push: true, email: true } }
        ],
        // The screen should not synchronize a different zone during this preference-only test.
        notificationHours: { enabled: true, start: '07:00', end: '22:00', timeZone: deviceTimeZone() }
      }));
      await renderRouter(APP_DIR, { initialUrl: '/account/notifications' });
      const toggle = await screen.findByTestId('pref-Following-email');
      expect(toggle.props.value).toBe(false);
      expect(toggle.props.disabled).toBe(false);
      expect(toggle.props.accessibilityLabel).toContain(strings.notificationSettings.digest);
      expect(screen.getByTestId('digest-note-Forums')).toHaveTextContent(strings.notificationSettings.digestNote);
      expect(screen.queryByTestId('digest-note-FeedPosts')).toBeNull();
      await fireEvent(toggle, 'valueChange', true);
      await waitFor(() => expect(s.called('PUT /me/notification-preferences')).toHaveLength(1));
      expect(s.called('PUT /me/notification-preferences')[0].body).toEqual({ categories: [{ category: 'Following', email: true }] });
    });

    test('toggles PUT only the changed cell; failures roll back', async () => {
      let fail = false;
      const s = server(undefined);
      const original = s.handlers['PUT /me/notification-preferences'];
      s.handlers['PUT /me/notification-preferences'] = (call) => (fail ? { status: 500, body: { error: 'InternalError' } } : original(call));
      await renderRouter(APP_DIR, { initialUrl: '/account/notifications' });
      await fireEvent(await screen.findByTestId('pref-Reactions-push'), 'valueChange', false);
      await waitFor(() => expect(s.called('PUT /me/notification-preferences').pop().body).toEqual({ categories: [{ category: 'Reactions', push: false }] }));
      expect(screen.getByTestId('pref-Reactions-push').props.value).toBe(false);

      fail = true;
      await fireEvent(screen.getByTestId('pref-Mentions-inApp'), 'valueChange', false);
      expect(await screen.findByTestId('notification-settings-save-error')).toHaveTextContent(strings.notificationSettings.saveFailed);
      expect(screen.getByTestId('pref-Mentions-inApp').props.value).toBe(true);
      expect(screen.getByTestId('pref-Mentions-push').props.value).toBe(true);
    });

    test('pause all pushes, and edit notification hours in 30 minute steps', async () => {
      const s = server();
      await renderRouter(APP_DIR, { initialUrl: '/account/notifications' });
      await fireEvent(await screen.findByTestId('notification-push-paused'), 'valueChange', true);
      await waitFor(() => expect(s.called('PUT /me/notification-preferences').pop().body).toEqual({ pushPaused: true }));
      await fireEvent.press(screen.getByTestId('notification-hours-start-later'));
      await waitFor(() => expect(s.called('PUT /me/notification-preferences').pop().body).toEqual({ notificationHours: { start: '07:30' } }));
      expect(screen.getByTestId('notification-hours-start-value')).toHaveTextContent('7:30 AM');
      await fireEvent.press(screen.getByTestId('notification-hours-end-earlier'));
      await waitFor(() => expect(s.called('PUT /me/notification-preferences').pop().body).toEqual({ notificationHours: { end: '21:30' } }));
      await fireEvent(screen.getByTestId('notification-hours-enabled'), 'valueChange', false);
      await waitFor(() => expect(s.called('PUT /me/notification-preferences').pop().body).toEqual({ notificationHours: { enabled: false } }));
      expect(screen.getByText(strings.notificationSettings.allDay)).toBeOnTheScreen();
    });

    test('the device time zone is sent when it differs from the server', async () => {
      const s = server(defaultPreferences({ notificationHours: { enabled: true, start: '07:00', end: '22:00', timeZone: 'Etc/Unknown-Zone' } }));
      await renderRouter(APP_DIR, { initialUrl: '/account/notifications' });
      await screen.findByTestId('notification-hours');
      await waitFor(() => expect(s.called('PUT /me/notification-preferences').map((c) => c.body)).toContainEqual({ notificationHours: { timeZone: deviceTimeZone() } }));
      expect(screen.getByTestId('notification-hours-zone')).toHaveTextContent(deviceTimeZone(), { exact: false });
    });

    test('without device permission a banner offers to turn it on; a push toggle prompts too', async () => {
      Notifications.getPermissionsAsync.mockResolvedValue(undetermined);
      Notifications.requestPermissionsAsync.mockResolvedValue(granted);
      server(defaultPreferences({ categories: defaultPreferences().categories.map((c) => (c.category === 'Replies' ? { ...c, push: false } : c)), notificationHours: { enabled: true, start: '07:00', end: '22:00', timeZone: deviceTimeZone() } }));
      await renderRouter(APP_DIR, { initialUrl: '/account/notifications' });
      const banner = await screen.findByTestId('notification-permission-banner');
      expect(banner).toHaveTextContent(strings.notificationSettings.permissionOffTitle, { exact: false });
      await fireEvent(screen.getByTestId('pref-Replies-push'), 'valueChange', true);
      await waitFor(() => expect(Notifications.requestPermissionsAsync).toHaveBeenCalled());
      Notifications.getPermissionsAsync.mockResolvedValue(granted);
      await waitFor(() => expect(screen.queryByTestId('notification-permission-banner')).toBeNull());
    });

    test('when the OS will not ask again, the banner opens Settings', async () => {
      Notifications.getPermissionsAsync.mockResolvedValue(denied);
      const openSettings = jest.spyOn(Linking, 'openSettings').mockResolvedValue(undefined);
      server();
      await renderRouter(APP_DIR, { initialUrl: '/account/notifications' });
      const button = await screen.findByTestId('notification-permission-turn-on');
      expect(button).toHaveTextContent(strings.notificationSettings.openSettings);
      await fireEvent.press(button);
      expect(openSettings).toHaveBeenCalled();
      expect(Notifications.requestPermissionsAsync).not.toHaveBeenCalled();
      openSettings.mockRestore();
    });

    test('Account settings links to notification settings and blocked members', async () => {
      server(undefined, { 'POST /me/blocks/enumerate': enumerationResult([]) });
      const router = await renderRouter(APP_DIR, { initialUrl: '/account' });
      await fireEvent.press(await screen.findByTestId('account-notifications'));
      await waitFor(() => expect(router.getPathname()).toBe('/account/notifications'));
      expect(await within(screen.getByTestId('screen-notification-settings')).findByTestId('notification-hours')).toBeOnTheScreen();
    });
  });
});
