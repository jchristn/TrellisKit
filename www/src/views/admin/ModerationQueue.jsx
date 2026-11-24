import { useCallback, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { moderationActionsFor, normalizeEnumerationResult } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, QUEUE_DEFAULT_SORT, QUEUE_FILTERS, buildQueueQuery } from '../../utils/adminQuery.js';
import { optionsFrom } from '../../utils/labels.js';
import { formatDateTime, formatShortDateTime } from '../../utils/format.js';
import { authorName, postPath } from '../../utils/engagement.js';
import { formatTimeUntil } from '@trelliskit/shared';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import MetadataPanel from '../../components/MetadataPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import Modal from '../../components/Modal.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import MediaGallery from '../../components/feed/MediaGallery.jsx';
import AuthorLine from '../../components/feed/AuthorLine.jsx';
import ModerationActionDialog from './ModerationActionDialog.jsx';
import AssistantSnapshotPanel from './AssistantSnapshotPanel.jsx';
import MessageSnapshotPanel from './MessageSnapshotPanel.jsx';
import TableColumns from '../../components/TableColumns.jsx';

const s = strings.admin.moderation;
const c = strings.admin.common;
const col = s.columns;
const REVIEW_TARGET_HOURS = 12;

/** The reason most reports give (ties go to the earliest report in the list), used as the suggested action reason. */
export function mostCommonReason(reports) {
  const counts = new Map();
  for (const report of reports) if (report && report.reason) counts.set(report.reason, (counts.get(report.reason) || 0) + 1);
  let best = null;
  for (const [reason, count] of counts) if (best === null || count > counts.get(best)) best = reason;
  return best;
}

/** Whether a queue row is overdue (server flag, or past `dueUtc`). */
export function isOverdue(row, now = new Date()) {
  if (typeof row.isOverdue === 'boolean') return row.isOverdue;
  return Boolean(row.status === 'Open' && row.dueUtc && new Date(row.dueUtc).getTime() < now.getTime());
}

function reasonText(code) {
  return (strings.safety.reasons[code] && strings.safety.reasons[code].label) || s.reasons[code] || code;
}

function listOf(value) {
  if (!value) return [];
  return Array.isArray(value) ? value : normalizeEnumerationResult(value).objects;
}

function StateBadge({ state }) {
  const tone = state === 'Removed' ? 'danger' : state === 'Hidden' ? 'warning' : 'neutral';
  return <Badge tone={tone}>{s.targetStates[state] || state}</Badge>;
}

/**
 * The moderation state of a queue detail target. Feed posts, comments, and threads carry `state`;
 * provider objects (venues, instructors, events, lessons) carry `moderationState` (`None`, `Hidden`,
 * `Removed`) beside their own lifecycle `status`, so a moderation state wins over the lifecycle.
 */
export function targetStateOf(target) {
  if (!target) return '';
  if (target.moderationState === 'Hidden' || target.moderationState === 'Removed') return target.moderationState;
  if (target.state) return target.state;
  if (target.reportedMessage && target.reportedMessage.status) return target.reportedMessage.status;
  if (target.isDeleted) return 'Deleted';
  return target.moderationState === 'None' ? 'Visible' : 'Active';
}

