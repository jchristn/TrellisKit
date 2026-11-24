import { resolveMobileNavigation } from '@trelliskit/shared';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import { router as appRouter } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { Dimensions, Platform } from 'react-native';
import { destinationMatches, drawerHeaderOptions } from '../src/navigation/AppNavigation';
import { enumerationResult } from '../test-utils/community';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { property } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');
const member = { id: 'usr_other', displayName: 'Another member', handle: 'another', profile: {}, labels: [], tags: {} };
function serve(propertyDoc = property, user = makeUser()) {
  return installFakeServer({
    ...signedInAs(user), 'GET /property': propertyDoc,
    'POST /members/directory/enumerate': enumerationResult([member]),
    'POST /feed/enumerate': enumerationResult([]), 'POST /labels/enumerate': enumerationResult([]), 'POST /tags/keys/enumerate': enumerationResult([]),
    'GET /users/usr_other': { ...member, bio: null, createdUtc: '2026-01-01T00:00:00Z', isBlockedByMe: false },
    'POST /conversations/enumerate': enumerationResult([]), 'POST /notifications/enumerate': enumerationResult([])
  });
}
async function openMenu() {
  await fireEvent.press(await screen.findByTestId('header-menu'));
  return screen.findByTestId('navigation-drawer');
}
beforeEach(() => { Platform.OS = 'android'; resetEnvironment(); });
afterEach(() => { Platform.OS = 'ios'; delete global.fetch; jest.restoreAllMocks(); });

test('the active bundled property renders its drawer and shortcuts, and Members navigates outside Settings', async () => {
  const user = makeUser(); serve(property, user);
  const router = await renderRouter(APP_DIR, { initialUrl: '/profile' });
  expect(await screen.findByTestId('screen-profile')).toBeOnTheScreen();
  expect(screen.queryByTestId('profile-members')).toBeNull();
  const expected = resolveMobileNavigation(property, user);
  const bar = screen.queryByTestId('navigation-shortcuts');
  if (expected.shortcuts.length) {
    expect(bar).toBeOnTheScreen();
    expect(within(bar).getAllByRole('tab').map((item) => item.props.testID)).toEqual(expected.shortcuts.map((item) => 'tab-' + item.id));
  } else expect(bar).toBeNull();
  const drawer = await openMenu();
  for (const destination of expected.drawerItems) expect(within(drawer).getByTestId('drawer-' + destination.id)).toBeOnTheScreen();
  expect(within(drawer).getByText(expected.drawerItems.find((item) => item.id === 'members').label)).toBeOnTheScreen();
  await fireEvent.press(within(drawer).getByTestId('drawer-members'));
  await waitFor(() => expect(router.getPathname()).toBe('/members'));
  expect(screen.queryByTestId('navigation-drawer')).toBeNull();
  expect(await screen.findByTestId('directory-member-usr_other')).toBeOnTheScreen();
  expect(screen.getByTestId('header-menu')).toBeOnTheScreen();
});

test('property-selected Members and Messages shortcuts use real routes and mark the active destination', async () => {
  serve({ ...property, mobile: { ...property.mobile, navigation: { shortcuts: ['feed', 'members', 'messages', 'profile'] } } });
  const router = await renderRouter(APP_DIR, { initialUrl: '/profile' });
  await fireEvent.press(await screen.findByTestId('tab-members'));
  await waitFor(() => expect(router.getPathname()).toBe('/members'));
  expect(screen.getByTestId('tab-members')).toHaveProp('accessibilityState', { selected: true });
  await fireEvent.press(screen.getByTestId('tab-messages'));
  await waitFor(() => expect(router.getPathname()).toBe('/messages'));
  expect(screen.getByTestId('tab-messages')).toHaveProp('accessibilityState', { selected: true });
  await fireEvent.press(screen.getByTestId('tab-profile'));
  await waitFor(() => expect(router.getPathname()).toBe('/profile'));
  expect(screen.getByTestId('tab-profile')).toHaveProp('accessibilityState', { selected: true });
});

test('drawer closes with close button, backdrop, accessibility escape and Android request-close', async () => {
  serve(); await renderRouter(APP_DIR, { initialUrl: '/profile' });
  await openMenu(); await fireEvent.press(screen.getByTestId('drawer-close'));
  expect(screen.queryByTestId('navigation-drawer')).toBeNull();
  await openMenu(); await fireEvent.press(screen.getByTestId('drawer-backdrop'));
  expect(screen.queryByTestId('navigation-drawer')).toBeNull();
  await openMenu(); await fireEvent(screen.getByTestId('navigation-drawer'), 'accessibilityEscape');
  expect(screen.queryByTestId('navigation-drawer')).toBeNull();
  await openMenu();
  await fireEvent(screen.getByTestId('navigation-drawer-modal'), 'requestClose');
  expect(screen.queryByTestId('navigation-drawer')).toBeNull();
});

