import { createContext, useContext, useEffect, useMemo, useState, useSyncExternalStore } from 'react';
import { AppState, RealtimeClient, canUseMessaging, createConversationListStore } from '@trelliskit/shared';
import { useNotifications } from './NotificationsContext.jsx';
import { realtimeUrl } from '../utils/messaging.js';
import { version as WEB_VERSION } from '../../package.json';

/**
 * Optional realtime settings for the website (tests inject a fake WebSocket and timers):
 * `{ WebSocketImpl, timers, random, url }`. Missing values use the browser.
 */
export const RealtimeConfigContext = createContext({});

const RealtimeContext = createContext({ realtime: null, listStore: null, status: 'Idle', polling: false, messagingAllowed: false });

const SYNC_TOKEN_KEY = 'tk.syncToken';

function sessionTokenStore() {
  return {
    get: () => { try { return window.sessionStorage.getItem(SYNC_TOKEN_KEY); } catch { return null; } },
    set: (token) => {
      try {
        if (token) window.sessionStorage.setItem(SYNC_TOKEN_KEY, token);
        else window.sessionStorage.removeItem(SYNC_TOKEN_KEY);
      } catch { /* storage unavailable */ }
    }
  };
}

/**
 * One RealtimeClient for the signed-in member session (contract sections 4 and 5): it connects
 * directly to the property's API domain (resolved decision 11; same origin on local hosts), falls
 * back to polling, feeds the shared conversation list store (unread counts included), and updates
 * the notification badge from `notification.created` and `unread.counts`. Messaging events are
 * requested only when the access model lets the member use messaging.
 *
 * @param {{ client: object, me: object|null, property: object, enabled: boolean, refreshSession?: () => Promise<boolean>, children: React.ReactNode }} props
 */
export function RealtimeProvider({ client, me, property, enabled, refreshSession, children }) {
  const config = useContext(RealtimeConfigContext);
  const { setUnreadCount } = useNotifications();
  const messagingAllowed = Boolean(me) && canUseMessaging(property, me, 'read').allowed;
  const meId = me ? me.id : null;
  const listStore = useMemo(() => createConversationListStore({ currentUserId: meId }), [meId]);
  const [realtime, setRealtime] = useState(null);
  const [status, setStatus] = useState({ status: 'Idle', polling: false });
  const url = config.url || realtimeUrl(property);
  const propertyId = property && property.id;

  useEffect(() => {
    if (!enabled || !client || !meId) return undefined;
    const rt = new RealtimeClient({
      url,
      WebSocketImpl: config.WebSocketImpl,
      getAccessToken: () => (typeof client.getAccessToken === 'function' ? client.getAccessToken() : null),
      refreshAccessToken: async () => {
        if (!refreshSession) return null;
        const ok = await refreshSession();
        return ok && typeof client.getAccessToken === 'function' ? client.getAccessToken() : null;
      },
      propertyId,
      client: 'Web',
      clientVersion: WEB_VERSION,
      capabilities: messagingAllowed ? ['messaging', 'notifications', 'tickets'] : ['notifications', 'tickets'],
      api: messagingAllowed ? { ...client.messaging, getUnreadCount: () => client.getUnreadCount() } : { getUnreadCount: () => client.getUnreadCount() },
      syncTokenStore: messagingAllowed ? sessionTokenStore() : undefined,
      timers: config.timers,
      random: config.random
    });
    const detach = messagingAllowed ? listStore.attachRealtime(rt) : () => {};
    const offs = [
      rt.on('notification.created', (data) => { if (data && typeof data.unreadCount === 'number') setUnreadCount(data.unreadCount); }),
      rt.on('unread.counts', (data) => { if (data && typeof data.notifications === 'number') setUnreadCount(data.notifications); }),
      rt.on('*', () => setStatus((prev) => (prev.status === rt.status && prev.polling === rt.isPolling ? prev : { status: rt.status, polling: rt.isPolling })))
    ];
    const visibility = () => rt.setAppState(document.visibilityState === 'hidden' ? AppState.Background : AppState.Foreground);
    const online = () => rt.networkChanged();
    document.addEventListener('visibilitychange', visibility);
    window.addEventListener('online', online);
    setRealtime(rt);
    rt.connect();
    if (messagingAllowed && typeof client.getMessagingUnreadCount === 'function') {
      client.getMessagingUnreadCount().then((counts) => listStore.setUnreadCounts(counts), () => {});
    }
    return () => {
      document.removeEventListener('visibilitychange', visibility);
      window.removeEventListener('online', online);
      for (const off of offs) off();
      detach();
      rt.disconnect();
      setRealtime(null);
    };
    // The client is rebuilt only when the session, the URL, or messaging access changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled, client, meId, url, propertyId, messagingAllowed, listStore, config.WebSocketImpl, config.timers]);

  const value = useMemo(() => ({ realtime, listStore, status: status.status, polling: status.polling, messagingAllowed }), [realtime, listStore, status, messagingAllowed]);
  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>;
}

/** `{ realtime, listStore, status, polling, messagingAllowed }`. Outside a provider everything is null. */
export function useRealtime() {
  return useContext(RealtimeContext);
}

const EMPTY_SNAPSHOT = Object.freeze({ conversations: Object.freeze([]), unread: Object.freeze({ notifications: null, conversations: null, messages: null, requests: null }), activeConversationId: null, version: 0 });
const noop = () => () => {};

/** The conversation list store snapshot (`{ conversations, unread, ... }`), live. */
export function useConversationList() {
  const { listStore } = useRealtime();
  return useSyncExternalStore(listStore ? listStore.subscribe : noop, listStore ? listStore.getSnapshot : () => EMPTY_SNAPSHOT, () => EMPTY_SNAPSHOT);
}

/** Subscribe to one realtime event type while mounted. `handler` should be stable. */
export function useRealtimeEvent(type, handler) {
  const { realtime } = useRealtime();
  useEffect(() => {
    if (!realtime || !handler) return undefined;
    return realtime.on(type, handler);
  }, [realtime, type, handler]);
}
