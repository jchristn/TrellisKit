import { addPendingUserMessage, applyChatEvent, createChatState, newClientMessageId, pollReply } from '@trelliskit/shared';
import { useCallback, useEffect, useRef, useState } from 'react';

/**
 * Use the streaming fetch from expo/fetch (React Native's built-in fetch has no response body
 * stream). Without it the shared client falls back to JSON replies.
 * @param {object} api MobileApiClient
 */
export function enableStreaming(api) {
  if (!api || api.streamFetchImpl) return;
  try {
    const mod = require('expo/fetch');
    if (mod && typeof mod.fetch === 'function') api.streamFetchImpl = (url, init) => mod.fetch(url, init);
  } catch {
    // Not available in this runtime; non-streamed replies still work.
  }
}

/** Error code of a failed send or stream, for the strings.assistants.errors lookup. */
export function chatErrorCode(error) {
  if (!error) return null;
  return error.code || (error.status === 429 ? 'RateLimited' : error.status === 402 ? 'MembershipRequired' : error.status === 404 ? 'NotFound' : 'generic');
}

/**
 * Chat state for one assistant conversation on mobile (docs/AI_CONTRACT.md sections 10 and 12):
 * loads the transcript of an existing conversation, sends with a client message id, applies the
 * stream events, stops, and polls a reply whose stream dropped.
 * @param {object} api MobileApiClient (with the shared aiMethods)
 * @param {object} assistant AssistantSummary
 * @param {string|null} conversationId
 * @param {(conversation: object) => void} [onConversation] Called when the first message creates the conversation.
 */
export function useAssistantChat(api, assistant, conversationId, onConversation) {
  const [state, setState] = useState(() => createChatState());
  const [loading, setLoading] = useState(Boolean(conversationId));
  const conversationRef = useRef(conversationId);
  const abortRef = useRef(null);
  const createdRef = useRef(null);

  useEffect(() => {
    enableStreaming(api);
  }, [api]);

  useEffect(() => {
    conversationRef.current = conversationId;
    if (!conversationId || createdRef.current === conversationId) {
      setLoading(false);
      return undefined;
    }
    let active = true;
    setLoading(true);
    api.enumerateConversationMessages(conversationId, { maxResults: 100 })
      .then((page) => { if (active) setState(createChatState((page && page.objects) || [])); })
      .catch((error) => { if (active) setState((s) => ({ ...s, error: { code: chatErrorCode(error) } })); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [api, conversationId]);

  useEffect(() => () => abortRef.current && abortRef.current.abort(), []);

  const send = useCallback(async (text) => {
    const value = String(text || '').trim();
    if (!value || state.streaming) return false;
    const clientMessageId = newClientMessageId();
    const controller = new AbortController();
    abortRef.current = controller;
    let current = addPendingUserMessage({ ...state, error: null }, value, clientMessageId);
    current = { ...current, streaming: true };
    setState(current);
    const stream = assistant.streaming !== false && Boolean(api.streamFetchImpl);
    try {
      const events = conversationRef.current
        ? api.sendAssistantMessage(conversationRef.current, { content: value, clientMessageId }, { signal: controller.signal, stream })
        : api.startConversation(assistant.id, { message: value, clientMessageId }, { signal: controller.signal, stream });
      for await (const item of events) {
        current = applyChatEvent(current, item);
        setState(current);
        if (item.event === 'message.created' && item.data && item.data.conversation && !conversationRef.current) {
          conversationRef.current = item.data.conversation.id;
          createdRef.current = item.data.conversation.id;
          if (onConversation) onConversation(item.data.conversation);
        }
      }
      if (current.streaming && current.replyId && conversationRef.current && !controller.signal.aborted) {
        const reply = await pollReply(api, conversationRef.current, current.replyId);
        if (reply) setState((s) => applyChatEvent(s, { event: 'reply.completed', data: { reply } }));
      }
      setState((s) => (s.streaming ? { ...s, streaming: false } : s));
      return true;
    } catch (error) {
      setState((s) => ({ ...s, streaming: false, messages: s.messages.filter((m) => !(m.pending && m.clientMessageId === clientMessageId)), error: controller.signal.aborted ? null : { code: chatErrorCode(error) } }));
      return false;
    }
  }, [api, assistant, onConversation, state]);

  const stop = useCallback(async () => {
    const replyId = state.replyId;
    if (abortRef.current) abortRef.current.abort();
    if (replyId && conversationRef.current) await api.cancelAssistantMessage(conversationRef.current, replyId).catch(() => {});
    setState((s) => ({ ...s, streaming: false }));
  }, [api, state.replyId]);

  const rate = useCallback(async (message, rating) => {
    if (!conversationRef.current) return;
    const same = message.feedback && message.feedback.rating === rating;
    try {
      if (same) await api.clearMessageFeedback(conversationRef.current, message.id);
      else await api.setMessageFeedback(conversationRef.current, message.id, { rating });
      setState((s) => ({ ...s, messages: s.messages.map((m) => (m.id === message.id ? { ...m, feedback: same ? null : { rating } } : m)) }));
    } catch {
      // Feedback is best effort.
    }
  }, [api]);

  return { state, loading, send, stop, rate };
}
