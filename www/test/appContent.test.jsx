import { afterEach, describe, expect, it } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { BANNER_DISMISSALS_STORAGE_KEY, ApiError, createAppContentStore, setRemoteStrings } from '@trelliskit/shared';
import StringsView, { buildStringsQuery, stringBadges, stringValueProblem } from '../src/views/admin/StringsView.jsx';
import BannersView, { bannerBody, bannerForm, buildBannersQuery } from '../src/views/admin/BannersView.jsx';
import AssetsView, { assetKeyProblem, buildAssetsQuery } from '../src/views/admin/AssetsView.jsx';
import SymbolicatePanel, { frameLocation } from '../src/views/admin/SymbolicatePanel.jsx';
import { draftCounts } from '../src/views/admin/ContentPublishPanel.jsx';
import RequireAdmin from '../src/views/admin/RequireAdmin.jsx';
import { groupedAdminRoutes } from '../src/views/admin/adminRoutes.js';
import { AdminAuthProvider, useAdminAuth } from '../src/context/AdminAuthContext.jsx';
import { AuthProvider } from '../src/context/AuthContext.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { AppContentProvider, VISITOR_ID_KEY, useFlag, useKillSwitch, useRemoteStringsRevision, visitorId } from '../src/context/AppContentContext.jsx';
import BannerSlot, { placementForPath } from '../src/components/BannerSlot.jsx';
import { noteApiFailure } from '../src/utils/appContentErrors.js';
import strings from '../src/strings.js';
import { authResponse, createFetch, json, makeMe, renderAdmin } from './helpers.jsx';
import { enumPage } from './engagementHelpers.jsx';

function adminFetch(handler) {
  return createFetch((call) => {
    if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
    if (call.path === '/api/v1/admin/app/drafts/enumerate') return json({ contentVersion: 7, objects: [], endOfResults: true, totalRecords: 1, counts: { flags: 1, strings: 2, banners: 0, assets: 0 } });
    if (call.path === '/api/v1/labels/enumerate' || call.path === '/api/v1/tags/enumerate') return json(enumPage([]));
    return handler(call);
  });
}

afterEach(() => {
  setRemoteStrings({});
});

const stringRow = (extra = {}) => ({ key: 'web.common.retry', client: 'web', defaultValue: 'Retry {name}', tokens: ['name'], maxLength: 200, id: null, value: null, publishedValue: null, notes: null, overridden: false, state: null, hasDraftChanges: false, pendingDelete: false, publishedInVersion: null, eTag: null, labels: [], tags: {}, updatedUtc: null, ...extra });
const banner = (extra = {}) => ({ id: 'ban_1', title: 'Maintenance', body: 'Read-only on **Sunday**.', style: 'Warning', placement: 'Global', dismissible: true, priority: 10, startsUtc: null, endsUtc: null, link: null, imageAssetId: null, clients: ['mobile', 'web'], rules: [], contentHash: 'h1', state: 'Published', hasDraftChanges: false, publishedInVersion: 3, eTag: '"bnv_1"', labels: [], tags: {}, createdUtc: '2026-11-01T00:00:00Z', updatedUtc: '2026-11-01T00:00:00Z', ...extra });
const asset = (extra = {}) => ({ id: 'ras_1', key: 'onboarding.welcome', contentType: 'image/webp', sizeBytes: 2048, width: 1200, height: 800, sha256: 'c0ffee', url: 'https://cdn.example.test/c0ffee', altText: 'Welcome', darkVariant: null, state: 'Draft', hasDraftChanges: true, publishedInVersion: null, eTag: '"rav_1"', labels: [], tags: {}, createdUtc: '2026-11-01T00:00:00Z', updatedUtc: '2026-11-01T00:00:00Z', ...extra });

