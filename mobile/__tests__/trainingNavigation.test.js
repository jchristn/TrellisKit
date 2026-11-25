import { resolveMobileNavigation } from '@trelliskit/shared';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import { router as appRouter } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { Platform } from 'react-native';
import { createLibraryFake, makeLesson, makeSeries } from '@trelliskit/shared/testing';
import { destinationMatches } from '../src/navigation/AppNavigation';
import { resolveAppLink } from '../src/linkTargets';
import { resolveNotificationPath } from '../src/notifications/deepLinks';
import * as Storage from '../src/library/storage';
import { enumerationResult } from '../test-utils/community';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { fiveShortcutsProperty } from '../test-utils/fiveShortcuts';
import { property } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');
const basics = makeSeries('ser_1', { title: 'Getting started' });
const first = makeLesson('les_1', basics, { title: 'First steps', position: 1 });
const overview = { sections: [{ key: 'Overview', available: true }, { key: 'Library', available: true, segments: ['Courses', 'Saved'] }, { key: 'Progress', available: true }], firstRun: true };
const withTrainingInDrawer = { ...property, features: { ...property.features, training: true }, mobile: { ...property.mobile, navigation: { drawerItems: ['feed', 'training', 'events', 'profile', 'settings'], shortcuts: ['feed', 'profile'], accountMenu: [] } } };

function serve(propertyDoc, routes = {}) {
  const fake = createLibraryFake({ series: [basics], lessons: [first] });
  return installFakeServer(
    {
      'GET /property': propertyDoc,
      ...signedInAs(makeUser()),
      'GET /me/training': overview,
      'POST /me/saves/state': (call) => ({ items: call.body.targets.map((target) => ({ ...target, saved: false, favoriteId: null })) }),
      'POST /feed/enumerate': enumerationResult([]),
      'POST /labels/enumerate': enumerationResult([]),
      'POST /tags/keys/enumerate': enumerationResult([]),
      'POST /conversations/enumerate': enumerationResult([]),
      'POST /notifications/enumerate': enumerationResult([]),
      'POST /members/directory/enumerate': enumerationResult([]),
      'POST /media/grant': { cookies: [], expiresUtc: '2099-01-01T00:00:00Z' },
      ...routes
    },
    { fallback: (call) => fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body }) }
  );
}

beforeEach(() => {
  Platform.OS = 'android';
  resetEnvironment();
  Storage.__store.clear();
});
afterEach(() => {
  Platform.OS = 'ios';
  delete global.fetch;
});

test('Training in the bar: five shortcuts, the Overview, and section routes keep the bar and the menu', async () => {
  serve(fiveShortcutsProperty);
  const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
  const bar = await screen.findByTestId('navigation-shortcuts');
  expect(within(bar).getAllByRole('tab').map((item) => item.props.testID)).toEqual(['tab-feed', 'tab-training', 'tab-members', 'tab-messages', 'tab-profile']);
  await fireEvent.press(screen.getByTestId('tab-training'));
  await waitFor(() => expect(router.getPathname()).toBe('/training'));
  expect(await screen.findByTestId('training-overview')).toBeOnTheScreen();
  await fireEvent.press(screen.getByTestId('training-section-Library'));
  await waitFor(() => expect(router.getPathname()).toBe('/training/library'));
  expect(await screen.findByTestId('screen-library')).toBeOnTheScreen();
  expect(screen.getByTestId('navigation-shortcuts')).toBeOnTheScreen();
  expect(screen.getByTestId('tab-training')).toHaveProp('accessibilityState', { selected: true });
  expect(screen.getByTestId('header-menu')).toBeOnTheScreen();
  // Training covers the library and the catalog, so the drawer does not append them.
  await fireEvent.press(screen.getByTestId('header-menu'));
  const drawer = await screen.findByTestId('navigation-drawer');
  expect(within(drawer).queryByTestId('drawer-library')).toBeNull();
  expect(within(drawer).queryByTestId('drawer-catalog')).toBeNull();
});

test('Training in the drawer replaces Library and Techniques there', async () => {
  serve(withTrainingInDrawer);
  const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
  await fireEvent.press(await screen.findByTestId('header-menu'));
  const drawer = await screen.findByTestId('navigation-drawer');
  expect(within(drawer).getByTestId('drawer-training')).toBeOnTheScreen();
  expect(within(drawer).queryByTestId('drawer-library')).toBeNull();
  expect(within(drawer).queryByTestId('drawer-catalog')).toBeNull();
  await fireEvent.press(within(drawer).getByTestId('drawer-training'));
  await waitFor(() => expect(router.getPathname()).toBe('/training'));
  expect(await screen.findByTestId('training-overview')).toBeOnTheScreen();
  expect(resolveMobileNavigation(withTrainingInDrawer, makeUser()).drawerItems.map((item) => item.id)).not.toContain('library');
});

