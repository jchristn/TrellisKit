import { useCallback, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { ThreadTypes } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useForumList from '../../hooks/useForumList.js';
import useTableState from '../../hooks/useTableState.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, TRENDING_FILTERS, buildTrendingQuery } from '../../utils/adminQuery.js';
import { optionsFrom } from '../../utils/labels.js';
import { formatShortDateTime } from '../../utils/format.js';
import { errorText } from '../../utils/engagement.js';
import { appThreadPath, forumErrors } from '../../utils/forums.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import { Badge } from '../../components/Badges.jsx';
import { PromoteDialog } from '../../components/forums/ThreadDialogs.jsx';

const s = strings.admin.trending;
const c = strings.admin.common;
const col = s.columns;
const fo = strings.forums;
const WEIGHT_KEYS = ['reactionWeight', 'commentWeight', 'commenterWeight', 'ageOffsetHours', 'gravity'];

function num(value, digits = 1) {
  return Number.isFinite(value) ? Number(value).toFixed(digits) : '-';
}

/** The current trending weights from the settings document (SystemAdmin only), with a link to the Trending group. */
function Weights() {
  const { api, isSystemAdmin } = useAdminAuth();
  const fetcher = useCallback((signal) => api.getSettings(signal), [api]);
  const query = useApiQuery(fetcher, { enabled: isSystemAdmin });
  const values = {};
  for (const field of (query.data && query.data.fields) || []) {
    const key = String(field.key || '');
    if (key.startsWith('trending.')) values[key.slice('trending.'.length)] = field.value;
  }
  // Trending weights are deployment settings: in a MultiProperty deployment only platform
  // administrators read them, so a property administrator gets 403 and sees an explanation instead.
  const deploymentManaged = Boolean(query.error && query.error.status === 403);
  return (
    <section className="panel" aria-labelledby="trending-weights">
      <div className="row-between">
        <h2 id="trending-weights">{s.weightsTitle}</h2>
        {isSystemAdmin && !deploymentManaged && <Link to="/admin/settings" className="button button-secondary button-sm">{s.weightsLink}</Link>}
      </div>
      <p className="muted small mono">{s.formula}</p>
      {deploymentManaged ? <p className="muted small">{s.weightsPlatformManaged}</p> : isSystemAdmin ? (
        <dl className="kv-grid small">
          {WEIGHT_KEYS.map((key) => (
            <div key={key}><dt>{s.weights[key]}</dt><dd>{values[key] !== undefined ? String(values[key]) : '-'}</dd></div>
          ))}
        </dl>
      ) : <p className="muted small">{s.weightsUnavailable}</p>}
    </section>
  );
}

/**
 * /admin/trending (PHASE3_CONTRACT.md section 5.3): threads by trending score with the score
 * breakdown, forum, type, promoted, and minimum score filters, Promote and Unpromote row actions,
 * and the current weights. Moderators see their scope (the server filters).
 */
function TrendingView() {
  const { client, isSystemAdmin } = useAdminAuth();
  const toast = useToast();
  const table = useTableState({ storageKey: 'tk.admin.trending', filterKeys: TRENDING_FILTERS, defaultSort: '' });
  const forums = useForumList(client, null, { admin: isSystemAdmin });
  const [promoting, setPromoting] = useState(null);
  const query = useMemo(() => buildTrendingQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.meta]);
  const fetcher = useCallback((signal) => client.adminEnumerateTrending(query, signal), [client, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const rows = page ? page.items : [];

  const unpromote = async (row) => {
    try {
      await client.unpromoteThread(row.thread.id);
      toast.success(fo.unpromotedToast);
      list.reload();
    } catch (err) {
      toast.error(errorText(err, forumErrors()));
    }
  };

  const columns = [
    { key: 'rank', label: col.rank, className: 'num', render: (row) => row.rank },
    { key: 'thread', label: col.thread, render: (row) => <a href={appThreadPath(row.thread.id)} target="_blank" rel="noreferrer">{row.thread.title}</a> },
    { key: 'forum', label: col.forum, render: (row) => (row.thread.forum ? row.thread.forum.name : '-') },
    { key: 'score', label: <span title={s.liveScoreHint}>{col.score}</span>, className: 'num', render: (row) => num(row.breakdown && Number.isFinite(row.breakdown.score) ? row.breakdown.score : row.thread.trendingScore, 2) },
    { key: 'reactions', label: col.reactions, className: 'num', render: (row) => (row.breakdown ? row.breakdown.reactions : '-') },
    { key: 'comments', label: col.comments, className: 'num', render: (row) => (row.breakdown ? row.breakdown.comments : '-') },
    { key: 'commenters', defaultHidden: true, label: col.commenters, className: 'num', render: (row) => (row.breakdown ? row.breakdown.uniqueCommenters : '-') },
    { key: 'age', label: col.age, className: 'num', render: (row) => (row.breakdown ? num(row.breakdown.ageHours) : '-') },
    { key: 'breakdown', label: col.breakdown, className: 'num nowrap', render: (row) => (row.breakdown ? num(row.breakdown.numerator, 2) + ' / ' + num(row.breakdown.denominator, 2) : '-') },
    { key: 'computed', defaultHidden: true, label: col.computed, className: 'nowrap muted', render: (row) => (row.computedUtc ? s.storedAt(num(row.thread.trendingScore, 2), formatShortDateTime(row.computedUtc)) : s.notComputed) },
    { key: 'promotion', label: col.promotion, render: (row) => (row.promotion ? <Badge tone="success">{s.inFeed}</Badge> : <span className="muted">-</span>) },
    { key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>, render: (row) => (
      <ActionMenu label={c.rowActions(row.thread.title)} items={[row.promotion
        ? { label: s.unpromote, icon: 'feed', onSelect: () => unpromote(row) }
        : { label: s.promote, icon: 'feed', onSelect: () => setPromoting(row.thread) }]} />
    ) }
  ];

  const filterFields = [
    { key: 'forumId', label: s.forum, type: 'select', options: forums.forums.map((f) => ({ value: f.id, label: f.name })) },
    { key: 'type', label: s.type, type: 'select', options: ThreadTypes.map((x) => ({ value: x, label: fo.types[x] })) },
    { key: 'promoted', label: s.promoted, type: 'select', options: optionsFrom(s.promotedOptions) },
    { key: 'minScore', label: s.minScore }
  ];

  return (
    <div className="view">
      <PageHeader title={strings.admin.routes.trending.label} summary={strings.admin.routes.trending.summary} />
      <Weights />
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={client} targetType="Thread" />
      </section>
      <section className="table-section">
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading} pageSizes={ADMIN_PAGE_SIZES} />
        <DataTable
          tableId="tk.admin.trending"
          caption={strings.admin.routes.trending.label}
          columns={columns}
          rows={rows}
          rowKey={(row) => row.thread.id}
          loading={list.loading}
          error={list.error ? c.unavailable(strings.admin.routes.trending.label) + ' ' + errorText(list.error, {}, '') : null}
          onRetry={list.reload}
          emptyMessage={s.empty}
        />
      </section>
      {promoting && <PromoteDialog thread={promoting} isAdmin={isSystemAdmin} onClose={() => setPromoting(null)} onDone={() => { setPromoting(null); toast.success(fo.promotedToast); list.reload(); }} />}
    </div>
  );
}

export default TrendingView;
