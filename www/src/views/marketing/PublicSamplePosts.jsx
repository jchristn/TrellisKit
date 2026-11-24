import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { normalizeEnumerationResult } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useHydrated from './useHydrated.js';
import AuthorLine from '../../components/feed/AuthorLine.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import MediaGallery from '../../components/feed/MediaGallery.jsx';
import LinkPreviewCard from '../../components/feed/LinkPreviewCard.jsx';

const p = strings.publicFeed;

/** Whether the property shows public sample posts (`features.publicSamples`, on unless false). */
export function publicSamplesEnabled(property) {
  const features = (property && property.features) || {};
  return features.publicSamples !== false;
}

/**
 * Public sample posts (contract section 5.10) for marketing pages: read anonymously from
 * `GET /public/feed` in the browser after hydration, so nothing is prerendered and the page stays
 * cacheable. Mentions render as plain text; cards show the comment count and invite the visitor
 * to join. Nothing renders when the feature is off, the request fails, or there are no posts.
 * @param {{ limit?: number }} props
 */
function PublicSamplePosts({ limit = 3 }) {
  const hydrated = useHydrated();
  const { client, isSignedIn } = useAuth();
  const { property, fill } = useProperty();
  const enabled = publicSamplesEnabled(property);
  const [posts, setPosts] = useState([]);

  useEffect(() => {
    if (!hydrated || !enabled || !client || typeof client.listPublicFeed !== 'function') return undefined;
    const controller = new AbortController();
    client.listPublicFeed({ maxResults: Math.min(20, limit) }, controller.signal).then(
      (body) => { if (!controller.signal.aborted) setPosts(normalizeEnumerationResult(body).objects.filter((x) => x && x.id).slice(0, limit)); },
      () => {}
    );
    return () => controller.abort();
  }, [hydrated, enabled, client, limit]);

  if (!hydrated || !enabled || !posts.length) return null;
  return (
    <section className="section public-samples" aria-labelledby="public-samples-title">
      <div className="site-container">
        <h2 id="public-samples-title">{fill(p.title)}</h2>
        <p className="muted">{fill(p.summary)}</p>
        <div className="public-sample-list">
          {posts.map((post) => (
            <article key={post.id} className="post-card public-sample" aria-labelledby={'sample-' + post.id}>
              <AuthorLine user={post.author} time={post.publishedUtc} linkProfile={false} />
              {post.title ? <h3 id={'sample-' + post.id} className="post-title">{post.title}</h3> : <span id={'sample-' + post.id} className="visually-hidden">{post.excerpt}</span>}
              <MarkdownBody body={post.body} allowHeadings linkMentions={false} />
              <MediaGallery items={post.attachments} />
              {post.linkPreview && <LinkPreviewCard preview={post.linkPreview} />}
              <footer className="post-foot">
                <span className="muted small">{p.reactions(post.reactionTotal || 0)} · {p.comments(post.commentCount || 0)}</span>
                <Link to={isSignedIn ? '/app/feed/' + encodeURIComponent(post.id) : '/sign-up'} className="button button-secondary button-sm">{isSignedIn ? p.openApp : strings.feed.joinToComment}</Link>
              </footer>
            </article>
          ))}
        </div>
        {!isSignedIn && <Link to="/sign-up" className="button button-primary">{p.join}</Link>}
      </div>
    </section>
  );
}

export default PublicSamplePosts;
