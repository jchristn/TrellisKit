import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { router as appRouter } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import * as Network from 'expo-network';
import * as Video from 'expo-video';
import path from 'path';
import { Dimensions } from 'react-native';
import { createLibraryFake, makeLesson, makeSeries } from '@trelliskit/shared/testing';
import { librarySearchBody } from '../src/components/training/LibrarySearch';
import * as Storage from '../src/library/storage';
import strings from '../src/strings';
import { librarySegmentsFor, normalizeOverview, normalizeRankCard, OverviewCacheMaxAgeMs, readCachedOverview, saveTargetPath, segmentHref, trainingWebPath, visibleSections, writeCachedOverview } from '../src/training/hub';
import { enumerationResult } from '../test-utils/community';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { property } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');
const t = strings.training;

const basics = makeSeries('ser_1', { title: 'Getting started' });
const first = makeLesson('les_1', basics, { title: 'First steps', position: 1 });
const second = makeLesson('les_2', basics, { title: 'Second steps', position: 2 });
const technique = { id: 'cat_1', slug: 'first-technique', name: 'First technique', kind: 'Technique', difficulty: 'Beginner', startSeconds: 95, position: 1 };

const sections = (extra = {}) => [
  { key: 'Overview', available: true },
  { key: 'Library', available: true, segments: ['Courses', 'Saved'], ...extra },
  { key: 'Schedule', available: false, reason: 'NoData' },
  { key: 'Progress', available: true },
  { key: 'Future', available: true }
];
const fullOverview = () => ({
  sections: sections(),
  upNext: enumerationResult([{ kind: 'Event', id: 'evt_1', title: 'Open session', startUtc: '2099-01-02T18:00:00Z', endUtc: '2099-01-02T20:00:00Z', timeZone: 'UTC', locationSummary: 'Main room' }]),
  continue: enumerationResult([{ lesson: first, progress: { lessonId: 'les_1', percent: 40, completed: false } }]),
  recentSaves: enumerationResult([
    { id: 'fav_1', targetType: 'Lesson', targetId: 'les_2', target: { available: true, title: 'Second steps' } },
    { id: 'fav_2', targetType: 'Event', targetId: 'evt_9', target: { available: false, title: null } }
  ]),
  newForYou: enumerationResult([second]),
  rank: null,
  coach: { available: true, placement: 'Menu' },
  firstRun: false,
  labels: [],
  tags: {}
});
const emptyOverview = () => ({ sections: sections(), upNext: enumerationResult([]), continue: enumerationResult([]), recentSaves: enumerationResult([]), newForYou: enumerationResult([]), rank: null, coach: { available: false }, firstRun: true, labels: [], tags: {} });

function serve({ user = makeUser(), overview = fullOverview(), features = {}, routes = {} } = {}) {
  const fake = createLibraryFake({ series: [basics], lessons: [first, second] });
  const propertyDoc = { ...property, features: { ...(property.features || {}), ...features } };
  return installFakeServer(
    {
      'GET /property': propertyDoc,
      ...(user ? signedInAs(user) : {}),
      'GET /me/training': overview,
      'POST /me/saves/state': (call) => ({ items: call.body.targets.map((target) => ({ ...target, saved: false, favoriteId: null })) }),
      'POST /media/grant': { cookies: [], expiresUtc: '2099-01-01T00:00:00Z' },
      ...routes
    },
    { fallback: (call) => fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body }) }
  );
}

/** Test ids matching a pattern, in render order (each once). */
function trainingTestIds(pattern) {
  const seen = [];
  const walk = (node) => {
    if (!node || typeof node === 'string') return;
    if (Array.isArray(node)) return node.forEach(walk);
    const id = node.props && node.props.testID;
    if (typeof id === 'string' && pattern.test(id) && !seen.includes(id)) seen.push(id);
    walk(node.children);
  };
  walk(screen.toJSON());
  return seen;
}

beforeEach(() => {
  resetEnvironment();
  Storage.__store.clear();
});
afterEach(() => {
  delete global.fetch;
});

