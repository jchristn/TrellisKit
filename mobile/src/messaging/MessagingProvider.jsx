import { AppState as RealtimeAppState, canUseMessaging, createConversationListStore, createMemorySyncTokenStore, RealtimeClient } from '@trelliskit/shared';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { AppState } from 'react-native';
import { identityHeaders } from '../api/identity';
import { useOptionalAuth } from '../auth/AuthProvider';
import { getAppVersion } from '../config';
import { onContentEvent } from '../content/events';
import { useNotificationsState } from '../notifications/NotificationsProvider';
import { useProperty } from '../property';
import { getActiveConversationId, onActiveConversationChange } from './activeConversation';
import { BackgroundCloseMs, getRealtimeTimers, getWebSocketImpl, isRealtimeEnabled, realtimeUrl } from './realtimeConfig';

/** Messaging unread count polling while the socket is not delivering counts (contract 5.1). */
export const MessagingUnreadPollMs = 60000;

const EMPTY_UNREAD = Object.freeze({ conversations: 0, messages: 0, requests: 0 });

const MessagingContext = createContext(null);

/**
 * Build the app's RealtimeClient for a signed-in session (contract sections 4 and 5, as-built
 * note A): no subprotocol, the `X-TK-Property` and `X-TK-App` identification headers on the
 * upgrade (React Native WebSocket headers), the access token in `hello` (refreshed first when it
 * is about to expire), `client: Mobile`, and the polling fallback over the same API client.
 * @param {object} options
 * @param {object} options.api MobileApiClient.
 * @param {object} [options.property]
 * @param {boolean} [options.messaging] Request the `messaging` capability.
 */
export function createAppRealtime({ api, property, messaging = true }) {
  const timers = getRealtimeTimers();
  return new RealtimeClient({
    url: () => realtimeUrl(),
    WebSocketImpl: getWebSocketImpl(),
    headers: identityHeaders(),
    getAccessToken: async () => {
      if (typeof api.baseHeaders === 'function') await api.baseHeaders();
      return api.accessToken || null;
    },
    refreshAccessToken: async () => {
      try {
        const auth = await api.refresh();
        return (auth && auth.accessToken) || api.accessToken || null;
      } catch {
        return null;
      }
    },
    propertyId: property && property.id ? property.id : null,
    client: 'Mobile',
    clientVersion: getAppVersion(),
    capabilities: messaging ? ['messaging', 'notifications', 'tickets'] : ['notifications', 'tickets'],
    appState: AppState.currentState === 'background' ? RealtimeAppState.Background : RealtimeAppState.Foreground,
    api,
    syncTokenStore: createMemorySyncTokenStore(),
    ...(timers ? { timers } : {})
  });
}

/**
 * App-wide messaging state for a signed-in account:
 *
 * - one RealtimeClient per signed-in session: it connects in the foreground, reports the app
 *   state and the open conversation (`state` frame), and closes 30 seconds after the app goes to
 *   the background (contract section 19); `notification.created` updates the notification badge;
 * - the Messages unread counts (`GET /me/messaging/unread-count` on launch, on foreground, every
 *   minute while no socket delivers `unread.counts`, and when a direct message push arrives);
 * - a conversation list store fed by the socket, shared by the list screens.
 */
