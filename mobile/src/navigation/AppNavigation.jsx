import Ionicons from '@expo/vector-icons/Ionicons';
import { resolveMobileNavigation } from '@trelliskit/shared';
import { useGlobalSearchParams, usePathname, useRouter } from 'expo-router';
import { createContext, createElement, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { AccessibilityInfo, Animated, findNodeHandle, Keyboard, Modal, Platform, Pressable, ScrollView, StyleSheet, useWindowDimensions, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useAuth, useOptionalAuth } from '../auth/AuthProvider';
import { canEnterTabs } from '../auth/routing';
import AppText from '../components/AppText';
import Avatar from '../components/Avatar';
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

/**
 * The header account button (the member's avatar) opens the property's account menu. Hidden for
 * guests and when the property configures no account menu.
 */
export function AccountMenuButton() {
  const navigation = useAppNavigation();
  const auth = useOptionalAuth();
  const api = auth ? auth.api : null;
  const user = auth ? auth.user : null;
  const theme = useTheme();
  const buttonRef = useRef(null);
  if (!navigation || !navigation.enabled || !user || !navigation.accountMenu || !navigation.accountMenu.length) return null;
  const size = theme.layout.minTouchTarget;
  return (
    <Pressable ref={buttonRef} testID="header-account" accessibilityRole="button" accessibilityLabel={strings.navigation.openAccountMenu} hitSlop={4} onPress={() => navigation.openAccount(buttonRef.current)}
      style={({ pressed }) => [{ width: size, height: size, alignItems: 'center', justifyContent: 'center', opacity: pressed ? 0.6 : 1 }]}>
      <Avatar uri={user.avatarUrl && api ? api.url(user.avatarUrl) : null} token={api ? api.accessToken : null} name={user.displayName} size={30} />
    </Pressable>
  );
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
 * Complete app navigation: a modal side drawer, optional property-selected bottom shortcuts, and
 * an optional account menu opened from the header avatar. The existing route stacks own screen
 * state and deep links; every surface navigates to those same routes. Detail and onboarding
 * screens keep their ordinary Back behavior and hide shortcuts.
 */
export function AppNavigationProvider({ children }) {
  const theme = useTheme();
  const property = useProperty();
  const { api, session, user, signOut } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const { section } = useGlobalSearchParams();
  const insets = useSafeAreaInsets();
  const { width } = useWindowDimensions();
  const reduceMotion = useReducedMotion();
  const { unread } = useMessaging();
  const { unreadCount } = useNotificationsState();
  const enabled = canEnterTabs(session, property);
  const sessionKey = session.status + ':' + (user?.id || '');
  const [drawerState, setDrawerState] = useState(() => ({ menu: null, pathname, section, property, enabled, sessionKey }));
  const changed = drawerState.pathname !== pathname || drawerState.section !== section || drawerState.property !== property || drawerState.enabled !== enabled || drawerState.sessionKey !== sessionKey;
  // Reset transient menu state with its route/session snapshot. A push or deep link must close
  // it immediately, and returning to the previous route must not reopen an abandoned menu.
  if (changed) setDrawerState({ menu: null, pathname, section, property, enabled, sessionKey });
  const visible = !changed && drawerState.menu === 'drawer';
  const accountVisible = !changed && drawerState.menu === 'account';
  const anyOpen = visible || accountVisible;
  const closeRef = useRef(null);
  const accountFirstRef = useRef(null);
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
  const isMainDestination = [...navigation.drawerItems, ...navigation.shortcuts, ...navigation.accountMenu].some((item) => destinationMatches(item, pathname, section));
  const show = useCallback((menu, trigger) => {
    if (enabled) { Keyboard.dismiss(); triggerRef.current = trigger; restoreFocusRef.current = true; setDrawerState({ menu, pathname, section, property, enabled, sessionKey }); }
  }, [enabled, pathname, section, property, sessionKey]);
  const open = useCallback((trigger) => show('drawer', trigger), [show]);
  const openAccount = useCallback((trigger) => show('account', trigger), [show]);
  const close = useCallback(() => setDrawerState((current) => ({ ...current, menu: null })), []);
  const shortcutIds = useMemo(() => navigation.shortcuts.map((item) => item.id), [navigation]);
  const labels = useMemo(() => Object.fromEntries([...navigation.drawerItems, ...navigation.shortcuts, ...navigation.accountMenu].map((item) => [item.id, item.label])), [navigation]);
  const value = useMemo(() => ({ enabled, isMainDestination, open, openAccount, close, shortcutIds, labels, accountMenu: navigation.accountMenu }), [enabled, isMainDestination, open, openAccount, close, shortcutIds, labels, navigation]);
  const accountItems = navigation.drawerItems.filter((item) => item.section !== 'community');
  const drawerWidth = Math.min(360, Math.max(240, width - 48));

  useEffect(() => {
    const wasVisible = wasVisibleRef.current;
    wasVisibleRef.current = anyOpen;
    if (wasVisible && !anyOpen && enabled && !changed && restoreFocusRef.current && triggerRef.current) {
      const frame = requestAnimationFrame(() => {
        const node = findNodeHandle(triggerRef.current);
        if (node) AccessibilityInfo.setAccessibilityFocus(node);
      });
      return () => cancelAnimationFrame(frame);
    }
    return undefined;
  }, [anyOpen, enabled, changed]);
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
        <View style={styles.frame} accessibilityElementsHidden={anyOpen} importantForAccessibility={anyOpen ? 'no-hide-descendants' : 'auto'}>
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
                {accountItems.length ? <>
                  <View style={{ marginVertical: theme.spacing.sm, borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: theme.colors.border }} />
                  <AppText variant="caption" color="textMuted" accessibilityRole="header" style={{ padding: theme.spacing.md }}>{strings.navigation.accountSection}</AppText>
                  {accountItems.map(row)}
                </> : null}
              </ScrollView>
            </Animated.View>
          </View>
        </Modal>
        <Modal testID="account-menu-modal" visible={accountVisible && enabled} transparent animationType="none" supportedOrientations={['portrait', 'landscape']} statusBarTranslucent navigationBarTranslucent onRequestClose={close}
          onShow={() => { const node = findNodeHandle(accountFirstRef.current); if (node) AccessibilityInfo.setAccessibilityFocus(node); }}>
          <View testID="account-menu" style={styles.overlay} accessibilityViewIsModal onAccessibilityEscape={close} {...(Platform.OS === 'web' ? { onKeyDown: (event) => { if (event.key === 'Escape') close(); } } : {})}>
            <Pressable testID="account-menu-backdrop" accessibilityRole="button" accessibilityLabel={strings.navigation.closeMenu} onPress={close} style={[styles.scrim, { backgroundColor: 'rgba(0,0,0,0.25)' }]} importantForAccessibility="no" accessible={false} />
            <View testID="account-menu-panel" style={[styles.accountPanel, { top: insets.top + 52, right: insets.right + theme.spacing.sm, width: Math.min(300, width - 32), padding: theme.spacing.sm, borderRadius: theme.radii.md, borderColor: theme.colors.border, backgroundColor: theme.colors.background }]}>
              {user ? (
                <View ref={accountFirstRef} accessible style={[styles.drawerRow, { gap: theme.spacing.md, padding: theme.spacing.md, borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: theme.colors.border, marginBottom: theme.spacing.xs }]}>
                  <Avatar uri={user.avatarUrl && api ? api.url(user.avatarUrl) : null} token={api ? api.accessToken : null} name={user.displayName} size={36} />
                  <View style={{ flex: 1 }}>
                    <AppText style={{ fontWeight: '700' }}>{user.displayName}</AppText>
                    {user.handle ? <AppText variant="caption" color="textMuted">{'@' + user.handle}</AppText> : null}
                  </View>
                </View>
              ) : null}
              <ScrollView testID="account-menu-scroll" style={{ maxHeight: 420 }} keyboardShouldPersistTaps="handled">
                {navigation.accountMenu.map(row)}
                {user ? <>
                  <View style={{ marginVertical: theme.spacing.xs, borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: theme.colors.border }} />
                  <Pressable testID="account-menu-sign-out" accessibilityRole="button" accessibilityLabel={strings.navigation.signOut} onPress={() => { restoreFocusRef.current = false; close(); signOut({ notice: 'signedOut' }); }}
                    style={({ pressed }) => [styles.drawerRow, { minHeight: theme.layout.minTouchTarget, gap: theme.spacing.md, padding: theme.spacing.md, borderRadius: theme.radii.md, backgroundColor: pressed ? theme.colors.surface : 'transparent' }]}>
                    <Ionicons name="log-out-outline" size={23} color={theme.colors.textMuted} />
                    <AppText style={{ flex: 1, fontWeight: '500' }}>{strings.navigation.signOut}</AppText>
                  </Pressable>
                </> : null}
              </ScrollView>
            </View>
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
  shortcutIcon: { flexDirection: 'row', alignItems: 'center', gap: 4 },
  accountPanel: { position: 'absolute', borderWidth: StyleSheet.hairlineWidth, shadowColor: '#000', shadowOpacity: 0.18, shadowRadius: 12, shadowOffset: { width: 0, height: 4 }, elevation: 8 }, badge: { minWidth: 18, paddingHorizontal: 4, alignItems: 'center' }
});
