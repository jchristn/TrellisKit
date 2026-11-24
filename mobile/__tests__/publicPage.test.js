import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { Linking, Share } from 'react-native';
import { publicPageUrl, slugProblem } from '@trelliskit/shared';
import { useSlugAvailability } from '../src/hooks/useSlugAvailability';
import { slugStatusText } from '../src/components/profile/PublicPageEditor';
import strings from '../src/strings';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { property } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');
const t = strings.publicPage;
const urlOf = (slug) => publicPageUrl(property, 'Profile', slug);
const pageOf = (overrides = {}) => {
  const slug = overrides.slug === undefined ? null : overrides.slug;
  return { enabled: false, slug, suggestedSlug: 'test-member', path: null, url: slug ? urlOf(slug) : null, ...overrides };
};

/** The fake server for Edit profile with the member's public page and a slug check answer. */
function serve({ page = pageOf(), check, put, propertyDoc } = {}) {
  const user = makeUser();
  return installFakeServer({
    ...signedInAs(user),
    'GET /auth/me': user,
    ...(propertyDoc ? { 'GET /property': propertyDoc } : {}),
    'GET /me/public-page': page,
    'POST /public-pages/slugs/check': check || ((call) => ({ kind: 'Profile', slug: call.body.slug, valid: true, available: true, problem: null, path: null, url: urlOf(call.body.slug) })),
    'PUT /me/public-page':
      put ||
      ((call) => {
        const slug = call.body.slug || 'test-member';
        return { enabled: Boolean(call.body.enabled), slug, suggestedSlug: 'test-member', path: null, url: urlOf(slug) };
      })
  });
}

