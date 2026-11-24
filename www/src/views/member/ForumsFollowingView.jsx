import { useCallback } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import useEnumerationUrlState from '../../hooks/useEnumerationUrlState.js';
import Icon from '../../components/Icon.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import ForumIcon from '../../components/forums/ForumIcon.jsx';
import FollowButton from '../../components/forums/FollowButton.jsx';
import ThreadRow from '../../components/forums/ThreadRow.jsx';
import { errorText } from '../../utils/engagement.js';
import { appForumPath } from '../../utils/forums.js';

const t = strings.forums;
const f = t.following;
const TABS = ['Thread', 'Forum'];

/**
 * /app/forums/following (PHASE3_CONTRACT.md section 9.2): the threads and forums the member
 * follows (`POST /me/follows/enumerate` with `filters.targetType`), with label and tag filters,
 * "Load more" paging, and unfollow. The tab lives in the URL (`?type=Forum`).
 */
function ForumsFollowingView() {
  const { client } = useAuth();
  useDocumentMeta(f.meta, f.summary);
  const [searchParams, setSearchParams] = useSearchParams();
  const type = TABS.includes(searchParams.get('type')) ? searchParams.get('type') : 'Thread';
  const [meta, setMeta] = useEnumerationUrlState();
  const fetchPage = useCallback((cursor, signal) => client.enumerateMyFollows({ ...meta, ordering: undefined, continuationToken: cursor, maxResults: 20, filters: { targetType: type } }, signal), [client, meta, type]);
  const list = useCursorList(fetchPage);
  const select = (value) => {
    const next = new URLSearchParams(searchParams);
    if (value === 'Thread') next.delete('type');
    else next.set('type', value);
    setSearchParams(next);
  };
  const drop = (row) => list.update((items) => items.filter((x) => x.id !== row.id));

  return (
    <div className="member-view forums-following">
      <Link to="/app/forums" className="back-link small"><Icon name="chevronLeft" size={14} /><span>{t.backToForums}</span></Link>
      <header className="page-header">
        <div className="page-header-text">
          <h1>{f.title}</h1>
          <p className="muted">{f.summary}</p>
        </div>
      </header>
      <div className="segmented tab-bar" role="tablist" aria-label={f.title}>
        {TABS.map((value) => (
          <button key={value} type="button" role="tab" aria-selected={type === value} className={'segment' + (type === value ? ' active' : '')} onClick={() => select(value)}>{f.tabs[value]}</button>
        ))}
      </div>
      <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType={type === 'Forum' ? 'ForumFollow' : 'ThreadFollow'} />
      <ErrorBanner message={list.error ? f.loadError + ' ' + errorText(list.error, {}, '') : ''} onRetry={list.refresh} />
      {!list.loaded && list.loading && <Loading />}
      {list.loaded && !list.items.length && !list.loading && !list.error && <EmptyState icon="bell" title={f.empty[type]} />}
      <div className="thread-list" role="tabpanel">
        {list.items.map((row) => (row.targetType === 'Thread' && row.target ? (
          <div key={row.id} className="follow-row">
            <ThreadRow thread={row.target} showForum headingLevel={2} />
            <FollowButton followed label={t.followThreadLabel} unfollowLabel={t.unfollowThreadLabel} compact onToggle={async () => { await client.unfollowThread(row.target.id); drop(row); }} />
          </div>
        ) : row.targetType === 'Forum' && row.target ? (
          <div key={row.id} className="follow-row forum-card">
            <ForumIcon forum={row.target} />
            <h2 className="forum-card-title"><Link to={appForumPath(row.target)}>{row.target.name}</Link></h2>
            <FollowButton followed label={t.followForumLabel(row.target.name)} unfollowLabel={t.unfollowForumLabel(row.target.name)} onToggle={async () => { await client.unfollowForum(row.target.id); drop(row); }} />
          </div>
        ) : null))}
      </div>
      {list.loaded && list.loading && <Loading inline />}
      {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary feed-more" onClick={list.loadMore}>{t.loadMore}</button>}
    </div>
  );
}

export default ForumsFollowingView;
