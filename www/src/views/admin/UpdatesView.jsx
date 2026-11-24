import { ErrorBanner } from '../../components/States.jsx';
import { useCallback, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { collectEnumeration, UPDATE_GROUP_FILTER_KEYS, UPDATE_PLATFORMS, canPromote, hasRollout, nextRolloutStep } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useLocalStorage from '../../hooks/useLocalStorage.js';
import useTableState from '../../hooks/useTableState.js';
import useRowSelection from '../../hooks/useRowSelection.jsx';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, pagedQuery } from '../../utils/adminQuery.js';
import { optionsFrom } from '../../utils/labels.js';
import { formatPreciseDateTime, formatShortDate } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import BulkMetadataDialog from '../../components/BulkMetadataDialog.jsx';
import BulkBar from '../../components/BulkBar.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import KpiCard from '../../components/KpiCard.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { FormDialog } from './LiveUpdateDialogs.jsx';

const u = strings.admin.liveUpdates;
const c = strings.admin.common;
const col = u.columns;
const FILTERS = Object.freeze(['search', ...UPDATE_GROUP_FILTER_KEYS]);
const DEFAULT_HIDDEN = ['id', 'labels'];

/** Badge tone for an update group status. */
export function updateStatusTone(status) {
  if (status === 'Published') return 'success';
  if (status === 'Paused') return 'warning';
  if (status === 'RolledBack') return 'danger';
  if (status === 'Draft') return 'info';
  return 'neutral';
}

/** POST /admin/update-groups/enumerate query from the table state; `critical` is sent as a boolean. */
export function buildUpdateGroupsQuery(filters, paging) {
  return pagedQuery(filters, FILTERS, paging, ({ critical, ...rest }) => ({ ...rest, ...(critical ? { critical: critical === 'true' } : {}) }));
}

/** The status badge plus halted and critical markers. */
export function UpdateStatus({ group }) {
  return (
    <span className="nowrap">
      <Badge tone={updateStatusTone(group.status)}>{u.statuses[group.status] || group.status}</Badge>
      {group.autoHalt && group.autoHalt.halted && <> <Badge tone="danger">{u.halted}</Badge></>}
      {group.critical && <> <Badge tone="warning">{u.criticalBadge}</Badge></>}
    </span>
  );
}

/** Rollback dialog: choose an earlier group on the same channel. */
export function RollbackDialog({ group, client, onClose, onDone }) {
  const fetcher = useCallback((signal) => client.enumerateUpdateGroups({ filters: { channel: group.channel }, maxResults: 50 }, signal), [client, group.channel]);
  const list = useApiQuery(fetcher);
  const options = (list.data ? normalizePage(list.data).items : [])
    .filter((g) => g.id !== group.id && g.kind !== 'RollBackToEmbedded' && g.status !== 'Draft')
    .map((g) => ({ value: g.id, label: g.message + ' (' + formatShortDate(g.createdUtc) + ')' }));
  if (list.loading) return null;
  return (
    <FormDialog
      open
      title={u.rollbackTitle}
      body={u.rollbackBody}
      submitLabel={u.actions.rollback}
      danger
      fields={[
        { key: 'targetUpdateGroupId', label: u.rollbackTarget, type: 'select', options: [{ value: '', label: u.chooseTarget }, ...options], required: true },
        { key: 'rolloutPercentage', label: u.rolloutPercentage, type: 'number', min: 0.01, max: 100, step: 0.01 },
        { key: 'message', label: u.message, maxLength: 500 }
      ]}
      initial={{ rolloutPercentage: '100' }}
      onClose={onClose}
      onSubmit={async (v) => {
        const created = await client.rollbackUpdateGroup(group.id, { targetUpdateGroupId: v.targetUpdateGroupId, rolloutPercentage: Number(v.rolloutPercentage) || 100, message: v.message || undefined });
        onDone(created);
      }}
    />
  );
}

