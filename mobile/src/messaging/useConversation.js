import { createMessageStore } from '@trelliskit/shared';
import * as Haptics from 'expo-haptics';
import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import { setActiveConversationId } from './activeConversation';
import { newestIncomingSeq } from './format';
import { useMessaging } from './MessagingProvider';

/** How long a typing indicator stays without a refresh (contract section 4.7). */
export const TypingExpiryMs = 6000;

/** A light tap after a message is sent (expo-haptics; failures are ignored). */
export function sendHaptic() {
  try {
    const result = Haptics.impactAsync(Haptics.ImpactFeedbackStyle ? Haptics.ImpactFeedbackStyle.Light : undefined);
    if (result && typeof result.catch === 'function') result.catch(() => {});
  } catch {
    // Haptics are optional.
  }
}

/**
 * One open conversation (contract sections 3 and 4): the Conversation, its messages from the
 * shared message store (REST pages, socket events, gap fills, and optimistic sends merged by `id`
 * and `seq`), older pages, read and delivered marks while it is on screen, the other member's
 * typing indicator, receipts, and every action of the screen. While mounted it is the active
 * conversation (the `state` frame and push suppression).
 * @param {object} options
 * @param {object} options.api MobileApiClient.
 * @param {string} options.conversationId
 * @param {object|null} options.user Me.
 * @param {(conversation: object) => void} [options.onConversationChange] Keeps a list in sync.
 * @param {(conversationId: string, seq: number) => void} [options.onRead] Keeps a list's unread badge in sync.
 */
