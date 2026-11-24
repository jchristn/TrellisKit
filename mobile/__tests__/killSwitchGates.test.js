/**
 * Kill switches in the app's gates (docs/LIVE_UPDATES_CONTRACT.md sections 11.3 and 13.2), like the website's route
 * guard: FeatureGate and CatalogGate show the operator's message in place of a killed feature without calling its
 * API, the feedComposer switch pauses new posts while the feed stays readable, the comments switch replaces the
 * comment composer, and the bundled fallback manifest applies on a first launch offline.
 */
import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { Text, Platform } from 'react-native';
import bundled from '../assets/app-manifest.json';
import { AppContentProvider, createMemoryStorage, createMobileAppContentStore, killSwitchKeyFor, useKillSwitch } from '../src/appConfig';
import strings from '../src/strings';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { enumerationResult, makePost } from '../test-utils/community';
import { propertyWithFeatures } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');

function manifest(killSwitches = {}) {
  return {
    contentVersion: 5,
    checkIntervalMinutes: 15,
    flags: {},
    killSwitches,
    strings: { upserts: {}, deletes: [] },
    banners: { objects: [], deletes: [] },
    assets: { upserts: [], deletes: [] },
    fullRefresh: false
  };
}

describe('kill switch keys', () => {
  test('gates map to engine feature keys like the website', () => {
    expect(killSwitchKeyFor({ feature: 'forums' })).toBe('forums');
    expect(killSwitchKeyFor({ feature: 'assistants' })).toBe('assistants');
    expect(killSwitchKeyFor({ feature: 'members' })).toBeNull();
    expect(killSwitchKeyFor({ feature: 'directory' })).toBeNull();
    expect(killSwitchKeyFor({ catalog: 'directory' })).toBe('directory');
    expect(killSwitchKeyFor({ catalog: 'events' })).toBe('events');
    expect(killSwitchKeyFor({})).toBeNull();
  });
});

describe('kill switch gates on a phone', () => {
  beforeEach(() => { Platform.OS = 'android'; resetEnvironment(); });
  afterEach(() => { Platform.OS = 'ios'; delete global.fetch; });

  test('opening a killed forums feature from the drawer shows the operator message and never lists forums; Try again rechecks', async () => {
    let killed = true;
    const server = installFakeServer({
      'GET /property': propertyWithFeatures({ forums: true, search: true }),
      ...signedInAs(makeUser()),
      'GET /app/manifest': () => manifest(killed ? { forums: { killed: true, message: 'Forums are paused for maintenance.' } } : {}),
      'POST /forums/enumerate': enumerationResult([])
    });
    await renderRouter(APP_DIR, { initialUrl: '/profile' });
    await fireEvent.press(await screen.findByTestId('header-menu'));
    await fireEvent.press(screen.getByTestId('drawer-forums'));
    expect(await screen.findByTestId('feature-off-forums')).toBeOnTheScreen();
    expect(screen.getByText('Forums are paused for maintenance.')).toBeOnTheScreen();
    const listed = server.called('POST /forums/enumerate').length;
    killed = false;
    const before = server.called('GET /app/manifest').length;
    await fireEvent.press(screen.getByText(strings.common.retry));
    await waitFor(() => expect(server.called('GET /app/manifest').length).toBeGreaterThan(before));
    await waitFor(() => expect(screen.queryByTestId('feature-off-forums')).toBeNull());
    await waitFor(() => expect(server.called('POST /forums/enumerate').length).toBeGreaterThan(listed));
  });

  test('a killed catalog shows the message through CatalogGate', async () => {
    installFakeServer({
      'GET /property': propertyWithFeatures({ events: true }),
      ...signedInAs(makeUser()),
      'GET /app/manifest': manifest({ events: { killed: true, message: null } }),
      'POST /events/enumerate': enumerationResult([])
    });
    await renderRouter(APP_DIR, { initialUrl: '/events' });
    expect(await screen.findByTestId('feature-off-events')).toBeOnTheScreen();
    expect(screen.getByText(strings.appContent.featureUnavailable)).toBeOnTheScreen();
  });

  test('feedComposer pauses new posts while the feed stays readable, and the compose screen is off', async () => {
    installFakeServer({
      ...signedInAs(makeUser({ flags: { canPostToFeed: true } })),
      'GET /app/manifest': manifest({ feedComposer: { killed: true, message: 'Posting is paused.' } }),
      'POST /feed/enumerate': enumerationResult([])
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    expect(await screen.findByTestId('feed-composer-off')).toHaveTextContent('Posting is paused.');
    expect(screen.queryByTestId('feed-new-post')).toBeNull();
  });

  test('the comments switch replaces the comment composer while the post and its comments stay readable', async () => {
    installFakeServer({
      ...signedInAs(makeUser()),
      'GET /app/manifest': manifest({ comments: { killed: true, message: 'Comments are paused.' } }),
      'GET /feed/fpo_1': makePost('fpo_1', { commentCount: 0 }),
      'POST /feed/fpo_1/comments/enumerate': enumerationResult([])
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed/fpo_1' });
    expect(await screen.findByTestId('comment-composer-off')).toHaveTextContent('Comments are paused.');
    expect(screen.queryByTestId('comment-composer-body')).toBeNull();
  });

  test('the compose screen is off while feedComposer is killed', async () => {
    installFakeServer({
      ...signedInAs(makeUser({ flags: { canPostToFeed: true } })),
      'GET /app/manifest': manifest({ feedComposer: { killed: true, message: 'Posting is paused.' } })
    });
    await renderRouter(APP_DIR, { initialUrl: '/compose' });
    expect(await screen.findByTestId('compose-off')).toBeOnTheScreen();
    expect(screen.getByText('Posting is paused.')).toBeOnTheScreen();
  });
});

describe('bundled fallback manifest', () => {
  test('the committed bundle is the empty fallback', () => {
    expect(bundled).toMatchObject({ fallback: true, contentVersion: null, flags: {}, killSwitches: {} });
  });

  test('a first launch offline starts from the bundled manifest', async () => {
    const api = { get: jest.fn(async () => { throw new TypeError('Network request failed'); }) };
    const offline = { ...bundled, contentVersion: 4, killSwitches: { messaging: { killed: true, message: 'Messages are paused.' } } };
    const store = createMobileAppContentStore({ api, storage: createMemoryStorage(), headers: async () => ({ 'X-Client': 'mobile' }), initial: offline });
    function Probe() {
      const kill = useKillSwitch('messaging');
      return <Text testID="probe">{kill.killed ? kill.message : 'live'}</Text>;
    }
    await render(
      <AppContentProvider store={store} api={api} storage={createMemoryStorage()}>
        <Probe />
      </AppContentProvider>
    );
    await waitFor(() => expect(screen.getByTestId('probe').props.children).toBe('Messages are paused.'));
    expect(store.getState().contentVersion).toBe(4);
  });
});