/** Report detail: full content in any state, reports, actions, author history, and the action buttons. */
function QueueDetail({ targetType, targetId, onClose, onChanged }) {
  const { api, client } = useAdminAuth();
  const toast = useToast();
  const fetcher = useCallback((signal) => api.getModerationTarget(targetType, targetId, signal), [api, targetType, targetId]);
  const query = useApiQuery(fetcher);
  const [dialog, setDialog] = useState(null);
  const [jsonOpen, setJsonOpen] = useState(false);
  const data = query.data;
  const target = data && data.target;
  const reports = listOf(data && data.reports);
  const actions = listOf(data && data.actions);
  const history = (data && data.authorHistory) || null;
  const state = targetStateOf(target);
  const what = (s.targetTypes[targetType] || targetType).toLowerCase();
  const topReason = reports.length ? reasonText(mostCommonReason(reports)) : '';
  const context = (data && data.context) || {};
  const postId = targetType === 'FeedPost' ? targetId : context.feedPostId;
  // Assistant replies (scope Assistant, SystemAdmin only): the target is the conversation snapshot;
  // only Dismiss applies and there is no member author.
  const assistantReply = targetType === 'AssistantMessage';
  // Direct messages (scope Messaging): only the frozen report snapshot, never a link into the conversation.
  const directMessage = targetType === 'Message' || targetType === 'Conversation';

  return (
    <Modal open variant="drawer" size="lg" onClose={onClose} title={s.detailTitle(what)} subtitle={<code className="mono">{targetId}</code>}
      headerExtra={data && <button type="button" className="button button-secondary button-sm" onClick={() => setJsonOpen(true)}><Icon name="code" size={14} /><span>{c.viewJson}</span></button>}
    >
      {query.loading && !data && <Loading />}
      {query.error && <ErrorBanner message={errorMessage(query.error, '')} onRetry={query.reload} />}
      {data && (
        <div className="detail-stack">
          <section className="detail-block" aria-labelledby="mod-content-title">
            <div className="row-between">
              <h3 id="mod-content-title">{s.contentTitle}</h3>
              {state && <StateBadge state={state} />}
            </div>
            {assistantReply ? (
              <>
                <p className="ai-user-text">{target.reportedMessage ? target.reportedMessage.content : ''}</p>
                <AssistantSnapshotPanel messageId={targetId} snapshot={target} />
              </>
            ) : directMessage ? (
              <MessageSnapshotPanel target={target} reportedId={targetType === 'Message' ? targetId : null} />
            ) : targetType === 'User' ? (
              <>
                <AuthorLine user={target} linkProfile={false} />
                <p className="muted small">{s.userTargetNote}</p>
                <Link to={'/admin/members?member=' + encodeURIComponent(targetId)} className="small">{s.openMember}</Link>
              </>
            ) : (
              <>
                <AuthorLine user={target.author} time={target.publishedUtc || target.createdUtc} linkProfile={false} />
                {(target.title || target.name || target.displayName) && <strong>{target.title || target.name || target.displayName}</strong>}
                <MarkdownBody body={target.body || target.summary || target.bio} mentions={target.mentions} allowHeadings={targetType === 'FeedPost'} linkMentions={false} />
                <MediaGallery items={target.attachments} compact />
                {postId && <a href={postPath(postId, targetType === 'Comment' ? targetId : null)} target="_blank" rel="noreferrer" className="small">{s.openPost}</a>}
              </>
            )}
            <div className="row-actions">
              {targetType !== 'User' && !assistantReply && moderationActionsFor(state).map((action) => (
                <button key={action} type="button" className={'button button-sm ' + (action === 'Remove' || action === 'Hide' ? 'button-danger-outline' : 'button-secondary')} onClick={() => setDialog(action)}>{s.act[action]}</button>
              ))}
              {reports.some((r) => r.status === 'Open') && <button type="button" className="button button-secondary button-sm" onClick={() => setDialog('Dismiss')}>{s.act.Dismiss}</button>}
            </div>
          </section>

          {history && !assistantReply && !directMessage && (
            <section className="detail-block">
              <h3>{s.authorHistoryTitle}</h3>
              <dl className="kv-grid kv-grid-wide">
                <div className="kv-item"><dt>{s.authorHistory.removed}</dt><dd>{history.removedCount90d}</dd></div>
                <div className="kv-item"><dt>{s.authorHistory.hidden}</dt><dd>{history.hiddenCount90d}</dd></div>
                <div className="kv-item"><dt>{s.authorHistory.reports}</dt><dd>{history.reportsAgainst90d}</dd></div>
                <div className="kv-item"><dt>{s.authorHistory.muted}</dt><dd>{history.isMuted ? (history.mutedUntilUtc ? formatDateTime(history.mutedUntilUtc) : c.yes) : c.no}</dd></div>
              </dl>
            </section>
          )}

          <section className="detail-block">
            <h3>{s.reportsTitle}</h3>
            <div className="table-scroll">
              <TableColumns id="tk.admin.ModerationQueue"><table className="data-table">
                <caption className="visually-hidden">{s.reportsTitle}</caption>
                <thead><tr>{['reporter', 'reason', 'details', 'status', 'created'].map((k) => <th key={k} scope="col">{s.reportColumns[k]}</th>)}</tr></thead>
                <tbody>
                  {reports.map((r) => (
                    <tr key={r.id}>
                      <td>{r.reporter ? authorName(r.reporter) : s.systemReporter}</td>
                      <td>{reasonText(r.reason)}</td>
                      <td className="cell-wrap">{r.details || '-'}</td>
                      <td><Badge tone={r.status === 'Open' ? 'warning' : 'neutral'}>{r.status}</Badge></td>
                      <td className="nowrap" title={formatDateTime(r.createdUtc)}>{formatShortDateTime(r.createdUtc)}</td>
                    </tr>
                  ))}
                </tbody>
              </table></TableColumns>
            </div>
          </section>

          <section className="detail-block">
            <h3>{s.actionsTitle}</h3>
            {actions.length === 0 ? <p className="muted small">{s.actionsEmpty}</p> : (
              <ul className="history-list">
                {actions.map((a) => (
                  <li key={a.id} className="history-item small">
                    <strong>{s.actionOptions[a.action] || a.action}</strong> {a.moderator ? authorName(a.moderator) : s.system} <span className="muted">{formatDateTime(a.createdUtc)}</span>
                    <div>{a.reason}{a.note ? ' (' + a.note + ')' : ''}</div>
                  </li>
                ))}
              </ul>
            )}
          </section>

          {targetType !== 'User' && !assistantReply && !directMessage && <MetadataPanel client={client} targetType={targetType} targetId={targetId} isStaff initial={target && target.labels ? target : null} />}
        </div>
      )}
      {dialog && (
        <ModerationActionDialog
          action={dialog}
          targetType={targetType}
          targetId={targetId}
          api={api}
          suggested={dialog === 'Dismiss' ? '' : topReason}
          onClose={() => setDialog(null)}
          onDone={() => { toast.success(dialog === 'Dismiss' ? s.dismissed : s.actDone(s.act[dialog])); setDialog(null); query.reload(); onChanged(); }}
        />
      )}
      <JsonViewerModal open={jsonOpen} onClose={() => setJsonOpen(false)} subtitle={targetId} value={data} />
    </Modal>
  );
}