/** Promote dialog: another channel, never development. */
export function PromoteDialog({ group, client, channels, onClose, onDone }) {
  const options = (channels || []).filter((ch) => ch !== group.channel && ch !== 'development').map((ch) => ({ value: ch, label: ch }));
  return (
    <FormDialog
      open
      title={u.promoteTitle}
      body={u.promoteBody}
      submitLabel={u.actions.promote}
      fields={[
        { key: 'channel', label: u.targetChannel, type: 'select', options, required: true },
        { key: 'rolloutPercentage', label: u.rolloutPercentage, type: 'number', min: 0.01, max: 100, step: 0.01 },
        { key: 'message', label: u.message, maxLength: 500 }
      ]}
      initial={{ channel: options.length ? options[0].value : '', rolloutPercentage: '10' }}
      onClose={onClose}
      onSubmit={async (v) => {
        const created = await client.promoteUpdateGroup(group.id, { channel: v.channel, rolloutPercentage: Number(v.rolloutPercentage) || undefined, message: v.message || undefined });
        onDone(created);
      }}
    />
  );
}

/** Header cards: signing status and the live update per channel and platform. */
function HealthCards({ health, channels }) {
  if (!health) return null;
  const cards = [];
  cards.push(<KpiCard key="signing" label={u.signing} value={health.signing === 'Active' ? u.signingActive(health.activeKeyId) : u.signingMissing} tone={health.signing === 'Active' ? 'success' : 'danger'} hint={health.signingProblem || undefined} />);
  for (const entry of channels || []) {
    const channel = entry.name;
    const platforms = entry.latestByPlatform;
    for (const platform of UPDATE_PLATFORMS) {
      const latest = platforms ? platforms[platform] : null;
      cards.push(<KpiCard key={channel + platform} label={u.latest(channel, platform)} value={latest ? u.percent(latest.rolloutPercentage) : u.none} hint={latest ? formatShortDate(latest.createdAt) : undefined} />);
    }
  }
  return <div className="kpi-grid">{cards}</div>;
}

/**
 * Releases, then Updates (docs/LIVE_UPDATES_CONTRACT.md section 21): enumerated update groups with platform,
 * channel, runtime version, status, kind, critical, label, and tag filters; row actions (view, JSON, raise the
 * rollout, pause or resume, promote, roll back, delete a draft); bulk labels and tags; rollback to the built-in bundle.
 */