test('Settings opens account preferences; nested pages keep Back and no shortcut bar', async () => {
  serve(); const router = await renderRouter(APP_DIR, { initialUrl: '/profile' });
  await openMenu(); await fireEvent.press(screen.getByTestId('drawer-settings'));
  await waitFor(() => expect(router.getPathname()).toBe('/account'));
  expect(await screen.findByTestId('screen-account')).toBeOnTheScreen();
  expect(screen.getByTestId('header-menu')).toBeOnTheScreen();
  await fireEvent.press(screen.getByTestId('account-edit-profile'));
  await waitFor(() => expect(router.getPathname()).toBe('/account/edit-profile'));
  expect(screen.queryByTestId('navigation-shortcuts')).toBeNull();
  expect(screen.queryByTestId('header-menu')).toBeNull();
  await act(async () => appRouter.back());
  await waitFor(() => expect(router.getPathname()).toBe('/account'));
  expect(screen.getByTestId('header-menu')).toBeOnTheScreen();
});

test('member detail retains its deep link and Back returns to the directory', async () => {
  serve(); const router = await renderRouter(APP_DIR, { initialUrl: '/members' });
  await fireEvent.press(await screen.findByTestId('directory-member-usr_other'));
  await waitFor(() => expect(router.getPathname()).toBe('/members/usr_other'));
  expect(screen.queryByTestId('header-menu')).toBeNull();
  expect(screen.queryByTestId('navigation-shortcuts')).toBeNull();
  await act(async () => appRouter.back());
  await waitFor(() => expect(router.getPathname()).toBe('/members'));
  expect(screen.getByTestId('header-menu')).toBeOnTheScreen();
});

test('disabled destinations hide from both surfaces and empty shortcuts allows drawer-only navigation', async () => {
  serve({ ...property, features: { ...property.features, library: false, messaging: false, ai: false }, mobile: { ...property.mobile, navigation: { shortcuts: [] } } });
  await renderRouter(APP_DIR, { initialUrl: '/profile' }); await openMenu();
  expect(screen.queryByTestId('drawer-library')).toBeNull();
  expect(screen.queryByTestId('drawer-messages')).toBeNull();
  expect(screen.queryByTestId('drawer-assistants')).toBeNull();
  expect(screen.queryByTestId('navigation-shortcuts')).toBeNull();
  expect(screen.getByTestId('drawer-members')).toBeOnTheScreen();
});

test('phone/tablet drawer exposes scrollable full-size labels', async () => {
  serve(); const original = Dimensions.get('window');
  const router = await renderRouter(APP_DIR, { initialUrl: '/profile' });
  for (const width of [320, 1180]) {
    await act(async () => Dimensions.set({ window: { ...original, width, fontScale: 1.8 } }));
    const drawer = await openMenu();
    expect(within(drawer).getByTestId('drawer-scroll')).toBeOnTheScreen();
    expect(within(drawer).getByTestId('drawer-settings')).toHaveProp('accessibilityState', { selected: false });
    await fireEvent.press(screen.getByTestId('drawer-close'));
  }
  await act(async () => Dimensions.set({ window: original }));
  expect(router.getPathname()).toBe('/profile');
});

test('map sections select separately and detail URLs do not select top-level shortcuts', () => {
  expect(destinationMatches({ href: '/map?section=events' }, '/map', 'events')).toBe(true);
  expect(destinationMatches({ href: '/map?section=places' }, '/map', 'events')).toBe(false);
  expect(destinationMatches({ href: '/map?section=places' }, '/map', undefined)).toBe(true);
  expect(destinationMatches({ href: '/members' }, '/members/usr_other')).toBe(false);
});


test('the drawer switches the shared map between Events and the venue directory', async () => {
  serve({ ...property, features: { ...property.features, directory: true, events: true } });
  const router = await renderRouter(APP_DIR, { initialUrl: '/profile' });
  await openMenu(); await fireEvent.press(screen.getByTestId('drawer-events'));
  await waitFor(() => expect(router.getPathname()).toBe('/map'));
  expect(await screen.findByTestId('map-section-events')).toHaveProp('accessibilityState', { selected: true });
  await openMenu();
  expect(screen.getByTestId('drawer-events')).toHaveProp('accessibilityState', { selected: true });
  await fireEvent.press(screen.getByTestId('drawer-directory'));
  expect(await screen.findByTestId('map-section-places')).toHaveProp('accessibilityState', { selected: true });
  await openMenu();
  expect(screen.getByTestId('drawer-directory')).toHaveProp('accessibilityState', { selected: true });
});

test('Notifications and About are drawer destinations with a menu to navigate onward', async () => {
  serve(); const router = await renderRouter(APP_DIR, { initialUrl: '/profile' });
  for (const [id, pathname] of [['notifications', '/notifications'], ['about', '/profile/about']]) {
    await openMenu(); await fireEvent.press(screen.getByTestId('drawer-' + id));
    await waitFor(() => expect(router.getPathname()).toBe(pathname));
    expect(screen.getByTestId('header-menu')).toBeOnTheScreen();
  }
});


test('native iOS menus use SF Symbol bar items and Android uses a React button', () => {
  const navigation = { enabled: true, open: jest.fn() };
  const ios = drawerHeaderOptions(navigation, 'ios');
  expect(ios.headerLeft).toBeUndefined();
  const items = ios.unstable_headerLeftItems();
  expect(items).toEqual([expect.objectContaining({ identifier: 'header-menu', icon: { type: 'sfSymbol', name: 'line.3.horizontal' }, accessibilityLabel: 'Open menu' })]);
  items[0].onPress();
  expect(navigation.open).toHaveBeenCalledTimes(1);
  expect(drawerHeaderOptions(navigation, 'android').headerLeft).toEqual(expect.any(Function));
  expect(drawerHeaderOptions(null, 'ios')).toEqual({});
});
