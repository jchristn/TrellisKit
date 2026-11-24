import { collectEnumeration } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { AppState } from 'react-native';
import { notifications as notificationsApi } from '../api/community';
import { deviceFlags } from '../api/tokenStore';
import { useAuth } from '../auth/AuthProvider';
import { canEnterTabs } from '../auth/routing';
import { emitContentEvent } from '../content/events';
import { getNotifications } from '../native/notifications';
import { pushTarget, staffWebPath } from './deepLinks';
import { openInAppBrowser } from '../links';
import { useProperty, webUrl } from '../property';
import { deviceTimeZone } from './preferences';
import { reconcilePushPermission } from './push';

/** Unread count polling interval while the app is in the foreground (contract 8.4). */
export const UnreadPollMs = 60000;

const NotificationsContext = createContext(null);

/**
 * App-wide notification state for a signed-in member:
 *
 * - the unread count (GET /notifications/unread-count) on launch, on every foreground, every 60
 *   seconds while foregrounded, and when a push arrives while the app is open; it drives the
 *   header badge and the app icon badge;
 * - push taps (also the one that cold-started the app): mark the notification read, then open
 *   its `data.path` through Expo Router;
 * - on foreground, the device permission is re-checked and the push token registered or removed
 *   to match (contract 8.10), and a changed device time zone is sent to the preferences.
 */
export function NotificationsProvider({ children }) {
  const router = useRouter();
  const property = useProperty();
  const { api, session } = useAuth();
  const member = canEnterTabs(session);
  const userId = member ? session.user.id : null;
  const [unreadCount, setUnreadCount] = useState(0);
  const [pending, setPending] = useState(null);
  const handledResponses = useRef(new Set());

  const refreshCount = useCallback(async () => {
    try {
      const result = await notificationsApi.unreadCount(api);
      if (result && typeof result.unreadCount === 'number') setUnreadCount(result.unreadCount);
      return result;
    } catch {
      return null;
    }
  }, [api]);

  const open = useCallback(
    async ({ notificationId, path, webPath }) => {
      if (notificationId) {
        try {
          const result = await notificationsApi.markRead(api, [notificationId]);
          if (result && typeof result.unreadCount === 'number') setUnreadCount(result.unreadCount);
        } catch {
          // Opening still works; the next count refresh catches up.
        }
      }
      const staffUrl = staffWebPath(webPath) ? webUrl(property, webPath) : null;
      if (staffUrl) await openInAppBrowser(staffUrl);
      else router.push(path);
    },
    [api, router, property]
  );

  // Unread count: launch, foreground, and every minute while active.
  useEffect(() => {
    if (!userId) {
      // Reset for the next member.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setUnreadCount(0);
      return undefined;
    }
    refreshCount();
    let timer = setInterval(refreshCount, UnreadPollMs);
    const sub = AppState.addEventListener('change', (state) => {
      if (state === 'active') {
        refreshCount();
        reconcilePushPermission(api, { userId }).catch(() => {});
        syncTimeZone(api, userId).catch(() => {});
        if (!timer) timer = setInterval(refreshCount, UnreadPollMs);
      } else if (timer) {
        clearInterval(timer);
        timer = null;
      }
    });
    syncTimeZone(api, userId).catch(() => {});
    return () => {
      if (timer) clearInterval(timer);
      sub.remove();
    };
  }, [api, userId, refreshCount]);

  // App icon badge follows the count.
  useEffect(() => {
    const Notifications = getNotifications();
    if (!userId || !Notifications || typeof Notifications.setBadgeCountAsync !== 'function') return;
    Notifications.setBadgeCountAsync(unreadCount).catch(() => {});
  }, [userId, unreadCount]);

  // Push listeners: received in the foreground (refresh the count) and tapped (deep link).
  useEffect(() => {
    const Notifications = getNotifications();
    if (!Notifications) return undefined;
    const subs = [];
    const handle = (response) => {
      const id = response && response.notification && response.notification.request && response.notification.request.identifier;
      if (id && handledResponses.current.has(id)) return;
      if (id) handledResponses.current.add(id);
      const target = pushTarget(response);
      if (target) setPending(target);
    };
    try {
      if (typeof Notifications.addNotificationReceivedListener === 'function') subs.push(
          Notifications.addNotificationReceivedListener((notification) => {
            refreshCount();
            // Other providers (messaging unread counts) react to pushes through the content events.
            emitContentEvent('pushReceived', notification);
          })
        );
      if (typeof Notifications.addNotificationResponseReceivedListener === 'function') subs.push(Notifications.addNotificationResponseReceivedListener(handle));
      if (typeof Notifications.getLastNotificationResponseAsync === 'function') {
        Notifications.getLastNotificationResponseAsync()
          .then((response) => {
            if (response) handle(response);
          })
          .catch(() => {});
      }
    } catch {
      // Push is optional; the notification center still works.
    }
    return () => subs.forEach((s) => s && s.remove && s.remove());
  }, [refreshCount]);

  // Follow a tapped push once the member can see content (a cold start waits for the session).
  useEffect(() => {
    if (!pending || !userId) return undefined;
    const target = pending;
    const timer = setTimeout(() => {
      setPending(null);
      open(target);
    }, 250);
    return () => clearTimeout(timer);
  }, [pending, userId, open]);

  const value = useMemo(() => ({ unreadCount, setUnreadCount, refreshCount, open }), [unreadCount, refreshCount, open]);
  return <NotificationsContext.Provider value={value}>{children}</NotificationsContext.Provider>;
}

/** Notification state; a static fallback outside the provider. */
export function useNotificationsState() {
  return useContext(NotificationsContext) || { unreadCount: 0, setUnreadCount: () => {}, refreshCount: async () => null, open: async () => {} };
}

function timeZoneKey(userId) {
  return 'tk.timeZone.' + String(userId).replace(/[^A-Za-z0-9._-]/g, '_') + '.' + deviceTimeZone().replace(/[^A-Za-z0-9._-]/g, '_');
}

/**
 * Send the device time zone to the notification preferences when it differs from the server's
 * (contract 8.5), at most once per member and zone on this device.
 */
export async function syncTimeZone(api, userId) {
  const zone = deviceTimeZone();
  if (!zone || !userId) return false;
  const key = timeZoneKey(userId);
  if (await deviceFlags.get(key)) return false;
  const prefs = await collectEnumeration((q) => notificationsApi.getPreferences(api, q)).then((page) => ({ ...page, categories: page.objects }));
  const current = prefs && prefs.notificationHours && prefs.notificationHours.timeZone;
  if (current !== zone) await notificationsApi.updatePreferences(api, { notificationHours: { timeZone: zone } });
  await deviceFlags.set(key);
  return current !== zone;
}