export function useConversation({ api, conversationId, user, onConversationChange, onRead }) {
  const { realtime, listStore, refreshUnread } = useMessaging();
  const me = useMemo(() => (user ? { id: user.id, displayName: user.displayName, handle: user.handle, avatarUrl: user.avatarUrl || null, isStaff: false, isDeleted: false } : null), [user]);
  const store = useMemo(() => createMessageStore({ conversationId, api: api.messaging, currentUser: me }), [api, conversationId, me]);
  const snapshot = useSyncExternalStore(store.subscribe, store.getSnapshot, store.getSnapshot);
  const [conversation, setConversationState] = useState(null);
  const [status, setStatus] = useState('loading');
  const [error, setError] = useState(null);
  const [olderBusy, setOlderBusy] = useState(false);
  const [typing, setTyping] = useState(false);
  const typingTimer = useRef(null);
  const readSeq = useRef(0);
  const changeRef = useRef(onConversationChange);
  const readRef = useRef(onRead);
  useEffect(() => {
    changeRef.current = onConversationChange;
    readRef.current = onRead;
  });

  const setConversation = useCallback((next) => {
    if (!next || !next.id) return;
    setConversationState(next);
    if (changeRef.current) changeRef.current(next);
  }, []);

  const load = useCallback(async () => {
    setError(null);
    try {
      const [c] = await Promise.all([api.messaging.getConversation(conversationId), store.loadLatest()]);
      setConversation(c);
      setStatus('ready');
    } catch (e) {
      setError(e);
      setStatus(e && e.status === 404 ? 'gone' : 'error');
    }
  }, [api, conversationId, store, setConversation]);

  useEffect(() => {
    // load sets state after its requests resolve.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setStatus('loading');
    readSeq.current = 0;
    load();
  }, [load]);

  // The active conversation: push suppression and the realtime `state` frame.
  useEffect(() => {
    setActiveConversationId(conversationId);
    if (listStore) listStore.setActiveConversation(conversationId);
    return () => {
      setActiveConversationId(null);
      if (listStore) listStore.setActiveConversation(null);
    };
  }, [conversationId, listStore]);

  // Live events: messages through the store; conversation, receipts, and typing here.
  useEffect(() => {
    if (!realtime) return undefined;
    const offs = [store.attachRealtime(realtime)];
    const mine = (data) => data && data.conversationId === conversationId;
    offs.push(
      realtime.on('conversation.updated', (data) => {
        if (data && data.conversation && data.conversation.id === conversationId) setConversation(data.conversation);
      })
    );
    const receipt = (field) => (data) => {
      if (!mine(data) || (me && data.userId === me.id)) return;
      setConversationState((c) => {
        if (!c) return c;
        const seq = Number(data.seq) || 0;
        return seq > (Number(c[field]) || 0) ? { ...c, [field]: seq } : c;
      });
    };
    offs.push(realtime.on('receipt.read', receipt('otherLastReadSeq')));
    offs.push(realtime.on('receipt.delivered', receipt('otherLastDeliveredSeq')));
    offs.push(
      realtime.on('typing', (data) => {
        if (!mine(data) || (me && data.userId === me.id)) return;
        if (typingTimer.current) clearTimeout(typingTimer.current);
        if (data.state === 'Stop') {
          setTyping(false);
          return;
        }
        setTyping(true);
        typingTimer.current = setTimeout(() => setTyping(false), Number(data.expiresInMs) > 0 ? Number(data.expiresInMs) : TypingExpiryMs);
      })
    );
    offs.push(
      realtime.on('message.created', (data) => {
        // A new message from them ends their typing indicator.
        if (mine(data) && data.message && data.message.author && me && data.message.author.id !== me.id) setTyping(false);
      })
    );
    return () => {
      for (const off of offs) off();
      if (typingTimer.current) clearTimeout(typingTimer.current);
    };
  }, [realtime, store, conversationId, me, setConversation]);

  // Read and delivered marks for what is on screen.
  const incoming = newestIncomingSeq(snapshot.messages, me ? me.id : null);
  useEffect(() => {
    if (status !== 'ready' || incoming === null || !conversation) return;
    const already = Math.max(readSeq.current, Number(conversation.myLastReadSeq) || 0);
    if (realtime) realtime.ack(conversationId, incoming);
    if (incoming <= already) return;
    readSeq.current = incoming;
    if (realtime) realtime.read(conversationId, incoming);
    else api.messaging.markConversationRead(conversationId, incoming).catch(() => {});
    if (listStore) listStore.markRead(conversationId, incoming);
    if (readRef.current) readRef.current(conversationId, incoming);
    // Counts follow once the server has recorded the read.
    setTimeout(() => refreshUnread(), 0);
  }, [status, incoming, conversation, realtime, api, conversationId, listStore, refreshUnread]);

  const loadOlder = useCallback(async () => {
    if (olderBusy || snapshot.hasOlder === false) return;
    setOlderBusy(true);
    try {
      await store.loadOlder();
    } catch {
      // The button stays; the member can try again.
    } finally {
      setOlderBusy(false);
    }
  }, [olderBusy, snapshot.hasOlder, store]);

  const send = useCallback(
    async (draft) => {
      const result = await store.sendMessage(draft);
      if (result.ok) {
        sendHaptic();
        if (conversation && conversation.requestState === 'Pending') load();
      }
      return result;
    },
    [store, conversation, load]
  );

  const typingChanged = useCallback(
    (text) => {
      if (!realtime) return;
      if (text && text.trim()) realtime.typing(conversationId, 'Start');
      else realtime.typing(conversationId, 'Stop');
    },
    [realtime, conversationId]
  );

  const edit = useCallback(
    async (message, body) => {
      const next = await api.messaging.editMessage(message.id, body);
      if (next && next.id) store.upsertMessage(next);
      return next;
    },
    [api, store]
  );

  const remove = useCallback(
    async (message) => {
      const next = await api.messaging.deleteMessage(message.id);
      store.upsertMessage(next && next.id ? next : { ...message, state: 'Deleted', body: '', originalBody: null, attachments: [] });
      return next;
    },
    [api, store]
  );

  const toggleReaction = useCallback(
    async (message, key) => {
      const on = !(message.myReactions || []).includes(key);
      const result = on ? await api.messaging.putMessageReaction(message.id, key) : await api.messaging.deleteMessageReaction(message.id, key);
      const current = store.getMessage(message.id) || message;
      const counts = result && result.reactionCounts ? result : { reactionCounts: current.reactionCounts, reactionTotal: current.reactionTotal, myReactions: on ? (current.myReactions || []).concat(key) : (current.myReactions || []).filter((k) => k !== key) };
      store.upsertMessage({ ...current, reactionCounts: counts.reactionCounts, reactionTotal: counts.reactionTotal, myReactions: counts.myReactions });
    },
    [api, store]
  );

  const conversationAction = useCallback(
    async (fn) => {
      const next = await fn();
      if (next && next.id) setConversation(next);
      return next;
    },
    [setConversation]
  );

  return {
    conversation,
    setConversation,
    status,
    error,
    reload: load,
    messages: snapshot.messages,
    hasOlder: snapshot.hasOlder !== false,
    olderBusy,
    loadOlder,
    typing,
    send,
    retry: (clientMessageId) => store.retrySend(clientMessageId),
    discard: (clientMessageId) => store.discardPending(clientMessageId),
    typingChanged,
    edit,
    remove,
    toggleReaction,
    accept: () => conversationAction(() => api.messaging.acceptRequest(conversationId)),
    decline: () => conversationAction(() => api.messaging.declineRequest(conversationId)),
    setFolder: (folder) => conversationAction(() => api.messaging.setConversationFolder(conversationId, folder)),
    mute: () => conversationAction(() => api.messaging.muteConversation(conversationId)),
    unmute: () => conversationAction(() => api.messaging.unmuteConversation(conversationId)),
    deleteConversation: async () => {
      await api.messaging.deleteConversation(conversationId);
      if (listStore) listStore.remove(conversationId);
    },
    store
  };
}
