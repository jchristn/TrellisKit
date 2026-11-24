import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import * as Notifications from 'expo-notifications';
import * as WebBrowser from 'expo-web-browser';
import { router as nativeRouter } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { Linking } from 'react-native';
import { metricDate } from '../app/admin/metrics/[pageId]';
import { notificationDestination, pushTarget, resolveNotificationPath } from '../src/notifications/deepLinks';
import strings from '../src/strings';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { enumerationResult, makeNotification } from '../test-utils/community';

const APP_DIR = path.resolve(__dirname, '../app');
const notice = makeNotification('ntf_metric', { type: 'MetricAlert', path: '/admin/metrics/health', title: 'Metric alert: signupDrop', body: 'Signups dropped below the configured threshold.' });
const page = { id: 'health', title: 'Health', question: 'Is the service healthy?', charts: [{ id: 'requests', title: 'Requests', metric: 'requests.count' }] };
const growth = { id: 'growth', title: 'Growth', charts: [{ id: 'signups', metric: 'users.new' }] };
const definition = { key: 'requests.count', title: 'Requests', unit: 'count', buckets: ['Day'] };
const signupDefinition = { key: 'users.new', title: 'New members', unit: 'count', buckets: ['Day'] };
const observation = (id, value, extra = {}) => ({ id, period: 'Current', seriesKey: 'all', startUtc: '2026-11-23T00:00:00Z', endUtc: '2026-11-24T00:00:00Z', value, complete: true, ...extra });
function routes(extra = {}, user = makeUser({ roles: [{ role: 'SystemAdmin' }] })) {
  return installFakeServer({ ...signedInAs(user),
    'GET /notifications/unread-count': { unreadCount: 1 },
    'POST /notifications/enumerate': enumerationResult([notice]),
    'POST /notifications/read': { unreadCount: 0 },
    'POST /admin/metrics/pages/enumerate': enumerationResult([page, growth]),
    'POST /admin/metrics/enumerate': enumerationResult([definition, signupDefinition]),
    'POST /admin/metrics/query': enumerationResult([observation('point_1', 42)]), ...extra });
}
beforeEach(() => { resetEnvironment(); jest.clearAllMocks(); Notifications.getLastNotificationResponseAsync.mockResolvedValue(null); });
afterEach(() => delete global.fetch);

test('metric alert row marks read and opens native metrics with alert context and Back', async () => {
  const server = routes();
  const router = await renderRouter(APP_DIR, { initialUrl: '/notifications' });
  await fireEvent.press(await screen.findByTestId('notification-ntf_metric-open'));
  await waitFor(() => expect(router.getPathname()).toBe('/admin/metrics/health'));
  expect(await screen.findByTestId('metric-requests-latest')).toHaveTextContent('42 count');
  const view = within(screen.getByTestId('screen-native-metrics'));
  expect(view.getByText(notice.title)).toBeOnTheScreen();
  expect(view.getByText(notice.body)).toBeOnTheScreen();
  expect(server.called('POST /notifications/read')[0].body).toEqual({ ids: ['ntf_metric'] });
  expect(server.called('POST /admin/metrics/query')[0].body).toMatchObject({ maxResults: 50, query: { metric: 'requests.count', bucket: 'Day', compare: true } });
  expect(WebBrowser.openBrowserAsync).not.toHaveBeenCalled();
  expect(Linking.openURL).not.toHaveBeenCalled();
  await act(async () => nativeRouter.back());
  expect(router.getPathname()).toBe('/notifications');
});

test('catalogs and observations follow continuation tokens and declared page selection', async () => {
  const server = routes({
    'POST /admin/metrics/pages/enumerate': ({ body }) => body.continuationToken ? enumerationResult([growth]) : enumerationResult([page], 'pages-next'),
    'POST /admin/metrics/enumerate': ({ body }) => body.continuationToken ? enumerationResult([signupDefinition]) : enumerationResult([definition], 'definitions-next'),
    'POST /admin/metrics/query': ({ body }) => body.continuationToken ? enumerationResult([observation('point_2', 30, { period: 'Previous' })]) : enumerationResult([observation('point_1', 42)], 'observations-next')
  });
  await renderRouter(APP_DIR, { initialUrl: '/admin/metrics/health' });
  await screen.findByTestId('metric-requests-latest');
  expect(server.called('POST /admin/metrics/pages/enumerate')[1].body.continuationToken).toBe('pages-next');
  expect(server.called('POST /admin/metrics/enumerate')[1].body.continuationToken).toBe('definitions-next');
  await fireEvent.press(screen.getByTestId('metric-requests-more'));
  await waitFor(() => expect(server.called('POST /admin/metrics/query')[1].body.continuationToken).toBe('observations-next'));
  await fireEvent.press(screen.getByTestId('metric-requests-toggle'));
  expect(await screen.findByTestId('metric-observation-point_2')).toHaveTextContent(new RegExp(strings.nativeMetrics.previous));
  await fireEvent.press(within(screen.getByTestId('metrics-pages')).getByText('Growth'));
  expect(await screen.findByTestId('metric-signups')).toBeOnTheScreen();
  expect(server.called('POST /admin/metrics/query').some((call) => call.body.query.metric === 'users.new')).toBe(true);
});

