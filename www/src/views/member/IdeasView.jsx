import { useCallback, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { IdeaOrdering, canUseIdeas, normalizeEnumerationResult } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import useEnumerationUrlState, { activeMetaCount } from '../../hooks/useEnumerationUrlState.js';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import Modal from '../../components/Modal.jsx';
import Icon from '../../components/Icon.jsx';
import { AccessPromptCard, useAccessPrompt } from '../../components/AccessPrompt.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import { errorText } from '../../utils/engagement.js';
import { ideaSubmissionsOf } from '../../utils/messaging.js';

const s = strings.ideas;
const PAGE_SIZE = 20;
const ORDERINGS = [IdeaOrdering.Top, IdeaOrdering.Trending, IdeaOrdering.CreatedDescending, IdeaOrdering.UpdatedDescending, IdeaOrdering.Relevance];

/** The route of an idea. */
export function ideaPath(id) {
  return '/app/ideas/' + encodeURIComponent(id);
}

/** A status chip with the configured color. */
export function IdeaStatusBadge({ status }) {
  if (!status) return null;
  return <span className="idea-status badge" style={status.color ? { borderColor: status.color, color: status.color } : undefined}>{status.label || status.key}</span>;
}

/**
 * The vote button (one upvote per member, contract 7.1): pressed when voted; the server's Idea
 * replaces the local copy; `409 IdeaMerged` and closed statuses explain themselves.
 * @param {{ idea: object, onChange: (idea: object) => void, disabled?: boolean }} props
 */
export function VoteButton({ idea, onChange, disabled = false }) {
  const { client } = useAuth();
  const toast = useToast();
  const accessPrompt = useAccessPrompt();
  const [busy, setBusy] = useState(false);
  const voted = Boolean(idea.hasVoted);
  const canVote = !idea.viewer || idea.viewer.canVote !== false || voted;
  const click = async () => {
    setBusy(true);
    try {
      const updated = voted ? await client.unvoteIdea(idea.id) : await client.voteIdea(idea.id);
      onChange(updated && updated.id ? updated : { ...idea, hasVoted: !voted, voteCount: (idea.voteCount || 0) + (voted ? -1 : 1) });
    } catch (err) {
      if (!accessPrompt.handle(err)) toast.error(errorText(err, s.errors, s.errors.generic));
    } finally {
      setBusy(false);
    }
  };
  return (
    <button type="button" className={'vote-button' + (voted ? ' voted' : '')} aria-pressed={voted} aria-label={s.voteLabel(idea.title, voted)} onClick={click} disabled={disabled || busy || !canVote}>
      <Icon name="chevronUp" size={16} />
      <span className="vote-count">{idea.voteCount || 0}</span>
      <span className="vote-text small">{voted ? s.voted : s.vote}</span>
    </button>
  );
}

function NewIdeaDialog({ onClose }) {
  const { client } = useAuth();
  const toast = useToast();
  const navigate = useNavigate();
  const [title, setTitle] = useState('');
  const [body, setBody] = useState('');
  const [problem, setProblem] = useState('');
  const [busy, setBusy] = useState(false);
  const submit = async (e) => {
    e.preventDefault();
    const tt = title.trim();
    if (tt.length < 3 || tt.length > 140) { setProblem(s.form.errors.title); return; }
    setBusy(true);
    setProblem('');
    try {
      const idea = await client.createIdea({ title: tt, body: body.trim() });
      toast.success(s.form.created);
      navigate(ideaPath(idea.id));
    } catch (err) {
      setProblem(errorText(err, s.form.errors));
      setBusy(false);
    }
  };
  return (
    <Modal open onClose={onClose} title={s.form.title}>
      <form className="form-stack" onSubmit={submit} noValidate>
        <div className="field">
          <label className="label-text" htmlFor="idea-title">{s.form.name}</label>
          <input id="idea-title" className="input" value={title} maxLength={140} onChange={(e) => setTitle(e.target.value)} aria-invalid={Boolean(problem)} />
          {problem && <span className="field-error">{problem}</span>}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="idea-body">{s.form.body}</label>
          <textarea id="idea-body" className="textarea" rows={6} value={body} maxLength={10000} onChange={(e) => setBody(e.target.value)} />
        </div>
        <div className="form-actions">
          <button type="submit" className="button button-primary" disabled={busy}>{busy ? s.form.submitting : s.form.submit}</button>
          <button type="button" className="button button-secondary" onClick={onClose}>{strings.common.cancel}</button>
        </div>
      </form>
    </Modal>
  );
}

/**
 * /app/ideas: the ideas board with orderings, status filter chips, Open and Voted by me chips,
 * search, label and tag filters, vote buttons, and load more. The submit form appears only when
 * member submissions are `Direct` (`ideas.memberSubmissions`).
 */
function IdeasView() {
  const { client, me } = useAuth();
  const { property } = useProperty();
  useDocumentMeta(s.meta, s.summary);
  const [searchParams, setSearchParams] = useSearchParams();
  const [meta, setMeta, metaKey] = useEnumerationUrlState({ defaultOrdering: IdeaOrdering.Top });
  const status = searchParams.get('status') || '';
  const openOnly = searchParams.get('open') === 'true';
  const votedByMe = searchParams.get('voted') === 'true';
  const [creating, setCreating] = useState(false);
  const read = canUseIdeas(property, me, 'read');
  const write = canUseIdeas(property, me, 'write');
  const statusesFetcher = useCallback((signal) => client.enumerateIdeaStatuses({ maxResults: 50 }, signal), [client]);
  const statusesQuery = useApiQuery(statusesFetcher, { enabled: read.allowed });
  const fallbackStatuses = (property && property.ideas && Array.isArray(property.ideas.statuses)) ? property.ideas.statuses : [];
  const statuses = statusesQuery.data ? normalizeEnumerationResult(statusesQuery.data).objects : fallbackStatuses;

  const fetchPage = useCallback((cursor, signal) => {
    const parsed = JSON.parse(metaKey);
    const filters = {};
    if (status) filters.status = [status];
    if (openOnly) filters.open = true;
    if (votedByMe) filters.votedByMe = true;
    return client.enumerateIdeas({ ordering: parsed.ordering || IdeaOrdering.Top, search: parsed.search, labels: parsed.labels, labelMatch: parsed.labelMatch, tags: parsed.tags, maxResults: PAGE_SIZE, continuationToken: cursor, filters }, signal);
  }, [client, metaKey, status, openOnly, votedByMe]);
  const list = useCursorList(fetchPage, { enabled: read.allowed });

  if (!read.allowed) {
    return <div className="member-view">{read.requires ? <AccessPromptCard required={read.requires} feature="ideas" /> : <EmptyState icon="trending" title={s.idea.notFound} />}</div>;
  }

  const setParam = (key, value) => setSearchParams((prev) => {
    const next = new URLSearchParams(prev);
    if (value) next.set(key, value);
    else next.delete(key);
    return next;
  });
  const direct = ideaSubmissionsOf(property) === 'Direct';
  const filtered = Boolean(status) || openOnly || votedByMe || activeMetaCount(meta) > 0;
  const orderings = ORDERINGS.map((value) => ({ value, label: s.orderings[value] }));

  return (
    <div className="member-view ideas-view">
      <header className="page-header">
        <div className="page-header-text">
          <h1>{s.title}</h1>
          <p className="page-summary">{s.summary}</p>
        </div>
        {direct && write.allowed && <div className="page-header-actions"><button type="button" className="button button-primary" onClick={() => setCreating(true)}><Icon name="plus" size={14} /><span>{s.submit}</span></button></div>}
      </header>
      {!direct && <p className="muted small">{s.staffOnlyNote} <Link to="/app/support/new">{strings.support.newTicket}</Link></p>}
      <div className="chip-row" role="group" aria-label={s.statusChips}>
        <button type="button" className={'chip-button' + (!status ? ' active' : '')} aria-pressed={!status} onClick={() => setParam('status', '')}>{s.allStatuses}</button>
        {statuses.map((st) => (
          <button key={st.key} type="button" className={'chip-button' + (status === st.key ? ' active' : '')} aria-pressed={status === st.key} onClick={() => setParam('status', status === st.key ? '' : st.key)}>{st.label || st.key}</button>
        ))}
        <button type="button" className={'chip-button' + (openOnly ? ' active' : '')} aria-pressed={openOnly} onClick={() => setParam('open', openOnly ? '' : 'true')}>{s.openOnly}</button>
        <button type="button" className={'chip-button' + (votedByMe ? ' active' : '')} aria-pressed={votedByMe} onClick={() => setParam('voted', votedByMe ? '' : 'true')}>{s.votedByMe}</button>
      </div>
      <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType="Idea" showSearch searchLabel={s.search} orderings={orderings} />
      <ErrorBanner message={list.error ? s.loadError : ''} onRetry={list.refresh} />
      {!list.loaded && list.loading && <Loading />}
      {list.loaded && !list.items.length && !list.loading && !list.error && <EmptyState icon="trending" title={filtered ? s.emptyFiltered : s.empty} />}
      {list.items.length > 0 && (
        <ul className="idea-list" aria-label={s.listLabel}>
          {list.items.map((idea) => (
            <li key={idea.id} className="idea-row panel">
              <VoteButton idea={idea} disabled={!write.allowed} onChange={(updated) => list.update((items) => items.map((x) => (x.id === updated.id ? { ...x, ...updated } : x)))} />
              <div className="idea-text">
                <Link to={ideaPath(idea.id)} className="idea-title">{idea.title}</Link>
                <div className="badge-row small">
                  <IdeaStatusBadge status={idea.status} />
                  <span className="muted">{s.comments(idea.commentCount || 0)}</span>
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}
      {list.loaded && !list.done && !list.loading && <button type="button" className="button button-secondary" onClick={list.loadMore}>{s.loadMore}</button>}
      {creating && <NewIdeaDialog onClose={() => setCreating(false)} />}
    </div>
  );
}

export default IdeasView;
