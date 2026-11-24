import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { signedOutBrowsable } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import { AccessPromptCard } from '../../components/AccessPrompt.jsx';
import PostCard, { KNOWN_POST_KINDS } from '../../components/feed/PostCard.jsx';
import { guestRoutesFor } from './memberRoutes.js';

const m = strings.member;

/** Posts shown in the guest feed preview. */
export const GUEST_PREVIEW_SIZE = 5;

/**
 * /app landing for a signed-out visitor when anonymous browsing is on (ACCESS_MODELS_CONTRACT.md
 * section 8.8): a sign-in prompt, a preview of the latest `Public` feed posts (fetched without a
 * token), and the sections open to guests. Nothing is requested when the feed is not browsable.
 */
function GuestHomeView() {
  const { client } = useAuth();
  const { property, fill } = useProperty();
  useDocumentMeta(m.metaTitle(m.title), m.metaDescription);
  const feedOpen = signedOutBrowsable(property, 'feed');
  const [feed, setFeed] = useState({ status: feedOpen ? 'loading' : 'off', posts: [], error: null });

  useEffect(() => {
    if (!feedOpen) return undefined;
    const controller = new AbortController();
    client.listFeed({ maxResults: GUEST_PREVIEW_SIZE }, controller.signal).then(
      (page) => setFeed({ status: 'ready', posts: ((page && page.objects) || []).filter((p) => KNOWN_POST_KINDS.includes(p.kind || 'Original')).slice(0, GUEST_PREVIEW_SIZE), error: null }),
      (error) => { if (!controller.signal.aborted) setFeed({ status: 'error', posts: [], error }); }
    );
    return () => controller.abort();
  }, [client, feedOpen]);

  const replace = (next) => setFeed((f) => ({ ...f, posts: f.posts.map((p) => (p.id === next.id ? next : p)) }));
  const drop = (gone) => setFeed((f) => ({ ...f, posts: f.posts.filter((p) => p.id !== gone.id) }));
  const routes = guestRoutesFor(property);

  return (
    <div className="member-view guest-home">
      <section className="member-hero">
        <h1>{fill(m.guestTitle)}</h1>
        <p className="muted">{fill(m.guestBody)}</p>
      </section>
      <AccessPromptCard compact required="SignIn" title={strings.access.joinIn} body={strings.access.joinInBody} />
      {feedOpen && (
        <section className="feed-section" aria-labelledby="guest-feed-title">
          <h2 id="guest-feed-title" className="feed-section-title">{m.guestFeedTitle}</h2>
          {feed.status === 'loading' && <Loading inline />}
          <ErrorBanner message={feed.status === 'error' ? m.guestFeedError : ''} />
          {feed.status === 'ready' && feed.posts.length === 0 && <p className="muted small">{m.guestFeedEmpty}</p>}
          <div className="post-list">
            {feed.posts.map((post) => <PostCard key={post.id} post={post} onChange={replace} onDeleted={drop} onBlocked={() => {}} />)}
          </div>
          <Link to="/app/feed" className="button button-secondary button-sm">{m.guestSeeFeed}</Link>
        </section>
      )}
      {routes.length > 0 && (
        <section aria-labelledby="guest-explore-title">
          <h2 id="guest-explore-title" className="feed-section-title">{m.guestExplore}</h2>
          <ul className="member-grid">
            {routes.map((route) => (
              <li key={route.id}>
                <Link to={'/app/' + route.id} className="member-tile">
                  <Icon name={route.icon} size={20} />
                  <span className="member-tile-name">{route.label}</span>
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}

export default GuestHomeView;
