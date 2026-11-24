import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { addPendingUserMessage, applyChatEvent, createChatState, isFinalStatus, newClientMessageId, pollReply } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useProperty } from '../../context/PropertyContext.jsx';
import Icon from '../../components/Icon.jsx';
import AssistantMarkdown from './AssistantMarkdown.jsx';
import ToolActivity from './ToolActivity.jsx';
import { useAccessPrompt } from '../../components/AccessPrompt.jsx';
import ReportDialog from '../../components/feed/ReportDialog.jsx';

const a = strings.ai;

/** Client-facing text for an API error or stream error code. */
export function chatErrorText(err) {
  const code = err && (err.code || err.error);
  return (code && a.errors[code]) || (err && err.status === 401 ? a.errors.Unauthorized : a.errors.generic);
}

function readFlag(key) {
  try {
    return window.localStorage.getItem(key) === '1';
  } catch {
    return false;
  }
}

function writeFlag(key) {
  try {
    window.localStorage.setItem(key, '1');
  } catch {
    // Storage can be unavailable; the notice then shows once per page load.
  }
}

function FeedbackControls({ message, onRate }) {
  const [open, setOpen] = useState(false);
  const [reasons, setReasons] = useState([]);
  const [note, setNote] = useState('');
  const { fill } = useProperty();
  const rating = message.feedback && message.feedback.rating;
  const toggle = (reason) => setReasons((r) => (r.includes(reason) ? r.filter((x) => x !== reason) : r.concat([reason])));
  return (
    <div className="ai-feedback">
      <button type="button" className={'icon-button' + (rating === 'Up' ? ' active' : '')} aria-pressed={rating === 'Up'} aria-label={a.feedbackUp} title={a.feedbackUp} onClick={() => onRate(message, { rating: 'Up' })}>
        <Icon name="check" size={14} />
      </button>
      <button type="button" className={'icon-button' + (rating === 'Down' ? ' active' : '')} aria-pressed={rating === 'Down'} aria-label={a.feedbackDown} title={a.feedbackDown} onClick={() => setOpen((o) => !o)}>
        <Icon name="close" size={14} />
      </button>
      {open && (
        <form className="ai-feedback-form" onSubmit={(e) => { e.preventDefault(); onRate(message, { rating: 'Down', reasons, note: note.trim() || undefined }); setOpen(false); }}>
          <p className="small"><strong>{a.feedbackNoteTitle}</strong></p>
          <div className="badge-row">
            {Object.entries(a.feedbackReasons).map(([key, label]) => (
              <label key={key} className="checkbox-inline"><input type="checkbox" checked={reasons.includes(key)} onChange={() => toggle(key)} /> {label}</label>
            ))}
          </div>
          <label className="field"><span className="label-text">{a.feedbackNoteLabel}</span><textarea rows={2} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} /></label>
          <p className="muted small">{fill(a.feedbackConsent)}</p>
          <button type="submit" className="button button-secondary button-sm">{a.feedbackSend}</button>
        </form>
      )}
    </div>
  );
}

function MessageBubble({ message, onRate, onRetry, onReport, canAct, citationBase }) {
  const reply = message.role === 'Assistant';
  const failed = message.status === 'Failed' || message.status === 'Cancelled';
  return (
    <li className={'ai-message ' + (reply ? 'ai-message-reply' : 'ai-message-user')} data-status={message.status}>
      {reply && <span className="badge badge-info ai-badge" aria-label={a.aiBadge}>{a.aiBadge}</span>}
      {reply && <ToolActivity message={message} linkBase={citationBase} />}
      {reply ? (
        message.status === 'Blocked' ? <p className="muted">{a.blocked}</p> : <AssistantMarkdown text={message.content} />
      ) : (
        <p className="ai-user-text">{message.content}</p>
      )}
      {reply && message.status === 'Streaming' && !message.content && <p className="muted small">{a.thinking}</p>}
      {reply && message.reasoning && (
        <details className="ai-reasoning"><summary>{a.showReasoning}</summary><p className="small">{message.reasoning}</p></details>
      )}
      {reply && message.citations && message.citations.length > 0 && (
        <div className="ai-citations" aria-label={a.sources}>
          {message.citations.map((c) => (
            citationBase === null
              ? <span key={c.index} className="ai-citation">[{c.index}] {c.title}</span>
              : <Link key={c.index} className="ai-citation" to={citationBase + c.path}>[{c.index}] {c.title}</Link>
          ))}
        </div>
      )}
      {reply && failed && (
        <p className="ai-error small">
          {message.status === 'Cancelled' ? a.stopped : (a.errors[message.errorCode] || a.errors.ModelError)}{' '}
          {canAct && message.status === 'Failed' && <button type="button" className="button button-secondary button-sm" onClick={() => onRetry(message)}>{a.tryAgain}</button>}
        </p>
      )}
      {reply && canAct && message.status === 'Completed' && onRate && <FeedbackControls message={message} onRate={onRate} />}
      {reply && canAct && onReport && (message.status === 'Completed' || message.status === 'Blocked') && !String(message.id).startsWith('pending_') && (
        <button type="button" className="button button-secondary button-sm ai-report" onClick={() => onReport(message)}>{a.reportReply}</button>
      )}
    </li>
  );
}

