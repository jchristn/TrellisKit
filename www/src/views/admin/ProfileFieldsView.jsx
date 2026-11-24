import { useCallback, useEffect, useMemo, useState } from 'react';
import { PROFILE_MAX_CARD_FIELDS, ProfileCollectAt, ProfileVisibility } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, pagedQuery } from '../../utils/adminQuery.js';
import { optionsFrom } from '../../utils/labels.js';
import { formatDateTime, formatNumber } from '../../utils/format.js';
import { canEdit, moveInOrder } from '../../utils/profileFieldForm.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import KpiCard from '../../components/KpiCard.jsx';
import Modal from '../../components/Modal.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, ErrorBanner, Loading } from '../../components/States.jsx';
import ProfileFieldEditor, { ProfilePreviewPanel } from './ProfileFieldEditor.jsx';

const s = strings.admin.profileFields;
const c = strings.admin.common;
const col = s.columns;

export const PROFILE_FIELD_FILTERS = Object.freeze(['search', 'type', 'collectAt', 'visibility', 'enabled', 'builtIn', 'filterable']);

/** POST /admin/profile-fields/enumerate query: the table page in profile order. */
export function buildProfileFieldsQuery(filters, paging) {
  return { ...pagedQuery(filters, PROFILE_FIELD_FILTERS, { ...paging, sort: '' }), ordering: 'ProfileOrderAscending' };
}

function YesNo({ value }) {
  return value ? <Icon name="check" size={14} /> : <span className="muted">-</span>;
}

function percent(rate) {
  const n = Number(rate) || 0;
  return Math.round(n * 1000) / 10 + '%';
}