describe('training helpers', () => {
  test('the overview normalizes lists, drops completed continue items, and keeps only known sections', () => {
    const overview = normalizeOverview({ sections: [{ key: 'Overview', available: true }, { key: 'Schedule', available: false, reason: 'NoData' }, { key: 'Library', available: true }, { key: 'Unknown', available: true }, null], continue: { objects: [{ lesson: first, progress: { completed: true } }, { lesson: second }] }, coach: { available: true, placement: 'Tab' } });
    expect(visibleSections(overview)).toEqual(['Overview', 'Library']);
    expect(overview.continue.objects.map((item) => item.lesson.id)).toEqual(['les_2']);
    expect(overview.upNext.objects).toEqual([]);
    expect(overview.coach).toEqual({ available: true, placement: 'Tab' });
    expect(normalizeOverview(null).sections).toEqual([]);
  });

  test('the rank card accepts the rank response shape or a single rank, with curricula', () => {
    expect(normalizeRankCard(null)).toBeNull();
    expect(normalizeRankCard({ fields: [] })).toBeNull();
    const card = normalizeRankCard({ fields: [{ rank: { fieldKey: 'rank', optionKey: 'a' }, next: null }], curricula: [{ id: 'cur_1', name: 'First' }], showVerificationLabels: false });
    expect(card).toEqual({ entries: [{ rank: { fieldKey: 'rank', optionKey: 'a' }, next: null }], curricula: [{ id: 'cur_1', name: 'First' }], showVerificationLabels: false });
    expect(normalizeRankCard({ rank: { fieldKey: 'rank' }, curriculum: { id: 'cur_2' } }).curricula).toEqual([{ id: 'cur_2' }]);
  });

  test('library segments: Techniques only when the overview lists it, Saved only signed in with saves on', () => {
    const user = makeUser();
    const catalogOn = { ...property, features: { ...property.features, catalog: true } };
    const listed = normalizeOverview({ sections: [{ key: 'Library', available: true, segments: ['Courses', 'Techniques', 'Saved'] }] });
    const unlisted = normalizeOverview({ sections: [{ key: 'Library', available: true, segments: ['Courses', 'Saved'] }] });
    expect(librarySegmentsFor({ overview: listed, property: catalogOn, user })).toEqual(['courses', 'techniques', 'saved']);
    expect(librarySegmentsFor({ overview: unlisted, property: catalogOn, user })).toEqual(['courses', 'saved']);
    expect(librarySegmentsFor({ overview: listed, property, user })).toEqual(['courses', 'saved']);
    expect(librarySegmentsFor({ overview: listed, property: { ...catalogOn, features: { ...catalogOn.features, saves: false } }, user })).toEqual(['courses', 'techniques']);
    expect(librarySegmentsFor({ overview: null, property: catalogOn, user: null })).toEqual(['courses', 'techniques']);
  });

  test('routes: segments, saved targets, and website Training links', () => {
    expect(segmentHref('techniques')).toBe('/training/library/techniques');
    expect(segmentHref('nope')).toBe('/training/library/courses');
    expect(saveTargetPath('CatalogEntry', 'cat_1')).toBe('/catalog/cat_1');
    expect(saveTargetPath('Thread', 'thr_1')).toBe('/threads/thr_1');
    expect(saveTargetPath('Post', 'fpo_1')).toBe('/feed/fpo_1');
    expect(saveTargetPath('Unknown', 'x')).toBeNull();
    expect(trainingWebPath('/training')).toBe('/training');
    expect(trainingWebPath('/training/library/saved')).toBe('/training/library/saved');
    expect(trainingWebPath('/training/series/ser_1')).toBe('/series/ser_1');
    expect(trainingWebPath('/training/lessons/les_1', '?comment=cmt_1')).toBe('/lessons/les_1?comment=cmt_1');
    expect(trainingWebPath('/training/catalog/first-technique')).toBe('/catalog/first-technique');
    expect(trainingWebPath('/training/curricula/cur_1')).toBe('/training/curricula/cur_1');
    expect(trainingWebPath('/training/admin')).toBeNull();
    expect(trainingWebPath('/training/lessons/a%2Fb')).toBeNull();
  });

  test('the unified search body needs 2 characters and adds techniques only when available', () => {
    expect(librarySearchBody('a')).toBeNull();
    expect(librarySearchBody(' guard ')).toEqual({ query: 'guard', kinds: ['Series', 'Lesson'], maxResultsPerKind: 10 });
    expect(librarySearchBody('guard', { techniques: true }).kinds).toEqual(['Series', 'Lesson', 'CatalogEntry']);
    expect(librarySearchBody('x'.repeat(150)).query).toHaveLength(100);
  });

  test('the overview cache is per member and expires after 24 hours', async () => {
    const now = Date.parse('2026-11-25T12:00:00Z');
    await writeCachedOverview(Storage.fileStorage, 'usr_1', fullOverview(), now);
    expect((await readCachedOverview(Storage.fileStorage, 'usr_1', now + 1000)).overview.continue.objects).toHaveLength(1);
    expect(await readCachedOverview(Storage.fileStorage, 'usr_2', now + 1000)).toBeNull();
    expect(await readCachedOverview(Storage.fileStorage, 'usr_1', now + OverviewCacheMaxAgeMs + 1)).toBeNull();
  });
});

