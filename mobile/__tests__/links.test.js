import { act, fireEvent, render, screen } from '@testing-library/react-native';
import { router } from 'expo-router';
import { Linking } from 'react-native';
import * as WebBrowser from 'expo-web-browser';
import * as Sharing from 'expo-sharing';
import { File } from 'expo-file-system';
import { fetch as nativeFetch } from 'expo/fetch';
import { resolveApiUrl } from '../src/config';
import { resolveAppLink, safeWebUrl } from '../src/linkTargets';
import { openAppLink, openExternal } from '../src/links';
import { shareDownloadedFile } from '../src/downloads';
import ExternalContentScreen from '../app/external-content';

const fixture = { domains: { web: 'community.test', api: 'api.community.test' }, documents: [{ key: 'agreement', slug: 'terms', kind: 'Terms' }] };
let mockParams = {};
const mockRouter = { push: jest.fn(), replace: jest.fn(), canGoBack: () => true, back: jest.fn() };
jest.mock('expo/fetch', () => ({ fetch: jest.fn() }));
jest.mock('expo-router', () => ({
  router: { push: jest.fn() },
  useRouter: () => mockRouter,
  useLocalSearchParams: () => mockParams,
  useFocusEffect: () => {},
  Stack: { Screen: () => null }
}));
jest.mock('../src/property', () => ({ useProperty: () => fixture }));

beforeEach(() => { jest.clearAllMocks(); mockParams = {}; });

it.each([
  ['/app/feed?comment=cmt_1', '/feed?comment=cmt_1'],
  ['https://community.test/app/members/usr_1', '/members/usr_1'],
  ['https://api.community.test/admin/metrics/health', '/admin/metrics/health'],
  ['/terms', '/documents/agreement'],
  ['/privacy', '/documents/privacy'],
  ['/directory', '/map?section=places'],
  ['/events', '/map?section=events'],
  ['https://community.test/app/check-in?t=Event&id=evt_1', '/check-in?t=Event&id=evt_1'],
  ['https://community.test/app/training', '/training'],
  ['https://community.test/app/training/library/techniques', '/training/library/techniques'],
  ['https://community.test/app/training/lessons/les_1', '/lessons/les_1'],
  ['https://community.test/app/training/curricula/cur_1', '/training/curricula/cur_1']
])('maps %s to native %s', (href, path) => expect(resolveAppLink(href, fixture)).toEqual({ kind: 'native', path }));

it('does not trust lookalike hosts, unsafe protocols, credentials, or ambiguous paths', () => {
  expect(resolveAppLink('https://community.test.evil.test/app/feed', fixture).kind).toBe('web');
  expect(resolveAppLink('/admin/unimplemented', fixture)).toEqual({ kind: 'unsupported' });
  for (const url of ['javascript:alert(1)', '//evil.test', 'https://user:password@community.test', 'https://community.test\\@evil.test', '/\u0000feed']) expect(resolveAppLink(url, fixture)).toBeNull();
  expect(safeWebUrl('file:///etc/passwd')).toBeNull();
});

it('routes native and external pages without any browser or Linking call', async () => {
  const external = jest.spyOn(Linking, 'openURL').mockResolvedValue(true);
  await openAppLink('https://community.test/app/feed', { property: fixture, router: mockRouter });
  expect(mockRouter.push).toHaveBeenLastCalledWith('/feed');
  await openAppLink('https://outside.test/article');
  expect(router.push).toHaveBeenLastCalledWith({ pathname: '/external-content', params: { url: 'https://outside.test/article' } });
  await openAppLink('/admin/unimplemented', { property: fixture, router: mockRouter });
  expect(mockRouter.push).toHaveBeenLastCalledWith({ pathname: '/external-content', params: { unavailable: '1' } });
  expect(external).not.toHaveBeenCalled();
  expect(WebBrowser.openBrowserAsync).not.toHaveBeenCalled();
  await openExternal('https://outside.test/article');
  expect(external).not.toHaveBeenCalled();
  await openExternal('https://apps.apple.com/account/subscriptions');
  expect(external).toHaveBeenCalledWith('https://apps.apple.com/account/subscriptions');
  external.mockRestore();
});

it('isolates external content and keeps redirects and popup navigation inside the app', async () => {
  mockParams = { url: 'https://outside.test/article' };
  const external = jest.spyOn(Linking, 'openURL').mockResolvedValue(true);
  await render(<ExternalContentScreen />);
  const view = screen.getByTestId('web-content');
  expect(view.props.source).toEqual({ uri: 'https://outside.test/article' });
  expect(view.props.originWhitelist).toEqual(['*']);
  expect(view.props.sharedCookiesEnabled).toBe(false);
  expect(view.props.incognito).toBe(true);
  let allowed;
  await act(() => { allowed = view.props.onShouldStartLoadWithRequest({ url: 'intent://escape' }); });
  expect(allowed).toBe(false);
  await act(() => { allowed = view.props.onShouldStartLoadWithRequest({ url: 'https://community.test/app/feed' }); });
  expect(allowed).toBe(false);
  expect(mockRouter.replace).toHaveBeenCalledWith('/feed');
  await act(() => { view.props.onOpenWindow({ nativeEvent: { targetUrl: 'https://outside.test/next' } }); });
  expect(screen.getByTestId('web-content').props.source).toEqual({ uri: 'https://outside.test/next' });
  await act(() => { screen.getByTestId('web-content').props.onError({}); });
  expect(screen.getByTestId('web-content-error')).toBeTruthy();
  await fireEvent.press(screen.getByRole('button', { name: /Retry/ }));
  expect(screen.getByTestId('web-content')).toBeTruthy();
  await fireEvent.press(screen.getByRole('button', { name: 'Close' }));
  expect(mockRouter.back).toHaveBeenCalled();
  expect(external).not.toHaveBeenCalled();
  expect(WebBrowser.openBrowserAsync).not.toHaveBeenCalled();
  external.mockRestore();
});

