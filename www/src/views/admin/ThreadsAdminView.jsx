import { useCallback, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { FeedbackStatuses, ThreadTypes, moderationActionsFor, pollDisplay, threadActions } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useForumList from '../../hooks/useForumList.js';
import useLocalStorage from '../../hooks/useLocalStorage.js';
import useTableState from '../../hooks/useTableState.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, ADMIN_THREAD_DEFAULT_SORT, ADMIN_THREAD_FILTERS, buildAdminThreadsQuery } from '../../utils/adminQuery.js';
import { optionsFrom } from '../../utils/labels.js';
import { formatDateTime, formatNumber, formatShortDateTime } from '../../utils/format.js';
import { authorName, errorText } from '../../utils/engagement.js';
import { appThreadPath, forumErrors } from '../../utils/forums.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import MetadataPanel from '../../components/MetadataPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import Modal from '../../components/Modal.jsx';
import Icon from '../../components/Icon.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import MediaGallery from '../../components/feed/MediaGallery.jsx';
import AuthorLine from '../../components/feed/AuthorLine.jsx';
import ThreadBadges from '../../components/forums/ThreadBadges.jsx';
import ThreadComposer from '../../components/forums/ThreadComposer.jsx';
import { LockDialog, MoveDialog, PromoteDialog } from '../../components/forums/ThreadDialogs.jsx';
import ModerationActionDialog from './ModerationActionDialog.jsx';

const s = strings.admin.threadsAdmin;
const c = strings.admin.common;
const col = s.columns;
const fo = strings.forums;
const DEFAULT_HIDDEN = ['id', 'score', 'reactions'];

function listOf(value) {
  if (!value) return [];
  if (Array.isArray(value)) return value;
  return Array.isArray(value.objects) ? value.objects : [];
}

/** Detail drawer (section 12): the thread in any state, poll totals, highlight, revisions, reports, and moderation history. */
function ThreadDetail({ id, onClose }) {
  const { api, client } = useAdminAuth();
  const fetcher = useCallback((signal) => api.getModerationTarget('Thread', id, signal), [api, id]);
  const query = useApiQuery(fetcher);
  const [jsonOpen, setJsonOpen] = useState(false);
  const detail = query.data;
  const thread = detail ? detail.target || detail : null;
  const poll = thread && thread.poll ? pollDisplay(thread.poll) : null;
  return (
    <Modal open variant="drawer" size="lg" onClose={onClose} title={thread ? thread.title : s.detailTitle} subtitle={<CopyableId value={id} label={col.id} />}
      headerExtra={thread && <button type="button" className="button button-secondary button-sm" onClick={() => setJsonOpen(true)}><Icon name="code" size={14} /><span>{c.viewJson}</span></button>}
    >
      {query.loading && !thread && <Loading />}
      {query.error && <ErrorBanner message={errorText(query.error)} onRetry={query.reload} />}
      {thread && (
        <div className="detail-stack">
          <section className="detail-block">
            <ThreadBadges thread={thread} />
            <AuthorLine user={thread.author} time={thread.publishedUtc || thread.createdUtc} linkProfile={false} />
            <MarkdownBody body={thread.body} mentions={thread.mentions} linkMentions={false} />
            <MediaGallery items={thread.attachments} />
            <a className="small" href={appThreadPath(thread.id)} target="_blank" rel="noreferrer">{s.openInApp}</a>
          </section>
          {poll && (
            <section className="detail-block">
              <h3>{s.pollTitle}</h3>
              <p className="small">{fo.poll.totalVoters(poll.totalVoters || 0)}</p>
              <ul className="link-list small">
                {poll.options.map((o) => <li key={o.id}>{fo.poll.optionResult(o.label, o.voteCount || 0, o.percent === null ? 0 : o.percent)}</li>)}
              </ul>
            </section>
          )}
          {(thread.acceptedAnswer || (thread.feedback && thread.feedback.reviewCommentId)) && (
            <section className="detail-block">
              <h3>{s.highlightTitle}</h3>
              {thread.acceptedAnswer ? <MarkdownBody body={thread.acceptedAnswer.body} linkMentions={false} /> : <a className="small" href={appThreadPath(thread.id, thread.feedback.reviewCommentId)} target="_blank" rel="noreferrer">{fo.review}</a>}
            </section>
          )}
          <MetadataPanel client={client} targetType="Thread" targetId={thread.id} isStaff initial={thread.labels ? thread : null} />
          <section className="detail-block">
            <h3>{s.revisionsTitle}</h3>
            {listOf(detail.revisions).length === 0 ? <p className="muted small">{s.revisionsEmpty}</p> : (
              <ol className="history-list">
                {listOf(detail.revisions).map((rev) => (
                  <li key={rev.id} className="history-item small"><strong>{strings.feed.historyRevision(rev.revision)}</strong> {formatDateTime(rev.createdUtc)} {rev.editedByRole === 'Moderator' ? '(' + fo.historyByModerator + ')' : ''}<div>{rev.title}</div></li>
                ))}
              </ol>
            )}
          </section>
          <section className="detail-block">
            <h3>{s.reportsTitle}</h3>
            {listOf(detail.reports).length === 0 ? <p className="muted small">{s.reportsEmpty}</p> : (
              <ul className="history-list">
                {listOf(detail.reports).map((r) => <li key={r.id} className="history-item small"><strong>{(strings.safety.reasons[r.reason] || { label: r.reason }).label}</strong> {r.reporter ? authorName(r.reporter) : ''} <span className="muted">{formatDateTime(r.createdUtc)}</span>{r.details && <div>{r.details}</div>}</li>)}
              </ul>
            )}
          </section>
          <section className="detail-block">
            <h3>{s.actionsTitle}</h3>
            {listOf(detail.actions).length === 0 ? <p className="muted small">{s.actionsEmpty}</p> : (
              <ul className="history-list">
                {listOf(detail.actions).map((a) => <li key={a.id} className="history-item small"><strong>{strings.admin.moderation.actionOptions[a.action] || a.action}</strong> {a.moderator ? authorName(a.moderator) : strings.admin.moderation.system} <span className="muted">{formatDateTime(a.createdUtc)}</span><div>{a.reason}</div></li>)}
              </ul>
            )}
          </section>
        </div>
      )}
      <JsonViewerModal open={jsonOpen} onClose={() => setJsonOpen(false)} subtitle={id} value={detail} />
    </Modal>
  );
}

/**
 * /admin/threads (PHASE3_CONTRACT.md section 12): threads across forums in every state the caller
 * may see (including Deleted), with forum, type, state, author, pinned, locked, promoted, review
 * status, time window, search, label, and tag filters; row actions View, View JSON, Pin or Unpin,
 * Lock or Unlock, Move, Promote or Unpromote, Hide, Remove, Restore, Edit metadata; and the detail
 * drawer. Moderators see their scope only (the server filters).
 */
function ThreadsAdminView() {
  const { api, client, isSystemAdmin } = useAdminAuth();
  const toast = useToast();
  const [searchParams, setSearchParams] = useSearchParams();
  const table = useTableState({ storageKey: 'tk.admin.threads', filterKeys: ADMIN_THREAD_FILTERS, defaultSort: ADMIN_THREAD_DEFAULT_SORT });
  const [hidden, setHidden] = useLocalStorage('tk.admin.threads.hiddenColumns', DEFAULT_HIDDEN);
  const [dialog, setDialog] = useState(null);
  const [jsonRow, setJsonRow] = useState(null);
  const forums = useForumList(client, null, { admin: isSystemAdmin });
  const detailId = searchParams.get('thread');

  const query = useMemo(() => buildAdminThreadsQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.sort, table.meta]);
  const fetcher = useCallback((signal) => client.adminEnumerateThreads(query, signal), [client, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const rows = page ? page.items : [];

  const openDetail = (id) => {
    const next = new URLSearchParams(searchParams);
    if (id) next.set('thread', id);
    else next.delete('thread');
    setSearchParams(next);
  };
  const done = (message) => {
    setDialog(null);
    toast.success(message);
    list.reload();
  };
  const run = async (fn, message) => {
    try {
      await fn();
      done(message);
    } catch (err) {
      toast.error(errorText(err, forumErrors()));
    }
  };

  const menu = (row) => {
    const items = [
      { label: c.view, icon: 'eye', onSelect: () => openDetail(row.id) },
      { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) }
    ];
    if (row.state === 'Deleted') return items;
    const actions = threadActions({ ...row, viewer: { canModerate: true, canPin: true, canLock: true, canMove: true, canPromote: true, ...(row.viewer || {}) } });
    if (actions.includes('pin')) items.push({ label: fo.pin, icon: 'thumbtack', onSelect: () => run(() => client.pinThread(row.id), fo.pinnedToast) });
    if (actions.includes('unpin')) items.push({ label: fo.unpin, icon: 'thumbtack', onSelect: () => run(() => client.unpinThread(row.id), fo.unpinnedToast) });
    if (actions.includes('lock')) items.push({ label: fo.lock, icon: 'lock', onSelect: () => setDialog({ kind: 'lock', row }) });
    if (actions.includes('unlock')) items.push({ label: fo.unlock, icon: 'lock', onSelect: () => run(() => client.unlockThread(row.id), fo.unlockedToast) });
    items.push({ label: fo.move, icon: 'forum', onSelect: () => setDialog({ kind: 'move', row }) });
    if (row.kind !== 'LinkedFeedPost') items.push(row.promotion
      ? { label: fo.unpromote, icon: 'feed', onSelect: () => run(() => client.unpromoteThread(row.id), fo.unpromotedToast) }
      : { label: fo.promote, icon: 'feed', onSelect: () => setDialog({ kind: 'promote', row }) });
    for (const action of moderationActionsFor(row.state)) items.push({ label: strings.admin.moderation.act[action], icon: action === 'Restore' || action === 'Unhide' ? 'refresh' : 'ban', danger: action === 'Remove' || action === 'Hide', onSelect: () => setDialog({ kind: 'moderate', action, row }) });
    items.push({ label: fo.editMetadata, icon: 'edit', onSelect: () => setDialog({ kind: 'metadata', row }) });
    return items;
  };

  const columns = [
    { key: 'thread', label: col.thread, pinned: true, render: (row) => (
      <span className="cell-stack">
        <strong className="truncate">{row.title}</strong>
        <ThreadBadges thread={row} showType={false} />
      </span>
    ) },
    { key: 'forum', label: col.forum, render: (row) => (row.forum ? row.forum.name : '-') },
    { key: 'type', label: col.type, render: (row) => fo.types[row.type] || row.type },
    { key: 'author', label: col.author, render: (row) => authorName(row.author) },
    { key: 'state', label: col.state, render: (row) => s.stateOptions[row.state] || row.state },
    { key: 'activity', label: col.activity, sortField: 'lastActivityUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => (row.lastActivityUtc ? <span title={formatDateTime(row.lastActivityUtc)}>{formatShortDateTime(row.lastActivityUtc)}</span> : '-') },
    { key: 'comments', label: col.comments, className: 'num', render: (row) => formatNumber(row.commentCount || 0) },
    { key: 'reactions', label: col.reactions, className: 'num', render: (row) => formatNumber(row.reactionTotal || 0) },
    { key: 'score', label: col.score, className: 'num', render: (row) => (Number.isFinite(row.trendingScore) ? row.trendingScore.toFixed(2) : '-') },
    { key: 'id', label: col.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={col.id} /> },
    { key: 'actions', pinned: true, selectorLabel: col.actions, className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>, render: (row) => <ActionMenu label={c.rowActions(row.title)} items={menu(row)} /> }
  ];
  const visibleColumns = columns.filter((x) => x.pinned || !hidden.includes(x.key));

  const filterFields = [
    { key: 'search', label: s.searchLabel, placeholder: s.searchPlaceholder, grow: true },
    { key: 'forumId', label: s.forum, type: 'select', options: forums.forums.map((f) => ({ value: f.id, label: f.name })) },
    { key: 'type', label: s.type, type: 'select', options: ThreadTypes.map((t) => ({ value: t, label: fo.types[t] })) },
    { key: 'state', label: s.state, type: 'select', options: optionsFrom(s.stateOptions) },
    { key: 'feedbackStatus', label: s.feedbackStatus, type: 'select', options: FeedbackStatuses.map((x) => ({ value: x, label: fo.feedbackStatus[x] })) },
    { key: 'authorUserId', label: s.author, placeholder: s.authorPlaceholder, mono: true },
    { key: 'fromUtc', label: s.from, type: 'datetime' },
    { key: 'toUtc', label: s.to, type: 'datetime' },
    { key: 'pinned', label: s.pinned, type: 'checkbox' },
    { key: 'locked', label: s.locked, type: 'checkbox' },
    { key: 'promoted', label: s.promoted, type: 'checkbox' }
  ];

  return (
    <div className="view">
      <PageHeader title={strings.admin.routes.threads.label} summary={strings.admin.routes.threads.summary} />
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={client} targetType="Thread" />
      </section>
      <section className="table-section">
        <div className="table-toolbar">
          <div className="toolbar-left" />
        </div>
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading} pageSizes={ADMIN_PAGE_SIZES} />
        <DataTable
          caption={strings.admin.routes.threads.label}
          columns={visibleColumns}
          columnChoices={columns.map((x) => ({ key: x.key, label: x.selectorLabel || x.label, pinned: x.pinned }))}
          columnsHidden={hidden}
          onColumnsChange={setHidden}
          rows={rows}
          rowKey={(row) => row.id}
          loading={list.loading}
          error={list.error ? c.unavailable(strings.admin.routes.threads.label) + ' ' + errorText(list.error, {}, '') : null}
          onRetry={list.reload}
          emptyMessage={table.hasFilters ? s.emptyFiltered : s.empty}
          onRowClick={(row) => openDetail(row.id)}
          sort={table.sort}
          onSortChange={table.setSort}
        />
      </section>
      {detailId && <ThreadDetail key={detailId} id={detailId} onClose={() => openDetail(null)} />}
      {dialog && dialog.kind === 'lock' && <LockDialog thread={dialog.row} onClose={() => setDialog(null)} onDone={() => done(fo.lockedToast)} />}
      {dialog && dialog.kind === 'move' && <MoveDialog thread={dialog.row} onClose={() => setDialog(null)} onDone={(next) => done(fo.movedToast(next && next.forum ? next.forum.name : ''))} />}
      {dialog && dialog.kind === 'promote' && <PromoteDialog thread={dialog.row} isAdmin={isSystemAdmin} onClose={() => setDialog(null)} onDone={() => done(fo.promotedToast)} />}
      {dialog && dialog.kind === 'moderate' && (
        <ModerationActionDialog action={dialog.action} targetType="Thread" targetId={dialog.row.id} api={api} onClose={() => setDialog(null)} onDone={() => done(strings.admin.moderation.actDone(strings.admin.moderation.act[dialog.action]))} />
      )}
      {dialog && dialog.kind === 'metadata' && (
        <Modal open variant="drawer" size="lg" onClose={() => setDialog(null)} title={fo.editMetadata}>
          <ThreadComposer client={client} thread={dialog.row} mode="metadata" perms={{ isAdmin: isSystemAdmin, isStaff: true }} onSaved={() => done(fo.composer.saved)} onCancel={() => setDialog(null)} />
        </Modal>
      )}
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.id} value={jsonRow} />
    </div>
  );
}

export default ThreadsAdminView;
