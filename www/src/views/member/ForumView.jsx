import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { MemberThreadOrderings, ThreadTypes, isKnownThread, splitPinned, threadFilters } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import useEnumerationUrlState from '../../hooks/useEnumerationUrlState.js';
import useInfiniteScroll from '../../hooks/useInfiniteScroll.js';
import Icon from '../../components/Icon.jsx';
import { Banner, EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import ForumIcon from '../../components/forums/ForumIcon.jsx';
import FollowButton from '../../components/forums/FollowButton.jsx';
import { AccessPromptCard } from '../../components/AccessPrompt.jsx';
import ThreadRow from '../../components/forums/ThreadRow.jsx';
import ThreadComposer from '../../components/forums/ThreadComposer.jsx';
import { errorText, postingPermissions } from '../../utils/engagement.js';
import { appForumPath, appThreadPath, forumErrors } from '../../utils/forums.js';

const t = strings.forums;
const PAGE_SIZE = 20;
const ORDERINGS = MemberThreadOrderings.map((value) => ({ value, label: t.orderings[value] }));

function ThreadList({ client, forum, meta, ordering, types }) {
  const typeKey = types.join(',');
  const fetchPage = useCallback(
    (cursor, signal) => client.enumerateForumThreads(forum.id, { ...meta, ordering, continuationToken: cursor, maxResults: PAGE_SIZE, filters: threadFilters({ types: typeKey ? typeKey.split(',') : [] }) }, signal),
    [client, forum.id, meta, ordering, typeKey]
  );
  const list = useCursorList(fetchPage);
  const sentinel = useInfiniteScroll(list.loadMore, { enabled: list.loaded && !list.done && !list.error });
  const threads = list.items.filter(isKnownThread);
  const { pinned, rest } = meta.search ? { pinned: [], rest: threads } : splitPinned(threads);
  const filtered = Boolean(meta.search || meta.labels.length || Object.keys(meta.tags).length || types.length);
  return (
    <>
      <ErrorBanner message={list.error ? t.threadsLoadError + ' ' + errorText(list.error, {}, '') : ''} onRetry={list.items.length ? list.loadMore : list.refresh} />
      {!list.loaded && list.loading && <Loading />}
      {list.loaded && !threads.length && !list.loading && !list.error && <EmptyState icon="forum" title={filtered ? t.threadsEmptyFiltered : t.threadsEmpty} />}
      {pinned.length > 0 && (
        <section aria-labelledby="forum-pinned" className="feed-section">
          <h2 id="forum-pinned" className="feed-section-title"><Icon name="thumbtack" size={14} /> {t.pinned}</h2>
          <div className="thread-list">{pinned.map((thread) => <ThreadRow key={thread.id} thread={thread} />)}</div>
        </section>
      )}
      {rest.length > 0 && (
        <section aria-labelledby="forum-threads" className="feed-section">
          <h2 id="forum-threads" className={pinned.length ? 'feed-section-title' : 'visually-hidden'}>{t.latest}</h2>
          <div className="thread-list">{rest.map((thread) => <ThreadRow key={thread.id} thread={thread} />)}</div>
        </section>
      )}
      <div ref={sentinel} className="feed-sentinel" aria-hidden="true" />
      {list.loaded && list.loading && <Loading inline label={t.loadingMore} />}
      {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary feed-more" onClick={list.loadMore}>{t.loadMore}</button>}
      {list.loaded && list.done && threads.length > 0 && <p className="muted small feed-end">{t.end}</p>}
    </>
  );
}

/**
 * /app/forums/:slug (PHASE3_CONTRACT.md section 19): the forum header (icon, description,
 * guidelines, follow, mark all read), the ordering selector (Active, Hot, New, Top), type filter
 * chips, label and tag filters and search (all in the URL), pinned threads first, infinite
 * scroll, and the "New thread" composer with a type picker limited to `viewer.allowedThreadTypes`.
 * The slug is resolved through `getForum`, which also accepts an id (notification paths).
 */
function ForumView() {
  const { slug } = useParams();
  const { client, me } = useAuth();
  const toast = useToast();
  const navigate = useNavigate();
  const perms = postingPermissions(me);
  const [searchParams, setSearchParams] = useSearchParams();
  const fetcher = useCallback((signal) => client.getForum(slug, signal), [client, slug]);
  const query = useApiQuery(fetcher);
  const [override, setOverride] = useState(null);
  const forum = useMemo(() => (override && query.data && override.id === query.data.id ? { ...query.data, ...override } : query.data), [override, query.data]);
  const [meta, setMeta] = useEnumerationUrlState();
  const [composing, setComposing] = useState(false);
  const [listKey, setListKey] = useState(0);
  const types = searchParams.getAll('type').filter((x) => ThreadTypes.includes(x));
  useDocumentMeta(strings.member.metaTitle(forum ? forum.name : t.title));
  const defaultOrdering = (forum && forum.defaultOrdering) || 'Active';
  const ordering = searchParams.get('ordering') || (meta.search ? 'Relevance' : defaultOrdering);
  const filterValue = useMemo(() => ({ ...meta, ordering }), [meta, ordering]);

  useEffect(() => {
    if (forum && forum.slug && slug !== forum.slug) navigate(appForumPath(forum) + (searchParams.toString() ? '?' + searchParams.toString() : ''), { replace: true });
  }, [forum, slug, navigate, searchParams]);

  const toggleType = (type) => {
    const next = new URLSearchParams(searchParams);
    const current = next.getAll('type');
    next.delete('type');
    for (const value of current.includes(type) ? current.filter((x) => x !== type) : current.concat(type)) next.append('type', value);
    setSearchParams(next);
  };

  const back = <Link to="/app/forums" className="back-link small"><Icon name="chevronLeft" size={14} /><span>{t.backToForums}</span></Link>;

  if (query.error && query.error.status === 404) {
    return (
      <div className="member-view">
        {back}
        <EmptyState icon="info" title={t.forumNotFound} action={<Link to="/app/forums" className="button button-primary">{t.backToForums}</Link>}>{t.forumNotFoundBody}</EmptyState>
      </div>
    );
  }
  if (!forum) {
    return (
      <div className="member-view">
        {back}
        {query.loading && <Loading />}
        <ErrorBanner message={query.error ? errorText(query.error, forumErrors(), t.loadError) : ''} onRetry={query.reload} />
      </div>
    );
  }

  const viewer = forum.viewer || {};
  const allowed = viewer.allowedThreadTypes || [];
  const canCreate = viewer.canCreateThread !== false && allowed.length > 0 && !forum.isArchived;

  const markRead = async () => {
    try {
      await client.markForumRead(forum.id);
      setOverride({ id: forum.id, unreadThreadCount: 0 });
      setListKey((k) => k + 1);
      toast.success(t.markedRead);
    } catch (err) {
      toast.error(errorText(err, forumErrors()));
    }
  };

  const created = (thread) => {
    setComposing(false);
    toast.success(thread.state === 'Processing' ? t.composer.processing : t.composer.posted);
    navigate(appThreadPath(thread.id));
  };

  return (
    <div className="member-view forum-view">
      {back}
      <header className="page-header forum-header">
        <ForumIcon forum={forum} size={48} />
        <div className="page-header-text">
          <h1>{forum.name}</h1>
          {forum.description && <p className="muted">{forum.description}</p>}
        </div>
        <div className="page-header-actions">
          {viewer.canFollow !== false && (
            <FollowButton
              followed={Boolean(forum.isFollowed)}
              label={t.followForumLabel(forum.name)}
              unfollowLabel={t.unfollowForumLabel(forum.name)}
              onToggle={async (next) => {
                const updated = next ? await client.followForum(forum.id) : await client.unfollowForum(forum.id);
                setOverride({ ...(updated && updated.id ? updated : {}), id: forum.id, isFollowed: next });
              }}
            />
          )}
          {me && <button type="button" className="button button-secondary button-sm" onClick={markRead}><Icon name="check" size={14} /><span>{t.markAllRead}</span></button>}
          {canCreate && !composing && <button type="button" className="button button-primary button-sm" onClick={() => setComposing(true)} disabled={perms.muted}><Icon name="plus" size={14} /><span>{t.newThread}</span></button>}
        </div>
      </header>
      {forum.isArchived && <Banner tone="info" icon="lock">{t.archivedNote}</Banner>}
      {!me && <AccessPromptCard compact required="SignIn" feature="forums" title={strings.access.joinIn} body={strings.access.joinInBody} />}
      {forum.guidelines && (
        <details className="panel forum-guidelines">
          <summary>{t.guidelines}</summary>
          <MarkdownBody body={forum.guidelines} allowHeadings={false} linkMentions={false} />
        </details>
      )}
      {composing && (
        <section className="panel composer-panel" aria-label={t.composer.newTitle}>
          <h2>{t.composer.newTitle}</h2>
          <ThreadComposer client={client} forum={forum} perms={perms} onSaved={created} onCancel={() => setComposing(false)} />
        </section>
      )}
      <MetadataFilterBar value={filterValue} onChange={(next) => setMeta(next.ordering === (next.search ? 'Relevance' : defaultOrdering) ? { ...next, ordering: '' } : next)} client={client} targetType="Thread" showSearch searchLabel={t.searchLabel} orderings={meta.search ? [{ value: 'Relevance', label: t.orderings.Relevance }, ...ORDERINGS] : ORDERINGS} />
      <div className="chip-row" role="group" aria-label={t.typeFilter}>
        {ThreadTypes.filter((type) => (forum.allowedThreadTypes || ThreadTypes).includes(type)).map((type) => (
          <button key={type} type="button" className={'chip-toggle' + (types.includes(type) ? ' active' : '')} aria-pressed={types.includes(type)} onClick={() => toggleType(type)}>{t.types[type]}</button>
        ))}
      </div>
      <ThreadList key={forum.id + ':' + listKey} client={client} forum={forum} meta={meta} ordering={ordering} types={types} />
    </div>
  );
}

export default ForumView;