describe('content admin helpers', () => {
  it('builds enumeration queries with boolean filters and orderings, and counts drafts of every type', () => {
    expect(buildStringsQuery({ search: 'retry', client: 'web', overridden: 'true', hasDraftChanges: 'false' }, { pageNumber: 1, pageSize: 25, meta: { labels: ['copy'], tags: { team: 'growth' } } }))
      .toMatchObject({ maxResults: 25, ordering: 'KeyAscending', search: 'retry', labels: ['copy'], tags: { team: 'growth' }, filters: { client: 'web', overridden: true, hasDraftChanges: false } });
    expect(buildBannersQuery({ active: 'true', placement: 'Home' }, { pageNumber: 2, pageSize: 10 })).toMatchObject({ skip: 10, ordering: 'PriorityDescending', filters: { active: true, placement: 'Home' } });
    expect(buildAssetsQuery({ state: 'Draft' }, { pageNumber: 1, pageSize: 50 })).toMatchObject({ ordering: 'KeyAscending', filters: { state: 'Draft' } });
    expect(draftCounts({ totalRecords: 1, counts: { flags: 1, strings: 2, banners: 3, assets: 4 } }).total).toBe(10);
    expect(draftCounts({ totalRecords: 2 })).toMatchObject({ flags: 2, total: 2 });
    expect(stringValueProblem('Hi {name}', { tokens: ['name'], maxLength: 20 })).toBeNull();
    expect(stringValueProblem('Hi {count}', { tokens: ['name'], maxLength: 20 })).toContain('{count}');
    expect(stringBadges(stringRow({ overridden: true, hasDraftChanges: true, pendingDelete: true })).map((b) => b.label)).toEqual(['Overridden', 'Removal pending']);
    const form = bannerForm(null);
    Object.assign(form, { title: 'Hello', style: 'Critical', dismissible: true, linkKind: 'path', linkTarget: '/app/support', linkLabel: 'Help' });
    expect(bannerBody(form)).toMatchObject({ title: 'Hello', style: 'Critical', dismissible: false, link: { label: 'Help', path: '/app/support', url: null }, clients: ['web', 'mobile'], rules: [] });
    expect(assetKeyProblem('onboarding.welcome')).toBeNull();
    expect(assetKeyProblem('Bad-Key')).toBe(strings.appContent.admin.assets.keyInvalid);
    expect(frameLocation({ source: 'src/a.jsx', line: 4, column: 2 })).toBe('src/a.jsx:4:2');
    expect(placementForPath('/app')).toBe('Home');
    expect(placementForPath('/app/forums/general')).toBe('Forums');
    expect(placementForPath('/sign-in')).toBe('SignIn');
    expect(placementForPath('/app/notifications')).toBeNull();
  });
});

describe('strings page', () => {
  it('lists the catalog, validates tokens, previews, and saves a draft override', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/app/strings/enumerate') return json(enumPage([stringRow()], { totalRecords: 1 }));
      if (call.path === '/api/v1/admin/app/strings/web.common.retry' && call.method === 'GET') return json({ string: stringRow(), history: { objects: [stringRow({ value: 'Again', publishedInVersion: 2, updatedUtc: '2026-11-02T00:00:00Z' })], totalRecords: 1, enumerate: '' } });
      if (call.path === '/api/v1/admin/app/strings/web.common.retry' && call.method === 'PUT') return json(stringRow({ id: 'rst_1', value: 'Try {name} again', hasDraftChanges: true }));
      return undefined;
    });
    renderAdmin(<StringsView />, { fetchImpl, path: '/admin/strings', routePath: '/admin/strings' });
    expect(await screen.findByRole('button', { name: 'web.common.retry' })).toBeInTheDocument();
    const list = calls.find((c) => c.path === '/api/v1/admin/app/strings/enumerate');
    expect(list.method).toBe('POST');
    expect(list.body.ordering).toBe('KeyAscending');
    expect(await screen.findByText('3 pending changes')).toBeInTheDocument();
    expect(screen.getByText('(1 flag, 2 strings)')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'web.common.retry' }));
    const editor = await screen.findByRole('dialog', { name: 'Edit web.common.retry' });
    expect(within(editor).getByTestId('string-default')).toHaveTextContent('Retry {name}');
    const input = within(editor).getByLabelText('Override');
    await userEvent.clear(input);
    fireEvent.change(input, { target: { value: 'Try {count}' } });
    expect(within(editor).getByRole('alert')).toHaveTextContent('Unknown tokens {count}');
    expect(within(editor).getByRole('button', { name: 'Save draft' })).toBeDisabled();
    fireEvent.change(input, { target: { value: 'Try {name} again' } });
    expect(within(editor).getByTestId('string-preview')).toHaveTextContent(/^Try .+ again$/);
    expect(within(editor).getByTestId('string-preview')).not.toHaveTextContent('{name}');
    expect(within(editor).getByText('Again')).toBeInTheDocument();
    await userEvent.click(within(editor).getByRole('button', { name: 'Save draft' }));
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT' && c.path === '/api/v1/admin/app/strings/web.common.retry').body).toMatchObject({ value: 'Try {name} again', labels: [], tags: {} }));
  });

  it('records a removal for a published override', async () => {
    const published = stringRow({ id: 'rst_1', value: 'Again', publishedValue: 'Again', overridden: true, state: 'Published' });
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/app/strings/enumerate') return json(enumPage([published]));
      if (call.path === '/api/v1/admin/app/strings/web.common.retry' && call.method === 'GET') return json({ string: published, history: { objects: [], totalRecords: 0 } });
      if (call.path === '/api/v1/admin/app/strings/web.common.retry' && call.method === 'DELETE') return json({ ...published, pendingDelete: true, hasDraftChanges: true });
      return undefined;
    });
    renderAdmin(<StringsView />, { fetchImpl, path: '/admin/strings', routePath: '/admin/strings' });
    await userEvent.click(await screen.findByRole('button', { name: 'Edit web.common.retry' }));
    const editor = await screen.findByRole('dialog', { name: 'Edit web.common.retry' });
    await userEvent.click(await within(editor).findByRole('button', { name: 'Remove override' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.path === '/api/v1/admin/app/strings/web.common.retry')).toBe(true));
  });
});

