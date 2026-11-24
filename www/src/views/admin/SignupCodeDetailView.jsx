import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { browser } from '../../utils/browser.js';
import { normalizePage } from '../../utils/query.js';
import { qrFileName, qrPngDataUrl } from '../../utils/qr.js';
import { ADMIN_PAGE_SIZES, REDEMPTION_DEFAULT_SORT, REDEMPTION_FILTERS, buildRedemptionsQuery, cleanQuery } from '../../utils/adminQuery.js';
import { codeStatusTone, optionsFrom, redemptionTone } from '../../utils/labels.js';
import { formatDateTime, formatNumber, formatShortDate } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import KpiCard from '../../components/KpiCard.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import ReasonModal from '../../components/ReasonModal.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import CopyButton from '../../components/CopyButton.jsx';
import RedemptionsChart from '../../components/RedemptionsChart.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import { SignupCodeFormModal } from './SignupCodeForms.jsx';
import { joinLink, usesText } from './SignupCodesView.jsx';
import { fetchCsvPages } from '../../utils/csv.js';
import useLivePurchases from '../../hooks/useLivePurchases.js';

const k = strings.admin.codes;
const d = k.detail;
const c = strings.admin.common;
const fl = k.fields;
const rc = d.redemptionColumns;

function QrPanel({ code }) {
  const [state, setState] = useState({ url: null, error: false });
  const link = joinLink(code);
  useEffect(() => {
    let active = true;
    qrPngDataUrl(link).then(
      (url) => { if (active) setState({ url, error: false }); },
      () => { if (active) setState({ url: null, error: true }); }
    );
    return () => { active = false; };
  }, [link]);
  return (
    <section className="panel qr-panel" aria-labelledby="qr-title">
      <h2 id="qr-title">{d.qrTitle}</h2>
      {state.error && <p className="muted small">{d.qrError}</p>}
      {!state.error && !state.url && <Loading inline label={d.qrLoading} />}
      {state.url && <img className="qr-image" src={state.url} alt={d.qrAlt(code.code)} width={180} height={180} />}
      <button type="button" className="button button-secondary button-sm" disabled={!state.url} onClick={() => browser.downloadDataUrl(state.url, qrFileName(code.code))}>
        <Icon name="download" size={14} /><span>{d.qrDownload}</span>
      </button>
    </section>
  );
}

/**
 * Sign-up code detail: overview, join link with QR code, redemptions over time (SVG), outcomes,
 * and the redemptions table with revoke and revoke-all (reason required) and CSV export.
 */