test('/library opens Training Courses when Training is placed, and stays the library home otherwise', async () => {
  serve(withTrainingInDrawer);
  const router = await renderRouter(APP_DIR, { initialUrl: '/library' });
  await waitFor(() => expect(router.getPathname()).toBe('/training/library/courses'));
  expect(await screen.findByTestId('screen-library')).toBeOnTheScreen();
});

test('/library without Training placed keeps its own screen', async () => {
  serve({ ...withTrainingInDrawer, features: { ...withTrainingInDrawer.features, training: false } });
  const router = await renderRouter(APP_DIR, { initialUrl: '/library' });
  expect(await screen.findByTestId('screen-library')).toBeOnTheScreen();
  expect(router.getPathname()).toBe('/library');
});

test('series, lesson, and catalog deep links still open their own screens', async () => {
  serve({ ...withTrainingInDrawer, features: { ...withTrainingInDrawer.features, catalog: true } }, { 'GET /catalog/config': { programs: [] }, 'GET /catalog/entries/cat_1': { id: 'cat_1', name: 'First technique', kind: 'Technique' }, 'POST /catalog/entries/cat_1/relationships/enumerate': enumerationResult([]), 'POST /catalog/entries/cat_1/lessons/enumerate': enumerationResult([]) });
  await renderRouter(APP_DIR, { initialUrl: '/series/ser_1' });
  expect(await screen.findByTestId('series-title')).toHaveTextContent('Getting started');
  await act(async () => appRouter.push('/lessons/les_1'));
  expect(await screen.findByTestId('lesson-title')).toHaveTextContent('First steps');
  await act(async () => appRouter.push('/catalog/cat_1'));
  expect(await screen.findByText('First technique')).toBeOnTheScreen();
});

test('a placed Techniques destination opens the Training Techniques segment', () => {
  const placed = { ...property, features: { ...property.features, catalog: true }, mobile: { ...property.mobile, navigation: { drawerItems: ['catalog'], shortcuts: [], accountMenu: [] } } };
  const item = resolveMobileNavigation(placed, makeUser()).drawerItems.find((entry) => entry.id === 'catalog');
  expect(item.href).toBe('/training/library/techniques');
  expect(destinationMatches(item, '/training/library/techniques')).toBe(true);
  const training = { id: 'training', href: '/training' };
  for (const pathname of ['/training', '/training/library', '/training/library/saved', '/training/saved', '/training/schedule', '/training/progress']) expect(destinationMatches(training, pathname)).toBe(true);
  expect(destinationMatches(training, '/training/curricula/cur_1')).toBe(false);
  expect(destinationMatches({ id: 'feed', href: '/feed' }, '/training')).toBe(false);
});

test('website Training links open native screens', () => {
  const web = 'https://' + property.domains.web;
  const cases = {
    '/app/training': '/training',
    '/app/training/library/courses': '/training/library/courses',
    '/app/training/library/techniques': '/training/library/techniques',
    '/app/training/library/saved': '/training/library/saved',
    '/app/training/schedule': '/training/schedule',
    '/app/training/progress': '/training/progress',
    '/app/training/curricula/cur_1': '/training/curricula/cur_1',
    '/app/training/series/ser_1': '/series/ser_1',
    '/app/training/lessons/les_1?comment=cmt_1': '/lessons/les_1?comment=cmt_1',
    '/app/training/catalog/cat_1': '/catalog/cat_1',
    '/app/library': '/library',
    '/app/series/ser_1': '/series/ser_1',
    '/app/lessons/les_1': '/lessons/les_1',
    '/app/catalog/cat_1': '/catalog/cat_1'
  };
  for (const [from, to] of Object.entries(cases)) expect([from, resolveAppLink(web + from, property)]).toEqual([from, { kind: 'native', path: to }]);
  expect(resolveAppLink(web + '/app/training/unknown/x', property)).toEqual({ kind: 'unsupported' });
  expect(resolveAppLink('/training/library/saved', property)).toEqual({ kind: 'native', path: '/training/library/saved' });
  expect(resolveNotificationPath('/training/library/techniques')).toBe('/training/library/techniques');
  expect(resolveNotificationPath('/training/elsewhere')).toBeNull();
});