function UpdatesView() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const navigate = useNavigate();
  const table = useTableState({ storageKey: 'tk.admin.updates', filterKeys: FILTERS, defaultSort: '-createdUtc' });
  const [hidden, setHidden] = useLocalStorage('tk.admin.updates.hiddenColumns', DEFAULT_HIDDEN);
  const [jsonRow, setJsonRow] = useState(null);
  const [dialog, setDialog] = useState(null);
  const [metaIds, setMetaIds] = useState(null);

  const query = useMemo(() => buildUpdateGroupsQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.sort, table.meta]);
  const fetcher = useCallback((signal) => client.enumerateUpdateGroups(query, signal), [client, query]);
  const list = useApiQuery(fetcher);
  const healthFetcher = useCallback((signal) => client.getUpdatesHealth(signal), [client]);
  const health = useApiQuery(healthFetcher);
  const channelsFetcher = useCallback((signal) => collectEnumeration((q, sig) => client.enumerateUpdateChannels(q, sig), {}, { signal }), [client]);
  const channelList = useApiQuery(channelsFetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const selection = useRowSelection(page ? page.items : [], JSON.stringify(query));
  const channels = (channelList.data?.objects || []).map((channel) => channel.name);

  const run = async (fn, message) => {
    try {
      await fn();
      toast.success(message);
      list.reload();
      health.reload();
      channelList.reload();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };

  const rowActions = (row) => {
    const items = [
      { label: u.actions.view, icon: 'eye', onSelect: () => navigate('/admin/updates/' + encodeURIComponent(row.id)) },
      { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) }
    ];
    if (row.status === 'Draft') items.push({ label: u.actions.publish, icon: 'upload', onSelect: () => run(() => client.publishUpdateGroup(row.id, {}), u.rolloutChanged(row.rolloutPercentage || 100)) });
    const next = nextRolloutStep(row.rolloutPercentage);
    if (hasRollout(row) && next) items.push({ label: u.actions.raise(next), icon: 'upload', onSelect: () => run(() => client.setUpdateRollout(row.id, next), u.rolloutChanged(next)) });
    if (row.status === 'Published') items.push({ label: u.actions.pause, icon: 'power', onSelect: () => setDialog({ type: 'pause', row }) });
    if (row.status === 'Paused') items.push({ label: u.actions.resume, icon: 'power', onSelect: () => run(() => client.resumeUpdateGroup(row.id), u.resumed) });
    if (canPromote(row)) items.push({ label: u.actions.promote, icon: 'external', onSelect: () => setDialog({ type: 'promote', row }) });
    if (hasRollout(row) && row.kind !== 'RollBackToEmbedded') items.push({ label: u.actions.rollback, icon: 'refresh', danger: true, onSelect: () => setDialog({ type: 'rollback', row }) });
    if (row.status === 'Draft') items.push({ label: u.actions.remove, icon: 'trash', danger: true, onSelect: () => setDialog({ type: 'delete', row }) });
    return items;
  };

  const columns = [
    selection.column((row) => row.message),
    { key: 'message', label: col.message, pinned: true, render: (row) => <span className="truncate" title={row.message}>{row.message}</span> },
    { key: 'channel', label: col.channel, render: (row) => <Badge>{row.channel}</Badge> },
    { key: 'platforms', label: col.platforms, render: (row) => (row.platforms || []).join(', ') || u.kinds[row.kind] },
    { key: 'status', label: col.status, pinned: true, render: (row) => <UpdateStatus group={row} /> },
    { key: 'rollout', label: col.rollout, className: 'nowrap', render: (row) => u.percent(row.rolloutPercentage) },
    { key: 'createdBy', label: col.createdBy, render: (row) => (row.createdBy ? row.createdBy.name : '') },
    { key: 'created', label: col.created, sortField: 'createdUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => <span title={formatPreciseDateTime(row.createdUtc)}>{formatShortDate(row.createdUtc)}</span> },
    { key: 'labels', label: col.labels, render: (row) => (row.labels || []).map((l) => <Badge key={l}>{l}</Badge>) },
    { key: 'id', label: col.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={col.id} /> },
    {
      key: 'actions', pinned: true, selectorLabel: col.actions, className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>,
      render: (row) => <ActionMenu label={c.rowActions(row.message)} items={rowActions(row)} />
    }
  ];

  const yesNo = [{ value: 'true', label: u.yes }, { value: 'false', label: u.no }];
  const filterFields = [
    { key: 'search', label: u.searchLabel, placeholder: u.searchPlaceholder, grow: true },
    { key: 'platform', label: u.platform, type: 'select', options: UPDATE_PLATFORMS.map((p) => ({ value: p, label: p })) },
    { key: 'channel', label: u.channel, type: 'select', options: channels.map((ch) => ({ value: ch, label: ch })) },
    { key: 'runtimeVersion', label: u.runtimeVersion },
    { key: 'status', label: u.status, type: 'select', options: optionsFrom(u.statuses) },
    { key: 'kind', label: u.kind, type: 'select', options: optionsFrom(u.kinds) },
    { key: 'critical', label: u.critical, type: 'select', options: yesNo },
    { key: 'gitCommit', label: u.gitCommit }
  ];

  return (
    <div className="view">
      <PageHeader
        title={strings.admin.routes.updates.label}
        summary={strings.admin.routes.updates.summary}
        actions={(
          <button type="button" className="button button-secondary button-sm" onClick={() => setDialog({ type: 'embedded' })}>
            <Icon name="refresh" size={14} /><span>{u.rollbackEmbedded}</span>
          </button>
        )}
      />
      <HealthCards health={health.data} channels={channelList.data?.objects} />
      <ErrorBanner message={channelList.error ? errorMessage(channelList.error) : null} onRetry={channelList.reload} />
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={client} targetType="UpdateGroup" />
      </section>
      <section className="table-section">
        <div className="table-toolbar">
          <div className="toolbar-left"><BulkBar count={selection.selected.length} onClear={selection.clear} onMetadata={() => setMetaIds(selection.selected)} /></div>
        </div>
        <TablePagination
          pageNumber={table.pageNumber}
          pageSize={table.pageSize}
          totalCount={page ? page.totalCount : 0}
          totalPages={page ? page.totalPages : 1}
          onPageChange={table.setPage}
          onPageSizeChange={table.setPageSize}
          onRefresh={list.reload}
          loading={list.loading}
          pageSizes={ADMIN_PAGE_SIZES}
        />
        <DataTable
          caption={strings.admin.routes.updates.label}
          columns={columns.filter((x) => x.pinned || !hidden.includes(x.key))}
          columnChoices={columns.map((x) => ({ key: x.key, label: x.selectorLabel || x.label, pinned: x.pinned }))}
          columnsHidden={hidden}
          onColumnsChange={setHidden}
          rows={page ? page.items : []}
          rowKey={(row) => row.id}
          loading={list.loading}
          error={list.error ? c.unavailable(strings.admin.routes.updates.label) + ' ' + errorMessage(list.error, '') : null}
          onRetry={list.reload}
          emptyMessage={table.hasFilters ? u.emptyFiltered : u.empty}
          onRowClick={(row) => navigate('/admin/updates/' + encodeURIComponent(row.id))}
          sort={table.sort}
          onSortChange={table.setSort}
        />
      </section>

      <ConfirmModal
        open={Boolean(dialog && dialog.type === 'pause')}
        title={u.pauseTitle}
        message={u.pauseBody}
        confirmLabel={u.actions.pause}
        onConfirm={() => { const row = dialog.row; setDialog(null); run(() => client.pauseUpdateGroup(row.id), u.paused); }}
        onCancel={() => setDialog(null)}
      />
      <ConfirmModal
        open={Boolean(dialog && dialog.type === 'delete')}
        title={u.deleteTitle}
        message={u.deleteBody}
        confirmLabel={u.actions.remove}
        onConfirm={() => { const row = dialog.row; setDialog(null); run(() => client.deleteUpdateGroup(row.id), u.deleted); }}
        onCancel={() => setDialog(null)}
      />
      {dialog && dialog.type === 'promote' && <PromoteDialog group={dialog.row} client={client} channels={channels} onClose={() => setDialog(null)} onDone={() => { setDialog(null); toast.success(u.promoted); list.reload(); }} />}
      {dialog && dialog.type === 'rollback' && <RollbackDialog group={dialog.row} client={client} onClose={() => setDialog(null)} onDone={() => { setDialog(null); toast.success(u.rolledBack); list.reload(); }} />}
      {dialog && dialog.type === 'embedded' && (
        <FormDialog
          open
          title={u.rollbackEmbeddedTitle}
          body={u.rollbackEmbeddedBody}
          submitLabel={u.rollbackEmbedded}
          danger
          fields={[
            { key: 'platform', label: u.platform, type: 'select', options: [{ value: 'all', label: u.platformAll }, ...UPDATE_PLATFORMS.map((p) => ({ value: p, label: p }))] },
            { key: 'channel', label: u.channel, type: 'select', options: channels.map((ch) => ({ value: ch, label: ch })) },
            { key: 'runtimeVersion', label: u.runtimeVersion, required: true, maxLength: 128 },
            { key: 'message', label: u.message, maxLength: 500 }
          ]}
          initial={{ platform: 'all', channel: channels[0] || 'production' }}
          onClose={() => setDialog(null)}
          onSubmit={async (v) => {
            await client.rollbackToEmbedded({ platform: v.platform, channel: v.channel, runtimeVersion: v.runtimeVersion.trim(), message: v.message || undefined });
            setDialog(null);
            toast.success(u.rolledBack);
            list.reload();
          }}
        />
      )}
      {metaIds && <BulkMetadataDialog open targetType="UpdateGroup" ids={metaIds} onClose={() => setMetaIds(null)} onDone={() => { selection.clear(); list.reload(); }} />}
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.message} value={jsonRow} />
    </div>
  );
}

export default UpdatesView;
