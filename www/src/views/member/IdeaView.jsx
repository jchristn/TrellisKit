import { useCallback, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { canUseIdeas } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';
import { AccessPromptCard } from '../../components/AccessPrompt.jsx';
import { Banner, EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import CommentThread from '../../components/feed/CommentThread.jsx';
import AuthorLine from '../../components/feed/AuthorLine.jsx';
import ReactionBar from '../../components/feed/ReactionBar.jsx';
import ReportDialog from '../../components/feed/ReportDialog.jsx';
import FollowButton from '../../components/forums/FollowButton.jsx';
import { formatDateTime } from '../../utils/format.js';
import { IdeaStatusBadge, VoteButton, ideaPath } from './IdeasView.jsx';

const s = strings.ideas;
const d = s.idea;

/**
 * The idea's conversation through the shared comment tree (the one feed posts, threads, lessons,
 * and events use): sorting, replies, reactions, edits, reports, and permalinks. A merged or locked
 * idea keeps its comments readable and closes the composer.
 */
function IdeaComments({ idea, canComment }) {
  const [searchParams] = useSearchParams();
  const focusId = searchParams.get('comment');
  const post = { ...idea, commentTarget: idea.commentTarget || { type: 'Idea', id: idea.id }, commentsEnabled: true };
  return (
    <section className="panel idea-comments-panel">
      <CommentThread key={idea.id + ':' + (focusId || '')} post={post} focusId={focusId} canComment={canComment} viewAllPath={ideaPath(idea.id)} accessFeature="ideas" />
      {!canComment && <p className="muted small">{d.commentsClosed}</p>}
    </section>
  );
}

/**
 * /app/ideas/:id: one idea with its status and the team's note, the vote button, follow,
 * reactions, report, the merged banner (a merged idea is read only and links its target), and
 * comments.
 */
function IdeaView() {
  const { id } = useParams();
  const { client, me } = useAuth();
  const { property } = useProperty();
  const read = canUseIdeas(property, me, 'read');
  const write = canUseIdeas(property, me, 'write');
  const fetcher = useCallback((signal) => client.getIdea(id, signal), [client, id]);
  const query = useApiQuery(fetcher, { enabled: read.allowed });
  const [local, setLocal] = useState(null);
  const [reporting, setReporting] = useState(false);
  const idea = local && query.data && local.id === query.data.id ? { ...query.data, ...local } : query.data;
  useDocumentMeta(idea ? idea.title + ' | {name}' : s.meta);

  if (!read.allowed) return <div className="member-view">{read.requires ? <AccessPromptCard required={read.requires} feature="ideas" /> : <EmptyState icon="trending" title={d.notFound} />}</div>;
  if (query.error && query.error.status === 404) {
    return <div className="member-view"><EmptyState icon="trending" title={d.notFound} action={<Link to="/app/ideas" className="button button-secondary">{d.back}</Link>} /></div>;
  }
  const viewer = (idea && idea.viewer) || {};
  const merged = idea && idea.mergedInto;
  const merge = (patch) => setLocal((prev) => ({ ...(prev || {}), ...patch, id: idea.id }));

  return (
    <div className="member-view idea-view">
      <Link to="/app/ideas" className="back-link small"><Icon name="chevronLeft" size={14} /><span>{d.back}</span></Link>
      {query.loading && !idea && <Loading />}
      <ErrorBanner message={query.error && query.error.status !== 404 ? d.loadError : ''} onRetry={query.reload} />
      {idea && (
        <>
          {merged && (
            <Banner tone="info" icon="info" actions={<Link to={ideaPath(merged.id)} className="button button-secondary button-sm">{d.openMerged}</Link>}>
              <strong>{d.mergedTitle}</strong> {d.mergedBody(merged.title || '')}
            </Banner>
          )}
          <article className="panel idea-detail">
            <div className="idea-head">
              <VoteButton idea={idea} disabled={!write.allowed || Boolean(merged)} onChange={(updated) => merge(updated)} />
              <div className="idea-text">
                <h1>{idea.title}</h1>
                <div className="badge-row small">
                  <IdeaStatusBadge status={idea.status} />
                  <span className="muted">{s.votes(idea.voteCount || 0)}</span>
                </div>
              </div>
            </div>
            {idea.author && <AuthorLine user={idea.author} time={idea.createdUtc} size={28} />}
            <MarkdownBody body={idea.body || ''} allowHeadings={false} />
            {idea.statusNote && (
              <aside className="idea-status-note">
                <h2 className="label-text">{d.statusNote}</h2>
                <p>{idea.statusNote}</p>
                {idea.statusUtc && <span className="muted small">{formatDateTime(idea.statusUtc)}</span>}
              </aside>
            )}
            <div className="row-actions">
              {write.allowed && !merged && (
                <FollowButton
                  followed={Boolean(idea.isFollowed)}
                  label={d.followLabel}
                  unfollowLabel={d.unfollowLabel}
                  onToggle={async (next) => {
                    const result = next ? await client.followIdea(idea.id) : await client.unfollowIdea(idea.id);
                    merge({ isFollowed: result ? Boolean(result.isFollowed) : next });
                    return result;
                  }}
                />
              )}
              {viewer.canReport && <button type="button" className="button button-secondary button-sm" onClick={() => setReporting(true)}><Icon name="flag" size={14} /><span>{d.report}</span></button>}
            </div>
            <ReactionBar target={{ type: 'Idea', id: idea.id }} value={idea} canReact={write.allowed && viewer.canReact !== false} onChange={(fields) => merge(fields)} />
          </article>
          <IdeaComments idea={idea} canComment={write.allowed && !merged && viewer.canComment !== false && !idea.locked} />
        </>
      )}
      {reporting && idea && <ReportDialog target={{ type: 'Idea', id: idea.id }} author={idea.author} onClose={() => setReporting(false)} />}
    </div>
  );
}

export default IdeaView;
