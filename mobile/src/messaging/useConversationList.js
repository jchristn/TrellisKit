import { createConversationListStore } from '@trelliskit/shared';
import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import { buildEnumerationQuery, filtersToQuery, readEnumeration } from '../content/enumeration';
import { onBlocksChanged } from './blockEvents';
import { useMessaging } from './MessagingProvider';

/** Conversations per page (member maximum is 100). */
export const ConversationPageSize = 25;

/**
 * One folder of the conversation list (POST /conversations/enumerate, contract section 3.1):
 * folder (`Inbox`, `Requests`, `Archived`), search by name, label and tag filters on the caller's
 * own metadata, load more by continuation token, pull to refresh. The pages merge into a shared
 * conversation list store that the realtime client keeps current (new messages, receipts, folder
 * changes from other devices, and sync after reconnects).
 * @param {object} options
 * @param {object} options.api MobileApiClient.
 * @param {string} options.folder
 * @param {object} options.filters List filter state.
 * @param {string|null} [options.currentUserId]
 * @param {string|null} [options.activeConversationId]
 */
export function useConversationList({ api, folder, filters, currentUserId = null, activeConversationId = null }) {
  const { realtime } = useMessaging();
  const key = folder + '|' + JSON.stringify(filtersToQuery(filters));
  const store = useMemo(
    () => createConversationListStore({ currentUserId }),
    // A fresh store per folder and filter set, so results never mix.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [key, currentUserId]
  );
  const snapshot = useSyncExternalStore(store.subscribe, store.getSnapshot, store.getSnapshot);
  const [status, setStatus] = useState('loading');
  const [error, setError] = useState(null);
  const [refreshing, setRefreshing] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [cursor, setCursor] = useState(null);
  const busy = useRef(false);
  const generation = useRef(0);

  useEffect(() => store.setActiveConversation(activeConversationId), [store, activeConversationId]);
  useEffect(() => (realtime ? store.attachRealtime(realtime) : undefined), [store, realtime]);

  const fetchPage = useCallback(
    (continuationToken) => api.messaging.enumerateConversations(buildEnumerationQuery({ continuationToken, maxResults: ConversationPageSize, filters, collectionFilters: { folder } })),
    [api, filters, folder]
  );

  const load = useCallback(
    async ({ pull = false } = {}) => {
      const gen = (generation.current += 1);
      busy.current = true;
      if (pull) setRefreshing(true);
      setError(null);
      try {
        const raw = await fetchPage(null);
        if (gen !== generation.current) return;
        store.mergePage(raw);
        setCursor(readEnumeration(raw).nextCursor);
        setStatus('ready');
      } catch (e) {
        if (gen !== generation.current) return;
        setError(e);
        setStatus((s) => (pull && s === 'ready' ? 'ready' : 'error'));
      } finally {
        if (gen === generation.current) {
          busy.current = false;
          setRefreshing(false);
        }
      }
    },
    [fetchPage, store]
  );

  useEffect(() => {
    // load sets state after its request resolves.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setStatus('loading');
    load();
  }, [load]);

  // A block hides a conversation and an unblock restores it: reload the first page (realtime may be closed).
  useEffect(() => onBlocksChanged(() => load()), [load]);

  const loadMore = useCallback(async () => {
    if (busy.current || !cursor) return;
    const gen = generation.current;
    busy.current = true;
    setLoadingMore(true);
    try {
      const raw = await fetchPage(cursor);
      if (gen !== generation.current) return;
      store.mergePage(raw);
      setCursor(readEnumeration(raw).nextCursor);
    } catch (e) {
      if (gen === generation.current) setError(e);
    } finally {
      if (gen === generation.current) {
        busy.current = false;
        setLoadingMore(false);
      }
    }
  }, [cursor, fetchPage, store]);

  const items = snapshot.conversations.filter((c) => (c.folder || 'Inbox') === folder);

  return {
    items,
    status,
    error,
    refreshing,
    loadingMore,
    hasMore: Boolean(cursor),
    loadMore,
    refresh: useCallback(() => load({ pull: true }), [load]),
    reload: useCallback(() => load(), [load]),
    /** Replace a conversation (an action's response); one in another folder drops out of this list. */
    upsert: useCallback((conversation) => store.upsertConversation(conversation), [store]),
    remove: useCallback((id) => store.remove(id), [store]),
    markRead: useCallback((id, seq) => store.markRead(id, seq), [store]),
    store
  };
}
