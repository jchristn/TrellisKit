import { useCallback, useState } from 'react';
import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import useEnumerationUrlState from '../../hooks/useEnumerationUrlState.js';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import Avatar from '../../components/Avatar.jsx';
import Icon from '../../components/Icon.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import BlockDialog from '../../components/feed/BlockDialog.jsx';
import { authorName, mediaUrl, memberPath } from '../../utils/engagement.js';
import { formatShortDate } from '../../utils/format.js';

const s = strings.safety;

/** /app/settings/blocked: the members you blocked (cursor paged), each with Unblock. */
function BlockedMembersView() {
  const { client } = useAuth();
  useDocumentMeta(s.blockedMeta, s.blockedSummary);
  const [meta, setMeta] = useEnumerationUrlState();
  const fetchPage = useCallback((cursor, signal) => client.listBlocks({ ...meta, continuationToken: cursor, maxResults: 50 }, signal), [client, meta]);
  const list = useCursorList(fetchPage);
  const [unblocking, setUnblocking] = useState(null);
  return (
    <div className="member-view settings-view">
      <Link to="/app/profile" className="back-link small"><Icon name="chevronLeft" size={14} /><span>{strings.notificationSettings.back}</span></Link>
      <header className="page-header">
        <div className="page-header-text">
          <h1>{s.blockedTitle}</h1>
          <p className="page-summary">{s.blockedSummary}</p>
        </div>
      </header>
      <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType="User" showSearch orderings={[{ value: '', label: strings.metadata.orderings.CreatedDescending }, { value: 'CreatedAscending', label: strings.metadata.orderings.CreatedAscending }]} />
      <ErrorBanner message={list.error ? s.blockedError : ''} onRetry={list.refresh} />
      {!list.loaded && list.loading && <Loading />}
      {list.loaded && !list.items.length && !list.loading && !list.error && <EmptyState icon="ban" title={s.blockedEmpty} />}
      {list.items.length > 0 && (
        <ul className="blocked-list panel">
          {list.items.map((row) => (
            <li key={row.user.id || row.createdUtc} className="blocked-row">
              <Avatar src={row.user.isDeleted ? null : mediaUrl(row.user.avatarUrl)} name={authorName(row.user)} size={36} />
              <span className="blocked-text">
                {row.user.id && !row.user.isDeleted ? <Link to={memberPath(row.user.id)}>{authorName(row.user)}</Link> : <span>{authorName(row.user)}</span>}
                <span className="muted small">{row.user.handle ? '@' + row.user.handle + ' · ' : ''}{s.blockedSince(formatShortDate(row.createdUtc))}</span>
              </span>
              <button type="button" className="button button-secondary button-sm" onClick={() => setUnblocking(row.user)}>{s.unblock}</button>
            </li>
          ))}
        </ul>
      )}
      {list.loaded && list.loading && <Loading inline />}
      {list.loaded && !list.done && !list.loading && <button type="button" className="button button-secondary" onClick={list.loadMore}>{s.blockedLoadMore}</button>}
      {unblocking && (
        <BlockDialog user={unblocking} unblock onClose={() => setUnblocking(null)} onDone={(user) => list.update((items) => items.filter((x) => x.user.id !== user.id))} />
      )}
    </div>
  );
}

export default BlockedMembersView;