describe('banners page', () => {
  it('creates a critical banner (never dismissible) and updates with If-Match', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/app/banners/enumerate') return json(enumPage([banner()], { totalRecords: 1 }));
      if (call.path === '/api/v1/admin/app/banners' && call.method === 'POST') return json(banner({ id: 'ban_2', state: 'Draft' }), 201);
      if (call.path === '/api/v1/admin/app/banners/ban_1' && call.method === 'GET') return json({ banner: banner(), published: banner(), history: { objects: [], totalRecords: 0 } });
      if (call.path === '/api/v1/admin/app/banners/ban_1' && call.method === 'PUT') return json(banner({ priority: 20 }));
      return undefined;
    });
    renderAdmin(<BannersView />, { fetchImpl, path: '/admin/banners', routePath: '/admin/banners' });
    expect(await screen.findByRole('button', { name: 'Maintenance' })).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/app/banners/enumerate').body.ordering).toBe('PriorityDescending');

    await userEvent.click(screen.getByRole('button', { name: 'New banner' }));
    let editor = await screen.findByRole('dialog', { name: 'New banner' });
    await userEvent.click(within(editor).getByRole('button', { name: 'Save draft' }));
    expect(within(editor).getByText('Use 1 to 80 characters.')).toBeInTheDocument();
    await userEvent.type(within(editor).getByLabelText('Title'), 'Outage');
    await userEvent.selectOptions(within(editor).getByLabelText('Style'), 'Critical');
    expect(within(editor).getByRole('checkbox', { name: 'Members can dismiss it' })).toBeDisabled();
    const preview = within(editor).getByTestId('banner-preview');
    expect(within(preview).getByText('Outage')).toBeInTheDocument();
    expect(within(preview).queryByRole('button', { name: /Dismiss/ })).toBeNull();
    await userEvent.click(within(editor).getByRole('button', { name: 'Save draft' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/app/banners' && c.method === 'POST').body).toMatchObject({ title: 'Outage', style: 'Critical', dismissible: false, placement: 'Global' }));

    await userEvent.click(await screen.findByRole('button', { name: 'Maintenance' }));
    editor = await screen.findByRole('dialog', { name: 'Edit Maintenance' });
    const priority = within(editor).getByLabelText('Priority');
    await userEvent.clear(priority);
    await userEvent.type(priority, '20');
    await userEvent.click(within(editor).getByRole('button', { name: 'Save draft' }));
    await waitFor(() => {
      const put = calls.find((c) => c.path === '/api/v1/admin/app/banners/ban_1' && c.method === 'PUT');
      expect(put.body).toMatchObject({ priority: 20 });
      expect(put.headers['If-Match']).toBe('"bnv_1"');
    });
  });
});

