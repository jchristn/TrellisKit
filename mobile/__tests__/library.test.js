import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { router as appRouter } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import * as FileSystem from 'expo-file-system';
import * as Network from 'expo-network';
import * as Video from 'expo-video';
import path from 'path';
import { Dimensions } from 'react-native';
import { createLibraryFake, makeLesson, makeProgress, makeSeries } from '@trelliskit/shared/testing';
import { libraryCollectionFilters } from '../src/components/library/LibraryHome';
import { lessonSource } from '../src/components/library/LessonPlayer';
import { clearAllDownloads, listDownloads, maintainDownloads, resetDownloadsState } from '../src/library/downloads';
import { progressQueue, replayProgress, sendProgress } from '../src/library/progress';
import { cueTextAt, parseVtt } from '../src/library/vtt';
import * as Storage from '../src/library/storage';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { propertyWithFeatures } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');

const levels = [
  { key: 'intro', label: 'Introductory', color: '#2E7D32' },
  { key: 'deep', label: 'Deep dive', color: '#6A1B9A' }
];
const propertyBody = (library = {}) => ({ ...propertyWithFeatures({}), library: { levelSource: 'Custom', levels, downloadsEnabled: false, progressIntervalSeconds: 15, completionThresholdPercent: 90, resumeTailSeconds: 10, ...library } });

const basics = makeSeries('ser_1', { title: 'Getting started', levels: [levels[0]] });
const advanced = makeSeries('ser_2', { title: 'Going further', levels: [levels[1]], isFollowed: true });
const locked = makeSeries('ser_3', { title: 'Members series', locked: true, viewer: { canFollow: false, canEdit: false, canOpen: false, requires: 'Membership' } });
const first = makeLesson('les_1', basics, {
  title: 'First steps',
  position: 1,
  markers: [
    { id: 'lmk_1', seconds: 0, label: 'Introduction' },
    { id: 'lmk_2', seconds: 95, label: 'Common mistakes' }
  ],
  captions: [{ language: 'en', label: 'English', media: { id: 'med_vtt', kind: 'Document', contentType: 'text/vtt', document: { url: '/media/med_vtt/file.vtt' } } }],
  resources: [
    { id: 'lrs_1', kind: 'Link', title: 'Further reading', media: null, url: 'https://example.test/reading', text: null },
    { id: 'lrs_2', kind: 'Reference', title: 'Handbook', media: null, url: null, text: 'Chapter 3' }
  ],
  myProgress: makeProgress('les_1', { positionSeconds: 151, maxPositionSeconds: 151, watchedSeconds: 160, percent: 50 })
});
const second = makeLesson('les_2', basics, { title: 'Second steps', position: 2 });
const memberOnly = makeLesson('les_9', locked, { title: 'Members lesson', locked: true, video: null, viewer: { canPlay: false, canOpen: false, requires: 'Membership' } });

function serve({ user = makeUser(), library = {}, seed = {} } = {}) {
  const fake = createLibraryFake({ series: [basics, advanced, locked], lessons: [first, second, memberOnly], progress: [first.myProgress], ...seed });
  const server = installFakeServer({ 'GET /property': propertyBody(library), ...signedInAs(user), 'POST /media/grant': { cookies: [{ name: 'CloudFront-Policy', value: 'p1' }], expiresUtc: '2099-01-01T00:00:00Z' } }, { fallback: (call) => fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body }) });
  return { server, fake };
}

/** Poll an async condition inside act (waitFor takes synchronous checks only). */
async function eventually(check) {
  for (let i = 0; i < 100; i += 1) {
    if (await check()) return;
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 10));
    });
  }
  throw new Error('Condition not met in time.');
}

function lastPlayer() {
  return Video.__players[Video.__players.length - 1];
}

