import { useCallback } from 'react';
import { Link, useParams } from 'react-router-dom';
import { formatRelativeTime } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import useEnumerationUrlState, { activeMetaCount } from '../../hooks/useEnumerationUrlState.js';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import Icon from '../../components/Icon.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import { formatDateTime } from '../../utils/format.js';

const s = strings.announcements;

/** The route of an announcement (notifications use `/announcements/{id}`). */
export function announcementPath(id) {
  return '/app/announcements/' + encodeURIComponent(id);
}

/** /app/announcements: the broadcasts the member received, newest first, with label and tag filters and load more. */
function AnnouncementsView() {
  const { client } = useAuth();
  const { fill } = useProperty();
  useDocumentMeta(s.meta, s.summary);
  const [meta, setMeta, metaKey] = useEnumerationUrlState();
  const fetchPage = useCallback((cursor, signal) => {
    const parsed = JSON.parse(metaKey);
    return client.enumerateAnnouncements({ search: parsed.search, labels: parsed.labels, labelMatch: parsed.labelMatch, tags: parsed.tags, maxResults: 20, continuationToken: cursor }, signal);
  }, [client, metaKey]);
  const list = useCursorList(fetchPage);
  return (
    <div className="member-view">
      <header className="page-header">
        <div className="page-header-text">
          <h1>{s.title}</h1>
          <p className="page-summary">{fill(s.summary)}</p>
        </div>
      </header>
      <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType="Announcement" showSearch />
      <ErrorBanner message={list.error ? s.loadError : ''} onRetry={list.refresh} />
      {!list.loaded && list.loading && <Loading />}
      {list.loaded && !list.items.length && !list.loading && !list.error && <EmptyState icon="megaphone" title={activeMetaCount(meta) ? s.emptyFiltered : s.empty} />}
      {list.items.length > 0 && (
        <ul className="announcement-list panel" aria-label={s.listLabel}>
          {list.items.map((a) => (
            <li key={a.id} className="announcement-row">
              <Link to={announcementPath(a.id)}>{a.title}</Link>
              {a.sentUtc && <time className="muted small" dateTime={a.sentUtc} title={formatDateTime(a.sentUtc)}>{s.sent(formatRelativeTime(a.sentUtc))}</time>}
            </li>
          ))}
        </ul>
      )}
      {list.loaded && !list.done && !list.loading && <button type="button" className="button button-secondary" onClick={list.loadMore}>{s.loadMore}</button>}
    </div>
  );
}

/** /app/announcements/:id: one received announcement. */
export function AnnouncementView() {
  const { id } = useParams();
  const { client } = useAuth();
  const fetcher = useCallback((signal) => client.getAnnouncement(id, signal), [client, id]);
  const query = useApiQuery(fetcher);
  const a = query.data;
  useDocumentMeta(a ? a.title + ' | {name}' : s.meta);
  if (query.error && query.error.status === 404) {
    return <div className="member-view"><EmptyState icon="megaphone" title={s.notFound} action={<Link to="/app/announcements" className="button button-secondary">{s.back}</Link>} /></div>;
  }
  return (
    <div className="member-view">
      <Link to="/app/announcements" className="back-link small"><Icon name="chevronLeft" size={14} /><span>{s.back}</span></Link>
      {query.loading && !a && <Loading />}
      <ErrorBanner message={query.error && query.error.status !== 404 ? s.loadError : ''} onRetry={query.reload} />
      {a && (
        <article className="panel announcement">
          <h1>{a.title}</h1>
          {a.sentUtc && <p className="muted small">{s.sent(formatDateTime(a.sentUtc))}</p>}
          <MarkdownBody body={a.body || ''} />
        </article>
      )}
    </div>
  );
}

export default AnnouncementsView;