describe('assets page', () => {
  it('shows the grid, creates an asset, and uploads content', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/app/assets/enumerate') return json(enumPage([asset()], { totalRecords: 1 }));
      if (call.path === '/api/v1/admin/app/assets' && call.method === 'POST') return json(asset({ id: 'ras_2', key: 'home.hero' }), 201);
      if (call.path === '/api/v1/admin/app/assets/ras_1/content') return json(asset());
      return undefined;
    });
    renderAdmin(<AssetsView />, { fetchImpl, path: '/admin/assets', routePath: '/admin/assets' });
    const card = await screen.findByRole('article', { name: 'onboarding.welcome' });
    expect(within(card).getByRole('img', { name: 'Welcome' })).toHaveAttribute('src', 'https://cdn.example.test/c0ffee');

    await userEvent.click(screen.getByRole('button', { name: 'New asset' }));
    const dialog = await screen.findByRole('dialog', { name: 'New remote asset' });
    await userEvent.type(within(dialog).getByLabelText('Key'), 'Bad Key');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save draft' }));
    expect(await within(dialog).findAllByText(strings.appContent.admin.assets.keyInvalid)).not.toHaveLength(0);
    await userEvent.clear(within(dialog).getByLabelText('Key'));
    await userEvent.type(within(dialog).getByLabelText('Key'), 'home.hero');
    await userEvent.type(within(dialog).getByLabelText('Alt text'), 'Hero');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save draft' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/app/assets' && c.method === 'POST').body).toEqual({ key: 'home.hero', altText: 'Hero' }));

    await userEvent.click(within(card).getByRole('button', { name: 'Upload image' }));
    const file = new File([new Uint8Array([137, 80, 78, 71])], 'welcome.png', { type: 'image/png' });
    fireEvent.change(screen.getByTestId('asset-file'), { target: { files: [file] } });
    await waitFor(() => {
      const put = calls.find((c) => c.path === '/api/v1/admin/app/assets/ras_1/content');
      expect(put.method).toBe('PUT');
      expect(put.headers['Content-Type']).toBe('image/png');
      expect(put.body).toBe(file);
    });
  });
});

function SymbolicateHost() {
  const { client } = useAdminAuth();
  return <SymbolicatePanel client={client} updates={[{ id: 'upd_ios', platform: 'ios' }, { id: 'upd_android', platform: 'android' }]} />;
}

describe('symbolication panel', () => {
  it('symbolicates a reported failure and a pasted trace, marking unmapped frames', async () => {
    const mapped = { updateId: 'upd_ios', sourceMapAvailable: true, sourceMap: { key: 'm', bundleKey: 'b', sizeBytes: 1 }, frames: [
      { raw: 'at foo (index.ios.bundle:1:2345)', function: 'foo', file: 'index.ios.bundle', line: 1, column: 2345, original: { source: 'src/feed/Composer.jsx', line: 41, column: 12, name: 'submit' } },
      { raw: 'at bar (native)', function: 'bar', file: null, line: null, column: null, original: null }
    ], text: 'at submit (src/feed/Composer.jsx:41:12)', truncated: false };
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/updates/upd_ios/failures/enumerate') return json(enumPage([{ id: 'uev_1', type: 'LaunchFailed', errorCode: 'E_LAUNCH', message: 'Crashed', hasStackTrace: true, occurredUtc: '2026-11-10T00:00:00Z', receivedUtc: '2026-11-10T00:00:01Z', labels: [], tags: {} }]));
      if (call.path === '/api/v1/admin/updates/upd_ios/symbolicate') return json(call.body.eventId ? mapped : { ...mapped, sourceMapAvailable: false, frames: [{ ...mapped.frames[0], original: null }], text: 'raw' });
      return undefined;
    });
    renderAdmin(<SymbolicateHost />, { fetchImpl });
    expect(await screen.findByText('Crashed')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Symbolicate uev_1' }));
    expect(await screen.findByText('src/feed/Composer.jsx:41:12')).toBeInTheDocument();
    expect(screen.getByText('in submit')).toBeInTheDocument();
    expect(screen.getByText('Unmapped')).toBeInTheDocument();
    expect(screen.getByTestId('symbolicated-text')).toHaveTextContent('at submit (src/feed/Composer.jsx:41:12)');
    expect(screen.getByRole('button', { name: 'Copy Symbolicated text' })).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/updates/upd_ios/symbolicate').body).toEqual({ eventId: 'uev_1' });

    fireEvent.change(screen.getByLabelText('Or paste a stack trace'), { target: { value: 'at foo (index.ios.bundle:1:2345)' } });
    await userEvent.click(screen.getByRole('button', { name: 'Symbolicate' }));
    expect(await screen.findByText(/This update has no source map/)).toBeInTheDocument();
    expect(calls.filter((c) => c.path === '/api/v1/admin/updates/upd_ios/symbolicate')[1].body).toEqual({ stackTrace: 'at foo (index.ios.bundle:1:2345)' });
  });
});