it('rejects an unsafe initial URL before creating the WebView', async () => {
  mockParams = { url: 'file:///etc/passwd' };
  await render(<ExternalContentScreen />);
  expect(screen.queryByTestId('web-content')).toBeNull();
  expect(screen.getByTestId('link-unavailable')).toBeTruthy();
});

it('shares a downloaded local file and never falls back to a browser on failure', async () => {
  const download = jest.spyOn(File, 'downloadFileAsync');
  await shareDownloadedFile('https://api.community.test/export?sig=abc', { name: 'export.zip', mimeType: 'application/zip' });
  expect(download).toHaveBeenCalledWith('https://api.community.test/export?sig=abc', expect.any(File), { idempotent: true });
  expect(Sharing.shareAsync).toHaveBeenCalledWith(expect.stringMatching(/export\.zip$/), { mimeType: 'application/zip' });
  download.mockRejectedValueOnce(new Error('network down'));
  await expect(shareDownloadedFile('https://api.community.test/export')).rejects.toThrow('network down');
  expect(WebBrowser.openBrowserAsync).not.toHaveBeenCalled();
  download.mockRestore();
});

it('refuses download credentials for untrusted origins and refuses credentialed redirects', async () => {
  await expect(shareDownloadedFile('https://untrusted.test/file', { headers: { Authorization: 'Bearer private' } })).rejects.toThrow('Untrusted download origin');
  expect(nativeFetch).not.toHaveBeenCalled();
  nativeFetch.mockRejectedValueOnce(new Error('Redirect is not allowed'));
  const url = resolveApiUrl() + '/api/v1/media/file';
  await expect(shareDownloadedFile(url, { headers: { Authorization: 'Bearer private' } })).rejects.toThrow('Redirect is not allowed');
  expect(nativeFetch).toHaveBeenCalledWith(url, { headers: { Authorization: 'Bearer private' }, credentials: 'omit', redirect: 'error' });
  expect(Sharing.shareAsync).not.toHaveBeenCalled();
});

it('streams a trusted authenticated file and retains it for the share recipient', async () => {
  const chunks = [new Uint8Array([1, 2]), new Uint8Array([3])];
  const reader = { read: jest.fn(async () => chunks.length ? { done: false, value: chunks.shift() } : { done: true }), cancel: jest.fn(async () => {}) };
  nativeFetch.mockResolvedValueOnce({ ok: true, body: { getReader: () => reader } });
  const url = resolveApiUrl() + '/api/v1/media/file';
  await shareDownloadedFile(url, { name: 'lesson.pdf', headers: { Authorization: 'Bearer private' }, mimeType: 'application/pdf' });
  expect(reader.read).toHaveBeenCalledTimes(3);
  expect(reader.cancel).toHaveBeenCalled();
  expect(Sharing.shareAsync).toHaveBeenCalledWith(expect.stringMatching(/lesson\.pdf$/), { mimeType: 'application/pdf' });
  const sharedUri = Sharing.shareAsync.mock.calls[0][0];
  expect(new File(sharedUri).exists).toBe(true);
});

it('keeps a page visible after subresource errors and resets on a new route URL', async () => {
  mockParams = { url: 'https://outside.test/article' };
  const rendered = await render(<ExternalContentScreen />);
  await act(() => { screen.getByTestId('web-content').props.onHttpError({ nativeEvent: { statusCode: 404, url: 'https://outside.test/missing-image.png' } }); });
  expect(screen.getByTestId('web-content')).toBeTruthy();
  await act(() => { screen.getByTestId('web-content').props.onHttpError({ nativeEvent: { statusCode: 503, url: 'https://outside.test/article' } }); });
  expect(screen.getByTestId('web-content-error')).toBeTruthy();
  mockParams = { url: 'https://second.test/page' };
  await rendered.rerender(<ExternalContentScreen />);
  expect(screen.getByTestId('web-content').props.source).toEqual({ uri: 'https://second.test/page' });
});

it('treats configured redirect aliases as first-party without trusting lookalike hosts', () => {
  const aliased = { ...fixture, domains: { ...fixture.domains, redirects: ['members.community.test'] } };
  expect(resolveAppLink('https://members.community.test/app/feed', aliased)).toEqual({ kind: 'native', path: '/feed' });
  expect(resolveAppLink('https://members.community.test.evil.test/app/feed', aliased).kind).toBe('web');
});