describe('Training Overview', () => {
  test('section tabs come from available sections and cards render in order, each only with data', async () => {
    serve();
    await renderRouter(APP_DIR, { initialUrl: '/training' });
    expect(await screen.findByTestId('training-overview')).toBeOnTheScreen();
    expect(trainingTestIds(/^training-section-/)).toEqual(['training-section-Overview', 'training-section-Library', 'training-section-Progress']);
    expect(screen.getByTestId('training-section-Overview')).toHaveProp('accessibilityState', { selected: true });
    expect(trainingTestIds(/^training-(up-next|continue|rank|coach|recent-saves|new|first-run)$/)).toEqual(['training-up-next', 'training-continue', 'training-coach', 'training-recent-saves', 'training-new']);
    expect(screen.getByText('Open session')).toBeOnTheScreen();
    expect(screen.getByTestId('training-continue-rail')).toHaveTextContent(new RegExp(t.overview.continueTraining));
    // A saved target that is no longer available shows without its title and does not open.
    expect(screen.getByTestId('training-save-fav_2')).toHaveProp('accessibilityState', { disabled: true });
    expect(screen.getByText(t.saved.unavailable)).toBeOnTheScreen();
    expect(screen.queryByTestId('training-first-run')).toBeNull();
  });

  test('cards open their targets and See all saved opens Saved', async () => {
    serve({ routes: { 'POST /me/saves/enumerate': enumerationResult([]), 'POST /me/save-collections/enumerate': enumerationResult([]) } });
    const router = await renderRouter(APP_DIR, { initialUrl: '/training' });
    await fireEvent.press(await screen.findByTestId('training-new-les_2'));
    await waitFor(() => expect(router.getPathname()).toBe('/lessons/les_2'));
    await act(async () => appRouter.back());
    await waitFor(() => expect(router.getPathname()).toBe('/training'));
    await fireEvent.press(await screen.findByTestId('training-see-saved'));
    await waitFor(() => expect(router.getPathname()).toBe('/training/library/saved'));
    expect(await screen.findByTestId('training-saved-empty')).toBeOnTheScreen();
  });

  test('a first-run member sees real featured courses and the places and events entries, never sample content', async () => {
    serve({ overview: emptyOverview() });
    const router = await renderRouter(APP_DIR, { initialUrl: '/training' });
    expect(await screen.findByTestId('training-first-run')).toBeOnTheScreen();
    expect(await screen.findByTestId('training-first-run-featured-ser_1')).toBeOnTheScreen();
    expect(screen.getByTestId('training-find-places')).toBeOnTheScreen();
    expect(screen.getByTestId('training-find-events')).toBeOnTheScreen();
    expect(screen.queryByTestId('training-up-next')).toBeNull();
    await fireEvent.press(screen.getByTestId('training-start-course'));
    await waitFor(() => expect(router.getPathname()).toBe('/training/library/courses'));
  });

  test('the rank card and curriculum card appear when the overview carries them', async () => {
    const overview = { ...emptyOverview(), firstRun: false, rank: { rank: { fieldKey: 'rank', optionKey: 'a', optionLabel: 'First', level: 0, verification: 'SelfReported' }, next: null, curriculum: { id: 'cur_1', name: 'First curriculum', itemCount: 4, progress: { completed: 1, total: 4 } }, showVerificationLabels: true } };
    serve({ overview });
    const router = await renderRouter(APP_DIR, { initialUrl: '/training' });
    expect(await screen.findByTestId('training-rank')).toBeOnTheScreen();
    expect(screen.queryByTestId('training-first-run')).toBeNull();
    await fireEvent.press(screen.getByTestId('training-curriculum-cur_1-open'));
    await waitFor(() => expect(router.getPathname()).toBe('/training/curricula/cur_1'));
  });

  test('signed out: the public Library only, without calling /me/training', async () => {
    const server = serve({ user: null });
    await renderRouter(APP_DIR, { initialUrl: '/training' });
    expect(await screen.findByTestId('screen-library')).toBeOnTheScreen();
    expect(screen.queryByTestId('training-section')).toBeNull();
    expect(screen.queryByTestId('training-segment-saved')).toBeNull();
    expect(server.called('GET /me/training')).toHaveLength(0);
  });

  test('offline, the cached overview keeps Continue training and the downloads entry', async () => {
    await writeCachedOverview(Storage.fileStorage, 'usr_1', normalizeOverview(fullOverview()));
    Network.__setState({ type: 'NONE', isConnected: false, isInternetReachable: false });
    const server = serve({ overview: { status: 500, body: { error: 'InternalError' } } });
    try {
      await renderRouter(APP_DIR, { initialUrl: '/training' });
      expect(await screen.findByTestId('training-offline')).toHaveTextContent(t.offlineCached);
      expect(screen.getByTestId('training-continue')).toBeOnTheScreen();
      expect(server.called('GET /me/training')).toHaveLength(0);
    } finally {
      Network.__setState({ type: 'WIFI', isConnected: true, isInternetReachable: true });
    }
  });

  test('a failed request with a cached overview shows the cached copy', async () => {
    await writeCachedOverview(Storage.fileStorage, 'usr_1', normalizeOverview(fullOverview()));
    serve({ overview: { status: 500, body: { error: 'InternalError' } } });
    await renderRouter(APP_DIR, { initialUrl: '/training' });
    expect(await screen.findByTestId('training-offline')).toBeOnTheScreen();
    expect(screen.getByTestId('training-up-next')).toBeOnTheScreen();
  });

  test.each([[402, 'MembershipRequired'], [403, 'Forbidden'], [404, 'NotAvailable'], [503, 'FeatureDisabled']])('%s %s falls back to the Library alone, even with a cached overview', async (status, error) => {
    await writeCachedOverview(Storage.fileStorage, 'usr_1', normalizeOverview(fullOverview()));
    serve({ overview: { status, body: { error } } });
    const router = await renderRouter(APP_DIR, { initialUrl: '/training' });
    expect(await screen.findByTestId('screen-library')).toBeOnTheScreen();
    expect(screen.queryByTestId('training-overview')).toBeNull();
    expect(screen.queryByTestId('training-section')).toBeNull();
    expect(router.getPathname()).toBe('/training');
  });

  describe('on a tablet', () => {
    const original = Dimensions.get('window');
    const screenSize = Dimensions.get('screen');
    beforeEach(() => Dimensions.set({ window: { ...original, width: 1024, height: 768 }, screen: { ...screenSize, width: 1024, height: 768 } }));
    afterEach(async () => act(async () => Dimensions.set({ window: original, screen: screenSize })));

    test('the Overview uses two columns at 768 points and wider', async () => {
      serve();
      await renderRouter(APP_DIR, { initialUrl: '/training' });
      expect(await screen.findByTestId('training-overview-column-0')).toBeOnTheScreen();
      expect(screen.getByTestId('training-overview-column-1')).toBeOnTheScreen();
    });
  });
});