describe('release manager access', () => {
  it('shows only Home and the Releases group without signing and deploy keys', () => {
    const groups = groupedAdminRoutes({ features: {} }, { moderatorOnly: true, moderator: false, releaseManager: true });
    expect(groups.map((g) => g.label)).toEqual(['Overview', 'Releases']);
    expect(groups[1].routes.map((r) => r.id)).toEqual(['updates', 'update-channels', 'flags', 'kill-switches', 'strings', 'banners', 'assets']);
    const none = groupedAdminRoutes({ features: {} }, { moderatorOnly: true, moderator: false });
    expect(none.map((g) => g.label)).toEqual(['Overview']);
  });

  it('signs a ReleaseManager in, opens Releases pages, and keeps the keys page and other sections out', async () => {
    const me = makeMe({ roles: [{ role: 'ReleaseManager', resourceType: 'Global', resourceId: '*' }] });
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/auth/refresh') return json(authResponse(me));
      return undefined;
    });
    const tree = (path) => (
      <ThemeProvider>
        <MemoryRouter initialEntries={[path]}>
          <AuthProvider fetchImpl={fetchImpl} restoreOnLoad>
            <AdminAuthProvider fetchImpl={fetchImpl}>
              <Routes>
                <Route path="/admin/home" element={<RequireAdmin><p>Home page</p></RequireAdmin>} />
                <Route path="/admin/flags" element={<RequireAdmin releaseManager><p>Flags page</p></RequireAdmin>} />
                <Route path="/admin/update-keys" element={<RequireAdmin adminOnly><p>Keys page</p></RequireAdmin>} />
                <Route path="/admin/members" element={<RequireAdmin adminOnly><p>Members page</p></RequireAdmin>} />
                <Route path="/admin/moderation" element={<p>Moderation page</p>} />
              </Routes>
            </AdminAuthProvider>
          </AuthProvider>
        </MemoryRouter>
      </ThemeProvider>
    );
    const first = render(tree('/admin/flags'));
    expect(await screen.findByText('Flags page')).toBeInTheDocument();
    first.unmount();
    const second = render(tree('/admin/update-keys'));
    expect(await screen.findByText('Home page')).toBeInTheDocument();
    expect(screen.queryByText('Keys page')).toBeNull();
    second.unmount();
    render(tree('/admin/members'));
    expect(await screen.findByText('Home page')).toBeInTheDocument();
  });

  it('sends a plain member without staff roles away from Releases pages', async () => {
    const me = makeMe({ roles: [{ role: 'SupportAgent', resourceType: 'Global', resourceId: '*' }] });
    const { fetchImpl } = createFetch((call) => (call.path === '/api/v1/auth/refresh' ? json(authResponse(me)) : undefined));
    render(
      <ThemeProvider>
        <MemoryRouter initialEntries={['/admin/flags']}>
          <AuthProvider fetchImpl={fetchImpl} restoreOnLoad>
            <AdminAuthProvider fetchImpl={fetchImpl}>
              <Routes>
                <Route path="/admin/home" element={<p>Home page</p>} />
                <Route path="/admin/flags" element={<RequireAdmin releaseManager><p>Flags page</p></RequireAdmin>} />
              </Routes>
            </AdminAuthProvider>
          </AuthProvider>
        </MemoryRouter>
      </ThemeProvider>
    );
    expect(await screen.findByText('Home page')).toBeInTheDocument();
  });
});

const clientBanner = (extra = {}) => ({ id: 'ban_g', title: 'Maintenance', body: 'Read-only on **Sunday**.', style: 'Info', placement: 'Global', dismissible: true, priority: 1, startsUtc: null, endsUtc: null, link: { label: 'Details', path: '/app/support', url: null }, imageAssetId: null, contentHash: 'h1', contentVersion: 3, labels: [], tags: {}, ...extra });

function renderWithContent(ui, manifest, { path = '/app' } = {}) {
  const store = createAppContentStore({ client: 'web', fetchManifest: async () => manifest, storage: null });
  const { fetchImpl } = createFetch(() => undefined);
  const result = render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider fetchImpl={fetchImpl} restoreOnLoad={false}>
        <AppContentProvider store={store}>{ui}</AppContentProvider>
      </AuthProvider>
    </MemoryRouter>
  );
  return { store, ...result };
}

