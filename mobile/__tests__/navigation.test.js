import { renderHook } from '@testing-library/react-native';
import fs from 'fs';
import path from 'path';
import { needsParentBackButton, useNestedStackOptions, useTabStackOptions } from '../src/hooks/useStackOptions';

// The native header labels the back button with the previous screen's title, falling back to the
// route name. Route groups such as "(tabs)" must therefore carry a title, or screens pushed from
// them (Notifications, Messages, the composers) show "(tabs)" as their back label. The native
// header is not rendered under jest, so this reads the root layout's screen declarations.
describe('root stack back labels', () => {
  const source = fs.readFileSync(path.resolve(__dirname, '../app/_layout.jsx'), 'utf8');
  const groups = [...source.matchAll(/<Stack\.Screen name="(\([^"]+\))" options=\{\{([^}]*)\}\}/g)];

  test('declares the route groups', () => {
    expect(groups.map((g) => g[1])).toEqual(expect.arrayContaining(['(onboarding)', '(setup)', '(tabs)']));
  });

  test.each(groups.map((g) => [g[1], g[2]]))('%s has a title so it never becomes a back label', (name, options) => {
    expect(options).toMatch(/\btitle:\s*strings\./);
  });
});

// Root-level stacks (account, threads, events, ...) are opened from the tabs. The native header
// shows a back button only for a previous screen in the same stack, so their first screen needs
// the parent back button from useNestedStackOptions, or it has no way back on screen.
describe('root-level nested stacks', () => {
  const appDir = path.resolve(__dirname, '../app');
  const root = fs.readFileSync(path.join(appDir, '_layout.jsx'), 'utf8');
  const nested = [...root.matchAll(/<Stack\.Screen name="([a-z-]+)" options=\{\{[^}]*headerShown: false[^}]*\}\}/g)].map((m) => m[1]).filter((name) => fs.existsSync(path.join(appDir, name, '_layout.jsx')));

  test('finds the nested stacks', () => {
    expect(nested).toEqual(expect.arrayContaining(['account', 'threads', 'events', 'messages']));
  });

  test.each(nested)('%s uses useNestedStackOptions', (name) => {
    const source = fs.readFileSync(path.join(appDir, name, '_layout.jsx'), 'utf8');
    expect(source).toMatch(/screenOptions = useNestedStackOptions\(/);
  });

  test('the parent back button shows only on the first screen and when the parent can go back', () => {
    const first = { key: 'a' };
    const second = { key: 'b' };
    const nav = (routes, parentCanGoBack) => ({ goBack: jest.fn(), getState: () => ({ routes }), getParent: () => (parentCanGoBack === null ? undefined : { canGoBack: () => parentCanGoBack }) });
    expect(needsParentBackButton(nav([first, second], true), first)).toBe(true);
    expect(needsParentBackButton(nav([first, second], true), second)).toBe(false);
    expect(needsParentBackButton(nav([first], false), first)).toBe(false);
    expect(needsParentBackButton(nav([first], null), first)).toBe(false);
    // Before the nested stack has state (the first screenOptions pass) nothing is decided.
    expect(needsParentBackButton({ getState: () => undefined, getParent: () => ({ canGoBack: () => true }) }, first)).toBe(false);
    expect(needsParentBackButton(null, first)).toBe(false);

    return renderHook(() => ({ android: useNestedStackOptions({}, 'android'), ios: useNestedStackOptions({}, 'ios') })).then(({ result }) => {
      const navigation = nav([first, second], true);
      // Android: a later screen keeps the native back button; the first screen gets the custom one.
      expect(result.current.android({ navigation, route: second }).headerLeft({ canGoBack: true })).toBeNull();
      const button = result.current.android({ navigation, route: first }).headerLeft({ canGoBack: true, tintColor: '#123456' });
      expect(button.props.testID).toBe('header-back');
      button.props.onPress();
      expect(navigation.goBack).toHaveBeenCalledTimes(1);
      expect(result.current.android({ navigation: nav([first], false), route: first }).headerLeft({ canGoBack: false })).toBeNull();
      // iOS: a native bar button item, never a React view in the navigation bar.
      const ios = result.current.ios({ navigation, route: first });
      expect(ios.headerLeft).toBeUndefined();
      const items = ios.unstable_headerLeftItems({ canGoBack: true });
      expect(items).toEqual([expect.objectContaining({ type: 'button', icon: { type: 'sfSymbol', name: 'chevron.backward' }, accessibilityLabel: 'Back' })]);
      items[0].onPress();
      expect(navigation.goBack).toHaveBeenCalledTimes(2);
      expect(result.current.ios({ navigation, route: second }).unstable_headerLeftItems({ canGoBack: true })).toBeUndefined();
    });
  });

  // A notification or push opens /feed/{id} above the root stack, in a tab stack that holds only
  // that screen. Without the parent back button the member cannot leave the post.
  test.each(['feed', 'forums', 'library', 'map', 'profile'])('the %s tab stack uses useTabStackOptions', (name) => {
    const source = fs.readFileSync(path.join(appDir, '(tabs)', name, '_layout.jsx'), 'utf8');
    expect(source).toMatch(/screenOptions = useTabStackOptions\(/);
  });

  test('a tab stack gives a detail screen opened on its own a back button, and never the tab index', () => {
    const nav = (routes, parentCanGoBack) => ({ goBack: jest.fn(), getState: () => ({ routes }), getParent: () => ({ canGoBack: () => parentCanGoBack }) });
    return renderHook(() => ({ android: useTabStackOptions({}, 'android'), ios: useTabStackOptions({}, 'ios') })).then(({ result }) => {
      const post = { key: 'p', name: '[id]' };
      const index = { key: 'i', name: 'index' };
      // Opened from a notification: the post is the only screen of the feed stack.
      const alone = nav([post], true);
      const button = result.current.android({ navigation: alone, route: post }).headerLeft({});
      expect(button.props.testID).toBe('header-back');
      button.props.onPress();
      expect(alone.goBack).toHaveBeenCalledTimes(1);
      expect(result.current.ios({ navigation: alone, route: post }).unstable_headerLeftItems({})).toEqual([expect.objectContaining({ identifier: 'header-back' })]);
      // Opened from the feed: the native back button already returns to the feed.
      expect(result.current.android({ navigation: nav([index, post], true), route: post }).headerLeft({})).toBeNull();
      expect(result.current.ios({ navigation: nav([index, post], true), route: post }).unstable_headerLeftItems({})).toBeUndefined();
      // The tab's first screen has no back button, even after switching tabs.
      const home = result.current.android({ navigation: nav([index], true), route: index });
      expect(home.headerLeft).toBeUndefined();
      expect(result.current.ios({ navigation: nav([index], true), route: index }).unstable_headerLeftItems).toBeUndefined();
    });
  });

  test('the Map tab header follows the selected segment', () => {
    const source = fs.readFileSync(path.join(appDir, '(tabs)', 'map', 'index.jsx'), 'utf8');
    expect(source).toMatch(/<Stack\.Screen options=\{\{ title: current === 'events' \? strings\.events\.title : t\.title \}\} \/>/);
  });
});