test('non-administrators get native denial without requesting metrics', async () => {
  const server = routes({}, makeUser());
  await renderRouter(APP_DIR, { initialUrl: '/admin/metrics/health' });
  expect(await screen.findByTestId('native-metrics-denied')).toHaveTextContent(new RegExp(strings.nativeMetrics.deniedTitle));
  expect(server.called('POST /admin/metrics/pages/enumerate')).toHaveLength(0);
  expect(WebBrowser.openBrowserAsync).not.toHaveBeenCalled();
});

test('server permission revocation and retry remain native', async () => {
  let denied = true;
  const server = routes({ 'POST /admin/metrics/pages/enumerate': () => denied ? { status: 403, body: { error: 'Forbidden', message: 'Denied' } } : enumerationResult([page]) });
  await renderRouter(APP_DIR, { initialUrl: '/admin/metrics/health' });
  expect(await screen.findByTestId('native-metrics-error')).toHaveTextContent(new RegExp(strings.nativeMetrics.deniedTitle));
  denied = false;
  await fireEvent.press(within(screen.getByTestId('native-metrics-error')).getByText(strings.common.retry));
  expect(await screen.findByTestId('metric-requests-latest')).toHaveTextContent(/42/);
  expect(server.called('POST /admin/metrics/pages/enumerate')).toHaveLength(2);
});

test('cold metric push retains displayed text after session restoration', async () => {
  Notifications.getLastNotificationResponseAsync.mockResolvedValue({ notification: { request: { identifier: 'cold-metric', content: { title: notice.title, body: notice.body, data: { notificationId: notice.id, path: notice.path } } } } });
  routes();
  const router = await renderRouter(APP_DIR, { initialUrl: '/' });
  await waitFor(() => expect(router.getPathname()).toBe('/admin/metrics/health'));
  expect(await screen.findByTestId('metric-requests-latest')).toHaveTextContent(/42/);
  expect(within(screen.getByTestId('screen-native-metrics')).getByText(notice.body)).toBeOnTheScreen();
  expect(WebBrowser.openBrowserAsync).not.toHaveBeenCalled();
  expect(Linking.openURL).not.toHaveBeenCalled();
});

test('unsupported staff destinations show full native notification details', async () => {
  const staff = makeNotification('ntf_staff', { path: '/admin/content-review', title: 'Content needs review', body: 'A member reported a post.' });
  routes({ 'POST /notifications/enumerate': enumerationResult([staff]) });
  const router = await renderRouter(APP_DIR, { initialUrl: '/notifications' });
  await fireEvent.press(await screen.findByTestId('notification-ntf_staff-open'));
  await waitFor(() => expect(router.getPathname()).toBe('/notification-detail'));
  expect(await screen.findByTestId('screen-notification-detail')).toHaveTextContent(new RegExp(staff.body));
  expect(WebBrowser.openBrowserAsync).not.toHaveBeenCalled();
  expect(Linking.openURL).not.toHaveBeenCalled();
});

test('unsafe paths and malformed pushes never become browser destinations', () => {
  for (const path of ['https://evil.test/admin/metrics/health', '//evil.test/admin/metrics/health', '/admin/metrics/../../other', '/admin/metrics/a%2fb']) expect(resolveNotificationPath(path)).toBeNull();
  expect(notificationDestination({ path: '//evil.test', title: 'No' })).toBe('/notifications');
  expect(pushTarget({ notification: { request: { content: { data: 'invalid' } } } })).toBeNull();
});

test('suppressed and unfinished observations remain explicitly unavailable in the native view', async () => {
  routes({ 'POST /admin/metrics/query': enumerationResult([
    observation('point_hidden', 999, { suppressed: true }),
    observation('point_partial', 0, { complete: false }),
    observation('point_missing', null)
  ]) });
  await renderRouter(APP_DIR, { initialUrl: '/admin/metrics/health' });
  await fireEvent.press(await screen.findByTestId('metric-requests-toggle'));
  expect(screen.getByTestId('metric-observation-point_hidden')).toHaveTextContent(/Hidden for privacy/);
  expect(screen.getByTestId('metric-observation-point_partial')).toHaveTextContent(/Incomplete/);
  expect(screen.getByTestId('metric-observation-point_missing')).toHaveTextContent(/Not available/);
  expect(within(screen.getByTestId('metric-requests')).queryByText('999')).toBeNull();
});

test('changing the native time range requests a bounded window and renders empty results', async () => {
  const server = routes({ 'POST /admin/metrics/query': enumerationResult([]) });
  await renderRouter(APP_DIR, { initialUrl: '/admin/metrics/health' });
  expect(await screen.findByText(strings.nativeMetrics.empty)).toBeOnTheScreen();
  await fireEvent.press(screen.getByTestId('metrics-range-30'));
  await waitFor(() => expect(server.called('POST /admin/metrics/query')).toHaveLength(2));
  const request = server.called('POST /admin/metrics/query')[1].body;
  expect(Date.parse(request.query.to) - Date.parse(request.query.from)).toBe(30 * 86400000);
  expect(request.maxResults).toBe(50);
});


test('metric calendar controls bucket labels independently of device locale', () => {
  const value = '2026-11-24T00:00:00Z';
  expect(metricDate(value, 'UTC')).toContain('24');
  expect(metricDate(value, 'Pacific/Honolulu')).toContain('23');
  expect(metricDate('invalid', 'UTC')).toBe(strings.nativeMetrics.unavailable);
});
