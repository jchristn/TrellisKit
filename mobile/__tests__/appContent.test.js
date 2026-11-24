/**
 * The content and configuration manifest in the app (docs/LIVE_UPDATES_CONTRACT.md sections 11.3,
 * 13.2, and 14): the provider and store wiring, banners, remote strings, the asset cache, the
 * FeatureDisabled kill switch path, and the fatal error stack attached to LaunchFailed.
 */
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { Linking, Pressable, Text } from 'react-native';
import * as FileSystem from 'expo-file-system';
import { APP_CONTENT_STORAGE_KEY, BANNER_DISMISSALS_STORAGE_KEY, createAppContentStore, directoryTerms, formatTemplate, propertyTokens, setRemoteStrings } from '@trelliskit/shared';
import { getApiClient } from '../src/api/client';
import { AppContentProvider, AssetIndexKey, BannerSlot, accountKeyOf, createAssetCache, createMemoryStorage, remoteAssetUri, useAppContent, useFlag, useKillSwitch, useRemoteAsset } from '../src/appConfig';
import { AuthProvider, useAuth } from '../src/auth/AuthProvider';
import { getBundledProperty } from '../src/property/property';
import strings, { fillPlaces } from '../src/strings';
import { createEventStore, createFatalErrorStore, fatalRecordOf, installFatalErrorRecorder, MaxFailureMessageLength, MaxStackTraceLength, UpdateEventReporter } from '../src/updates';
import { resetInstallIdCache } from '../src/updates/installId';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';

const mockRouter = { push: jest.fn(), replace: jest.fn(), back: jest.fn() };
jest.mock('expo-router', () => ({ ...jest.requireActual('expo-router'), useRouter: () => mockRouter }));

const H1 = 'a'.repeat(64);
const H2 = 'b'.repeat(64);
const U1 = '6f0b6c1e-1111-4a2b-8c3d-000000000001';
const U2 = '6f0b6c1e-2222-4a2b-8c3d-000000000002';

function manifest(overrides = {}) {
  return {
    contentVersion: 3,
    checkIntervalMinutes: 15,
    flags: { 'feed.composerVersion': 'v2' },
    killSwitches: {},
    strings: { upserts: {}, deletes: [] },
    banners: { objects: [], deletes: [] },
    assets: { upserts: [], deletes: [] },
    fullRefresh: false,
    ...overrides
  };
}

function banner(overrides = {}) {
  return { id: 'ban_1', title: 'Maintenance tonight', body: 'Read-only from **06:00**.', style: 'Info', placement: 'Home', dismissible: true, priority: 10, startsUtc: null, endsUtc: null, link: null, imageAssetId: null, contentHash: 'h1', contentVersion: 3, labels: [], tags: {}, ...overrides };
}

function staticStore(body, storage = createMemoryStorage()) {
  const fetchManifest = jest.fn(async () => body);
  return { store: createAppContentStore({ client: 'mobile', fetchManifest, storage, applyStrings: false }), fetchManifest, storage };
}

function Probe() {
  const content = useAppContent();
  const flag = useFlag('feed.composerVersion', 'v1');
  const kill = useKillSwitch('feed');
  return (
    <Text testID="probe">
      {[content ? content.state.contentVersion : 'none', flag, kill.killed ? 'killed:' + kill.message : 'live'].join('|')}
    </Text>
  );
}

beforeEach(() => {
  resetEnvironment();
  resetInstallIdCache();
  FileSystem.__reset();
  setRemoteStrings({});
  mockRouter.push.mockClear();
});