/**
 * The chat transcript and composer shared by the member page, the public page, and the widget
 * (AI contract sections 10 and 11): streaming over SSE with a stop button, starter prompts, the
 * disclaimer, the privacy disclosure (administrators can read conversations), the first-use notice,
 * quota messaging, citations, feedback, and "Try again" on failed replies.
 *
 * @param {{ client: object, assistant: object, conversationId?: string|null, onConversation?: (conversation: object) => void,
 *   citationBase?: string|null, allowFeedback?: boolean, allowReport?: boolean }} props
 *   `allowReport` (signed-in members only) adds "Report" to replies, filed with
 *   `POST /api/v1/reports` as `targetType: "AssistantMessage"` (AI contract section 12.3).
 */
function ChatPanel({ client, assistant, conversationId = null, onConversation, citationBase = '/app', allowFeedback = true, allowReport = false }) {
  const [reporting, setReporting] = useState(null);
  const accessPrompt = useAccessPrompt();
  const { fill } = useProperty();
  const [state, setState] = useState(() => createChatState());
  const [text, setText] = useState('');
  const [error, setError] = useState('');
  const [announce, setAnnounce] = useState('');
  const [firstUse, setFirstUse] = useState(() => !readFlag('tk.ai.notice.' + assistant.id));
  const conversationRef = useRef(conversationId);
  const createdRef = useRef(null);
  const abortRef = useRef(null);
  const listRef = useRef(null);
  const limits = assistant.limits || {};
  const remaining = state.remainingToday ?? limits.remainingToday;
  const exhausted = remaining === 0;

  useEffect(() => {
    // A conversation this panel just created is already on screen (and may still be streaming).
    if (conversationId && conversationId === createdRef.current) return undefined;
    conversationRef.current = conversationId;
    createdRef.current = null;
    if (!conversationId) {
      setState(createChatState());
      return undefined;
    }
    const controller = new AbortController();
    client.enumerateConversationMessages(conversationId, { maxResults: 100 }, controller.signal)
      .then((page) => setState(createChatState(((page && page.objects) || []).filter((m) => !m.isSuperseded))))
      .catch(() => {});
    return () => controller.abort();
  }, [client, conversationId]);

  useEffect(() => {
    const node = listRef.current;
    if (node && typeof node.scrollTo === 'function') node.scrollTo({ top: node.scrollHeight });
  }, [state.messages]);

  const consume = useCallback(async (iterator) => {
    let replyId = null;
    try {
      for await (const event of iterator) {
        if (event.event === 'message.created') {
          replyId = event.data.replyMessageId;
          if (event.data.conversation && !conversationRef.current) {
            conversationRef.current = event.data.conversation.id;
            createdRef.current = event.data.conversation.id;
            if (onConversation) onConversation(event.data.conversation);
          }
        }
        setState((s) => applyChatEvent(s, event));
        if (event.event === 'reply.completed') setAnnounce(a.replyDone);
      }
    } catch (err) {
      // A dropped connection (no HTTP status: a network failure or a stream that broke midway)
      // after the reply started: the server keeps generating, so poll for the final message.
      if (replyId && conversationRef.current && (!err || !err.status)) {
        const final = await pollReply(client, conversationRef.current, replyId).catch(() => null);
        if (final) setState((s) => applyChatEvent(s, { event: isFinalStatus(final.status) && final.status === 'Completed' ? 'reply.completed' : 'error', data: { reply: final, error: final.errorCode, replyMessageId: final.id } }));
        return;
      }
      setState((s) => ({ ...s, streaming: false, messages: s.messages.filter((m) => !m.pending) }));
      if (!accessPrompt.handle(err)) setError(chatErrorText(err));
    } finally {
      abortRef.current = null;
    }
  }, [client, onConversation, accessPrompt]);

  const send = useCallback(async (content) => {
    const value = String(content || '').trim();
    if (!value || state.streaming) return;
    setError('');
    setText('');
    const clientMessageId = newClientMessageId();
    setState((s) => addPendingUserMessage(s, value, clientMessageId));
    const controller = new AbortController();
    abortRef.current = controller;
    const iterator = conversationRef.current
      ? client.sendAssistantMessage(conversationRef.current, { content: value, clientMessageId }, { signal: controller.signal, stream: assistant.streaming !== false })
      : client.startConversation(assistant.id, { message: value, clientMessageId }, { signal: controller.signal, stream: assistant.streaming !== false });
    await consume(iterator);
  }, [assistant, client, consume, state.streaming]);

  const stop = async () => {
    if (state.replyId && conversationRef.current) {
      try {
        await client.cancelAssistantMessage(conversationRef.current, state.replyId);
      } catch {
        // Already finished.
      }
    }
  };

  const retry = async (message) => {
    if (!conversationRef.current) return;
    setError('');
    await consume(client.regenerateAssistantMessage(conversationRef.current, message.id, { stream: assistant.streaming !== false }));
  };

  const rate = async (message, feedback) => {
    try {
      const updated = await client.setMessageFeedback(conversationRef.current, message.id, feedback);
      setState((s) => ({ ...s, messages: s.messages.map((m) => (m.id === message.id ? { ...m, feedback: updated.feedback } : m)) }));
      setAnnounce(a.feedbackThanks);
    } catch (err) {
      setError(chatErrorText(err));
    }
  };

  const max = limits.maxMessageCharacters || 4000;
  return (
    <div className="ai-chat">
      {firstUse && (
        <div className="banner banner-info ai-first-use" role="note">
          <p><strong>{a.firstUseTitle}</strong> {a.firstUseBody}</p>
          <button type="button" className="button button-secondary button-sm" onClick={() => { writeFlag('tk.ai.notice.' + assistant.id); setFirstUse(false); }}>{a.firstUseOk}</button>
        </div>
      )}
      <ol className="ai-transcript" ref={listRef} aria-label={assistant.title}>
        {state.messages.length === 0 && assistant.starterPrompts && assistant.starterPrompts.length > 0 && (
          <li className="ai-starters">
            <p className="muted small">{a.starterTitle}</p>
            <div className="badge-row">
              {assistant.starterPrompts.map((prompt) => (
                <button key={prompt} type="button" className="button button-secondary button-sm" disabled={exhausted} onClick={() => send(prompt)}>{prompt}</button>
              ))}
            </div>
          </li>
        )}
        {state.messages.map((message) => (
          <MessageBubble key={message.id} message={message} onRate={allowFeedback ? rate : null} onRetry={retry} onReport={allowReport ? setReporting : null} canAct={!state.streaming && Boolean(conversationRef.current)} citationBase={citationBase} />
        ))}
      </ol>
      <div className="visually-hidden" aria-live="polite">{announce}</div>
      {reporting && <ReportDialog target={{ type: 'AssistantMessage', id: reporting.id }} onClose={() => setReporting(null)} />}
      {error && <p className="ai-error" role="alert">{error}</p>}
      <form className="ai-composer" onSubmit={(e) => { e.preventDefault(); send(text); }}>
        <label className="visually-hidden" htmlFor={'ai-composer-' + assistant.id}>{a.composerLabel}</label>
        <textarea
          id={'ai-composer-' + assistant.id}
          rows={2}
          value={text}
          maxLength={max}
          placeholder={a.composerPlaceholder}
          disabled={exhausted}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send(text); } }}
        />
        <div className="ai-composer-row">
          <span className="muted small">{a.characters(text.length, max)}</span>
          {remaining !== null && remaining !== undefined && <span className="muted small">{exhausted ? a.limitReached : a.remaining(remaining)}</span>}
          {state.streaming
            ? <button type="button" className="button button-secondary" onClick={stop}>{a.stop}</button>
            : <button type="submit" className="button button-primary" disabled={!text.trim() || exhausted}>{a.send}</button>}
        </div>
      </form>
      {assistant.disclaimer && <p className="muted small ai-disclaimer">{assistant.disclaimer}</p>}
      <p className="muted small ai-privacy">{fill(a.privacyNotice)}</p>
    </div>
  );
}

export default ChatPanel;