describe('library helpers', () => {
  beforeEach(() => Storage.__store.clear());

  test('WebVTT parsing and active cues', () => {
    const cues = parseVtt('WEBVTT\n\n00:00.000 --> 00:02.500\nHello <b>there</b>\n\nNOTE skip me\n\n1\n00:00:02.000 --> 00:00:04.000 align:start\nSecond &amp; last\n');
    expect(cues).toEqual([
      { start: 0, end: 2.5, text: 'Hello there' },
      { start: 2, end: 4, text: 'Second & last' }
    ]);
    expect(cueTextAt(cues, 2.2)).toBe('Hello there\nSecond & last');
    expect(cueTextAt(cues, 5)).toBe('');
    expect(parseVtt('not a caption file')).toEqual([]);
  });

  test('collection filters and the player source (grant cookies, or a downloaded file)', () => {
    expect(libraryCollectionFilters({ levels: ['intro'], progress: 'InProgress', followedOnly: true })).toEqual({ levels: ['intro'], progress: 'InProgress', followedOnly: true });
    expect(libraryCollectionFilters({ followedOnly: true }, { lessons: true })).toEqual({});
    const context = { source: (url) => ({ uri: 'https://media.example.test' + url, headers: { Cookie: 'a=b' } }) };
    expect(lessonSource(first, context, null)).toEqual({ uri: 'https://media.example.test/media/les_1/hls/master.m3u8', headers: { Cookie: 'a=b' }, contentType: 'hls' });
    expect(lessonSource(first, context, 'file:///document/x.mp4')).toEqual({ uri: 'file:///document/x.mp4' });
  });

  test('offline progress is queued and replayed in order; 404 entries are dropped', async () => {
    const sent = [];
    const api = {
      putLessonProgress: jest.fn(async (id, body) => {
        sent.push([id, body.positionSeconds]);
        if (id === 'les_gone') {
          const e = new Error('gone');
          e.status = 404;
          throw e;
        }
        return { lessonId: id };
      })
    };
    const now = new Date().toISOString();
    expect(await sendProgress(api, 'les_1', { positionSeconds: 10, playedSeconds: 10, clientUpdatedUtc: now }, { offline: true })).toEqual({ queued: true });
    const failing = { putLessonProgress: jest.fn(async () => Promise.reject(Object.assign(new Error('net'), { status: 0 }))) };
    expect(await sendProgress(failing, 'les_1', { positionSeconds: 20, playedSeconds: 10, clientUpdatedUtc: now })).toEqual({ queued: true });
    await progressQueue().enqueue('les_gone', { positionSeconds: 5, playedSeconds: 5, clientUpdatedUtc: now });
    expect(await progressQueue().size()).toBe(3);
    expect(await replayProgress(api)).toEqual({ sent: 2, dropped: 1, remaining: 0 });
    expect(sent).toEqual([
      ['les_1', 10],
      ['les_1', 20],
      ['les_gone', 5]
    ]);
  });

  test('download upkeep: expired files go, near-expiry licenses renew, 403 removes, 503 pauses', async () => {
    resetDownloadsState();
    const day = 86400000;
    const now = Date.parse('2026-11-12T00:00:00Z');
    const entry = (id, days) => ({ id, lessonId: 'les_' + id, title: id, fileUri: 'file:///document/tk-library/downloads/' + id + '.mp4', sizeBytes: 10, licenseExpiresUtc: new Date(now + days * day).toISOString() });
    await Storage.fileStorage.setItem('downloads-index', JSON.stringify([entry('a', -1), entry('b', 3), entry('c', 20), entry('d', 2)]));
    const api = {
      renewLessonDownload: jest.fn(async (id) => {
        if (id === 'd') throw Object.assign(new Error('no'), { status: 403 });
        return { licenseExpiresUtc: new Date(now + 30 * day).toISOString() };
      })
    };
    expect(await maintainDownloads(api, { now })).toEqual({ renewed: 1, removed: 2, paused: false });
    expect((await listDownloads()).map((d) => d.id)).toEqual(['b', 'c']);
    const killed = { renewLessonDownload: jest.fn(async () => Promise.reject(Object.assign(new Error('off'), { status: 503, code: 'FeatureDisabled' }))) };
    expect(await maintainDownloads(killed, { now: now + 26 * day })).toMatchObject({ paused: true });
    expect((await listDownloads()).length).toBe(2);
    await clearAllDownloads();
    expect(await listDownloads()).toEqual([]);
  });
});