describe('provider and store wiring', () => {
  test('fetches the manifest at launch with the mobile headers and caches it', async () => {
    const server = installFakeServer({ 'GET /app/manifest': manifest() });
    const storage = createMemoryStorage();
    await render(
      <AppContentProvider api={getApiClient()} storage={storage}>
        <Probe />
      </AppContentProvider>
    );
    await waitFor(() => expect(screen.getByTestId('probe').props.children).toBe('3|v2|live'));
    const call = server.called('GET /app/manifest')[0];
    expect(call.url).not.toContain('since=');
    expect(call.headers['X-Client']).toBe('mobile');
    expect(call.headers['X-Client-Update-Id']).toBe('embedded');
    expect(call.headers['X-TK-Install-Id']).toBeTruthy();
    expect(call.headers['X-Client-Channel']).toBeTruthy();
    expect(call.headers['X-Client-Platform']).toBeTruthy();
    await waitFor(() => expect(JSON.parse(storage.map.get(APP_CONTENT_STORAGE_KEY)).contentVersion).toBe(3));
  });

  test('a full refresh re-enumerates strings, banners, and assets with the same headers', async () => {
    const server = installFakeServer({
      'GET /app/manifest': manifest({ fullRefresh: true, strings: null, banners: null, assets: null }),
      'POST /app/strings/enumerate': { objects: [{ key: 'mobile.common.retry', value: 'Again' }], endOfResults: true },
      'POST /app/banners/enumerate': { objects: [banner()], endOfResults: true },
      'POST /app/assets/enumerate': { objects: [], endOfResults: true }
    });
    await render(
      <AppContentProvider api={getApiClient()} storage={createMemoryStorage()}>
        <BannerSlot placement="Home" />
      </AppContentProvider>
    );
    await waitFor(() => expect(screen.getByTestId('banner-ban_1')).toBeTruthy());
    const strCall = server.called('POST /app/strings/enumerate')[0];
    expect(strCall.body.filters).toEqual(expect.objectContaining({ client: 'mobile' }));
    expect(strCall.headers['X-Client']).toBe('mobile');
    expect(strings.common.retry).toBe('Again');
  });

  test('the launch fetch waits for the session, and sign-out refetches despite the throttle', async () => {
    const user = makeUser();
    const server = installFakeServer({ ...signedInAs(user), 'GET /app/manifest': manifest(), 'POST /auth/logout': { status: 204, body: null } });
    function SignOut() {
      const { signOut, session } = useAuth();
      return (
        <Pressable testID="sign-out" onPress={() => signOut({ revoke: false })}>
          <Text testID="status">{session.status}</Text>
        </Pressable>
      );
    }
    await render(
      <AuthProvider>
        <AppContentProvider storage={createMemoryStorage()}>
          <SignOut />
        </AppContentProvider>
      </AuthProvider>
    );
    await waitFor(() => expect(server.called('GET /app/manifest')).toHaveLength(1));
    // Fetched after the restore, so targeting sees the member.
    expect(server.called('GET /app/manifest')[0].headers.Authorization).toBe('Bearer access-refresh-2');
    await fireEvent.press(screen.getByTestId('sign-out'));
    await waitFor(() => expect(server.called('GET /app/manifest')).toHaveLength(2));
    const second = server.called('GET /app/manifest')[1];
    expect(second.headers.Authorization).toBeUndefined();
    expect(second.url).toContain('since=3');
  });

  test('account keys change on sign-in, sign-out, and membership change only', async () => {
    const member = makeUser();
    expect(accountKeyOf(null)).toBe('anonymous');
    expect(accountKeyOf({ session: { status: 'loading' } })).toBeNull();
    expect(accountKeyOf({ session: { status: 'signedOut', user: null } })).toBe('anonymous');
    const active = accountKeyOf({ session: { status: 'signedIn', user: member } });
    expect(accountKeyOf({ session: { status: 'signedIn', user: { ...member, displayName: 'Renamed' } } })).toBe(active);
    expect(accountKeyOf({ session: { status: 'signedIn', user: { ...member, membership: { isActive: false } } } })).not.toBe(active);
  });

  test('outside the provider the hooks fall back', async () => {
    await render(<Probe />);
    expect(screen.getByTestId('probe').props.children).toBe('none|v1|live');
  });
});