describe('public profile page', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => {
    delete global.fetch;
    jest.restoreAllMocks();
  });

  test('is off by default; turning it on prefills the suggested address, checks it, and saves', async () => {
    const server = serve();
    await renderRouter(APP_DIR, { initialUrl: '/account/edit-profile' });
    const toggle = await screen.findByTestId('public-page-toggle');
    expect(toggle.props.value).toBe(false);
    expect(screen.queryByTestId('public-page-slug')).toBeNull();
    expect(screen.getByTestId('public-page-save')).toBeDisabled();

    await fireEvent(toggle, 'valueChange', true);
    expect(screen.getByTestId('public-page-slug').props.value).toBe('test-member');
    expect(screen.getByTestId('public-page-slug').props.autoCapitalize).toBe('none');
    expect(screen.getByTestId('public-page-url')).toHaveTextContent(urlOf('test-member'));
    expect(screen.getByTestId('public-page-url-pending')).toBeOnTheScreen();
    expect(screen.queryByTestId('public-page-share')).toBeNull();
    await waitFor(() => expect(screen.getByTestId('public-page-slug-status')).toHaveTextContent(t.available));
    expect(server.called('POST /public-pages/slugs/check')[0].body).toEqual({ kind: 'Profile', slug: 'test-member' });

    await fireEvent.press(screen.getByTestId('public-page-save'));
    expect(await screen.findByTestId('public-page-saved')).toHaveTextContent(t.saved);
    expect(server.called('PUT /me/public-page')[0].body).toEqual({ enabled: true, slug: 'test-member' });
    expect(screen.getByTestId('public-page-slug-status')).toHaveTextContent(t.current);
    expect(screen.queryByTestId('public-page-url-pending')).toBeNull();

    const share = jest.spyOn(Share, 'share').mockResolvedValue({ action: 'sharedAction' });
    const open = jest.spyOn(Linking, 'openURL').mockResolvedValue(true);
    await fireEvent.press(screen.getByTestId('public-page-share'));
    expect(share).toHaveBeenCalledWith({ message: urlOf('test-member'), url: urlOf('test-member') });
    await fireEvent.press(screen.getByTestId('public-page-open'));
    expect(open).toHaveBeenCalledWith(urlOf('test-member'));
  });

  test('typed addresses are normalized and the shared rules explain what is wrong', async () => {
    serve({ page: pageOf({ enabled: true, slug: 'test-member' }) });
    await renderRouter(APP_DIR, { initialUrl: '/account/edit-profile' });
    const field = await screen.findByTestId('public-page-slug');
    expect(screen.getByTestId('public-page-share')).toBeOnTheScreen();

    await fireEvent.changeText(field, 'My Page');
    expect(screen.getByTestId('public-page-slug').props.value).toBe('my-page');
    expect(screen.queryByTestId('public-page-share')).toBeNull();

    await fireEvent.changeText(field, 'ab');
    expect(screen.getByTestId('public-page-slug-error')).toHaveTextContent(t.problems.length);
    expect(screen.getByTestId('public-page-save')).toBeDisabled();
    expect(screen.queryByTestId('public-page-url')).toBeNull();

    await fireEvent.changeText(field, '-abc');
    expect(screen.getByTestId('public-page-slug-error')).toHaveTextContent(t.problems.format);

    await fireEvent.changeText(field, 'admin');
    expect(screen.getByTestId('public-page-slug-error')).toHaveTextContent(t.problems.reserved);

    await fireEvent.changeText(field, '');
    expect(screen.getByTestId('public-page-slug-error')).toHaveTextContent(t.problems.required);
  });

  test('an address someone else uses shows taken from the check and from a 409 on save', async () => {
    serve({
      check: (call) => ({ kind: 'Profile', slug: call.body.slug, valid: true, available: call.body.slug !== 'taken-page', problem: call.body.slug === 'taken-page' ? 'taken' : null, path: null, url: null }),
      put: { status: 409, body: { error: 'SlugInUse', message: 'That address is in use.', fieldErrors: { slug: ['That address is in use.'] } } }
    });
    await renderRouter(APP_DIR, { initialUrl: '/account/edit-profile' });
    await fireEvent(await screen.findByTestId('public-page-toggle'), 'valueChange', true);
    await fireEvent.changeText(screen.getByTestId('public-page-slug'), 'taken-page');
    await waitFor(() => expect(screen.getByTestId('public-page-slug-error')).toHaveTextContent(t.problems.taken));
    expect(screen.getByTestId('public-page-save')).toBeDisabled();

    await fireEvent.changeText(screen.getByTestId('public-page-slug'), 'free-page');
    await waitFor(() => expect(screen.getByTestId('public-page-slug-status')).toHaveTextContent(t.available));
    await fireEvent.press(screen.getByTestId('public-page-save'));
    expect(await screen.findByTestId('public-page-slug-error')).toHaveTextContent('That address is in use.');
  });

  test('turning the page off saves enabled false only', async () => {
    const server = serve({ page: pageOf({ enabled: true, slug: 'test-member' }) });
    await renderRouter(APP_DIR, { initialUrl: '/account/edit-profile' });
    await fireEvent(await screen.findByTestId('public-page-toggle'), 'valueChange', false);
    expect(screen.queryByTestId('public-page-slug')).toBeNull();
    await fireEvent.press(screen.getByTestId('public-page-save'));
    await screen.findByTestId('public-page-saved');
    expect(server.called('PUT /me/public-page')[0].body).toEqual({ enabled: false });
  });

  test('hidden when the property turns public pages off or the server does not offer them', async () => {
    const off = serve({ propertyDoc: { ...property, publicPages: { enabled: false } } });
    await renderRouter(APP_DIR, { initialUrl: '/account/edit-profile' });
    await screen.findByTestId('profile-form');
    await waitFor(() => expect(off.called('GET /property').length).toBeGreaterThan(0));
    await waitFor(() => expect(screen.queryByTestId('public-page')).toBeNull());
  });

  test('hidden when GET /me/public-page is not found', async () => {
    const user = makeUser();
    installFakeServer({ ...signedInAs(user), 'GET /auth/me': user });
    await renderRouter(APP_DIR, { initialUrl: '/account/edit-profile' });
    await screen.findByTestId('profile-form');
    expect(screen.queryByTestId('public-page')).toBeNull();
  });

  test('a failed load offers Retry, which loads the settings', async () => {
    const user = makeUser();
    let fail = true;
    installFakeServer({
      ...signedInAs(user),
      'GET /auth/me': user,
      'GET /me/public-page': () => (fail ? { status: 500, body: { error: 'InternalError', message: 'Boom' } } : pageOf())
    });
    await renderRouter(APP_DIR, { initialUrl: '/account/edit-profile' });
    expect(await screen.findByTestId('public-page-load-error')).toHaveTextContent(t.loadError);
    fail = false;
    await fireEvent.press(screen.getByTestId('public-page-retry'));
    expect(await screen.findByTestId('public-page-toggle')).toBeOnTheScreen();
  });

  test('status text covers every status the hook returns', () => {
    for (const problem of ['required', 'length', 'format', 'reserved', 'taken']) expect(slugStatusText(problem).error).toBe(t.problems[problem]);
    expect(slugStatusText('available').text).toBe(t.available);
    expect(slugStatusText('checking').text).toBe(t.checking);
    expect(slugStatusText('error').text).toBe(t.checkError);
    expect(typeof useSlugAvailability).toBe('function');
    expect(slugProblem('fine-slug')).toBeNull();
  });
});