export function MessagingProvider({ children }) {
  const auth = useOptionalAuth();
  const property = useProperty();
  const notifications = useNotificationsState();
  const setNotificationCount = notifications.setUnreadCount;
  const api = auth ? auth.api : null;
  const session = auth ? auth.session : { status: 'signedOut' };
  const user = session.status === 'signedIn' ? session.user : null;
  const userId = user ? user.id : null;
  const messaging = canUseMessaging(property, user, 'read');
  const messagingAllowed = Boolean(userId) && messaging.allowed;
  const [realtime, setRealtime] = useState(null);
  const [unread, setUnread] = useState(EMPTY_UNREAD);
  const [connected, setConnected] = useState(false);
  const listStore = useMemo(() => createConversationListStore({ currentUserId: userId }), [userId]);
  const notifyRef = useRef(setNotificationCount);
  useEffect(() => {
    notifyRef.current = setNotificationCount;
  });

  const refreshUnread = useCallback(async () => {
    if (!api || !messagingAllowed || typeof api.getMessagingUnreadCount !== 'function') return null;
    try {
      const counts = await api.getMessagingUnreadCount();
      if (counts && typeof counts === 'object') {
        setUnread({ conversations: Number(counts.conversations) || 0, messages: Number(counts.messages) || 0, requests: Number(counts.requests) || 0 });
        listStore.setUnreadCounts(counts);
      }
      return counts;
    } catch {
      return null;
    }
  }, [api, messagingAllowed, listStore]);

  // One realtime client per signed-in session.
  useEffect(() => {
    if (!api || !userId || !isRealtimeEnabled()) return undefined;
    const rt = createAppRealtime({ api, property, messaging: messagingAllowed });
    const offs = [];
    offs.push(
      rt.on('notification.created', (data) => {
        if (data && typeof data.unreadCount === 'number' && notifyRef.current) notifyRef.current(data.unreadCount);
      })
    );
    offs.push(
      rt.on('unread.counts', (data) => {
        if (!data) return;
        if (typeof data.notifications === 'number' && notifyRef.current) notifyRef.current(data.notifications);
        setUnread((u) => ({
          conversations: typeof data.conversations === 'number' ? data.conversations : u.conversations,
          messages: typeof data.messages === 'number' ? data.messages : u.messages,
          requests: typeof data.requests === 'number' ? data.requests : u.requests
        }));
      })
    );
    offs.push(rt.on('connected', () => setConnected(true)));
    offs.push(rt.on('disconnected', () => setConnected(false)));
    offs.push(listStore.attachRealtime(rt));
    const active = getActiveConversationId();
    if (active) rt.setActiveConversation(active);
    offs.push(onActiveConversationChange((id) => rt.setActiveConversation(id)));

    const timerSource = getRealtimeTimers();
    const schedule = timerSource ? (fn, ms) => timerSource.setTimeout(fn, ms) : (fn, ms) => setTimeout(fn, ms);
    const cancel = timerSource ? (id) => timerSource.clearTimeout(id) : (id) => clearTimeout(id);
    let closeTimer = null;
    const foreground = AppState.currentState !== 'background' && AppState.currentState !== 'inactive';
    if (foreground) rt.connect();
    else closeTimer = schedule(() => rt.disconnect(), BackgroundCloseMs);
    const sub = AppState.addEventListener('change', (state) => {
      if (state === 'active') {
        if (closeTimer !== null) cancel(closeTimer);
        closeTimer = null;
        rt.setAppState(RealtimeAppState.Foreground);
        rt.connect();
      } else if (state === 'background') {
        rt.setAppState(RealtimeAppState.Background);
        if (closeTimer === null) {
          closeTimer = schedule(() => {
            closeTimer = null;
            rt.disconnect();
          }, BackgroundCloseMs);
        }
      }
    });
    // The client is created by this effect and lives for the session.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setRealtime(rt);
    return () => {
      sub.remove();
      if (closeTimer !== null) cancel(closeTimer);
      for (const off of offs) off();
      rt.disconnect();
      setRealtime(null);
      setConnected(false);
    };
    // A new client per session (user) and when messaging access changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [api, userId, messagingAllowed, listStore]);

  // Unread counts: launch, foreground, a direct message push, and every minute without a socket.
  useEffect(() => {
    if (!messagingAllowed) {
      // Reset for the next member or when messaging is not available.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setUnread(EMPTY_UNREAD);
      return undefined;
    }
    refreshUnread();
    const sub = AppState.addEventListener('change', (state) => {
      if (state === 'active') refreshUnread();
    });
    const timer = connected ? null : setInterval(() => {
      if (AppState.currentState !== 'background') refreshUnread();
    }, MessagingUnreadPollMs);
    const offPush = onContentEvent('pushReceived', (notification) => {
      const data = notification && notification.request && notification.request.content && notification.request.content.data;
      if (data && (data.type === 'DirectMessage' || data.type === 'MessageRequest')) refreshUnread();
    });
    return () => {
      sub.remove();
      if (timer) clearInterval(timer);
      offPush();
    };
  }, [messagingAllowed, connected, refreshUnread]);

  const value = useMemo(
    () => ({ realtime, listStore, unread, refreshUnread, setUnread, connected, messaging, messagingAllowed }),
    [realtime, listStore, unread, refreshUnread, connected, messaging, messagingAllowed]
  );
  return <MessagingContext.Provider value={value}>{children}</MessagingContext.Provider>;
}

/** Messaging state; a static fallback outside the provider. */
export function useMessaging() {
  return (
    useContext(MessagingContext) || {
      realtime: null,
      listStore: null,
      unread: EMPTY_UNREAD,
      refreshUnread: async () => null,
      setUnread: () => {},
      connected: false,
      messaging: { allowed: false, requires: null, reason: null },
      messagingAllowed: false
    }
  );
}