describe('BannerSlot', () => {
  test('shows the banners of its placement only, highest priority first', async () => {
    const { store } = staticStore(
      manifest({ banners: { objects: [banner({ id: 'ban_low', priority: 1 }), banner({ id: 'ban_high', priority: 50 }), banner({ id: 'ban_global', placement: 'Global' }), banner({ id: 'ban_later', startsUtc: '2099-01-01T00:00:00Z' })], deletes: [] } })
    );
    await render(
      <AppContentProvider store={store} storage={createMemoryStorage()} assetCache={createAssetCache({ storage: createMemoryStorage() })}>
        <BannerSlot placement="Home" />
      </AppContentProvider>
    );
    await waitFor(() => expect(screen.getByTestId('banner-slot-Home')).toBeTruthy());
    const ids = screen.getAllByTestId(/^banner-ban_[a-z]+$/).map((n) => n.props.testID);
    expect(ids).toEqual(['banner-ban_high', 'banner-ban_low']);
    expect(screen.getByTestId('banner-ban_high').props.accessibilityLabel).toBe(strings.appContent.bannerLabel(strings.appContent.styles.Info, 'Maintenance tonight'));
  });

  test('dismissal is persisted and holds until the content changes; Critical has no dismiss button', async () => {
    const storage = createMemoryStorage();
    const { store } = staticStore(manifest({ banners: { objects: [banner(), banner({ id: 'ban_crit', style: 'Critical', dismissible: true })], deletes: [] } }));
    const view = await render(
      <AppContentProvider store={store} storage={storage} assetCache={createAssetCache({ storage: createMemoryStorage() })}>
        <BannerSlot placement="Home" />
      </AppContentProvider>
    );
    await waitFor(() => expect(screen.getByTestId('banner-ban_1-dismiss')).toBeTruthy());
    expect(screen.queryByTestId('banner-ban_crit-dismiss')).toBeNull();
    expect(screen.getByTestId('banner-ban_crit').props.accessibilityRole).toBe('alert');
    await fireEvent.press(screen.getByTestId('banner-ban_1-dismiss'));
    expect(screen.queryByTestId('banner-ban_1')).toBeNull();
    await waitFor(() => expect(JSON.parse(storage.map.get(BANNER_DISMISSALS_STORAGE_KEY))).toEqual({ ban_1: 'h1' }));
    await view.unmount();

    // A new launch reads the dismissal; an edited banner (new contentHash) shows again.
    const same = staticStore(manifest({ banners: { objects: [banner()], deletes: [] } }), storage);
    const again = await render(
      <AppContentProvider store={same.store} storage={storage} assetCache={createAssetCache({ storage: createMemoryStorage() })}>
        <BannerSlot placement="Home" />
        <Probe />
      </AppContentProvider>
    );
    await waitFor(() => expect(screen.getByTestId('probe').props.children).toBe('3|v2|live'));
    await waitFor(() => expect(screen.queryByTestId('banner-ban_1')).toBeNull());
    await again.unmount();
    const edited = staticStore(manifest({ banners: { objects: [banner({ contentHash: 'h2' })], deletes: [] } }), createMemoryStorage());
    await render(
      <AppContentProvider store={edited.store} storage={storage} assetCache={createAssetCache({ storage: createMemoryStorage() })}>
        <BannerSlot placement="Home" />
      </AppContentProvider>
    );
    await waitFor(() => expect(screen.getByTestId('banner-ban_1')).toBeTruthy());
  });

  test('a link path navigates in the app; a url opens embedded content; an image comes from the asset', async () => {
    const open = jest.spyOn(Linking, 'openURL').mockResolvedValue(true);
    const asset = { id: 'ras_1', key: 'banner.art', contentType: 'image/webp', sha256: H1, url: 'https://cdn.test/' + H1, width: 200, height: 100, altText: 'Art', darkVariant: null };
    const { store } = staticStore(
      manifest({
        banners: { objects: [banner({ link: { label: 'Details', path: '/support', url: null }, imageAssetId: 'ras_1' }), banner({ id: 'ban_2', placement: 'Feed', link: { label: 'Read', path: null, url: 'https://status.test/' } })], deletes: [] },
        assets: { upserts: [asset], deletes: [] }
      })
    );
    await render(
      <AppContentProvider store={store} storage={createMemoryStorage()} assetCache={createAssetCache({ storage: createMemoryStorage(), download: async () => undefined })}>
        <BannerSlot placement="Home" />
        <BannerSlot placement="Feed" />
      </AppContentProvider>
    );
    await waitFor(() => expect(screen.getByTestId('banner-ban_1-link')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('banner-ban_1-link'));
    expect(mockRouter.push).toHaveBeenCalledWith('/support');
    await fireEvent.press(screen.getByTestId('banner-ban_2-link'));
    expect(mockRouter.push).toHaveBeenCalledWith({ pathname: '/external-content', params: { url: 'https://status.test/' } });
    expect(open).not.toHaveBeenCalled();
    expect(screen.getByTestId('banner-ban_1-image').props.accessibilityLabel).toBe('Art');
    open.mockRestore();
  });

  test('renders nothing outside the provider', async () => {
    await render(<BannerSlot placement="Global" />);
    expect(screen.queryByTestId('banner-slot-Global')).toBeNull();
  });
});

describe('remote strings overlay', () => {
  test('an override replaces its key with the property tokens filled, and removal restores it', async () => {
    const property = getBundledProperty();
    const tokens = propertyTokens(property);
    const before = strings.common.comingSoonNote;
    setRemoteStrings({ 'mobile.common.comingSoonNote': 'Not in {name} yet.' });
    expect(strings.common.comingSoonNote).toBe(formatTemplate('Not in {name} yet.', tokens));
    expect(strings.common.retry).toBe('Retry');
    setRemoteStrings({});
    expect(strings.common.comingSoonNote).toBe(before);
  });

  test('keys under mobile.places fill the directory terms too', async () => {
    const property = getBundledProperty();
    const expected = formatTemplate('Suggest a {venueLower} in {name}', { ...propertyTokens(property), ...directoryTerms(property) });
    setRemoteStrings({ 'mobile.places.submissions.types.NewVenue': 'Suggest a {venueLower} in {name}' });
    expect(strings.places.submissions.types.NewVenue).toBe(expected);
    expect(fillPlaces(property).submissions.types.NewVenue).toBe(expected);
    expect(typeof strings.access.lockedLabel).toBe('function');
  });

  test('the provider re-renders its consumers when overrides change', async () => {
    const { store } = staticStore(manifest());
    function Label() {
      useAppContent();
      return <Text testID="label">{strings.common.retry}</Text>;
    }
    await render(
      <AppContentProvider store={store} storage={createMemoryStorage()} assetCache={createAssetCache({ storage: createMemoryStorage() })}>
        <Label />
      </AppContentProvider>
    );
    expect(screen.getByTestId('label').props.children).toBe('Retry');
    await act(async () => setRemoteStrings({ 'mobile.common.retry': 'Try again' }));
    expect(screen.getByTestId('label').props.children).toBe('Try again');
  });
});

describe('remote asset cache', () => {
  const assets = { ras_1: { id: 'ras_1', key: 'onboarding.welcome', sha256: H1, url: 'https://cdn.test/' + H1, darkVariant: { sha256: H2, url: 'https://cdn.test/' + H2 } } };

  test('downloads each hash once and evicts what is no longer published', async () => {
    const storage = createMemoryStorage();
    const cache = createAssetCache({ storage });
    expect((await cache.sync(assets)).sort()).toEqual([H1, H2]);
    expect(FileSystem.__downloads.map((d) => d.url).sort()).toEqual(['https://cdn.test/' + H1, 'https://cdn.test/' + H2]);
    expect(cache.uriFor(H1)).toBe('file:///cache/tk-app-assets/' + H1);
    await cache.sync(assets);
    expect(FileSystem.__downloads).toHaveLength(2);
    expect(JSON.parse(storage.map.get(AssetIndexKey)).sort()).toEqual([H1, H2]);

    // The OS purged one file: it is downloaded again.
    FileSystem.__files.delete('file:///cache/tk-app-assets/' + H2);
    await cache.sync(assets);
    expect(FileSystem.__downloads).toHaveLength(3);

    await cache.sync({});
    expect(FileSystem.__files.has('file:///cache/tk-app-assets/' + H1)).toBe(false);
    expect(cache.uriFor(H1)).toBeNull();
    expect(JSON.parse(storage.map.get(AssetIndexKey))).toEqual([]);
  });

  test('a failed download keeps the network URL; a later sync retries', async () => {
    const download = jest.fn().mockRejectedValueOnce(new Error('offline')).mockResolvedValue(undefined);
    const cache = createAssetCache({ storage: createMemoryStorage(), download, fileFor: (hash) => ({ uri: 'file:///x/' + hash, exists: true }) });
    const only = { ras_1: { ...assets.ras_1, darkVariant: null } };
    expect(await cache.sync(only)).toEqual([]);
    expect(remoteAssetUri(only.ras_1, { cached: [], uriFor: cache.uriFor })).toBe('https://cdn.test/' + H1);
    expect(await cache.sync(only)).toEqual([H1]);
    expect(remoteAssetUri(only.ras_1, { cached: [H1], uriFor: cache.uriFor })).toBe('file:///x/' + H1);
  });

  test('useRemoteAsset returns the network URL until cached, then the file; dark picks the variant', async () => {
    const { store } = staticStore(manifest({ assets: { upserts: [assets.ras_1], deletes: [] } }));
    function Img({ dark }) {
      return <Text testID={dark ? 'dark' : 'light'}>{useRemoteAsset('onboarding.welcome', { dark }) || 'none'}</Text>;
    }
    await render(
      <AppContentProvider store={store} storage={createMemoryStorage()} assetCache={createAssetCache({ storage: createMemoryStorage() })}>
        <Img dark={false} />
        <Img dark />
      </AppContentProvider>
    );
    await waitFor(() => expect(screen.getByTestId('light').props.children).toBe('file:///cache/tk-app-assets/' + H1));
    expect(screen.getByTestId('dark').props.children).toBe('file:///cache/tk-app-assets/' + H2);
    expect(remoteAssetUri(assets.ras_1, { dark: true })).toBe('https://cdn.test/' + H2);
  });
});

describe('FeatureDisabled', () => {
  test('a 503 FeatureDisabled from any API call marks the kill switch at once', async () => {
    installFakeServer({ 'GET /app/manifest': manifest(), 'GET /feed/posts': { status: 503, body: { error: 'FeatureDisabled', feature: 'feed', message: 'Paused for now.' } } });
    const api = getApiClient();
    await render(
      <AppContentProvider api={api} storage={createMemoryStorage()}>
        <Probe />
      </AppContentProvider>
    );
    await waitFor(() => expect(screen.getByTestId('probe').props.children).toBe('3|v2|live'));
    await act(async () => {
      await expect(api.get('/feed/posts')).rejects.toMatchObject({ status: 503 });
    });
    expect(screen.getByTestId('probe').props.children).toBe('3|v2|killed:Paused for now.');
  });

  test('the client tells listeners about 503 answers only', async () => {
    installFakeServer({ 'GET /a': { status: 503, body: { error: 'FeatureDisabled', feature: 'search' } }, 'GET /b': { status: 500, body: { error: 'Server' } } });
    const api = getApiClient();
    const seen = [];
    const off = api.onFeatureDisabled((e) => seen.push(e.code));
    await expect(api.get('/a')).rejects.toBeTruthy();
    await expect(api.get('/b')).rejects.toBeTruthy();
    off();
    await expect(api.get('/a')).rejects.toBeTruthy();
    expect(seen).toEqual(['FeatureDisabled']);
  });
});

describe('fatal error capture for LaunchFailed', () => {
  function reporterWith(crashes) {
    const sent = [];
    const reporter = new UpdateEventReporter({
      send: async (events) => sent.push(...events),
      installId: async () => 'install-1',
      store: createEventStore(() => null),
      crashes,
      flushDelayMs: 3600000,
      launchFlushDelayMs: 3600000,
      stableMs: 3600000
    });
    return { reporter, sent };
  }

  test('the global handler records fatal errors only and chains the previous handler', async () => {
    const previous = jest.fn();
    let handler = null;
    const errorUtils = { getGlobalHandler: () => previous, setGlobalHandler: (fn) => (handler = fn) };
    const store = createFatalErrorStore(() => null);
    const save = jest.spyOn(store, 'save');
    expect(installFatalErrorRecorder({ errorUtils, store, updateId: () => U2.toUpperCase() })).toBe(true);
    const error = new Error('boom');
    handler(new Error('soft'), false);
    expect(save).not.toHaveBeenCalled();
    handler(error, true);
    expect(previous).toHaveBeenCalledWith(error, true);
    expect(save.mock.calls[0][0]).toEqual(expect.objectContaining({ message: 'boom', updateId: U2, stackTrace: error.stack }));
    // Installing twice does not chain twice.
    expect(installFatalErrorRecorder({ errorUtils, store })).toBe(true);
  });

  test('clips the message and stack to the server limits', async () => {
    const error = new Error('m'.repeat(900));
    error.stack = 's'.repeat(20000);
    const record = fatalRecordOf(error, U1);
    expect(record.message).toHaveLength(MaxFailureMessageLength);
    expect(record.stackTrace).toHaveLength(MaxStackTraceLength);
  });

  test('LaunchFailed for the crashed update carries its message and stack, and the record is cleared', async () => {
    const crashes = createFatalErrorStore(() => null);
    crashes.save(fatalRecordOf(Object.assign(new Error('render failed'), { stack: 'at App (index.bundle:1:42)' }), U2));
    const { reporter, sent } = reporterWith(crashes);
    await reporter.noteDownloaded({ updateId: U2 });
    await reporter.onLaunch({ isEnabled: true, isEmbeddedLaunch: false, updateId: U1 });
    await reporter.flush();
    const failed = sent.find((e) => e.type === 'LaunchFailed');
    expect(failed).toEqual(expect.objectContaining({ updateId: U2, message: 'render failed', stackTrace: 'at App (index.bundle:1:42)' }));
    expect(sent.find((e) => e.type === 'Downloaded').stackTrace).toBeUndefined();
    expect(await crashes.read()).toBeNull();
    reporter.dispose();
  });

  test('a record from another update stays and is not attached', async () => {
    const crashes = createFatalErrorStore(() => null);
    crashes.save(fatalRecordOf(new Error('other'), U1));
    const { reporter, sent } = reporterWith(crashes);
    await reporter.noteDownloaded({ updateId: U2 });
    await reporter.onLaunch({ isEnabled: true, isEmbeddedLaunch: true, updateId: null });
    await reporter.flush();
    const failed = sent.find((e) => e.type === 'LaunchFailed');
    expect(failed.message).toBeUndefined();
    expect((await crashes.read()).message).toBe('other');
    reporter.dispose();
  });

  test('message and stackTrace are kept on failure events only', async () => {
    const { reporter } = reporterWith(null);
    await reporter.record('Applied', { updateId: U1, message: 'x', stackTrace: 'y' });
    await reporter.record('DownloadFailed', { updateId: U1, errorCode: 'Network', message: 'x'.repeat(600), stackTrace: 'y' });
    const [applied, failed] = (await reporter.snapshot()).queue;
    expect(applied.message).toBeUndefined();
    expect(failed.message).toHaveLength(500);
    expect(failed.stackTrace).toBe('y');
    reporter.dispose();
  });
});
