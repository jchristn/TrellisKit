import { useCallback, useState } from 'react';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import useCursorList from '../../hooks/useCursorList.js';
import useInfiniteScroll from '../../hooks/useInfiniteScroll.js';
import Icon from '../../components/Icon.jsx';
import { Banner, EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import PostCard, { KNOWN_POST_KINDS } from '../../components/feed/PostCard.jsx';
import PostComposer from '../../components/feed/PostComposer.jsx';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import useEnumerationUrlState from '../../hooks/useEnumerationUrlState.js';
import { errorText, mutedText, postingPermissions } from '../../utils/engagement.js';
import { featureAccess } from '@trelliskit/shared';
import { useProperty } from '../../context/PropertyContext.jsx';
import { AccessPromptCard } from '../../components/AccessPrompt.jsx';

const f = strings.feed;
const c = strings.composer;
const PAGE_SIZE = 20;
const MY_STATES = ['Scheduled', 'Processing', 'Published', 'Hidden', 'Removed'];
/** Feed orderings: the server default (pinned first, newest published), or oldest first. */
const FEED_ORDERINGS = [{ value: '', label: f.orderLatest }, { value: 'CreatedAscending', label: strings.metadata.orderings.CreatedAscending }];

function MyPosts({ client }) {
  const [state, setState] = useState('');
  const fetchPage = useCallback((cursor, signal) => client.listMyFeedPosts({ continuationToken: cursor, maxResults: PAGE_SIZE, filters: { state } }, signal), [client, state]);
  const list = useCursorList(fetchPage);
  return (
    <section className="panel" aria-labelledby="my-posts-title">
      <div className="panel-header">
        <div>
          <h2 id="my-posts-title">{f.yourPosts}</h2>
          <p className="muted small">{f.yourPostsSummary}</p>
        </div>
        <label className="inline-field">
          <span className="label-text">{f.stateFilter}</span>
          <select className="select-compact" value={state} onChange={(e) => setState(e.target.value)}>
            <option value="">{f.allStates}</option>
            {MY_STATES.map((s) => <option key={s} value={s}>{f.states[s]}</option>)}
          </select>
        </label>
      </div>
      <ErrorBanner message={list.error ? errorText(list.error, {}, f.loadError) : ''} onRetry={list.refresh} />
      {list.loaded && !list.items.length && !list.loading && <p className="muted small">{f.yourPostsEmpty}</p>}
      <div className="post-list">
        {list.items.filter((p) => KNOWN_POST_KINDS.includes(p.kind || 'Original')).map((post) => (
          <PostCard key={post.id} post={post} onChange={(next) => list.update((items) => items.map((x) => (x.id === next.id ? next : x)))} onDeleted={(gone) => list.update((items) => items.filter((x) => x.id !== gone.id))} />
        ))}
      </div>
      {list.loading && <Loading inline />}
      {list.loaded && !list.done && !list.loading && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{f.loadMore}</button>}
    </section>
  );
}

/**
 * /app/feed: the composer for members who may post, pinned posts first, then the latest posts
 * with infinite scroll (cursor paging, de-duplicated), a refresh control, and a "Your posts" panel
 * for posters' scheduled and processing posts.
 */
function FeedView() {
  const { client, me } = useAuth();
  const toast = useToast();
  useDocumentMeta(f.meta, f.summary);
  const { property } = useProperty();
  const writeAccess = featureAccess(property, me, 'feed', 'write');
  // Posting rights come from Me; a gated feed (PartiallyGated) also needs a membership.
  const perms = { ...postingPermissions(me), canCreateFeedPost: postingPermissions(me).canCreateFeedPost && writeAccess.allowed };
  const lockedPoster = Boolean(me) && postingPermissions(me).canCreateFeedPost && !writeAccess.allowed;
  const [composing, setComposing] = useState(false);
  const [showMine, setShowMine] = useState(false);
  const [mineKey, setMineKey] = useState(0);
  const [meta, setMeta] = useEnumerationUrlState();
  const fetchPage = useCallback((cursor, signal) => client.listFeed({ ...meta, continuationToken: cursor, maxResults: PAGE_SIZE }, signal), [client, meta]);
  const list = useCursorList(fetchPage);
  const sentinel = useInfiniteScroll(list.loadMore, { enabled: list.loaded && !list.done && !list.error });

  const posts = list.items.filter((p) => KNOWN_POST_KINDS.includes(p.kind || 'Original'));
  const pinned = posts.filter((p) => p.isPinned);
  const latest = posts.filter((p) => !p.isPinned);
  const replace = (next) => list.update((items) => items.map((x) => (x.id === next.id ? next : x)));
  const drop = (post) => list.update((items) => items.filter((x) => x.id !== post.id));
  const dropAuthor = (user) => list.update((items) => items.filter((x) => !x.author || x.author.id !== user.id));

  const created = (post) => {
    setComposing(false);
    setMineKey((k) => k + 1);
    if (post.state === 'Published') {
      list.update((items) => [post, ...items.filter((x) => x.id !== post.id)]);
      toast.success(c.posted);
    } else {
      setShowMine(true);
      toast.success(post.state === 'Scheduled' ? c.scheduled : c.processingSaved);
    }
  };

  const refresh = async () => {
    await list.refresh();
    toast.info(f.refreshed);
  };

  const renderCard = (post) => <PostCard key={post.id} post={post} onChange={replace} onDeleted={drop} onBlocked={dropAuthor} />;

  return (
    <div className="member-view feed-view">
      <header className="page-header">
        <div className="page-header-text">
          <h1>{f.title}</h1>
        </div>
        <div className="page-header-actions">
          {perms.canCreateFeedPost && (
            <button type="button" className="button button-secondary button-sm" onClick={() => setShowMine((v) => !v)} aria-expanded={showMine}>{f.yourPosts}</button>
          )}
          <button type="button" className="icon-button" onClick={refresh} disabled={list.loading} aria-label={f.refresh} title={f.refresh}>
            <Icon name="refresh" className={list.loading ? 'spin' : ''} />
          </button>
        </div>
      </header>

      {perms.canCreateFeedPost && (
        composing ? (
          <section className="panel composer-panel" aria-label={c.newTitle}>
            <h2 className="visually-hidden">{c.newTitle}</h2>
            <PostComposer client={client} perms={perms} onSubmit={(body) => client.createFeedPost(body)} onSaved={created} onCancel={() => setComposing(false)} />
          </section>
        ) : (
          <button type="button" className="composer-launch" onClick={() => setComposing(true)} disabled={perms.muted}>
            <Icon name="edit" size={16} /><span>{f.write}</span>
          </button>
        )
      )}
      {!me && <AccessPromptCard compact required="SignIn" feature="feed" title={strings.access.joinIn} body={strings.access.joinInBody} />}
      {lockedPoster && <AccessPromptCard compact required={writeAccess.requires} feature="feed" />}
      {perms.canCreateFeedPost && perms.muted && <Banner tone="warning" icon="mute">{mutedText(perms)}</Banner>}
      {showMine && perms.canCreateFeedPost && <MyPosts key={mineKey} client={client} />}
      <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType="FeedPost" showSearch searchLabel={f.searchLabel} orderings={FEED_ORDERINGS} />

      <ErrorBanner message={list.error ? f.loadError + ' ' + errorText(list.error, {}, '') : ''} onRetry={list.items.length ? list.loadMore : list.refresh} />
      {!list.loaded && list.loading && <Loading />}
      {list.loaded && !posts.length && !list.loading && !list.error && <EmptyState icon="feed" title={perms.canCreateFeedPost ? f.emptyPoster : f.empty} />}

      {pinned.length > 0 && (
        <section aria-labelledby="feed-pinned" className="feed-section">
          <h2 id="feed-pinned" className="feed-section-title"><Icon name="thumbtack" size={14} /> {f.pinned}</h2>
          <div className="post-list">{pinned.map(renderCard)}</div>
        </section>
      )}
      {latest.length > 0 && (
        <section aria-labelledby="feed-latest" className="feed-section">
          {pinned.length > 0 && <h2 id="feed-latest" className="feed-section-title">{f.latest}</h2>}
          {pinned.length === 0 && <h2 id="feed-latest" className="visually-hidden">{f.latest}</h2>}
          <div className="post-list">{latest.map(renderCard)}</div>
        </section>
      )}

      <div ref={sentinel} className="feed-sentinel" aria-hidden="true" />
      {list.loaded && list.loading && <Loading inline label={f.loadingMore} />}
      {list.loaded && !list.done && !list.loading && !list.error && (
        <button type="button" className="button button-secondary feed-more" onClick={list.loadMore}>{f.loadMore}</button>
      )}
      {list.loaded && list.done && posts.length > 0 && <p className="muted small feed-end">{f.end}</p>}
    </div>
  );
}

export default FeedView;