describe('Library tab on a phone', () => {
  beforeEach(() => {
    resetEnvironment();
    resetDownloadsState();
    Storage.__store.clear();
    FileSystem.__reset();
  });
  afterEach(() => delete global.fetch);

  test('home: continue watching, series by level, the history note, and lesson search', async () => {
    const { server } = serve();
    const router = await renderRouter(APP_DIR, { initialUrl: '/library' });
    expect(await screen.findByTestId('continue-les_1')).toBeOnTheScreen();
    expect(await screen.findByTestId('series-ser_1')).toBeOnTheScreen();
    expect(screen.getByText('Introductory')).toBeOnTheScreen();
    expect(screen.getByText('Deep dive')).toBeOnTheScreen();
    expect(screen.getByTestId('followed-ser_2')).toBeOnTheScreen();
    expect(screen.getByTestId('series-ser_3-locked')).toBeOnTheScreen();
    expect(screen.getByTestId('library-history-note')).toHaveTextContent(/Staff can see members' viewing history/);
    expect(server.called('POST /series/enumerate')[0].body).toMatchObject({ maxResults: 25, ordering: 'SortOrderAscending' });
    await fireEvent.press(screen.getByTestId('series-ser_1'));
    await waitFor(() => expect(router.getPathname()).toBe('/series/ser_1'));
    expect(await screen.findByTestId('lesson-les_2')).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('series-follow'));
    await waitFor(() => expect(screen.getByTestId('series-follow')).toHaveTextContent('Following'));
    expect(server.called('PUT /series/ser_1/follow')).toHaveLength(1);
  });

  test('a locked series shows the prompt and lists only what the caller may open', async () => {
    serve();
    await renderRouter(APP_DIR, { initialUrl: '/series/ser_3' });
    expect(await screen.findByTestId('series-locked')).toBeOnTheScreen();
    expect(await screen.findByTestId('lesson-les_9-locked')).toBeOnTheScreen();
  });

  test('the lesson player resumes, sends the grant cookies, reports progress, and jumps to chapters', async () => {
    const { server } = serve();
    await renderRouter(APP_DIR, { initialUrl: '/lessons/les_1' });
    expect(await screen.findByTestId('lesson-title')).toHaveTextContent('First steps');
    await waitFor(() => expect(lastPlayer().source.headers.Cookie).toBe('CloudFront-Policy=p1'));
    const player = lastPlayer();
    expect(player.currentTime).toBe(151);
    expect(player.source.contentType).toBe('hls');
    await act(async () => player.__emit('playingChange', { isPlaying: true }));
    await act(async () => player.__emit('playingChange', { isPlaying: false }));
    await waitFor(() => expect(server.called('PUT /lessons/les_1/progress').length).toBeGreaterThan(0));
    const body = server.called('PUT /lessons/les_1/progress')[0].body;
    expect(body).toMatchObject({ positionSeconds: 151, playedSeconds: 0 });
    expect(typeof body.sessionId).toBe('string');
    await fireEvent.press(screen.getByTestId('lesson-chapter-lmk_2'));
    expect(player.currentTime).toBe(95);
    await waitFor(() => expect(server.called('PUT /lessons/les_1/progress').at(-1).body.positionSeconds).toBe(95));
    expect(screen.getByTestId('lesson-player-tick-lmk_2')).toBeOnTheScreen();
    expect(screen.getByTestId('resource-lrs_2')).toHaveTextContent(/Chapter 3/);
  });

  test('captions load as WebVTT and show the active cue', async () => {
    serve();
    const base = global.fetch;
    global.fetch = jest.fn(async (url, init) => {
      if (String(url).endsWith('.vtt')) return { ok: true, status: 200, headers: { get: () => 'text/vtt' }, text: async () => 'WEBVTT\n\n00:00.000 --> 01:00:00.000\nWelcome to the lesson\n' };
      return base(url, init);
    });
    await renderRouter(APP_DIR, { initialUrl: '/lessons/les_1' });
    await fireEvent.press(await screen.findByTestId('lesson-player-captions-en'));
    await act(async () => lastPlayer().__emit('timeUpdate', { currentTime: 152 }));
    expect(await screen.findByTestId('lesson-player-caption')).toHaveTextContent('Welcome to the lesson');
    const vttCall = global.fetch.mock.calls.find(([u]) => String(u).endsWith('.vtt'));
    expect(vttCall[1].headers.Cookie).toBe('CloudFront-Policy=p1');
  });

  test('progress made offline is queued and replayed when the device reconnects', async () => {
    const { server } = serve();
    await renderRouter(APP_DIR, { initialUrl: '/lessons/les_1' });
    await screen.findByTestId('lesson-title');
    await act(async () => Network.__setState({ isConnected: false, isInternetReachable: false }));
    expect(await screen.findByTestId('lesson-offline')).toBeOnTheScreen();
    const before = server.called('PUT /lessons/les_1/progress').length;
    await act(async () => lastPlayer().__emit('playingChange', { isPlaying: false }));
    await eventually(async () => (await progressQueue().size()) === 1);
    expect(server.called('PUT /lessons/les_1/progress').length).toBe(before);
    await act(async () => Network.__setState({ isConnected: true, isInternetReachable: true }));
    await waitFor(() => expect(server.called('PUT /lessons/les_1/progress').length).toBe(before + 1));
    await eventually(async () => (await progressQueue().size()) === 0);
  });

  test('mark complete and back', async () => {
    const { server } = serve();
    await renderRouter(APP_DIR, { initialUrl: '/lessons/les_2' });
    await fireEvent.press(await screen.findByTestId('lesson-complete'));
    expect(await screen.findByTestId('lesson-progress')).toHaveTextContent('Completed');
    await fireEvent.press(screen.getByTestId('lesson-uncomplete'));
    await waitFor(() => expect(screen.getByTestId('lesson-complete')).toBeOnTheScreen());
    expect(server.called('POST /lessons/les_2/complete')).toHaveLength(1);
    expect(server.called('POST /lessons/les_2/uncomplete')).toHaveLength(1);
  });

  test('a locked lesson shows the membership prompt and never plays', async () => {
    serve();
    const count = Video.__players.length;
    await renderRouter(APP_DIR, { initialUrl: '/lessons/les_9' });
    expect(await screen.findByTestId('lesson-locked-prompt')).toBeOnTheScreen();
    expect(Video.__players.length).toBe(count);
  });

  test('downloads: download a lesson, list it with its expiry, and remove it', async () => {
    const downloadable = { ...first, viewer: { ...first.viewer, canDownload: true } };
    const { server } = serve({ library: { downloadsEnabled: true }, seed: { lessons: [downloadable, second], settings: { downloadsEnabled: true } } });
    await renderRouter(APP_DIR, { initialUrl: '/lessons/les_1' });
    await fireEvent.press(await screen.findByTestId('lesson-download'));
    expect(await screen.findByTestId('lesson-download-done')).toBeOnTheScreen();
    const license = server.called('POST /lessons/les_1/downloads')[0].body;
    expect(license.platform).toMatch(/^(Ios|Android)$/);
    expect(typeof license.deviceId).toBe('string');
    expect(FileSystem.__downloads[0].uri).toMatch(/^file:\/\/\/document\/tk-library\/downloads\/ldl_.*\.mp4$/);
    await act(async () => appRouter.push('/library/downloads'));
    const entry = (await listDownloads())[0];
    expect(await screen.findByTestId('download-' + entry.id + '-expires')).toHaveTextContent(/Available offline until/);
    await fireEvent.press(screen.getByTestId('download-' + entry.id + '-remove'));
    expect(await screen.findByTestId('downloads-empty')).toBeOnTheScreen();
    expect(server.called('DELETE /me/library/downloads/' + entry.id)).toHaveLength(1);
  });

  test('the libraryDownloads kill switch pauses downloads', async () => {
    const downloadable = { ...first, viewer: { ...first.viewer, canDownload: true } };
    serve({ library: { downloadsEnabled: true }, seed: { lessons: [downloadable], settings: { downloadsEnabled: true }, downloadsKilled: true } });
    await renderRouter(APP_DIR, { initialUrl: '/lessons/les_1' });
    await fireEvent.press(await screen.findByTestId('lesson-download'));
    expect(await screen.findByTestId('lesson-download-error')).toHaveTextContent('Downloads are paused right now.');
  });

  test('features.library off hides the tab', async () => {
    installFakeServer({ 'GET /property': { ...propertyWithFeatures({ library: false }) }, ...signedInAs(makeUser()) });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await waitFor(() => expect(screen.queryByTestId('tab-library')).toBeNull());
  });
});

describe('Library tab on a tablet', () => {
  const original = Dimensions.get('window');
  const screenSize = Dimensions.get('screen');
  beforeEach(() => {
    resetEnvironment();
    Dimensions.set({ window: { ...original, width: 1100, height: 820 }, screen: { ...screenSize, width: 1100, height: 820 } });
  });
  afterEach(async () => {
    await act(async () => Dimensions.set({ window: original, screen: screenSize }));
    delete global.fetch;
  });

  test('the series list and the selected series sit side by side', async () => {
    serve();
    const router = await renderRouter(APP_DIR, { initialUrl: '/library' });
    expect(await screen.findByTestId('library-select')).toBeOnTheScreen();
    // With Training placed, /library opens Training's Courses segment; either way selecting a
    // series keeps the library home on screen instead of pushing the series.
    const home = router.getPathname();
    expect(['/library', '/training/library/courses']).toContain(home);
    await fireEvent.press(await screen.findByTestId('series-ser_1'));
    expect(await screen.findByTestId('lesson-les_1')).toBeOnTheScreen();
    expect(router.getPathname()).toBe(home);
  });
});
