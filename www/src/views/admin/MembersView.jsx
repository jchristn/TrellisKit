import { useCallback, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useLocalStorage from '../../hooks/useLocalStorage.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, MEMBER_DEFAULT_SORT, MEMBER_FILTERS, buildMembersQuery } from '../../utils/adminQuery.js';
import { membershipText, optionsFrom, roleText } from '../../utils/labels.js';
import { PROFILE_MAX_CARD_FIELDS, filterableFields, isProfileFilterParam, parseProfileFilterParams } from '@trelliskit/shared';
import { useAdminProfileFields, writeProfileFilterParams } from './adminProfileFields.js';
import { CardFields } from '../../components/profile/ProfileFieldDisplay.jsx';
import MemberProfileFilters from './MemberProfileFilters.jsx';
import { formatDateTime, formatShortDate } from '../../utils/format.js';
import { formatRelativeTime } from '@trelliskit/shared';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import BulkMetadataDialog from '../../components/BulkMetadataDialog.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import ReasonModal from '../../components/ReasonModal.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import Avatar from '../../components/Avatar.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import MemberDetailDrawer from './MemberDetailDrawer.jsx';

const s = strings.admin.members;
const c = strings.admin.common;
const col = s.columns;
const DEFAULT_HIDDEN = ['id', 'roles'];
const ROLE_FILTERS = ['SystemAdmin', 'Moderator', 'FeedModerator', 'DirectoryModerator', 'EventManager', 'LibraryEditor', 'SupportAgent', 'Communicator'];

/**
 * Members: server-side search and filters (membership, role, banned, and profile filters on every
 * filterable field), sortable columns, paging, card profile values, bulk session revoke and ban,
 * a row Delete member action (reason plus typed DELETE), and a detail drawer with every member action.
 */
function MembersView() {
  const fields = useAdminProfileFields();
  const { api } = useAdminAuth();
  const toast = useToast();
  const [searchParams, setSearchParams] = useSearchParams();
  const adminClient = useAdminAuth().client;
  const table = useTableState({ storageKey: 'tk.admin.members', filterKeys: MEMBER_FILTERS, defaultSort: MEMBER_DEFAULT_SORT });
  const [hidden, setHidden] = useLocalStorage('tk.admin.members.hiddenColumns', DEFAULT_HIDDEN);
  const [selection, setSelection] = useState({ key: '', ids: [] });
  const [jsonRow, setJsonRow] = useState(null);
  const [bulk, setBulk] = useState(null);
  const [metaIds, setMetaIds] = useState(null);
  const [busy, setBusy] = useState(false);
  const [deleteRow, setDeleteRow] = useState(null);
  const [deleteText, setDeleteText] = useState('');
  const detailId = searchParams.get('member');
  const filterable = useMemo(() => filterableFields(fields), [fields]);
  const profileText = Array.from(searchParams.entries()).filter(([name]) => isProfileFilterParam(name)).map(([n, v]) => n + '=' + v).join('&');
  const profileState = useMemo(() => parseProfileFilterParams(new URLSearchParams(profileText)), [profileText]);

  const query = useMemo(
    () => buildMembersQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }, profileState),
    [table.filters, table.pageNumber, table.pageSize, table.sort, table.meta, profileState]
  );
  const fetcher = useCallback((signal) => api.enumerateAdminMembers(query, signal), [api, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const rows = page ? page.items : [];

  const selectionKey = JSON.stringify(query);
  const selected = selection.key === selectionKey ? selection.ids : [];
  const setSelected = (ids) => setSelection({ key: selectionKey, ids });
  const allSelected = rows.length > 0 && rows.every((row) => selected.includes(row.id));

  const openDetail = (id) => {
    const next = new URLSearchParams(searchParams);
    if (id) next.set('member', id);
    else next.delete('member');
    setSearchParams(next);
  };

  const deleteName = (row) => row.displayName || row.email || row.id;
  const runDelete = async (reason) => {
    try {
      const result = await api.deleteMember(deleteRow.id, reason);
      toast.success(s.deleteDone(deleteName(deleteRow)));
      if (result && result.storeSubscriptionNote) toast.info(s.deleteStoreNote(result.storeSubscriptionNote));
      setSelected(selected.filter((x) => x !== deleteRow.id));
      setDeleteRow(null);
      list.reload();
    } catch (err) {
      throw new Error(errorMessage(err, strings.common.errorGeneric));
    }
  };

  const runBulk = async (reason) => {
    setBusy(true);
    let count = 0;
    try {
      for (const id of bulk.ids) {
        if (bulk.kind === 'ban') await api.banMember(id, reason);
        else await api.revokeMemberSessions(id);
        count += 1;
      }
      toast.success(s.bulkDone(count));
      setBulk(null);
      setSelected([]);
      list.reload();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
      if (count) list.reload();
      throw err;
    } finally {
      setBusy(false);
    }
  };

  const columns = [
    {
      key: 'select', pinned: true, selectorLabel: col.select, className: 'cell-check',
      label: <input type="checkbox" checked={allSelected} onChange={() => setSelected(allSelected ? [] : rows.map((r) => r.id))} aria-label={c.selectAll} disabled={rows.length === 0} />,
      render: (row) => <input type="checkbox" checked={selected.includes(row.id)} onChange={() => setSelected(selected.includes(row.id) ? selected.filter((x) => x !== row.id) : [...selected, row.id])} aria-label={c.selectRow(row.displayName || row.email)} />
    },
    {
      key: 'member', label: col.member, pinned: true, sortField: 'displayName',
      render: (row) => (
        <span className="member-cell">
          <Avatar src={row.avatarUrl} name={row.displayName} size={28} />
          <span className="member-cell-text">
            <strong>{row.displayName || '-'}</strong>
            {row.handle && <span className="muted small mono">@{row.handle}</span>}
          </span>
        </span>
      )
    },
    { key: 'email', label: col.email, render: (row) => <span className="truncate">{row.email}</span> },
    { key: 'profile', label: col.profile, render: (row) => <CardFields fields={fields} profile={row.profile || {}} max={PROFILE_MAX_CARD_FIELDS} allFields={fields} /> },
    { key: 'membership', label: col.membership, render: (row) => <Badge tone={row.membership && row.membership.isActive ? 'success' : 'neutral'}>{membershipText(row.membership)}</Badge> },
    { key: 'roles', label: col.roles, render: (row) => ((row.roles || []).length ? (row.roles || []).map(roleText).join(', ') : <span className="muted">{s.noRoles}</span>) },
    {
      key: 'status', label: col.status, render: (row) => (
        <span className="badge-row">
          {row.isBanned && <Badge tone="danger">{s.banned}</Badge>}
          {row.isMuted && <Badge tone="warning">{s.muted}</Badge>}
          {!row.isBanned && !row.isMuted && <span className="muted small">{s.okStatus}</span>}
        </span>
      )
    },
    { key: 'created', label: col.created, sortField: 'createdUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => <span title={formatDateTime(row.createdUtc)}>{formatShortDate(row.createdUtc)}</span> },
    { key: 'lastSeen', label: col.lastSeen, sortField: 'lastSeenUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => (row.lastSeenUtc ? <span title={formatDateTime(row.lastSeenUtc)}>{formatRelativeTime(row.lastSeenUtc)}</span> : <span className="muted">{c.never}</span>) },
    { key: 'id', label: col.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={col.id} /> },
    {
      key: 'actions', pinned: true, selectorLabel: col.actions, className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>,
      render: (row) => (
        <ActionMenu
          label={c.rowActions(row.displayName || row.email)}
          items={[
            { label: c.view, icon: 'eye', onSelect: () => openDetail(row.id) },
            { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) },
            { label: s.actions.revokeSessions, icon: 'logout', danger: true, onSelect: () => setBulk({ kind: 'revoke', ids: [row.id] }) },
            row.isBanned
              ? { label: s.actions.unban, icon: 'ban', onSelect: () => openDetail(row.id) }
              : { label: s.actions.ban, icon: 'ban', danger: true, onSelect: () => setBulk({ kind: 'ban', ids: [row.id] }) },
            { label: s.actions.deleteMember, icon: 'trash', danger: true, onSelect: () => { setDeleteText(''); setDeleteRow(row); } }
          ]}
        />
      )
    }
  ].filter(Boolean);
  const visibleColumns = columns.filter((x) => x.pinned || !hidden.includes(x.key));

  const filterFields = [
    { key: 'search', label: s.searchLabel, placeholder: s.searchPlaceholder, grow: true },
    { key: 'membership', label: s.membership, type: 'select', options: optionsFrom(s.membershipOptions) },
    { key: 'role', label: s.role, type: 'select', options: optionsFrom(strings.enums.role, ROLE_FILTERS) },
    { key: 'banned', label: s.bannedOnly, type: 'checkbox' }
  ].filter(Boolean);

  return (
    <div className="view">
      <PageHeader title={strings.admin.routes.members.label} summary={strings.admin.routes.members.summary} />

      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={adminClient} targetType="User" />
        <MemberProfileFilters fields={filterable} value={profileState} onApply={(state) => setSearchParams(writeProfileFilterParams(searchParams, state))} />
      </section>

      <section className="table-section">
        <div className="table-toolbar">
          <div className="toolbar-left">
            {selected.length > 0 ? (
              <div className="bulk-bar" role="region" aria-label={c.selectedCount(selected.length)}>
                <span>{c.selectedCount(selected.length)}</span>
                <button type="button" className="button button-danger-outline button-sm" onClick={() => setBulk({ kind: 'revoke', ids: selected })}>
                  <Icon name="logout" size={14} /><span>{s.bulkRevoke(selected.length)}</span>
                </button>
                <button type="button" className="button button-danger button-sm" onClick={() => setBulk({ kind: 'ban', ids: selected })}>
                  <Icon name="ban" size={14} /><span>{s.bulkBan(selected.length)}</span>
                </button>
                <button type="button" className="button button-secondary button-sm" onClick={() => setMetaIds(selected)}>
                  <Icon name="list" size={14} /><span>{strings.admin.bulkMetadata.action}</span>
                </button>
                <button type="button" className="button button-secondary button-sm" onClick={() => setSelected([])}>{c.clearSelection}</button>
              </div>
            ) : null}
          </div>
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
          caption={strings.admin.routes.members.label}
          columns={visibleColumns}
          columnChoices={columns.map((x) => ({ key: x.key, label: x.selectorLabel || x.label, pinned: x.pinned }))}
          columnsHidden={hidden}
          onColumnsChange={setHidden}
          rows={rows}
          rowKey={(row) => row.id}
          loading={list.loading}
          error={list.error ? c.unavailable(strings.admin.routes.members.label) + ' ' + errorMessage(list.error, '') : null}
          onRetry={list.reload}
          emptyMessage={table.hasFilters || Object.keys(profileState).length ? s.emptyFiltered : s.empty}
          onRowClick={(row) => openDetail(row.id)}
          rowClassName={(row) => (row.isBanned ? 'row-failed' : '')}
          sort={table.sort}
          onSortChange={table.setSort}
        />
      </section>

      {detailId && <MemberDetailDrawer key={detailId} id={detailId} onClose={() => openDetail(null)} onChanged={list.reload} />}
      {metaIds && <BulkMetadataDialog open targetType="User" ids={metaIds} onClose={() => setMetaIds(null)} onDone={() => { setSelected([]); list.reload(); }} />}
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.id} value={jsonRow} />
      {deleteRow && (
        <ReasonModal
          key={deleteRow.id}
          open
          title={s.deleteTitle}
          message={s.deleteBody(deleteName(deleteRow))}
          confirmLabel={s.actions.deleteMember}
          onCancel={() => setDeleteRow(null)}
          onConfirm={runDelete}
          validateExtra={() => (deleteText === 'DELETE' ? '' : s.deleteConfirmError)}
        >
          <div className="field">
            <label className="label-text" htmlFor="delete-confirm-input">{s.deleteConfirmLabel}</label>
            <input id="delete-confirm-input" type="text" autoComplete="off" autoCapitalize="characters" spellCheck={false} value={deleteText} onChange={(e) => setDeleteText(e.target.value)} />
          </div>
        </ReasonModal>
      )}
      {bulk && bulk.kind === 'ban' && (
        <ReasonModal
          open
          title={s.bulkBanTitle}
          message={s.bulkBanBody(bulk.ids.length)}
          confirmLabel={s.actions.ban}
          onCancel={() => setBulk(null)}
          onConfirm={runBulk}
        />
      )}
      <ConfirmModal
        open={Boolean(bulk && bulk.kind === 'revoke')}
        title={s.bulkRevokeTitle}
        message={bulk ? s.bulkRevokeBody(bulk.ids.length) : ''}
        confirmLabel={s.actions.revokeSessions}
        busy={busy}
        onCancel={() => setBulk(null)}
        onConfirm={() => runBulk('').catch(() => {})}
      />
    </div>
  );
}

export default MembersView;
