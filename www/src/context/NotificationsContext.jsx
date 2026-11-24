import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { UNREAD_POLL_MS } from '@trelliskit/shared';

const NotificationsContext = createContext({ unreadCount: 0, refresh: () => {}, setUnreadCount: () => {} });

/**
 * Unread notification count for the member app (contract section 8.4): fetched on sign-in, every
 * 60 seconds while the tab is visible, on tab focus, and whenever a view calls `refresh`
 * (for example after opening the notification center).
 * @param {{ client: object|null, enabled?: boolean, intervalMs?: number, children: React.ReactNode }} props
 */
export function NotificationsProvider({ client, enabled = true, intervalMs = UNREAD_POLL_MS, children }) {
  const [unreadCount, setUnreadCount] = useState(0);
  const inFlight = useRef(false);

  const refresh = useCallback(async () => {
    if (!enabled || !client || typeof client.getUnreadCount !== 'function' || inFlight.current) return;
    inFlight.current = true;
    try {
      const body = await client.getUnreadCount();
      if (body && typeof body.unreadCount === 'number') setUnreadCount(body.unreadCount);
    } catch {
      // Keep the last count; the next poll tries again.
    } finally {
      inFlight.current = false;
    }
  }, [client, enabled]);

  useEffect(() => {
    if (!enabled) return undefined;
    refresh();
    const visible = () => typeof document === 'undefined' || document.visibilityState === 'visible';
    const timer = setInterval(() => { if (visible()) refresh(); }, intervalMs);
    const onFocus = () => { if (visible()) refresh(); };
    window.addEventListener('focus', onFocus);
    document.addEventListener('visibilitychange', onFocus);
    return () => {
      clearInterval(timer);
      window.removeEventListener('focus', onFocus);
      document.removeEventListener('visibilitychange', onFocus);
    };
  }, [enabled, intervalMs, refresh]);

  const value = useMemo(() => ({ unreadCount, refresh, setUnreadCount }), [unreadCount, refresh]);
  return <NotificationsContext.Provider value={value}>{children}</NotificationsContext.Provider>;
}

/** `{ unreadCount, refresh, setUnreadCount }`. Works without a provider (count stays 0). */
export function useNotifications() {
  return useContext(NotificationsContext);
}
