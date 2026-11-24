import { useCallback, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useAuth } from '../../context/AuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useLocalStorage from '../../hooks/useLocalStorage.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, FEED_DEFAULT_SORT, FEED_FILTERS, buildFeedQuery } from '../../utils/adminQuery.js';
import { optionsFrom } from '../../utils/labels.js';
import { formatDateTime, formatShortDateTime, formatNumber } from '../../utils/format.js';
import { authorName, errorText, postPath, postingPermissions } from '../../utils/engagement.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import BulkMetadataDialog from '../../components/BulkMetadataDialog.jsx';
import BulkBar from '../../components/BulkBar.jsx';
import useRowSelection from '../../hooks/useRowSelection.jsx';
import MetadataPanel from '../../components/MetadataPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import Modal from '../../components/Modal.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, ErrorBanner, Loading } from '../../components/States.jsx';
import PostComposer from '../../components/feed/PostComposer.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import MediaGallery from '../../components/feed/MediaGallery.jsx';
import AuthorLine from '../../components/feed/AuthorLine.jsx';

const s = strings.admin.feedAdmin;
const c = strings.admin.common;
const col = s.columns;
const STATE_TONES = { Scheduled: 'info', Processing: 'warning', Published: 'success', Hidden: 'warning', Removed: 'danger', Deleted: 'neutral' };
const DEFAULT_HIDDEN = ['id', 'created', 'reactions'];

