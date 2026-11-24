import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import AuthorLine from '../../components/feed/AuthorLine.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import MediaGallery from '../../components/feed/MediaGallery.jsx';
import ThreadBadges from '../../components/forums/ThreadBadges.jsx';

const t = strings.publicThreads;

/**
 * Public page of one sample thread (PHASE3_CONTRACT section 10, `GET /public/threads/{id}`) on
 * the marketing site, for visitors who are not signed in. It shows the forum, title, author,
 * badges, body, the attachment preview, a poll summary without voting, and the counts; replies
 * are not public, so it invites the visitor to join (or a signed-in member to open the thread in
 * the app). A thread that is not public, or does not exist, shows "Thread not found".
 */
function PublicThreadPage() {
  const { id } = useParams();
  const { client, isSignedIn } = useAuth();
  const [state, setState] = useState({ id: null, thread: null, error: null });
  const [attempt, setAttempt] = useState(0);
  const current = state.id === id ? state : { id, thread: null, error: null };
  const thread = current.thread;
  useDocumentMeta(thread ? t.meta.replace('{title}', thread.title) : t.loading + ' | {name}', thread ? thread.excerpt : '');

  useEffect(() => {
    if (!client || typeof client.getPublicThread !== 'function') return undefined;
    const controller = new AbortController();
    client.getPublicThread(id, controller.signal).then(
      (body) => { if (!controller.signal.aborted) setState({ id, thread: body, error: null }); },
      (err) => { if (!controller.signal.aborted) setState({ id, thread: null, error: err || new Error('failed') }); }
    );
    return () => controller.abort();
  }, [client, id, attempt]);

  if (current.error && current.error.status === 404) {
    return (
      <div className="site-container page">
        <EmptyState icon="info" title={t.notFoundTitle} action={<Link to="/" className="button button-primary">{t.backHome}</Link>}>{t.notFoundBody}</EmptyState>
      </div>
    );
  }

  const appPath = '/app/threads/' + encodeURIComponent(id);
  const extraAttachments = thread ? Math.max(0, (thread.attachmentCount || 0) - (thread.attachmentPreview || []).length) : 0;
  return (
    <div className="site-container page public-thread">
      {!thread && !current.error && <Loading label={t.loading} />}
      {current.error && <ErrorBanner message={t.loadError} onRetry={() => setAttempt((n) => n + 1)} />}
      {thread && (
        <article className="post-card public-sample" aria-labelledby="public-thread-title">
          {thread.forum && thread.forum.name && <p className="eyebrow">{t.inForum(thread.forum.name)}</p>}
          <h1 id="public-thread-title" className="post-title">{thread.title}</h1>
          <ThreadBadges thread={thread} />
          <AuthorLine user={thread.author} time={thread.publishedUtc || thread.createdUtc} linkProfile={false} />
          {thread.body ? <MarkdownBody body={thread.body} allowHeadings linkMentions={false} /> : thread.excerpt && <p>{thread.excerpt}</p>}
          <MediaGallery items={thread.attachmentPreview || []} />
          {extraAttachments > 0 && <p className="muted small">{t.moreAttachments(extraAttachments)}</p>}
          {thread.poll && <p className="muted small">{thread.poll.isClosed ? strings.forums.pollClosed : strings.forums.pollOpen} · {t.voters(thread.poll.totalVoters || 0)}</p>}
          <footer className="post-foot">
            <span className="muted small">{t.reactions(thread.reactionTotal || 0)} · {t.comments(thread.commentCount || 0)}</span>
            {isSignedIn
              ? <Link to={appPath} className="button button-primary button-sm">{t.openApp}</Link>
              : (
                <span className="badge-row">
                  <Link to="/sign-up" className="button button-primary button-sm">{t.joinToReply}</Link>
                  <Link to={'/sign-in?next=' + encodeURIComponent(appPath)} className="button button-secondary button-sm">{t.signIn}</Link>
                </span>
              )}
          </footer>
        </article>
      )}
    </div>
  );
}

export default PublicThreadPage;
