import { useCallback, useMemo, useState } from 'react';
import { InviteDelivery, PlatformAdminOrderings, isEmail } from '@trelliskit/shared';
import strings from '../../strings.js';
import { usePlatformAuth } from '../../context/PlatformAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES } from '../../utils/adminQuery.js';
import { PLATFORM_ADMIN_TABLE_FILTERS, buildPlatformAdminsQuery, platformInviteLink, platformMetadataSource } from '../../utils/platformQuery.js';
import { formatDateTime, formatShortDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import CopyButton from '../../components/CopyButton.jsx';
import Modal from '../../components/Modal.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, ErrorBanner } from '../../components/States.jsx';

const t = strings.platform.admins;
const col = t.columns;
const c = strings.admin.common;
const route = strings.platform.routes.admins;

/**
 * The outcome of an invitation: emailed (no token is returned), or, without a deployment email
 * provider, the one-time token and its accept link, shown once.
 */
export function InviteResult({ result }) {
  const email = result.admin ? result.admin.email : '';
  const expires = formatDateTime(result.inviteExpiresUtc);
  if (result.inviteDelivery === InviteDelivery.Email || !result.inviteToken) {
    return (
      <div className="stack-form">
        <Banner tone="success" icon="check">{t.inviteEmailed(email)}</Banner>
        <p className="muted small">{t.inviteEmailedNote(expires)}</p>
      </div>
    );
  }
  const link = platformInviteLink(result.inviteToken);
  return (
    <div className="stack-form">
      <p>{t.inviteCreatedBody(email, expires)}</p>
      <Banner tone="warning" icon="alert">{t.inviteLogNote}</Banner>
      <span className="label-text">{t.inviteToken}</span>
      <div className="copy-row">
        <code id="invite-token-value" className="mono break">{result.inviteToken}</code>
        <CopyButton value={result.inviteToken} label={t.inviteToken} />
      </div>
      <span className="label-text">{t.inviteLink}</span>
      <div className="copy-row">
        <code className="mono break">{link}</code>
        <CopyButton value={link} label={t.inviteLink} />
      </div>
    </div>
  );
}

function InviteModal({ open, onClose, onInvited }) {
  const { call } = usePlatformAuth();
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState(null);

  const submit = async (ev) => {
    ev.preventDefault();
    if (!isEmail(email.trim()) || !displayName.trim()) {
      setError(t.inviteRequired);
      return;
    }
    setBusy(true);
    setError('');
    try {
      const response = await call((client) => client.invitePlatformAdmin({ email: email.trim(), displayName: displayName.trim() }));
      setResult(response);
      onInvited();
    } catch (err) {
      const fe = getFieldErrors(err);
      setError(Object.keys(fe).length ? Object.values(fe).join(' ') : errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={busy ? () => {} : onClose}
      title={result ? t.inviteCreatedTitle : t.inviteTitle}
      size="sm"
      footer={result ? (
        <button type="button" className="button button-primary" onClick={onClose}>{strings.common.close}</button>
      ) : (
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="invite-admin-form" className="button button-primary" disabled={busy}>{busy ? t.inviting : t.inviteSubmit}</button>
        </>
      )}
    >
      {result ? <InviteResult result={result} /> : (
        <form id="invite-admin-form" className="stack-form" onSubmit={submit} noValidate>
          <ErrorBanner message={error} />
          <label className="field" htmlFor="invite-email">
            <span className="label-text">{t.email}</span>
            <input id="invite-email" type="email" value={email} onChange={(x) => setEmail(x.target.value)} />
          </label>
          <label className="field" htmlFor="invite-name">
            <span className="label-text">{t.displayName}</span>
            <input id="invite-name" value={displayName} onChange={(x) => setDisplayName(x.target.value)} />
          </label>
        </form>
      )}
    </Modal>
  );
}

/** Platform administrators: enumerate, invite (one-time token shown once), remove. */
function PlatformAdminsView() {
  const { call, client, admin: me } = usePlatformAuth();
  const toast = useToast();
  const metadataSource = useMemo(() => platformMetadataSource(client), [client]);
  const table = useTableState({ storageKey: 'tk.platform.admins', filterKeys: PLATFORM_ADMIN_TABLE_FILTERS, defaultSort: '' });
  const [inviting, setInviting] = useState(0);
  const [removing, setRemoving] = useState(null);
  const [jsonRow, setJsonRow] = useState(null);
  const query = useMemo(() => buildPlatformAdminsQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.meta]);
  const fetcher = useCallback((signal) => call((client) => client.listPlatformAdmins(query, signal)), [call, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;

  const columns = [
    { key: 'name', label: col.name, render: (row) => <span className="member-cell-text"><span>{row.displayName}{me && me.id === row.id ? ' (' + t.you + ')' : ''}</span><code className="mono small">{row.id}</code></span> },
    { key: 'email', label: col.email, render: (row) => row.email },
    { key: 'status', label: col.status, render: (row) => (row.invitationPending ? <Badge tone="warning">{t.pending}</Badge> : <Badge tone="success">{t.active}</Badge>) },
    { key: 'lastLogin', label: col.lastLogin, className: 'nowrap', render: (row) => (row.lastLoginUtc ? formatShortDateTime(row.lastLoginUtc) : <span className="muted">{c.never}</span>) },
    { key: 'created', label: col.created, className: 'nowrap', render: (row) => formatShortDateTime(row.createdUtc) },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>,
      render: (row) => (
        <ActionMenu
          label={c.rowActions(row.email)}
          items={[
            { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) },
            ...(me && me.id === row.id ? [] : [{ label: t.remove, icon: 'trash', danger: true, onSelect: () => setRemoving(row) }])
          ]}
        />
      )
    }
  ];

  const filterFields = [
    { key: 'search', label: t.search, placeholder: t.searchPlaceholder },
    { key: 'ordering', label: t.ordering, type: 'select', options: PlatformAdminOrderings.map((o) => ({ value: o, label: strings.platform.orderings[o] })) }
  ];

  return (
    <div className="view">
      <PageHeader
        title={route.label}
        summary={route.summary}
        actions={<button type="button" className="button button-primary button-sm" onClick={() => setInviting((n) => n + 1)}><Icon name="plus" size={14} /><span>{t.invite}</span></button>}
      />
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={metadataSource} targetType="PlatformAdmin" />
      </section>
      <section className="table-section">
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
          caption={route.label}
          columns={columns}
          rows={page ? page.items : []}
          rowKey={(row) => row.id}
          loading={list.loading}
          error={list.error ? c.unavailable(route.label) + ' ' + errorMessage(list.error, '') : null}
          onRetry={list.reload}
          emptyMessage={t.empty}
          onRowClick={(row) => setJsonRow(row)}
        />
      </section>
      {inviting > 0 && <InviteModal key={inviting} open onClose={() => setInviting(0)} onInvited={list.reload} />}
      <ConfirmModal
        open={Boolean(removing)}
        title={t.removeTitle}
        message={removing ? t.removeBody(removing.displayName || removing.email) : ''}
        confirmLabel={t.remove}
        onCancel={() => setRemoving(null)}
        onConfirm={async () => {
          const target = removing;
          setRemoving(null);
          try {
            await call((client) => client.removePlatformAdmin(target.id));
            toast.success(t.removed);
            list.reload();
          } catch (err) {
            toast.error(errorMessage(err, strings.common.errorGeneric));
          }
        }}
      />
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.id} value={jsonRow} />
    </div>
  );
}

export default PlatformAdminsView;