function SignupCodeDetailView() {
  const { id } = useParams();
  const { api } = useAdminAuth();
  const readOnly = !useLivePurchases().enabled;
  const toast = useToast();
  const codeFetcher = useCallback((signal) => api.getSignupCode(id, signal), [api, id]);
  const codeQuery = useApiQuery(codeFetcher);
  const code = codeQuery.data;

  const adminClient = useAdminAuth().client;
  const table = useTableState({ storageKey: 'tk.admin.redemptions', filterKeys: REDEMPTION_FILTERS, defaultSort: REDEMPTION_DEFAULT_SORT, prefix: 'r.' });
  const query = useMemo(() => buildRedemptionsQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.sort, table.meta]);
  const listFetcher = useCallback((signal) => api.listCodeRedemptions(id, query, signal), [api, id, query]);
  const list = useApiQuery(listFetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;

  const [editing, setEditing] = useState(false);
  const [revoking, setRevoking] = useState(null);
  const [revokeAll, setRevokeAll] = useState(false);
  const [deactivating, setDeactivating] = useState(false);
  const navigate = useNavigate();
  const [deleting, setDeleting] = useState(false);
  const [jsonValue, setJsonValue] = useState(null);
  const [busy, setBusy] = useState(false);

  const reloadAll = () => {
    codeQuery.reload();
    list.reload();
  };

  const exportCsv = async () => {
    setBusy(true);
    try {
      const result = await fetchCsvPages((query) => api.exportCodeRedemptions(id, query), cleanQuery(table.filters, REDEMPTION_FILTERS));
      browser.download(result.text, 'redemptions-' + (code ? code.code.toLowerCase() : id) + '.csv', 'text/csv');
      if (result.complete) toast.success(c.exported);
      else toast.error(c.exportTruncated);
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  const deactivate = async () => {
    setBusy(true);
    try {
      await api.deactivateSignupCode(id);
      toast.success(k.deactivated);
      setDeactivating(false);
      codeQuery.reload();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    setBusy(true);
    try {
      await api.deleteSignupCode(id);
      toast.success(k.deleted);
      navigate('/admin/signup-codes');
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
      setDeleting(false);
    }
  };

  if (codeQuery.loading && !code) return <div className="view"><Loading /></div>;
  if (!code) {
    return (
      <div className="view">
        <Link to="/admin/signup-codes" className="small back-link"><Icon name="chevronLeft" size={14} />{d.back}</Link>
        <ErrorBanner message={d.loadError + ' ' + errorMessage(codeQuery.error, '')} onRetry={codeQuery.reload} />
      </div>
    );
  }

  const outcomes = code.outcomes || {};
  const activeRedemptions = typeof outcomes.active === 'number' ? outcomes.active : (code.redemptionCount || 0);
  const link = joinLink(code);

  const columns = [
    { key: 'member', label: rc.member, render: (row) => <span className="member-cell-text"><Link to={'/admin/members?member=' + encodeURIComponent(row.userId)}>{row.memberDisplayName || row.userId}</Link><span className="muted small">{row.memberEmail}</span></span> },
    { key: 'redeemed', label: rc.redeemed, sortField: 'redeemedUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => <span title={formatDateTime(row.redeemedUtc)}>{formatShortDate(row.redeemedUtc)}</span> },
    { key: 'ends', label: rc.ends, className: 'nowrap', render: (row) => (row.membershipEndsUtc ? formatShortDate(row.membershipEndsUtc) : strings.admin.members.lifetime) },
    { key: 'account', label: rc.account, render: (row) => (row.isNewAccount ? <Badge tone="info">{d.newAccount}</Badge> : <span className="muted">{d.existingAccount}</span>) },
    { key: 'status', label: rc.status, render: (row) => <Badge tone={redemptionTone(row.status)}>{strings.enums.redemptionStatus[row.status] || row.status}</Badge> },
    { key: 'converted', label: rc.converted, render: (row) => (row.convertedToPaid ? c.yes : c.no) },
    { key: 'revoked', label: rc.revoked, className: 'cell-wrap', render: (row) => (row.revokedUtc ? <span title={row.revokeReason || ''}>{formatShortDate(row.revokedUtc)}{row.revokeReason ? ': ' + row.revokeReason : ''}</span> : '-') },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{rc.actions}</span>,
      render: (row) => (
        <ActionMenu
          label={c.rowActions(row.memberDisplayName || row.id)}
          items={[
            { label: c.viewJson, icon: 'code', onSelect: () => setJsonValue(row) },
            ...(row.status === 'Active' ? [{ label: d.revoke, icon: 'ban', danger: true, onSelect: () => setRevoking(row) }] : [])
          ]}
        />
      )
    }
  ];

  return (
    <div className="view">
      <Link to="/admin/signup-codes" className="small back-link"><Icon name="chevronLeft" size={14} />{d.back}</Link>
      <PageHeader
        title={<span className="copyable-id"><code className="mono">{code.code}</code><CopyButton value={code.code} label={fl.code} /></span>}
        summary={code.name}
        actions={(
          <>
            <button type="button" className="button button-secondary button-sm" onClick={() => setJsonValue(code)}><Icon name="code" size={14} /><span>{c.viewJson}</span></button>
            {!readOnly && <button type="button" className="button button-secondary button-sm" onClick={() => setEditing(true)}><Icon name="gear" size={14} /><span>{c.edit}</span></button>}
            {code.isActive && <button type="button" className="button button-danger-outline button-sm" onClick={() => setDeactivating(true)}><Icon name="ban" size={14} /><span>{k.actions.deactivate}</span></button>}
            {!readOnly && !code.redemptionCount && <button type="button" className="button button-danger-outline button-sm" onClick={() => setDeleting(true)}><Icon name="trash" size={14} /><span>{k.actions.delete}</span></button>}
          </>
        )}
      >
        <div className="badge-row"><Badge tone={codeStatusTone(code.status)}>{strings.enums.codeStatus[code.status] || code.status}</Badge></div>
      </PageHeader>

      <div className="kpi-grid">
        <KpiCard label={fl.uses} value={usesText(code)} />
        <KpiCard label={d.outcomes.active} value={formatNumber(outcomes.active || 0)} tone="success" />
        <KpiCard label={d.outcomes.convertedToPaid} value={formatNumber(outcomes.convertedToPaid || 0)} />
        <KpiCard label={d.outcomes.lapsed} value={formatNumber(outcomes.lapsed || 0)} />
        <KpiCard label={d.outcomes.revoked} value={formatNumber(outcomes.revoked || 0)} tone={outcomes.revoked ? 'danger' : undefined} />
      </div>

      <div className="detail-columns">
        <section className="panel" aria-labelledby="code-overview-title">
          <h2 id="code-overview-title">{d.overview}</h2>
          <dl className="kv-grid">
            <div className="kv-item"><dt>{fl.grant}</dt><dd>{code.grantSummary}</dd></div>
            {code.grantType === 'Discount' && <div className="kv-item"><dt>{fl.stripeCoupon}</dt><dd>{code.stripeCouponId ? <CopyableId value={code.stripeCouponId} label={fl.stripeCoupon} /> : <span className="muted">{fl.stripeCouponPending}</span>}</dd></div>}
            <div className="kv-item"><dt>{fl.starts}</dt><dd title={code.startsUtc || ''}>{code.startsUtc ? formatDateTime(code.startsUtc) : '-'}</dd></div>
            <div className="kv-item"><dt>{fl.expires}</dt><dd title={code.expiresUtc || ''}>{code.expiresUtc ? formatDateTime(code.expiresUtc) : '-'}</dd></div>
            <div className="kv-item"><dt>{fl.lastRedeemed}</dt><dd>{code.lastRedeemedUtc ? formatDateTime(code.lastRedeemedUtc) : c.never}</dd></div>
            <div className="kv-item"><dt>{fl.tags}</dt><dd>{(code.tags || []).length ? code.tags.map((t) => <Badge key={t}>{t}</Badge>) : '-'}</dd></div>
            {code.batchId && <div className="kv-item"><dt>{fl.batch}</dt><dd><CopyableId value={code.batchId} label={fl.batch} /></dd></div>}
            <div className="kv-item"><dt>{fl.createdBy}</dt><dd>{code.createdByUserId ? <CopyableId value={code.createdByUserId} label={fl.createdBy} /> : '-'}</dd></div>
            <div className="kv-item"><dt>{fl.created}</dt><dd title={code.createdUtc}>{formatDateTime(code.createdUtc)}</dd></div>
            <div className="kv-item"><dt>{fl.id}</dt><dd><CopyableId value={code.id} label={fl.id} /></dd></div>
          </dl>
          {code.description && <div><h3 className="label-text">{fl.description}</h3><p>{code.description}</p></div>}
          {code.notes && <div><h3 className="label-text">{fl.notes}</h3><p className="muted">{code.notes}</p></div>}
          <div>
            <h3 className="label-text">{d.joinLink}</h3>
            <span className="copyable-id"><code className="mono truncate" title={link}>{link}</code><CopyButton value={link} label={d.joinLink} /></span>
          </div>
        </section>
        <QrPanel code={code} />
      </div>

      <section className="panel" aria-labelledby="code-chart-title">
        <h2 id="code-chart-title">{d.chartTitle}</h2>
        <RedemptionsChart points={code.redemptionsByDay || []} />
      </section>

      <section className="table-section" aria-labelledby="code-redemptions-title">
        <div className="panel-header">
          <h2 id="code-redemptions-title">{d.redemptionsTitle}</h2>
          <div className="panel-actions">
            <button type="button" className="button button-secondary button-sm" onClick={exportCsv} disabled={busy}><Icon name="download" size={14} /><span>{d.exportRedemptions}</span></button>
            <button type="button" className="button button-danger-outline button-sm" onClick={() => setRevokeAll(true)} disabled={activeRedemptions === 0}><Icon name="ban" size={14} /><span>{d.revokeAll}</span></button>
          </div>
        </div>
        <AdminFilterPanel fields={[{ key: 'status', label: d.redemptionStatus, type: 'select', options: optionsFrom(strings.enums.redemptionStatus) }]} table={table} client={adminClient} targetType="SignupCodeRedemption" />
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
          caption={d.redemptionsTitle}
          columns={columns}
          rows={page ? page.items : []}
          rowKey={(row) => row.id}
          loading={list.loading}
          error={list.error ? errorMessage(list.error, strings.common.errorGeneric) : null}
          onRetry={list.reload}
          emptyMessage={d.redemptionsEmpty}
          onRowClick={(row) => setJsonValue(row)}
          sort={table.sort}
          onSortChange={table.setSort}
        />
      </section>

      {editing && <SignupCodeFormModal mode="edit" code={code} onClose={() => setEditing(false)} onSaved={() => { setEditing(false); toast.success(k.form.updated); codeQuery.reload(); }} />}
      {revoking && (
        <ReasonModal
          open
          title={d.revokeTitle(revoking.memberDisplayName || revoking.userId)}
          message={d.revokeBody}
          confirmLabel={d.revoke}
          onCancel={() => setRevoking(null)}
          onConfirm={async (reason) => {
            await api.revokeRedemption(revoking.id, reason);
            toast.success(d.revoked);
            setRevoking(null);
            reloadAll();
          }}
        />
      )}
      {revokeAll && (
        <ReasonModal
          open
          title={d.revokeAllTitle(code.code)}
          message={d.revokeAllBody(activeRedemptions)}
          confirmLabel={d.revokeAll}
          onCancel={() => setRevokeAll(false)}
          onConfirm={async (reason) => {
            const result = await api.revokeAllRedemptions(id, reason);
            toast.success(d.revokedAll(result && typeof result.revokedCount === 'number' ? result.revokedCount : 0));
            setRevokeAll(false);
            reloadAll();
          }}
        />
      )}
      <ConfirmModal open={deleting} title={k.deleteTitle(code.code)} message={k.deleteBody} confirmLabel={k.actions.delete} busy={busy} onConfirm={remove} onCancel={() => setDeleting(false)} />
      <ConfirmModal open={deactivating} title={k.deactivateTitle(code.code)} message={k.deactivateBody} confirmLabel={k.actions.deactivate} busy={busy} onConfirm={deactivate} onCancel={() => setDeactivating(false)} />
      <JsonViewerModal open={Boolean(jsonValue)} onClose={() => setJsonValue(null)} subtitle={jsonValue && jsonValue.id} value={jsonValue} />
    </div>
  );
}

export default SignupCodeDetailView;
