import Ionicons from '@expo/vector-icons/Ionicons';
import { resolveMobileNavigation } from '@trelliskit/shared';
import { useGlobalSearchParams, usePathname, useRouter } from 'expo-router';
import { createContext, createElement, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { AccessibilityInfo, Animated, findNodeHandle, Keyboard, Modal, Platform, Pressable, ScrollView, StyleSheet, useWindowDimensions, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useAuth } from '../auth/AuthProvider';
import { canEnterTabs } from '../auth/routing';
import AppText from '../components/AppText';
import IconButton from '../components/IconButton';
import { useReducedMotion } from '../hooks/useReducedMotion';
import { messagesBadgeCount } from '../messaging/format';
import { useMessaging } from '../messaging/MessagingProvider';
import { badgeText } from '../notifications/deepLinks';
import { useNotificationsState } from '../notifications/NotificationsProvider';
import { useProperty } from '../property';
import strings from '../strings';
import { useTheme } from '../theme';

const NavigationContext = createContext(null);

/** Whether a destination matches the current top-level route, including the two map sections. */
export function destinationMatches(item, pathname, section) {
  const [path, query] = item.href.split('?');
  if (path !== pathname) return false;
  if (!query) return true;
  const expected = new URLSearchParams(query).get('section');
  return !expected || expected === (section || 'places');
}

/** Navigation state is optional so standalone screen and header tests retain their existing behavior. */
export function useAppNavigation() {
  return useContext(NavigationContext);
}

/** The top-left menu button opens the drawer without changing the native navigation stack. */
export function DrawerMenuButton() {
  const navigation = useAppNavigation();
  const buttonRef = useRef(null);
  if (!navigation || !navigation.enabled) return null;
  return <IconButton icon="menu-outline" label={strings.navigation.openMenu} onPress={() => navigation.open(buttonRef.current)} buttonRef={buttonRef} testID="header-menu" style={{ marginRight: 8 }} />;
}

/** Native iOS bar items avoid mounting React controls in a native navigation bar. */
export function drawerHeaderOptions(navigation, os = Platform.OS) {
  if (!navigation?.enabled) return {};
  if (os === 'ios') return {
    headerBackVisible: false,
    unstable_headerLeftItems: () => [{ type: 'button', label: strings.navigation.openMenu, accessibilityLabel: strings.navigation.openMenu, icon: { type: 'sfSymbol', name: 'line.3.horizontal' }, identifier: 'header-menu', onPress: () => navigation.open() }]
  };
  return { headerBackVisible: false, headerLeft: () => createElement(DrawerMenuButton) };
}

function CountBadge({ count, testID }) {
  const theme = useTheme();
  const text = badgeText(count);
  if (!text) return null;
  return (
    <View testID={testID} style={[styles.badge, { backgroundColor: theme.colors.danger, borderRadius: theme.radii.pill }]} accessibilityElementsHidden importantForAccessibility="no">
      <AppText variant="caption" style={{ color: theme.dark ? '#0E1116' : '#FFFFFF', fontWeight: '700' }}>{text}</AppText>
    </View>
  );
}

/**
 * Complete app navigation, with a modal side drawer and optional property-selected shortcuts.
 * The existing route stacks own screen state and deep links; both menus navigate to those same
 * routes. Detail and onboarding screens keep their ordinary Back behavior and hide shortcuts.
 */
export function AppNavigationProvider({ children }) {
  const theme = useTheme();
  const property = useProperty();
  const { session, user } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const { section } = useGlobalSearchParams();
  const insets = useSafeAreaInsets();
  const { width } = useWindowDimensions();
  const reduceMotion = useReducedMotion();
  const { unread } = useMessaging();
  const { unreadCount } = useNotificationsState();
  const enabled = canEnterTabs(session, property);
  const [drawerState, setDrawerState] = useState(() => ({ visible: false, pathname, section, property, enabled }));
  const changed = drawerState.pathname !== pathname || drawerState.section !== section || drawerState.property !== property || drawerState.enabled !== enabled;
  // Reset transient menu state with its route/session snapshot. A push or deep link must close
  // it immediately, and returning to the previous route must not reopen an abandoned menu.
  if (changed) setDrawerState({ visible: false, pathname, section, property, enabled });
  const visible = !changed && drawerState.visible;
  const closeRef = useRef(null);
  const triggerRef = useRef(null);
  const restoreFocusRef = useRef(true);
  const wasVisibleRef = useRef(false);
  const [progress] = useState(() => new Animated.Value(0));
  const navigation = useMemo(() => resolveMobileNavigation(property, user, {
    feed: strings.tabs.feed, forums: strings.tabs.forums, library: strings.tabs.library,
    events: strings.events.title, messages: strings.messaging.title, assistants: strings.assistants.menu,
    profile: strings.tabs.profile, announcements: strings.announcements.menu, ideas: strings.ideas.menu,
    support: strings.support.menu, settings: strings.navigation.settings, about: strings.profile.about,
    notifications: strings.notifications.title
  }), [property, user]);
  const isMainDestination = navigation.drawerItems.some((item) => destinationMatches(item, pathname, section));
  const open = useCallback((trigger) => {
    if (enabled) { Keyboard.dismiss(); triggerRef.current = trigger; restoreFocusRef.current = true; setDrawerState({ visible: true, pathname, section, property, enabled }); }
  }, [enabled, pathname, section, property]);
  const close = useCallback(() => setDrawerState((current) => ({ ...current, visible: false })), []);
  const value = useMemo(() => ({ enabled, isMainDestination, open, close }), [enabled, isMainDestination, open, close]);
  const drawerWidth = Math.min(360, Math.max(240, width - 48));

  useEffect(() => {
    const wasVisible = wasVisibleRef.current;
    wasVisibleRef.current = visible;
    if (wasVisible && !visible && enabled && !changed && restoreFocusRef.current && triggerRef.current) {
      const frame = requestAnimationFrame(() => {
        const node = findNodeHandle(triggerRef.current);
        if (node) AccessibilityInfo.setAccessibilityFocus(node);
      });
      return () => cancelAnimationFrame(frame);
    }
    return undefined;
  }, [visible, enabled, changed]);
  useEffect(() => {
    if (!visible) { progress.setValue(0); return undefined; }
    const animation = Animated.timing(progress, { toValue: 1, duration: reduceMotion ? 0 : 180, useNativeDriver: true });
    animation.start();
    return () => animation.stop();
  }, [visible, progress, reduceMotion]);

  function select(item) {
    restoreFocusRef.current = destinationMatches(item, pathname, section);
    close();
    if (!destinationMatches(item, pathname, section)) router.navigate(item.href);
  }
  function count(item) {
    if (item.locked) return 0;
    return item.id === 'messages' ? messagesBadgeCount(unread) : item.id === 'notifications' ? unreadCount : 0;
  }
  function label(item) {
    const base = item.locked ? strings.access.lockedLabel(item.label) : item.label;
    const n = count(item);
    return n ? strings.navigation.unread(base, badgeText(n)) : base;
  }
  function row(item) {
    const selected = destinationMatches(item, pathname, section);
    return (
      <Pressable key={item.id} testID={'drawer-' + item.id} accessibilityRole="button" accessibilityLabel={label(item)} accessibilityState={{ selected }} onPress={() => select(item)}
        style={({ pressed }) => [styles.drawerRow, { minHeight: theme.layout.minTouchTarget, gap: theme.spacing.md, padding: theme.spacing.md, borderRadius: theme.radii.md, backgroundColor: selected ? theme.colors.accentSoft : pressed ? theme.colors.surface : 'transparent' }]}>
        <Ionicons name={item.locked ? 'lock-closed-outline' : item.icon + '-outline'} size={23} color={selected ? theme.colors.onAccentSoft : theme.colors.textMuted} />
        <AppText style={{ flex: 1, color: selected ? theme.colors.onAccentSoft : theme.colors.text, fontWeight: selected ? '700' : '500' }}>{item.label}</AppText>
        <CountBadge count={count(item)} testID={'drawer-' + item.id + '-badge'} />
      </Pressable>
    );
  }

  return (
    <NavigationContext.Provider value={value}>
      <View style={styles.frame}>
        <View style={styles.frame} accessibilityElementsHidden={visible} importantForAccessibility={visible ? 'no-hide-descendants' : 'auto'}>
          {children}
          {enabled && isMainDestination && navigation.shortcuts.length ? (
            <View testID="navigation-shortcuts" accessibilityRole="tablist" style={[styles.shortcuts, { paddingBottom: insets.bottom, paddingLeft: insets.left, paddingRight: insets.right, borderTopColor: theme.colors.border, backgroundColor: theme.colors.tabBar }]}>
              {navigation.shortcuts.map((item) => {
                const selected = destinationMatches(item, pathname, section);
                return <Pressable key={item.id} testID={'tab-' + item.id} accessibilityRole="tab" accessibilityLabel={label(item)} accessibilityState={{ selected }} onPress={() => select(item)}
                  style={({ pressed }) => [styles.shortcut, { minHeight: theme.layout.minTouchTarget + 16, paddingVertical: theme.spacing.xs, opacity: pressed ? 0.65 : 1 }]}>
                  <View style={styles.shortcutIcon}>
                    <Ionicons name={item.locked ? 'lock-closed-outline' : item.icon + (selected ? '' : '-outline')} size={23} color={selected ? theme.colors.primary : theme.colors.tabInactive} />
                    <CountBadge count={count(item)} testID={'tab-' + item.id + '-badge'} />
                  </View>
                  <AppText variant="caption" style={{ textAlign: 'center', color: selected ? theme.colors.primary : theme.colors.tabInactive, fontWeight: selected ? '700' : '500' }}>{item.label}</AppText>
                </Pressable>;
              })}
            </View>
          ) : null}
        </View>
        <Modal testID="navigation-drawer-modal" visible={visible && enabled} transparent animationType="none" supportedOrientations={['portrait', 'landscape']} statusBarTranslucent navigationBarTranslucent onRequestClose={close}
          onShow={() => { const node = findNodeHandle(closeRef.current); if (node) AccessibilityInfo.setAccessibilityFocus(node); }}>
          <View testID="navigation-drawer" style={styles.overlay} accessibilityViewIsModal onAccessibilityEscape={close} {...(Platform.OS === 'web' ? { onKeyDown: (event) => { if (event.key === 'Escape') close(); } } : {})}>
            <Pressable testID="drawer-backdrop" accessibilityRole="button" accessibilityLabel={strings.navigation.closeMenu} onPress={close} style={styles.scrim} importantForAccessibility="no" accessible={false} />
            <Animated.View testID="navigation-drawer-panel" style={[styles.drawer, { width: drawerWidth, paddingTop: insets.top, paddingBottom: insets.bottom, paddingLeft: insets.left, backgroundColor: theme.colors.background, transform: [{ translateX: progress.interpolate({ inputRange: [0, 1], outputRange: [-drawerWidth, 0] }) }] }]}>
              <View style={[styles.drawerHeading, { padding: theme.spacing.md, borderBottomColor: theme.colors.border }]}>
                <AppText variant="heading" accessibilityRole="header" style={{ flex: 1 }}>{property.shortName || property.name || strings.navigation.menu}</AppText>
                <Pressable ref={closeRef} testID="drawer-close" accessibilityRole="button" accessibilityLabel={strings.navigation.closeMenu} onPress={close} style={[styles.close, { minHeight: theme.layout.minTouchTarget, minWidth: theme.layout.minTouchTarget }]}>
                  <Ionicons name="close-outline" size={26} color={theme.colors.text} />
                </Pressable>
              </View>
              <ScrollView testID="drawer-scroll" keyboardShouldPersistTaps="handled" contentContainerStyle={{ padding: theme.spacing.sm }}>
                {navigation.drawerItems.filter((item) => item.section === 'community').map(row)}
                <View style={{ marginVertical: theme.spacing.sm, borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: theme.colors.border }} />
                <AppText variant="caption" color="textMuted" accessibilityRole="header" style={{ padding: theme.spacing.md }}>{strings.navigation.accountSection}</AppText>
                {navigation.drawerItems.filter((item) => item.section !== 'community').map(row)}
              </ScrollView>
            </Animated.View>
          </View>
        </Modal>
      </View>
    </NavigationContext.Provider>
  );
}

const styles = StyleSheet.create({
  frame: { flex: 1 }, overlay: { flex: 1 }, scrim: { position: 'absolute', top: 0, right: 0, bottom: 0, left: 0, backgroundColor: 'rgba(0,0,0,0.45)' },
  drawer: { height: '100%', alignSelf: 'flex-start' }, drawerHeading: { flexDirection: 'row', alignItems: 'center', borderBottomWidth: StyleSheet.hairlineWidth },
  close: { alignItems: 'center', justifyContent: 'center' }, drawerRow: { flexDirection: 'row', alignItems: 'center' },
  shortcuts: { flexDirection: 'row', borderTopWidth: StyleSheet.hairlineWidth }, shortcut: { flex: 1, alignItems: 'center', justifyContent: 'center', paddingHorizontal: 4, gap: 3 },
  shortcutIcon: { flexDirection: 'row', alignItems: 'center', gap: 4 }, badge: { minWidth: 18, paddingHorizontal: 4, alignItems: 'center' }
});
