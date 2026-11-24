import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { hasServerCapability, normalizeEnumerationResult, ServerCapability } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useHydrated from './useHydrated.js';
import { publicSamplesEnabled } from './PublicSamplePosts.jsx';
import AuthorLine from '../../components/feed/AuthorLine.jsx';
import ThreadBadges from '../../components/forums/ThreadBadges.jsx';

const t = strings.publicThreads;

/** Public page of a sample thread on the marketing site. */
export function publicThreadPath(id) {
  return '/threads/' + encodeURIComponent(id);
}

/** Whether public sample threads can be shown: the server has forums and public samples are on. */
export function publicThreadsEnabled(property) {
  return publicSamplesEnabled(property) && hasServerCapability(property, ServerCapability.Forums);
}

/**
 * Public sample threads (PHASE3_CONTRACT section 10) for marketing pages: read anonymously from
 * `POST /public/threads/enumerate` in the browser after hydration, so nothing is prerendered.
 * Each card links to the public thread page (`/threads/{id}`), which also links signed-in members
 * to the thread in the member app. Nothing renders when forums or public samples are off, the request
 * fails, or there are no public threads.
 * @param {{ limit?: number }} props
 */
function PublicSampleThreads({ limit = 3 }) {
  const hydrated = useHydrated();
  const { client, isSignedIn } = useAuth();
  const { property, fill } = useProperty();
  const enabled = publicThreadsEnabled(property);
  const [threads, setThreads] = useState([]);

  useEffect(() => {
    if (!hydrated || !enabled || !client || typeof client.enumeratePublicThreads !== 'function') return undefined;
    const controller = new AbortController();
    client.enumeratePublicThreads({ maxResults: Math.min(20, limit) }, controller.signal).then(
      (body) => { if (!controller.signal.aborted) setThreads(normalizeEnumerationResult(body).objects.filter((x) => x && x.id).slice(0, limit)); },
      () => {}
    );
    return () => controller.abort();
  }, [hydrated, enabled, client, limit]);

  if (!hydrated || !enabled || !threads.length) return null;
  return (
    <section className="section public-samples" aria-labelledby="public-threads-title">
      <div className="site-container">
        <h2 id="public-threads-title">{fill(t.title)}</h2>
        <p className="muted">{fill(t.summary)}</p>
        <div className="public-sample-list">
          {threads.map((thread) => (
            <article key={thread.id} className="post-card public-sample" aria-labelledby={'thread-sample-' + thread.id}>
              <AuthorLine user={thread.author} time={thread.publishedUtc || thread.createdUtc} linkProfile={false} />
              <ThreadBadges thread={thread} />
              <h3 id={'thread-sample-' + thread.id} className="post-title">
                <Link to={publicThreadPath(thread.id)}>{thread.title}</Link>
              </h3>
              {thread.forum && thread.forum.name && <p className="muted small">{t.inForum(thread.forum.name)}</p>}
              {thread.excerpt && <p>{thread.excerpt}</p>}
              <footer className="post-foot">
                <span className="muted small">{t.reactions(thread.reactionTotal || 0)} · {t.comments(thread.commentCount || 0)}</span>
                <Link to={publicThreadPath(thread.id)} className="button button-secondary button-sm" aria-label={t.readLabel(thread.title)}>{t.read}</Link>
              </footer>
            </article>
          ))}
        </div>
        {!isSignedIn && <Link to="/sign-up" className="button button-primary">{t.join}</Link>}
      </div>
    </section>
  );
}

export default PublicSampleThreads;
