import Ionicons from '@expo/vector-icons/Ionicons';
import { canUseMessaging, propertyFeatureEnabled } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';
import { badgeText } from '../notifications/deepLinks';
import { useNotificationsState } from '../notifications/NotificationsProvider';
import strings from '../strings';
import { useTheme } from '../theme';
import AppText from './AppText';
import { useOptionalAuth } from '../auth/AuthProvider';
import { messagesBadgeCount } from '../messaging/format';
import { useMessaging } from '../messaging/MessagingProvider';
import { useProperty } from '../property';
import { AccountMenuButton, useAppNavigation } from '../navigation/AppNavigation';

/**
 * Header buttons for Messages, Notifications, and the account menu, shown on every tab. The Notifications button
 * carries the unread count badge (polled by NotificationsProvider; "99+" above 99), and Messages
 * the unread conversations plus pending requests (MessagingProvider, from the socket or polling).
 * Messages is hidden when `features.messaging` is off and shows a lock where messaging needs a
 * membership the caller does not have, and when the property already places Messages in the
 * bottom shortcuts. The account button (the member's avatar) appears when the property configures
 * an account menu. Signed-out guests (anonymous browsing) get a Sign in button instead.
 */
export default function HeaderActions() {
  const theme = useTheme();
  const router = useRouter();
  const { unreadCount } = useNotificationsState();
  const size = theme.layout.minTouchTarget;
  const badge = badgeText(unreadCount);
  const auth = useOptionalAuth();
  const guest = Boolean(auth && auth.session && auth.session.status === 'signedOut');
  const property = useProperty();
  const { unread } = useMessaging();
  const appNavigation = useAppNavigation();
  const messagesShortcut = Boolean(appNavigation && appNavigation.enabled && appNavigation.shortcutIds && appNavigation.shortcutIds.includes('messages'));
  const messagingOn = propertyFeatureEnabled(property, 'messaging') && !messagesShortcut;
  const messagingLocked = messagingOn && !canUseMessaging(property, auth ? auth.user : null, 'read').allowed;
  const messagesBadge = messagingLocked ? null : badgeText(messagesBadgeCount(unread));

  const button = (key, icon, label, path, count) => (
    <Pressable
      key={key}
      testID={'header-' + key}
      onPress={() => router.push(path)}
      accessibilityRole="button"
      accessibilityLabel={label}
      hitSlop={4}
      style={({ pressed }) => [styles.button, { width: size, height: size, opacity: pressed ? 0.6 : 1 }]}
    >
      <Ionicons name={icon} size={24} color={theme.colors.text} />
      {count ? (
        <View testID={'header-' + key + '-badge'} style={[styles.badge, { backgroundColor: theme.colors.danger, borderRadius: theme.radii.pill }]} importantForAccessibility="no" accessibilityElementsHidden>
          <AppText variant="caption" style={{ color: theme.dark ? '#0E1116' : '#FFFFFF', fontWeight: '700' }}>
            {count}
          </AppText>
        </View>
      ) : null}
    </Pressable>
  );

  if (guest) {
    return (
      <View style={[styles.row, { marginRight: theme.spacing.xs }]}>
        {button('sign-in', 'log-in-outline', strings.access.signInCta, '/sign-in')}
      </View>
    );
  }

  return (
    <View style={[styles.row, { marginRight: theme.spacing.xs }]}>
      {messagingOn
        ? button(
            'messages',
            messagingLocked ? 'lock-closed-outline' : messagesBadge ? 'chatbubbles' : 'chatbubbles-outline',
            messagingLocked ? strings.header.messagesLocked : messagesBadge ? strings.header.messagesUnread(messagesBadge) : strings.header.messages,
            '/messages',
            messagesBadge
          )
        : null}
      {button('notifications', badge ? 'notifications' : 'notifications-outline', badge ? strings.header.notificationsUnread(badge) : strings.header.notifications, '/notifications', badge)}
      <AccountMenuButton />
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center' },
  button: { alignItems: 'center', justifyContent: 'center' },
  badge: { position: 'absolute', top: 2, right: 0, minWidth: 18, paddingHorizontal: 4, alignItems: 'center' }
});