/**
 * Moderation queue (contract section 10.2): one row per reported target, overdue rows first,
 * filters for status, target type, reason, and overdue, label and tag filters, and a detail
 * drawer with Hide, Unhide, Remove, Restore, and Dismiss.
 */
/** Target type and scope options; assistant replies (scope Assistant) are for SystemAdmin only, direct messages (scope Messaging) for SystemAdmin and Messaging moderators. */
export function queueFilterOptions(isSystemAdmin, { messagingModerator = false } = {}) {
  const messaging = isSystemAdmin || messagingModerator;
  const targetTypes = optionsFrom(s.targetTypes).filter((o) => (isSystemAdmin || o.value !== 'AssistantMessage') && (messaging || (o.value !== 'Message' && o.value !== 'Conversation')));
  const scopeTypes = optionsFrom(s.scopeTypes).filter((o) => (isSystemAdmin || o.value !== 'Assistant') && (messaging || o.value !== 'Messaging'));
  return { targetTypes, scopeTypes };
}

function ModerationQueue() {
  const { api, client, isSystemAdmin, staff } = useAdminAuth();
  const options = queueFilterOptions(Boolean(isSystemAdmin), { messagingModerator: Boolean(staff && staff.messagingModerator) });
  const [searchParams, setSearchParams] = useSearchParams();
  const table = useTableState({ storageKey: 'tk.admin.queue', filterKeys: QUEUE_FILTERS, defaultSort: QUEUE_DEFAULT_SORT });
  const [jsonRow, setJsonRow] = useState(null);
  const filters = useMemo(() => ({ ...table.filters, status: table.filters.status || 'Open' }), [table.filters]);
  const query = useMemo(() => buildQueueQuery(filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }), [filters, table.pageNumber, table.pageSize, table.sort, table.meta]);
  const fetcher = useCallback((signal) => api.listModerationQueue(query, signal), [api, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const rows = page ? page.items : [];
  const detail = searchParams.get('target');
  const [detailType, detailId] = detail ? detail.split(':') : [null, null];

  const open = (row) => {
    const next = new URLSearchParams(searchParams);
    if (row) next.set('target', row.targetType + ':' + row.targetId);
    else next.delete('target');
    setSearchParams(next);
  };

  const columns = [
    {
      key: 'target', label: col.target, pinned: true, render: (row) => (
        <span className="cell-stack">
          <span className="badge-row"><Badge tone="neutral">{s.targetTypes[row.targetType] || row.targetType}</Badge>{row.preview && row.preview.hasMedia && <Icon name="image" size={12} />}</span>
          <span className="small line-clamp">{(row.preview && (row.preview.title || row.preview.excerpt)) || row.targetId}</span>
        </span>
      )
    },
    { key: 'author', label: col.author, render: (row) => (row.targetType === 'AssistantMessage' ? ((row.scope && row.scope.name) || (row.preview && row.preview.title) || s.assistantAuthor) : authorName(row.author)) },
    { key: 'scope', defaultHidden: true, label: s.scopeColumn, render: (row) => (row.scope ? (row.scope.name || s.scopeTypes[row.scope.type] || row.scope.type) : <span className="muted">-</span>) },
    {
      key: 'reasons', label: col.reasons, render: (row) => (
        <span className="badge-row">{Object.entries(row.reasons || {}).map(([code, n]) => <Badge key={code} tone={code === 'Filter' ? 'info' : 'warning'}>{reasonText(code)} {n}</Badge>)}</span>
      )
    },
    { key: 'reports', label: col.reports, sortField: 'reportCount', sortDefault: 'desc', className: 'num', render: (row) => row.reportCount },
    { key: 'reporters', defaultHidden: true, label: col.reporters, className: 'num', render: (row) => row.reporterCount },
    { key: 'first', defaultHidden: true, label: col.first, sortField: 'firstReportedUtc', className: 'nowrap', render: (row) => <span title={formatDateTime(row.firstReportedUtc)}>{formatShortDateTime(row.firstReportedUtc)}</span> },
    {
      key: 'due', label: col.due, sortField: 'dueUtc', className: 'nowrap', render: (row) => (isOverdue(row)
        ? <Badge tone="danger" title={s.overdueTitle(REVIEW_TARGET_HOURS)}><Icon name="clock" size={12} /> {s.overdue}</Badge>
        : <span title={formatDateTime(row.dueUtc)}>{row.dueUtc ? s.dueIn(formatTimeUntil(row.dueUtc)) : '-'}</span>)
    },
    {
      key: 'state', label: col.state, render: (row) => (
        <span className="badge-row">
          <StateBadge state={row.targetState} />
          {row.targetState === 'Hidden' && row.reporterCount >= 3 && <Badge tone="info" title={s.autoHiddenTitle}>{s.autoHidden}</Badge>}
        </span>
      )
    },
    { key: 'status', label: col.status, render: (row) => <Badge tone={row.status === 'Open' ? 'warning' : 'neutral'}>{s.statusOptions[row.status] || row.status}</Badge> },
    {
      key: 'actions', pinned: true, className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>,
      render: (row) => <ActionMenu label={c.rowActions(row.targetId)} items={[{ label: c.view, icon: 'eye', onSelect: () => open(row) }, { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) }]} />
    }
  ];

  const filterFields = [
    { key: 'status', label: s.status, type: 'select', options: optionsFrom(s.statusOptions) },
    { key: 'targetType', label: s.targetType, type: 'select', options: options.targetTypes },
    { key: 'reason', label: s.reason, type: 'select', options: [...Object.keys(strings.safety.reasons).map((k) => ({ value: k, label: strings.safety.reasons[k].label })), { value: 'Filter', label: s.reasons.Filter }] },
    { key: 'scopeType', label: s.scopeType, type: 'select', options: options.scopeTypes },
    { key: 'forumIds', label: s.scopeForum, placeholder: 'frm_...', mono: true },
    { key: 'overdue', label: s.overdueOnly, type: 'checkbox' }
  ];

  return (
    <>
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={client} targetType="Report" values={filters} />
      </section>
      <section className="table-section">
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading} pageSizes={ADMIN_PAGE_SIZES} />
        <DataTable
          caption={s.tabs.queue}
          columns={columns}
          rows={rows}
          rowKey={(row) => row.targetType + ':' + row.targetId}
          loading={list.loading}
          error={list.error ? c.unavailable(s.tabs.queue) + ' ' + errorMessage(list.error, '') : null}
          onRetry={list.reload}
          emptyMessage={table.hasFilters ? s.emptyFiltered : s.empty}
          onRowClick={open}
          rowClassName={(row) => (isOverdue(row) ? 'row-overdue' : '')}
          sort={table.sort}
          onSortChange={table.setSort}
        />
      </section>
      {detail && <QueueDetail key={detail} targetType={detailType} targetId={detailId} onClose={() => open(null)} onChanged={list.reload} />}
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.targetId} value={jsonRow} />
    </>
  );
}

export default ModerationQueue;