describe('Training Library', () => {
  test('section tabs switch to the Library, whose segments follow the overview', async () => {
    serve();
    const router = await renderRouter(APP_DIR, { initialUrl: '/training' });
    await fireEvent.press(await screen.findByTestId('training-section-Library'));
    await waitFor(() => expect(router.getPathname()).toBe('/training/library'));
    expect(await screen.findByTestId('screen-library')).toBeOnTheScreen();
    expect(trainingTestIds(/^training-segment-/)).toEqual(['training-segment-courses', 'training-segment-saved']);
    expect(screen.getByTestId('training-section-Library')).toHaveProp('accessibilityState', { selected: true });
  });

  test('Techniques appears when the overview lists it and the catalog is on', async () => {
    const overview = { ...fullOverview(), sections: sections({ segments: ['Courses', 'Techniques', 'Saved'] }) };
    serve({ overview, features: { catalog: true }, routes: { 'GET /catalog/config': { programs: [] }, 'POST /catalog/entries/enumerate': enumerationResult([technique]) } });
    const router = await renderRouter(APP_DIR, { initialUrl: '/training/library' });
    await fireEvent.press(await screen.findByTestId('training-segment-techniques'));
    await waitFor(() => expect(router.getPathname()).toBe('/training/library/techniques'));
    expect(await screen.findByText('First technique')).toBeOnTheScreen();
  });

  test('one search covers courses, lessons, and techniques', async () => {
    const overview = { ...fullOverview(), sections: sections({ segments: ['Courses', 'Techniques', 'Saved'] }) };
    const server = serve({ overview, features: { catalog: true }, routes: { 'POST /library/search': { series: enumerationResult([basics]), lessons: enumerationResult([first]), techniques: enumerationResult([technique]) } } });
    const router = await renderRouter(APP_DIR, { initialUrl: '/training/library/courses' });
    await fireEvent.changeText(await screen.findByTestId('training-search'), 'steps');
    expect(await screen.findByTestId('library-search-lesson-les_1')).toBeOnTheScreen();
    expect(screen.getByTestId('library-search-series-ser_1')).toBeOnTheScreen();
    expect(server.called('POST /library/search')[0].body).toEqual({ query: 'steps', kinds: ['Series', 'Lesson', 'CatalogEntry'], maxResultsPerKind: 10 });
    await fireEvent.press(screen.getByTestId('library-search-technique-cat_1'));
    await waitFor(() => expect(router.getPathname()).toBe('/catalog/first-technique'));
  });
});