/** Detail drawer: the post in any state, revisions, moderation history, reports, and metadata. */
function FeedPostDetail({ id, onClose, onEdit }) {
  const { api, client } = useAdminAuth();
  const fetcher = useCallback((signal) => api.getAdminFeedPost(id, signal), [api, id]);
  const query = useApiQuery(fetcher);
  const [jsonOpen, setJsonOpen] = useState(false);
  const post = query.data;
  const revisions = post ? (post.revisions && post.revisions.objects) || post.revisions || [] : [];
  const actions = post ? (post.moderationActions && post.moderationActions.objects) || post.moderationActions || [] : [];
  return (
    <Modal open variant="drawer" size="lg" onClose={onClose} title={post ? post.title || s.untitled : s.detailTitle} subtitle={<CopyableId value={id} label={col.id} />}
      headerExtra={post && (
        <>
          <button type="button" className="button button-secondary button-sm" onClick={() => onEdit(post)}><Icon name="edit" size={14} /><span>{c.edit}</span></button>
          <button type="button" className="button button-secondary button-sm" onClick={() => setJsonOpen(true)}><Icon name="code" size={14} /><span>{c.viewJson}</span></button>
        </>
      )}
    >
      {query.loading && !post && <Loading />}
      {query.error && <ErrorBanner message={errorMessage(query.error, '')} onRetry={query.reload} />}
      {post && (
        <div className="detail-stack">
          <section className="detail-block">
            <div className="badge-row">
              <Badge tone={STATE_TONES[post.deletedUtc ? 'Deleted' : post.state] || 'neutral'}>{s.stateOptions[post.deletedUtc ? 'Deleted' : post.state] || post.state}</Badge>
              {post.isPinned && <Badge tone="info">{s.pinnedBadge}</Badge>}
              <Badge tone={post.visibility === 'Public' ? 'success' : 'neutral'}>{post.visibility === 'Public' ? s.publicBadge : s.membersBadge}</Badge>
              {post.reportSummary && <Badge tone={post.reportSummary.open ? 'warning' : 'neutral'}>{s.reportsSummary(post.reportSummary.open, post.reportSummary.total)}</Badge>}
            </div>
            <AuthorLine user={post.author} time={post.publishedUtc || post.createdUtc} linkProfile={false} />
            <MarkdownBody body={post.body} mentions={post.mentions} linkMentions={false} />
            <MediaGallery items={post.attachments} />
            <a className="small" href={postPath(post.id)} target="_blank" rel="noreferrer">{s.openInApp}</a>
          </section>
          <MetadataPanel client={client} targetType="FeedPost" targetId={post.id} isStaff initial={post.labels ? post : null} />
          <section className="detail-block">
            <h3>{s.revisionsTitle}</h3>
            {revisions.length === 0 ? <p className="muted small">{s.revisionsEmpty}</p> : (
              <ol className="history-list">
                {revisions.map((rev) => (
                  <li key={rev.id} className="history-item">
                    <div className="history-head"><strong>{strings.feed.historyRevision(rev.revision)}</strong><span className="muted small">{formatDateTime(rev.createdUtc)}</span>{rev.editedByName && <span className="muted small">{strings.feed.historyEditedBy(rev.editedByName)}</span>}</div>
                    {rev.title && <strong>{rev.title}</strong>}
                    <MarkdownBody body={rev.body} linkMentions={false} />
                    {rev.attachmentSummary && <p className="muted small">{rev.attachmentSummary}</p>}
                  </li>
                ))}
              </ol>
            )}
          </section>
          <section className="detail-block">
            <h3>{s.moderationTitle}</h3>
            {actions.length === 0 ? <p className="muted small">{s.moderationEmpty}</p> : (
              <ul className="history-list">
                {actions.map((a) => (
                  <li key={a.id} className="history-item small">
                    <strong>{strings.admin.moderation.actionOptions[a.action] || a.action}</strong> {a.moderator ? authorName(a.moderator) : strings.admin.moderation.system} <span className="muted">{formatDateTime(a.createdUtc)}</span>
                    <div>{a.reason}{a.note ? ' (' + a.note + ')' : ''}</div>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>
      )}
      <JsonViewerModal open={jsonOpen} onClose={() => setJsonOpen(false)} subtitle={id} value={post} />
    </Modal>
  );
}

/** Create or edit drawer using the shared composer (admin mode: author, public sample, pin). */
function FeedPostEditor({ post, initial, onClose, onSaved }) {
  const { api, mode, client } = useAdminAuth();
  const auth = useAuth();
  const perms = { ...postingPermissions(auth.me), isAdmin: true, isStaff: true, canAttachMedia: true, canAttachVideo: true, muted: false, postingBlocked: false };
  const fetcher = useCallback((signal) => (post ? api.getAdminFeedPost(post.id, signal) : Promise.resolve(null)), [api, post]);
  const query = useApiQuery(fetcher, { enabled: Boolean(post) });
  const full = post ? query.data : null;
  return (
    <Modal open variant="drawer" size="lg" onClose={onClose} title={post ? strings.composer.editTitle : s.create}>
      {mode === 'token' && <Banner tone="warning" icon="lock">{s.tokenNote}</Banner>}
      {post && query.loading && <Loading />}
      {post && query.error && <ErrorBanner message={errorMessage(query.error, '')} onRetry={query.reload} />}
      {(!post || full) && mode !== 'token' && (
        <PostComposer
          client={client}
          perms={perms}
          admin
          showAuthorField
          post={full}
          initial={initial || {}}
          onSubmit={(body) => (full ? api.updateAdminFeedPost(full.id, body) : api.createAdminFeedPost(body))}
          onSaved={onSaved}
          onCancel={onClose}
        />
      )}
    </Modal>
  );
}

/**
 * /admin/feed (contract section 10.1): posts in every state with filters, label and tag filters,
 * sortable columns, row actions (View, Edit, View JSON, Pin or Unpin, Publish now, Duplicate,
 * Delete), a create and edit drawer with the shared composer, and a detail drawer with revisions
 * and moderation history.
 */
function FeedAdminView() {
  const { api, client } = useAdminAuth();
  const toast = useToast();
  const [searchParams, setSearchParams] = useSearchParams();
  const table = useTableState({ storageKey: 'tk.admin.feed', filterKeys: FEED_FILTERS, defaultSort: FEED_DEFAULT_SORT });
  const [hidden, setHidden] = useLocalStorage('tk.admin.feed.hiddenColumns', DEFAULT_HIDDEN);
  const [editor, setEditor] = useState(null);
  const [jsonRow, setJsonRow] = useState(null);
  const [confirm, setConfirm] = useState(null);
  const [busy, setBusy] = useState(false);
  const detailId = searchParams.get('post');

  const query = useMemo(() => buildFeedQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.sort, table.meta]);
  const fetcher = useCallback((signal) => api.listAdminFeed(query, signal), [api, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const rows = page ? page.items : [];
  const selection = useRowSelection(rows, JSON.stringify(query));
  const [metaIds, setMetaIds] = useState(null);

  const openDetail = (id) => {
    const next = new URLSearchParams(searchParams);
    if (id) next.set('post', id);
    else next.delete('post');
    setSearchParams(next);
  };

  const run = async (fn, message) => {
    setBusy(true);
    try {
      await fn();
      toast.success(message);
      list.reload();
    } catch (err) {
      toast.error(errorText(err, strings.composer.errors));
    } finally {
      setBusy(false);
      setConfirm(null);
    }
  };

  const columns = [
    selection.column((row) => row.title || s.untitled),
    {
      key: 'post', label: col.post, pinned: true, render: (row) => (
        <span className="cell-stack">
          <strong className="truncate">{row.title || s.untitled}</strong>
          <span className="muted small line-clamp">{row.excerpt}</span>
        </span>
      )
    },
    { key: 'author', label: col.author, render: (row) => authorName(row.author) },
    {
      key: 'state', label: col.state, render: (row) => (
        <span className="badge-row">
          <Badge tone={STATE_TONES[row.state] || 'neutral'}>{s.stateOptions[row.state] || row.state}</Badge>
          {row.isPinned && <Badge tone="info">{s.pinnedBadge}</Badge>}
        </span>
      )
    },
    { key: 'visibility', label: col.visibility, render: (row) => <Badge tone={row.visibility === 'Public' ? 'success' : 'neutral'}>{row.visibility === 'Public' ? s.publicBadge : s.membersBadge}</Badge> },
    { key: 'published', label: col.published, sortField: 'publishedUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => (row.publishedUtc ? <span title={formatDateTime(row.publishedUtc)}>{formatShortDateTime(row.publishedUtc)}</span> : <span className="muted">-</span>) },
    { key: 'scheduled', label: col.scheduled, sortField: 'publishAtUtc', className: 'nowrap', render: (row) => (row.publishAtUtc ? formatShortDateTime(row.publishAtUtc) : <span className="muted">-</span>) },
    { key: 'created', label: col.created, sortField: 'createdUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => formatShortDateTime(row.createdUtc) },
    { key: 'media', label: col.media, render: (row) => (row.attachmentCount ? <span>{row.attachmentCount}{row.hasVideo ? ' (' + s.videoBadge + ')' : ''}</span> : <span className="muted">0</span>) },
    { key: 'comments', label: col.comments, sortField: 'commentCount', sortDefault: 'desc', className: 'num', render: (row) => formatNumber(row.commentCount || 0) },
    { key: 'reactions', label: col.reactions, sortField: 'reactionTotal', sortDefault: 'desc', className: 'num', render: (row) => formatNumber(row.reactionTotal || 0) },
    { key: 'reports', label: col.reports, className: 'num', render: (row) => (row.openReportCount ? <Badge tone="warning">{row.openReportCount}</Badge> : <span className="muted">0</span>) },
    { key: 'id', label: col.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={col.id} /> },
    {
      key: 'actions', pinned: true, selectorLabel: col.actions, className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>,
      render: (row) => {
        const items = [
          { label: c.view, icon: 'eye', onSelect: () => openDetail(row.id) },
          { label: c.edit, icon: 'edit', onSelect: () => setEditor({ post: row }) },
          { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) }
        ];
        if (row.state === 'Published') items.push(row.isPinned
          ? { label: s.unpin, icon: 'thumbtack', onSelect: () => run(() => api.unpinAdminFeedPost(row.id), strings.feed.unpinnedToast) }
          : { label: s.pin, icon: 'thumbtack', onSelect: () => run(() => api.pinAdminFeedPost(row.id), strings.feed.pinnedToast) });
        if (row.state === 'Scheduled') items.push({ label: s.publishNow, icon: 'play', onSelect: () => run(() => api.publishFeedPostNow(row.id), s.published) });
        items.push({ label: c.duplicate, icon: 'copy', onSelect: () => setEditor({ initial: { title: s.duplicateOf(row.title), body: row.body || row.excerpt || '', visibility: row.visibility, commentsEnabled: row.commentsEnabled !== false } }) });
        if (row.state !== 'Deleted') items.push({ label: strings.common.delete, icon: 'trash', danger: true, onSelect: () => setConfirm(row) });
        return <ActionMenu label={c.rowActions(row.title || s.untitled)} items={items} />;
      }
    }
  ];
  const visibleColumns = columns.filter((x) => x.pinned || !hidden.includes(x.key));

  const filterFields = [
    { key: 'search', label: s.searchLabel, placeholder: s.searchPlaceholder, grow: true },
    { key: 'state', label: s.state, type: 'select', options: optionsFrom(s.stateOptions) },
    { key: 'authorUserId', label: s.author, placeholder: s.authorPlaceholder, mono: true },
    { key: 'fromUtc', label: s.from, type: 'datetime' },
    { key: 'toUtc', label: s.to, type: 'datetime' },
    { key: 'pinned', label: s.pinnedOnly, type: 'checkbox' }
  ];

  return (
    <div className="view">
      <PageHeader
        title={strings.admin.routes.feed.label}
        summary={strings.admin.routes.feed.summary}
        actions={<button type="button" className="button button-primary button-sm" onClick={() => setEditor({})}><Icon name="plus" size={14} /><span>{s.create}</span></button>}
      />
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={client} targetType="FeedPost" />
      </section>
      <section className="table-section">
        <div className="table-toolbar">
          <div className="toolbar-left"><BulkBar count={selection.selected.length} onClear={selection.clear} onMetadata={() => setMetaIds(selection.selected)} /></div>
        </div>
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading || busy} pageSizes={ADMIN_PAGE_SIZES} />
        <DataTable
          caption={strings.admin.routes.feed.label}
          columns={visibleColumns}
          columnChoices={columns.map((x) => ({ key: x.key, label: x.selectorLabel || x.label, pinned: x.pinned }))}
          columnsHidden={hidden}
          onColumnsChange={setHidden}
          rows={rows}
          rowKey={(row) => row.id}
          loading={list.loading}
          error={list.error ? c.unavailable(strings.admin.routes.feed.label) + ' ' + errorMessage(list.error, '') : null}
          onRetry={list.reload}
          emptyMessage={table.hasFilters ? s.emptyFiltered : s.empty}
          onRowClick={(row) => setEditor({ post: row })}
          sort={table.sort}
          onSortChange={table.setSort}
        />
      </section>
      {detailId && <FeedPostDetail key={detailId} id={detailId} onClose={() => openDetail(null)} onEdit={(post) => { openDetail(null); setEditor({ post }); }} />}
      {editor && (
        <FeedPostEditor
          post={editor.post || null}
          initial={editor.initial}
          onClose={() => setEditor(null)}
          onSaved={() => { toast.success(editor.post ? s.savedToast : s.createdToast); setEditor(null); list.reload(); }}
        />
      )}
      {metaIds && <BulkMetadataDialog open targetType="FeedPost" ids={metaIds} onClose={() => setMetaIds(null)} onDone={() => { selection.clear(); list.reload(); }} />}
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.id} value={jsonRow} />
      <ConfirmModal open={Boolean(confirm)} title={s.deleteTitle} message={s.deleteBody} confirmLabel={strings.common.delete} busy={busy} onCancel={() => setConfirm(null)} onConfirm={() => run(() => api.deleteAdminFeedPost(confirm.id), s.deleted)} />
    </div>
  );
}

export default FeedAdminView;