/** Detail of one definition with its stats (fill rate, option counts, recent updates). */
function ProfileFieldDetail({ field, onClose, onEdit, onJson }) {
  const { api } = useAdminAuth();
  const fetcher = useCallback((signal) => api.getProfileFieldStats(field.id, signal), [api, field.id]);
  const stats = useApiQuery(fetcher, { enabled: !field.builtIn });
  const v = field.validation || {};
  const optionLabel = (key) => ((field.options || []).find((o) => o.key === key) || {}).label || key;
  const rules = Object.entries(v).filter(([, value]) => value !== null && value !== false && value !== '' && value !== undefined);
  return (
    <Modal
      open
      size="lg"
      onClose={onClose}
      title={field.label || field.key}
      subtitle={<CopyableId value={field.id} label={col.id} />}
      headerExtra={(
        <>
          <button type="button" className="button button-secondary button-sm" onClick={onJson}><Icon name="code" size={14} /><span>{c.viewJson}</span></button>
          <button type="button" className="button button-primary button-sm" onClick={onEdit}><Icon name="edit" size={14} /><span>{c.edit}</span></button>
        </>
      )}
    >
      <div className="detail-stack">
        <section className="detail-block" aria-labelledby="pf-detail-title">
          <h3 id="pf-detail-title">{s.detail.attributes}</h3>
          <dl className="kv-grid">
            <div className="kv-item"><dt>{col.key}</dt><dd><code className="mono">{field.key}</code></dd></div>
            <div className="kv-item"><dt>{col.type}</dt><dd>{s.types[field.type] || field.type}</dd></div>
            <div className="kv-item"><dt>{col.collectAt}</dt><dd>{s.collectAt[field.collectAt] || field.collectAt}</dd></div>
            <div className="kv-item"><dt>{s.editor.editableBy}</dt><dd>{s.editableBy[field.editableBy] || field.editableBy}</dd></div>
            <div className="kv-item"><dt>{col.visibility}</dt><dd>{s.visibility[field.visibility] || field.visibility}</dd></div>
            <div className="kv-item"><dt>{s.editor.showOnProfile}</dt><dd>{field.showOnProfile ? c.yes : c.no}</dd></div>
            <div className="kv-item"><dt>{col.card}</dt><dd>{field.showOnCard ? c.yes : c.no}</dd></div>
            <div className="kv-item"><dt>{s.editor.searchable}</dt><dd>{field.searchable ? c.yes : c.no}</dd></div>
            <div className="kv-item"><dt>{col.filterable}</dt><dd>{field.filterable ? c.yes : c.no}</dd></div>
            <div className="kv-item"><dt>{col.enabled}</dt><dd>{field.enabled ? c.yes : c.no}</dd></div>
            <div className="kv-item"><dt>{col.order}</dt><dd>{field.profileOrder}</dd></div>
            <div className="kv-item"><dt>{s.detail.updated}</dt><dd>{formatDateTime(field.updatedUtc)}</dd></div>
          </dl>
          {field.helpText && <p className="muted small">{field.helpText}</p>}
          {rules.length > 0 && (
            <p className="small"><strong>{s.editor.validationLegend}:</strong> {rules.map(([k, value]) => k + (value === true ? '' : ' ' + value)).join(', ')}</p>
          )}
        </section>

        {!field.builtIn && (
          <section className="detail-block" aria-labelledby="pf-stats-title">
            <h3 id="pf-stats-title">{s.detail.stats}</h3>
            {stats.loading && !stats.data && <Loading inline />}
            {stats.error && <ErrorBanner message={s.detail.statsError + ' ' + errorMessage(stats.error, '')} onRetry={stats.reload} />}
            {stats.data && (
              <>
                <div className="kpi-grid">
                  <KpiCard label={s.detail.valueCount} value={formatNumber(stats.data.valueCount)} />
                  <KpiCard label={s.detail.fillRate} value={percent(stats.data.fillRate)} hint={s.detail.fillRateHint} />
                  <KpiCard label={s.detail.updated30d} value={formatNumber(stats.data.updated30d)} />
                </div>
                {(stats.data.optionCounts || []).length > 0 && (
                  <div className="table-scroll">
                    <table className="data-table compact">
                      <caption className="visually-hidden">{s.detail.optionCounts}</caption>
                      <thead><tr><th scope="col">{s.editor.optionLabel}</th><th scope="col">{s.editor.optionKey}</th><th scope="col" className="num">{s.detail.members}</th></tr></thead>
                      <tbody>
                        {stats.data.optionCounts.map((row) => {
                          const option = (field.options || []).find((o) => o.key === row.key);
                          return (
                            <tr key={row.key}>
                              <td>
                                {option && option.color && <span className="rank-swatch" style={{ background: option.color }} aria-hidden="true" />} {optionLabel(row.key)}
                                {option && option.retired && <> <Badge>{s.editor.optionRetired}</Badge></>}
                              </td>
                              <td><code className="mono">{row.key}</code></td>
                              <td className="num">{formatNumber(row.count)}</td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                )}
              </>
            )}
          </section>
        )}
        {field.builtIn && <Banner tone="info" icon="info">{s.detail.builtInStats}</Banner>}
      </div>
    </Modal>
  );
}

/** Delete with confirmation; a field with values needs its key typed (values are deleted too). */
function DeleteFieldModal({ field, onClose, onDeleted }) {
  const { api } = useAdminAuth();
  const [typed, setTyped] = useState('');
  const [error, setError] = useState('');
  const [inputError, setInputError] = useState('');
  const [busy, setBusy] = useState(false);
  const hasValues = field.valueCount > 0;
  const confirm = async () => {
    if (hasValues && typed !== field.key) {
      setInputError(s.delete.typeKeyError(field.key));
      return;
    }
    setBusy(true);
    setError('');
    try {
      await api.deleteProfileField(field.id, hasValues ? { deleteValues: true, confirm: field.key } : {});
      onDeleted();
    } catch (err) {
      const fe = getFieldErrors(err);
      if (fe.confirm) setInputError(String(fe.confirm));
      setError(errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };
  return (
    <Modal
      open
      size="sm"
      onClose={busy ? () => {} : onClose}
      title={s.delete.title(field.label || field.key)}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="button" className="button button-danger" onClick={confirm} disabled={busy || (hasValues && typed !== field.key)}>{busy ? s.delete.deleting : s.delete.confirm}</button>
        </>
      )}
    >
      <div className="stack-form">
        <ErrorBanner message={error} />
        {hasValues ? (
          <>
            <Banner tone="warning" icon="alert">{s.delete.withValues(field.valueCount)}</Banner>
            <div className="field">
              <label className="label-text" htmlFor="pf-delete-confirm">{s.delete.typeKey(field.key)}</label>
              <input id="pf-delete-confirm" className="mono" value={typed} onChange={(ev) => { setTyped(ev.target.value); setInputError(''); }} autoComplete="off" spellCheck="false" aria-invalid={Boolean(inputError)} />
              {inputError && <span className="field-error" role="alert">{inputError}</span>}
            </div>
          </>
        ) : <p className="confirm-message">{s.delete.noValues}</p>}
      </div>
    </Modal>
  );
}

/**
 * /admin/profile-fields: every profile field definition (built-ins included) in profile order,
 * with reorder, enable and disable, create and edit, delete, stats, preview, and View JSON.
 */
function ProfileFieldsView() {
  const { api, client } = useAdminAuth();
  const toast = useToast();
  const table = useTableState({ storageKey: 'tk.admin.profileFields', filterKeys: PROFILE_FIELD_FILTERS, defaultSort: '' });
  const [editor, setEditor] = useState(null);
  const [detail, setDetail] = useState(null);
  const [deleting, setDeleting] = useState(null);
  const [jsonRow, setJsonRow] = useState(null);
  const [busyId, setBusyId] = useState(null);
  const [showPreview, setShowPreview] = useState(false);

  const query = useMemo(() => buildProfileFieldsQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.meta]);
  const fetcher = useCallback((signal) => api.listProfileFields(query, signal), [api, query]);
  const list = useApiQuery(fetcher);
  const allFetcher = useCallback((signal) => api.listProfileFields({ maxResults: 200, ordering: 'ProfileOrderAscending' }, signal), [api]);
  const all = useApiQuery(allFetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const rows = page ? page.items : [];
  const fields = useMemo(() => (all.data ? normalizePage(all.data, 200).items : []), [all.data]);
  const order = fields.map((f) => f.id);
  const reorderable = !table.hasFilters;
  const cardCount = fields.filter((f) => !f.builtIn && f.enabled && f.showOnCard).length;

  const reload = () => {
    list.reload();
    all.reload();
  };

  useEffect(() => {
    if (!detail) return;
    const fresh = fields.find((f) => f.id === detail.id);
    if (fresh && fresh.updatedUtc !== detail.updatedUtc) setDetail(fresh);
  }, [fields, detail]);

  const move = async (row, delta) => {
    const next = moveInOrder(order, row.id, delta);
    if (!next) return;
    setBusyId(row.id);
    try {
      await api.reorderProfileFields(next);
      toast.success(s.reordered);
      reload();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusyId(null);
    }
  };

  const toggle = async (row) => {
    setBusyId(row.id);
    try {
      await api.updateProfileField(row.id, { enabled: !row.enabled });
      toast.success(row.enabled ? s.disabledToast(row.label) : s.enabledToast(row.label));
      reload();
    } catch (err) {
      const fe = getFieldErrors(err);
      toast.error(fe.showOnCard ? String(fe.showOnCard) : errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusyId(null);
    }
  };

  const onSaved = (saved, mode) => {
    setEditor(null);
    toast.success(mode === 'edit' ? s.updated(saved.label) : s.created(saved.label));
    reload();
  };

  const columns = [
    {
      key: 'order', label: col.order, className: 'nowrap cell-actions',
      render: (row) => {
        const index = order.indexOf(row.id);
        const title = reorderable ? null : s.reorderFiltered;
        return (
          <span className="nowrap" title={title || undefined}>
            <button type="button" className="icon-button" onClick={() => move(row, -1)} disabled={!reorderable || busyId !== null || index <= 0} aria-label={s.moveUp(row.label)} title={title || s.moveUp(row.label)}><Icon name="chevronUp" size={14} /></button>
            <button type="button" className="icon-button" onClick={() => move(row, 1)} disabled={!reorderable || busyId !== null || index < 0 || index >= order.length - 1} aria-label={s.moveDown(row.label)} title={title || s.moveDown(row.label)}><Icon name="chevronDown" size={14} /></button>
          </span>
        );
      }
    },
    {
      key: 'label', label: col.label,
      render: (row) => (
        <span>
          <strong>{row.label}</strong>
          {row.builtIn && <> <Badge tone="info">{s.builtIn}</Badge></>}
          {row.validation && row.validation.required && <> <Badge tone="warning">{strings.profileFields.required}</Badge></>}
        </span>
      )
    },
    { key: 'key', label: col.key, render: (row) => <code className="mono">{row.key}</code> },
    { key: 'type', label: col.type, render: (row) => s.types[row.type] || row.type },
    { key: 'collectAt', label: col.collectAt, render: (row) => s.collectAt[row.collectAt] || row.collectAt },
    { key: 'visibility', label: col.visibility, render: (row) => <Badge tone={row.visibility === 'Public' ? 'success' : row.visibility === 'Staff' ? 'warning' : 'neutral'}>{s.visibility[row.visibility] || row.visibility}</Badge> },
    { key: 'card', label: col.card, className: 'num', render: (row) => <YesNo value={row.showOnCard} /> },
    { key: 'filterable', label: col.filterable, className: 'num', render: (row) => <YesNo value={row.filterable} /> },
    {
      key: 'enabled', label: col.enabled,
      render: (row) => (
        <label className="toggle" title={canEdit(row, 'enabled') ? undefined : s.alwaysOn}>
          <input type="checkbox" role="switch" checked={row.enabled !== false} onChange={() => toggle(row)} disabled={!canEdit(row, 'enabled') || busyId !== null} aria-label={s.toggleLabel(row.label)} />
          <span className="toggle-track" aria-hidden="true"><span className="toggle-thumb" /></span>
        </label>
      )
    },
    { key: 'values', label: col.values, className: 'num', render: (row) => (row.builtIn ? <span className="muted">-</span> : formatNumber(row.valueCount || 0)) },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>,
      render: (row) => (
        <ActionMenu
          label={c.rowActions(row.label)}
          items={[
            { label: c.view, icon: 'eye', onSelect: () => setDetail(row) },
            { label: c.edit, icon: 'edit', onSelect: () => setEditor({ field: row }) },
            { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) },
            ...(row.builtIn ? [] : [{ label: s.delete.action, icon: 'trash', danger: true, onSelect: () => setDeleting(row) }])
          ]}
        />
      )
    }
  ];

  const filterFields = [
    { key: 'search', label: s.searchLabel, placeholder: s.searchPlaceholder, grow: true },
    { key: 'type', label: col.type, type: 'select', options: optionsFrom(s.types) },
    { key: 'collectAt', label: col.collectAt, type: 'select', options: optionsFrom(s.collectAt, Object.values(ProfileCollectAt)) },
    { key: 'visibility', label: col.visibility, type: 'select', options: optionsFrom(s.visibility, Object.values(ProfileVisibility)) },
    { key: 'enabled', label: col.enabled, type: 'select', options: [{ value: 'true', label: c.yes }, { value: 'false', label: c.no }] },
    { key: 'builtIn', label: s.builtIn, type: 'select', options: [{ value: 'true', label: c.yes }, { value: 'false', label: c.no }] },
    { key: 'filterable', label: s.filterableOnly, type: 'checkbox' }
  ];

  const route = strings.admin.routes['profile-fields'];
  return (
    <div className="view">
      <PageHeader
        title={route.label}
        summary={route.summary}
        actions={(
          <>
            <button type="button" className="button button-secondary button-sm" onClick={() => setShowPreview((v) => !v)} aria-expanded={showPreview}>
              <Icon name="eye" size={14} /><span>{s.preview.toggle}</span>
            </button>
            <button type="button" className="button button-primary button-sm" onClick={() => setEditor({ field: null })} disabled={!all.data}>
              <Icon name="plus" size={14} /><span>{s.create}</span>
            </button>
          </>
        )}
      />
      <Banner tone="info" icon="info">{s.cardSummary(cardCount, PROFILE_MAX_CARD_FIELDS)}</Banner>
      {showPreview && <section className="panel"><ProfilePreviewPanel idPrefix="pf-page-preview" /></section>}
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={client} targetType="ProfileField" />
      </section>
      <section className="table-section">
        {!reorderable && <p className="muted small">{s.reorderFiltered}</p>}
        <TablePagination
          pageNumber={table.pageNumber}
          pageSize={table.pageSize}
          totalCount={page ? page.totalCount : 0}
          totalPages={page ? page.totalPages : 1}
          onPageChange={table.setPage}
          onPageSizeChange={table.setPageSize}
          onRefresh={reload}
          loading={list.loading}
          pageSizes={ADMIN_PAGE_SIZES}
        />
        <DataTable
          caption={route.label}
          columns={columns}
          rows={rows}
          rowKey={(row) => row.id}
          loading={list.loading}
          error={list.error ? c.unavailable(route.label) + ' ' + errorMessage(list.error, '') : null}
          onRetry={list.reload}
          emptyMessage={table.hasFilters ? s.emptyFiltered : s.empty}
          onRowClick={(row) => setDetail(row)}
          rowClassName={(row) => (row.enabled === false ? 'row-muted' : '')}
        />
      </section>

      {editor && <ProfileFieldEditor field={editor.field} fields={fields} onClose={() => setEditor(null)} onSaved={onSaved} />}
      {detail && !editor && <ProfileFieldDetail field={detail} onClose={() => setDetail(null)} onEdit={() => setEditor({ field: detail })} onJson={() => setJsonRow(detail)} />}
      {deleting && <DeleteFieldModal field={deleting} onClose={() => setDeleting(null)} onDeleted={() => { toast.success(s.delete.done(deleting.label)); setDeleting(null); reload(); }} />}
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.key} value={jsonRow} />
    </div>
  );
}

export default ProfileFieldsView;
