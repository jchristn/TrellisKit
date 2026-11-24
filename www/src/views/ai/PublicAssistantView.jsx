import { useCallback, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import strings from '../../strings.js';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import { Loading, ErrorBanner } from '../../components/States.jsx';
import ChatPanel, { chatErrorText } from './ChatPanel.jsx';
import useVisitorClient from './useVisitorClient.js';

const a = strings.ai;

function normalizeOrigin(value) {
  if (typeof value !== 'string' || !value) return null;
  try {
    const url = new URL(value);
    if (url.protocol !== 'https:' && url.protocol !== 'http:') return null;
    return url.origin.toLowerCase();
  } catch {
    return null;
  }
}

/**
 * The origin of the page embedding this frame: `location.ancestorOrigins[0]` where the browser has
 * it, else the origin of `document.referrer`; null when neither is known.
 * @param {{ location?: Location, document?: Document }} [env]
 * @returns {string|null}
 */
export function embeddingOrigin(env = typeof window !== 'undefined' ? window : {}) {
  const ancestors = env.location && env.location.ancestorOrigins;
  if (ancestors && ancestors.length) return normalizeOrigin(ancestors[0]);
  return normalizeOrigin(env.document && env.document.referrer);
}

/**
 * Where the embed page may post `tk-assistant:*` messages (AI contract section 13.3): the embedding
 * page's origin when it is in `AssistantSummary.embed.allowedOrigins` (exact, case-insensitive),
 * else null (post nothing). Never `"*"`.
 * @param {{ allowedOrigins?: string[] }|null} embed
 * @param {string|null} parentOrigin
 * @returns {string|null}
 */
export function embedTargetOrigin(embed, parentOrigin) {
  const origin = normalizeOrigin(parentOrigin);
  if (!origin || !embed || !Array.isArray(embed.allowedOrigins)) return null;
  return embed.allowedOrigins.some((allowed) => normalizeOrigin(allowed) === origin) ? origin : null;
}

function postToParent(target, type) {
  if (!target || typeof window === 'undefined' || !window.parent || window.parent === window) return;
  try {
    window.parent.postMessage({ type }, target);
  } catch {
    // The parent navigated away; nothing to tell.
  }
}

function VisitorChat({ id, surface, fetchImpl }) {
  const { client, error } = useVisitorClient({ fetchImpl, surface });
  const fetcher = useCallback((signal) => (client ? client.getAssistant(id, signal) : Promise.resolve(null)), [client, id]);
  const assistant = useApiQuery(fetcher, { enabled: Boolean(client) });
  // The embed frame talks only to an allowed embedding origin (never "*").
  const target = surface === 'widget' && assistant.data ? embedTargetOrigin(assistant.data.embed, embeddingOrigin()) : null;
  useEffect(() => {
    if (target) postToParent(target, 'tk-assistant:ready');
  }, [target]);
  useDocumentMeta(assistant.data ? a.publicTitle(assistant.data.title) : a.assistantsTitle, '');
  if (error) return <ErrorBanner message={chatErrorText(error)} />;
  if (!client || (assistant.loading && !assistant.data)) return <Loading />;
  if (assistant.error) return <ErrorBanner message={chatErrorText(assistant.error)} />;
  if (!assistant.data) return null;
  if (surface === 'widget' && !assistant.data.embed) return <p className="ai-error">{a.embedNotAllowed}</p>;
  if (surface === 'widget' && !target) return <p className="ai-error" data-testid="embed-not-allowed">{a.embedNotAllowed}</p>;
  return (
    <div className={'ai-public' + (surface === 'widget' ? ' ai-embed' : '')}>
      <header className="ai-chat-header">
        <span className="ai-avatar" aria-hidden="true">{(assistant.data.avatar && assistant.data.avatar.emoji) || assistant.data.title.slice(0, 1)}</span>
        <h1>{assistant.data.title} <span className="badge badge-info">{a.aiBadge}</span></h1>
        {surface === 'widget' && (
          <button type="button" className="icon-button ai-embed-close" aria-label={a.embedClose} onClick={() => postToParent(target, 'tk-assistant:close')}>×</button>
        )}
      </header>
      <ChatPanel client={client} assistant={assistant.data} citationBase={null} allowFeedback />
    </div>
  );
}

/** /assistants/:id: public chat for Public assistants, signed out (visitor mode). */
export function PublicAssistantView({ fetchImpl = null }) {
  const { id } = useParams();
  return <div className="center-page ai-public-page"><VisitorChat id={id} surface="web" fetchImpl={fetchImpl} /></div>;
}

/**
 * /embed/assistants/:id: minimal chrome for the widget iframe (section 13.3). Only embeddable Public
 * assistants render, and only inside a page whose origin is in `embed.allowedOrigins`;
 * frame-ancestors for those origins is set by the web server. Posts tk-assistant:ready and
 * tk-assistant:close to that origin only.
 */
export function EmbedAssistantView({ fetchImpl = null }) {
  const { id } = useParams();
  return <VisitorChat id={id} surface="widget" fetchImpl={fetchImpl} />;
}

export default PublicAssistantView;
