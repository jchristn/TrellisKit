import { createElement } from 'react';
import { Platform } from 'react-native';
import IconButton from '../components/IconButton';
import strings from '../strings';
import { useTheme } from '../theme';
import { useReducedMotion } from './useReducedMotion';

/**
 * Shared Stack screenOptions: themed header and content, and a cross-fade instead of the slide
 * transition when Reduce Motion is on.
 * @param {object} [extra] Additional screenOptions.
 */
export function useStackOptions(extra = {}) {
  const theme = useTheme();
  const reduceMotion = useReducedMotion();
  return {
    headerStyle: { backgroundColor: theme.colors.tabBar },
    headerTintColor: theme.colors.text,
    contentStyle: { backgroundColor: theme.colors.background },
    // iOS back buttons show only the chevron: a screen without a header (or a title) would
    // otherwise label the button with its raw route name, such as "welcome" or "index".
    headerBackButtonDisplayMode: 'minimal',
    ...(reduceMotion ? { animation: 'fade' } : null),
    ...extra
  };
}

/**
 * Whether a screen needs a back button to its parent navigator: it is the first screen of its
 * own stack and the parent stack can go back. React Navigation hands such a screen the parent's
 * back information, but the native iOS header cannot draw a back item for the root of a nested
 * stack, so /threads/{id} opened from the Forums tab would otherwise have no way back on screen.
 * Read when the header renders, because the nested stack's state does not exist yet when
 * screenOptions is first evaluated.
 * @param {object} navigation The screen's navigation object.
 * @param {object} route The screen's route.
 * @returns {boolean}
 */
export function needsParentBackButton(navigation, route) {
  if (!navigation || !route || !navigation.getState || !navigation.getParent) return false;
  const state = navigation.getState();
  const first = state && state.routes && state.routes[0];
  if (!first || first.key !== route.key) return false;
  const parent = navigation.getParent();
  return Boolean(parent && parent.canGoBack && parent.canGoBack());
}

/**
 * screenOptions for a root-level nested stack (account, threads, events, ...): useStackOptions,
 * plus a back button on the stack's first screen that returns to wherever the stack was opened
 * from. Later screens keep the native back button.
 *
 * iOS uses a native bar button item (an SF Symbol chevron) rather than a React view in the
 * navigation bar: popping the stack from a pressed React view in the bar left the navigation
 * bar's accessibility tree in a state that accessibility snapshots (XCTest UI automation on the
 * iOS simulator) walked without end, freezing the app.
 * Android uses headerLeft.
 * @param {object} [extra] Additional screenOptions.
 * @param {string} [os] Platform override for tests.
 * @returns {(props: { navigation: object, route: object }) => object}
 */
export function useNestedStackOptions(extra = {}, os = Platform.OS) {
  const base = useStackOptions(extra);
  return ({ navigation, route }) => {
    const goBack = () => navigation.goBack();
    if (os === 'ios') {
      return {
        ...base,
        unstable_headerLeftItems: () => (needsParentBackButton(navigation, route) ? [{ type: 'button', label: strings.common.back, accessibilityLabel: strings.common.back, icon: { type: 'sfSymbol', name: 'chevron.backward' }, identifier: 'header-back', onPress: goBack }] : undefined)
      };
    }
    return {
      ...base,
      headerLeft: ({ tintColor } = {}) =>
        needsParentBackButton(navigation, route)
          ? createElement(IconButton, {
              icon: 'arrow-back',
              label: strings.common.back,
              color: tintColor || base.headerTintColor,
              onPress: goBack,
              testID: 'header-back',
              style: { marginRight: 8 }
            })
          : null
    };
  };
}

/**
 * screenOptions for a tab's stack (feed, forums, library, map, profile): useStackOptions, plus the
 * parent back button on a detail screen that is the first screen of its stack. A notification,
 * a push, or a link elsewhere in the app opens `/feed/{id}` on top of the root stack, in a tab
 * stack that holds only that screen, so the native header has no previous screen to go back to
 * and the member would be stuck on the post. The tab's own `index` never gets the button.
 * @param {object} [extra] Additional screenOptions.
 * @param {string} [os] Platform override for tests.
 * @returns {(props: { navigation: object, route: object }) => object}
 */
export function useTabStackOptions(extra = {}, os = Platform.OS) {
  const base = useStackOptions(extra);
  const nested = useNestedStackOptions(extra, os);
  return (props) => (props && props.route && props.route.name === 'index' ? base : nested(props));
}