describe('member banners', () => {
  it('renders the banners of a placement, dismisses dismissible ones, and never offers dismissal for critical banners', async () => {
    const manifest = { contentVersion: 3, flags: {}, killSwitches: {}, banners: { objects: [
      clientBanner(),
      clientBanner({ id: 'ban_c', title: 'Security notice', style: 'Critical', dismissible: true, priority: 5, link: { label: 'Read more', path: null, url: 'https://status.example.test' } }),
      clientBanner({ id: 'ban_h', title: 'Home only', placement: 'Home', link: null }),
      clientBanner({ id: 'ban_late', title: 'Later', startsUtc: '2999-01-01T00:00:00Z' })
    ], deletes: [] } };
    renderWithContent(<><BannerSlot placement="Global" /><div data-testid="home"><BannerSlot placement="Home" /></div></>, manifest);
    expect(await screen.findByText('Maintenance')).toBeInTheDocument();
    expect(screen.queryByText('Later')).toBeNull();
    const global = screen.getAllByRole('region', { name: 'Announcements' })[0];
    expect(within(global).getAllByText(/Security notice|Maintenance/).map((n) => n.textContent)).toEqual(['Security notice', 'Maintenance']);
    expect(within(global).getAllByText('Sunday').every((n) => n.tagName === 'STRONG')).toBe(true);
    expect(within(global).getByRole('link', { name: 'Details' })).toHaveAttribute('href', '/app/support');
    expect(within(global).getByRole('link', { name: /Read more/ })).toHaveAttribute('href', 'https://status.example.test');
    expect(within(screen.getByTestId('home')).getByText('Home only')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Dismiss Security notice' })).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Dismiss Maintenance' }));
    expect(screen.queryByText('Maintenance')).toBeNull();
    expect(screen.getByText('Security notice')).toBeInTheDocument();
    expect(JSON.parse(window.localStorage.getItem(BANNER_DISMISSALS_STORAGE_KEY))).toEqual({ ban_g: 'h1' });
  });

  it('shows a dismissed banner again when its content changed', async () => {
    window.localStorage.setItem(BANNER_DISMISSALS_STORAGE_KEY, JSON.stringify({ ban_g: 'old' }));
    renderWithContent(<BannerSlot placement="Global" />, { contentVersion: 3, banners: { objects: [clientBanner()], deletes: [] } });
    expect(await screen.findByText('Maintenance')).toBeInTheDocument();
  });
});

function Probe() {
  useRemoteStringsRevision();
  const flag = useFlag('feed.v2', 'fallback');
  const kill = useKillSwitch('feed');
  return <p data-testid="probe">{strings.common.retry + '|' + String(flag) + '|' + (kill.killed ? 'killed:' + kill.message : 'live')}</p>;
}

describe('remote strings overlay and runtime hooks', () => {
  it('replaces string keys on the next render and exposes flags and kill switches', async () => {
    expect(strings.common.retry).toBe('Retry');
    setRemoteStrings({ 'web.common.retry': 'Try again' });
    expect(strings.common.retry).toBe('Try again');
    setRemoteStrings({});
    expect(strings.common.retry).toBe('Retry');

    const { store } = renderWithContent(<Probe />, { contentVersion: 4, flags: { 'feed.v2': true }, killSwitches: {}, strings: { upserts: { 'web.common.retry': 'Try once more' }, deletes: [] } });
    await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('Try once more|true|live'));
    act(() => {
      noteApiFailure(new ApiError('Off', { status: 503, code: 'FeatureDisabled', body: { error: 'FeatureDisabled', feature: 'feed', message: 'Paused' } }));
    });
    expect(screen.getByTestId('probe')).toHaveTextContent('killed:Paused');
    expect(store.isKilled('feed')).toBe(true);
  });

  it('keeps a stable visitor id in localStorage', () => {
    const id = visitorId();
    expect(id).toBeTruthy();
    expect(window.localStorage.getItem(VISITOR_ID_KEY)).toBe(id);
    expect(visitorId()).toBe(id);
  });

  it('fetches the manifest with the web content headers', async () => {
    const { fetchImpl, calls } = createFetch((call) => (call.path === '/api/v1/app/manifest' ? json({ contentVersion: 1, flags: {}, killSwitches: {}, banners: { objects: [], deletes: [] } }) : undefined));
    render(
      <MemoryRouter initialEntries={['/app']}>
        <AuthProvider fetchImpl={fetchImpl} restoreOnLoad={false}>
          <AppContentProvider><p>ready</p></AppContentProvider>
        </AuthProvider>
      </MemoryRouter>
    );
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/app/manifest')).toBe(true));
    const call = calls.find((c) => c.path === '/api/v1/app/manifest');
    expect(call.headers).toMatchObject({ 'X-Client': 'web', 'X-Client-Platform': 'web' });
    expect(call.headers['X-TK-Visitor-Id']).toBeTruthy();
  });
});