describe('lesson and technique links', () => {
  function lastPlayer() {
    return Video.__players[Video.__players.length - 1];
  }

  test('techniques in a lesson seek the player and open entries; lessons that teach a technique open lessons; Back returns', async () => {
    serve({
      features: { catalog: true },
      routes: {
        'POST /lessons/les_1/catalog-entries/enumerate': enumerationResult([technique]),
        'POST /lessons/les_2/catalog-entries/enumerate': enumerationResult([]),
        'GET /catalog/config': { programs: [] },
        'GET /catalog/entries/first-technique': technique,
        'POST /catalog/entries/cat_1/relationships/enumerate': enumerationResult([]),
        'POST /catalog/entries/cat_1/lessons/enumerate': enumerationResult([second])
      }
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/lessons/les_1' });
    expect(await screen.findByText(t.links.techniquesInLesson)).toBeOnTheScreen();
    await waitFor(() => expect(lastPlayer()).toBeTruthy());
    await fireEvent.press(screen.getByTestId('lesson-techniques-seek-cat_1'));
    expect(lastPlayer().currentTime).toBe(95);
    await fireEvent.press(screen.getByTestId('lesson-techniques-cat_1'));
    await waitFor(() => expect(router.getPathname()).toBe('/catalog/first-technique'));
    expect(await screen.findByText(t.links.lessonsTeaching)).toBeOnTheScreen();
    await fireEvent.press(await screen.findByTestId('catalog-lessons-les_2'));
    await waitFor(() => expect(router.getPathname()).toBe('/lessons/les_2'));
    await act(async () => appRouter.back());
    await waitFor(() => expect(router.getPathname()).toBe('/catalog/first-technique'));
    await act(async () => appRouter.back());
    await waitFor(() => expect(router.getPathname()).toBe('/lessons/les_1'));
  });
});
