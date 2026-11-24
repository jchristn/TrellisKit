import useCollectionPage from '../../hooks/useCollectionPage.js';
import { useCallback, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { collectEnumeration, EmailCategory, EmailMessageOrderings, EmailStatus, EmailSuppressionOrderings, EmailSuppressionReason } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, pagedQuery } from '../../utils/adminQuery.js';
import { optionsFrom } from '../../utils/labels.js';
import { formatDateTime, formatShortDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import CopyButton from '../../components/CopyButton.jsx';
import MetadataEditor from '../../components/MetadataEditor.jsx';
import Modal from '../../components/Modal.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, ErrorBanner, Loading } from '../../components/States.jsx';
import EmailTemplateEditor from './EmailTemplateEditor.jsx';
import TableColumns from '../../components/TableColumns.jsx';

const s = strings.admin.email;
const c = strings.admin.common;
const TABS = Object.freeze(['templates', 'staffEvents', 'messages', 'suppressions']);
const STATUS_TONES = { Queued: 'info', Sent: 'neutral', Delivered: 'success', Failed: 'danger', Suppressed: 'warning' };
const STAFF_RECIPIENTS_MAX = 10;

export const EMAIL_MESSAGE_FILTERS = Object.freeze(['search', 'status', 'template', 'category', 'recipient', 'userId', 'order']);
export const EMAIL_SUPPRESSION_FILTERS = Object.freeze(['search', 'reason', 'order']);

/** POST /admin/email/messages/enumerate query (`order` is the ordering select). */
export function buildEmailMessagesQuery(filters, paging) {
  const order = filters && EmailMessageOrderings.includes(filters.order) ? filters.order : 'CreatedDescending';
  return { ...pagedQuery(filters, EMAIL_MESSAGE_FILTERS, { ...paging, sort: '' }, ({ order: _order, ...rest }) => rest), ordering: order };
}

/** POST /admin/email/suppressions/enumerate query. */
export function buildEmailSuppressionsQuery(filters, paging) {
  const order = filters && EmailSuppressionOrderings.includes(filters.order) ? filters.order : 'CreatedDescending';
  return { ...pagedQuery(filters, EMAIL_SUPPRESSION_FILTERS, { ...paging, sort: '' }, ({ order: _order, ...rest }) => rest), ordering: order };
}

/** Parse a recipients textarea (one address per line, or separated by commas). */
export function parseRecipients(text) {
  return String(text || '').split(/[\s,;]+/).map((x) => x.trim()).filter(Boolean);
}

function validEmail(text) {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(text);
}

/** "Send test email" dialog: any template (default `test`) to the caller or a typed address. */
export function SendTestModal({ templates, onClose }) {
  const { api } = useAdminAuth();
  const [template, setTemplate] = useState('');
  const [to, setTo] = useState('');
  const [result, setResult] = useState(null);
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);
  const send = async (ev) => {
    ev.preventDefault();
    if (to.trim() && !validEmail(to.trim())) {
      setErrors({ to: s.test.toInvalid });
      return;
    }
    setBusy(true);
    setErrors({});
    setResult(null);
    try {
      const response = template === '__catalog'
        ? await api.sendTestEmailCatalog({ to: to.trim() || undefined })
        : await api.sendTestEmail({ template: template || undefined, to: to.trim() || undefined });
      setResult(template === '__catalog' ? { ...response, ok: response.failed === 0, message: s.test.catalogResult(response.sent, response.failed) } : response);
    } catch (err) {
      const fe = getFieldErrors(err);
      setErrors(Object.fromEntries(Object.entries(fe).map(([k, v]) => [k, String(v)])));
      setResult({ ok: false, message: errorMessage(err, strings.common.errorGeneric) });
    } finally {
      setBusy(false);
    }
  };
  return (
    <Modal
      open
      size="sm"
      onClose={busy ? () => {} : onClose}
      title={s.test.title}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.close}</button>
          <button type="submit" form="email-test-form" className="button button-primary" disabled={busy}>{busy ? s.test.sending : template === '__catalog' ? s.test.sendCatalog : s.test.send}</button>
        </>
      )}
    >
      <form id="email-test-form" className="stack-form" onSubmit={send} noValidate>
        <div className="field">
          <label className="label-text" htmlFor="email-test-template">{s.test.template}</label>
          <select id="email-test-template" value={template} onChange={(ev) => setTemplate(ev.target.value)} disabled={busy} aria-invalid={Boolean(errors.template)}>
            <option value="">{s.test.defaultTemplate}</option>
            <option value="__catalog">{s.test.allTemplates}</option>
            {(templates || []).map((t) => <option key={t.key} value={t.key}>{t.key}</option>)}
          </select>
          {template === '__catalog' && <p className="muted small">{s.test.catalogHint(templates?.length || 0)}</p>}
          {errors.template && <span className="field-error" role="alert">{errors.template}</span>}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="email-test-to">{s.test.to}</label>
          <input id="email-test-to" type="email" value={to} onChange={(ev) => setTo(ev.target.value)} placeholder={s.test.toPlaceholder} disabled={busy} aria-invalid={Boolean(errors.to)} />
          {errors.to && <span className="field-error" role="alert">{errors.to}</span>}
        </div>
        {result && <Banner tone={result.ok ? 'success' : 'danger'} icon={result.ok ? 'check' : 'alert'}>{result.message || (result.ok ? s.test.ok : s.test.failed)}</Banner>}
        {result?.results && <div className="table-scroll"><table className="data-table"><caption>{s.test.catalogResults}</caption><thead><tr><th>{s.test.template}</th><th>{s.test.outcome}</th></tr></thead><tbody>{result.results.map((row) => <tr key={row.template}><th scope="row">{row.template}</th><td>{row.message || (row.ok ? s.test.ok : s.test.failed)}</td></tr>)}</tbody></table></div>}
      </form>
    </Modal>
  );
}

function useTemplates() {
  const { api } = useAdminAuth();
  const fetcher = useCallback((signal) => collectEnumeration((q, sig) => api.listEmailTemplates(q, sig), {}, { signal }), [api]);
  return useApiQuery(fetcher);
}

function TemplatesTab() {
  const { api } = useAdminAuth();
  const pagination = useCollectionPage();
  const { query: paginationQuery, setResult: paginationSetResult } = pagination;
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState('');
  const [editing, setEditing] = useState(null);
  const [jsonRow, setJsonRow] = useState(null);
  const fetcher = useCallback(async (signal) => { const page = await api.listEmailTemplates({ ...paginationQuery, search, filters: { category } }, signal); paginationSetResult(page); return page; }, [api, paginationQuery, paginationSetResult, search, category]);
  const templatesQuery = useApiQuery(fetcher);
  const templates = templatesQuery.data?.objects || [];
  const needle = search.trim().toLowerCase();
  const rows = templates.filter((t) => (!category || t.category === category) && (!needle || t.key.toLowerCase().includes(needle) || String(t.description || '').toLowerCase().includes(needle)));
  const columns = [
    { key: 'key', label: s.columns.key, render: (row) => <code className="mono">{row.key}</code> },
    { key: 'category', label: s.columns.category, render: (row) => <Badge tone={row.category === 'Staff' ? 'info' : 'neutral'}>{s.categories[row.category] || row.category}</Badge> },
    { key: 'recipient', label: s.columns.recipient, render: (row) => s.recipients[row.recipient] || row.recipient },
    { key: 'description', label: s.columns.description, render: (row) => <span className="small">{row.description}</span> },
    { key: 'override', label: s.columns.override, render: (row) => (row.override ? <Badge tone="info" title={formatDateTime(row.override.updatedUtc)}>{s.overridden}</Badge> : <span className="muted small">{s.engineDefault}</span>) },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{s.columns.actions}</span>,
      render: (row) => (
        <ActionMenu
          label={c.rowActions(row.key)}
          items={[
            { label: c.edit, icon: 'edit', onSelect: () => setEditing(row) },
            { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) }
          ]}
        />
      )
    }
  ];
  return (
    <>
      <section className="panel">
        <div className="filter-bar">
          <label className="field field-compact field-grow" htmlFor="email-tpl-search">
            <span className="label-text">{s.templateSearch}</span>
            <input id="email-tpl-search" className="input-compact" value={search} onChange={(ev) => setSearch(ev.target.value)} placeholder={s.templateSearchPlaceholder} />
          </label>
          <label className="field field-compact" htmlFor="email-tpl-category">
            <span className="label-text">{s.columns.category}</span>
            <select id="email-tpl-category" className="select-compact" value={category} onChange={(ev) => setCategory(ev.target.value)}>
              <option value="">{c.any}</option>
              {Object.values(EmailCategory).map((x) => <option key={x} value={x}>{s.categories[x]}</option>)}
            </select>
          </label>
        </div>
        <p className="muted small">{s.catalogNote}</p>
      </section>
      <section className="table-section">
        <TablePagination {...pagination.props} onRefresh={templatesQuery.reload} loading={templatesQuery.loading} />
        <DataTable
          caption={s.tabs.templates}
          columns={columns}
          rows={rows}
          rowKey={(row) => row.key}
          loading={templatesQuery.loading}
          error={templatesQuery.error ? c.unavailable(s.tabs.templates) + ' ' + errorMessage(templatesQuery.error, '') : null}
          onRetry={templatesQuery.reload}
          emptyMessage={s.templatesEmpty}
          onRowClick={(row) => setEditing(row)}
        />
      </section>
      {editing && (
        <EmailTemplateEditor
          key={editing.key}
          template={editing}
          onClose={() => setEditing(null)}
          onSaved={(_, message) => { toast.success(message); setEditing(null); templatesQuery.reload(); }}
        />
      )}
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.key} value={jsonRow} />
    </>
  );
}

function StaffEventRow({ event, onSaved }) {
  const { api } = useAdminAuth();
  const [text, setText] = useState((event.recipients || []).join('\n'));
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);
  const initial = (event.recipients || []).join('\n');

  const save = async (patch) => {
    setBusy(true);
    setErrors({});
    try {
      const saved = await api.updateEmailStaffEvent(event.template, patch);
      onSaved(saved);
    } catch (err) {
      const fe = getFieldErrors(err);
      setErrors(Object.keys(fe).length ? Object.fromEntries(Object.entries(fe).map(([k, v]) => [k, String(v)])) : { recipients: errorMessage(err, strings.common.errorGeneric) });
    } finally {
      setBusy(false);
    }
  };

  const saveRecipients = () => {
    const list = parseRecipients(text);
    const local = {};
    if (list.length > STAFF_RECIPIENTS_MAX) local.recipients = s.staff.tooMany(STAFF_RECIPIENTS_MAX);
    const bad = list.filter((x) => !validEmail(x));
    if (bad.length) local.recipients = s.staff.invalid(bad.join(', '));
    setErrors(local);
    if (Object.keys(local).length) return;
    save({ recipients: list });
  };

  const id = 'staff-' + event.template;
  const recipientErrors = Object.entries(errors).filter(([k]) => k.startsWith('recipients')).map(([, v]) => v);
  return (
    <tr>
      <td>
        <code className="mono">{event.template}</code>
        <div className="muted small">{event.description}</div>
      </td>
      <td>
        <label className="toggle">
          <input type="checkbox" role="switch" checked={event.enabled} onChange={() => save({ enabled: !event.enabled })} disabled={busy} aria-label={s.staff.enabledLabel(event.template)} />
          <span className="toggle-track" aria-hidden="true"><span className="toggle-thumb" /></span>
          <span>{event.enabled ? s.staff.on : s.staff.off}</span>
        </label>
      </td>
      <td>
        <textarea
          id={id}
          rows={2}
          className="mono small"
          value={text}
          onChange={(ev) => setText(ev.target.value)}
          placeholder={s.staff.placeholder(s.staff.defaults[event.defaultRecipients] || event.defaultRecipients)}
          aria-label={s.staff.recipientsLabel(event.template)}
          aria-invalid={recipientErrors.length > 0}
          disabled={busy}
        />
        <div className="muted small">{(event.recipients || []).length ? s.staff.custom((event.recipients || []).length) : s.staff.usingDefaults(s.staff.defaults[event.defaultRecipients] || event.defaultRecipients)}</div>
        {recipientErrors.map((m) => <span key={m} className="field-error" role="alert">{m}</span>)}
      </td>
      <td className="cell-actions nowrap">
        <button type="button" className="button button-secondary button-sm" onClick={saveRecipients} disabled={busy || text === initial}>{busy ? s.staff.saving : s.staff.save}</button>
      </td>
    </tr>
  );
}

function StaffEventsTab() {
  const { api } = useAdminAuth();
  const toast = useToast();
  const pagination = useCollectionPage();
  const { query: paginationQuery, setResult: paginationSetResult } = pagination;
  const fetcher = useCallback(async (signal) => { const page = await api.listEmailStaffEvents(paginationQuery, signal); paginationSetResult(page); return page; }, [api, paginationQuery, paginationSetResult]);
  const query = useApiQuery(fetcher);
  const events = (query.data && query.data.objects) || [];
  const [version, setVersion] = useState(0);
  return (
    <section className="table-section">
      <p className="muted small">{s.staff.hint(STAFF_RECIPIENTS_MAX)}</p>
      <TablePagination {...pagination.props} onRefresh={query.reload} loading={query.loading} />
      {query.error && <ErrorBanner message={c.unavailable(s.tabs.staffEvents) + ' ' + errorMessage(query.error, '')} onRetry={query.reload} />}
      {query.loading && !query.data && <Loading />}
      {query.data && (
        <div className="table-frame">
          <div className="table-scroll">
            <TableColumns id="tk.admin.EmailView"><table className="data-table">
              <caption className="visually-hidden">{s.tabs.staffEvents}</caption>
              <thead><tr><th scope="col">{s.staff.event}</th><th scope="col">{s.staff.enabled}</th><th scope="col">{s.staff.recipients}</th><th scope="col"><span className="visually-hidden">{s.columns.actions}</span></th></tr></thead>
              <tbody>
                {events.length === 0 && <tr><td colSpan={4} className="table-empty">{s.staff.empty}</td></tr>}
                {events.map((event) => (
                  <StaffEventRow
                    key={event.template + ':' + version + ':' + event.enabled + ':' + (event.recipients || []).join(',')}
                    event={event}
                    onSaved={() => { toast.success(s.staff.saved(event.template)); setVersion((v) => v + 1); query.reload(); }}
                  />
                ))}
              </tbody>
            </table></TableColumns>
          </div>
        </div>
      )}
    </section>
  );
}

function RecordDetail({ title, record, rows, onClose, onJson, loading, error, onRetry }) {
  return (
    <Modal
      open
      size="lg"
      onClose={onClose}
      title={title}
      subtitle={record ? <CopyableId value={record.id} label={s.columns.id} /> : null}
      headerExtra={record ? <button type="button" className="button button-secondary button-sm" onClick={onJson}><Icon name="code" size={14} /><span>{c.viewJson}</span></button> : null}
    >
      {loading && !record && <Loading />}
      {error && <ErrorBanner message={errorMessage(error, strings.common.errorGeneric)} onRetry={onRetry} />}
      {record && (
        <dl className="kv-grid kv-grid-wide">
          {rows.map(([label, value]) => <div key={label} className="kv-item"><dt>{label}</dt><dd>{value === null || value === undefined || value === '' ? '-' : value}</dd></div>)}
        </dl>
      )}
    </Modal>
  );
}

function MessageDetail({ id, onClose, onJson }) {
  const { api } = useAdminAuth();
  const fetcher = useCallback((signal) => api.getEmailMessage(id, signal), [api, id]);
  const q = useApiQuery(fetcher);
  const m = q.data;
  const when = (v) => (v ? formatDateTime(v) : null);
  const rows = m ? [
    [s.columns.template, <code key="t" className="mono">{m.template}</code>],
    [s.columns.category, s.categories[m.category] || m.category],
    [s.columns.recipient, <span key="r" className="copy-row">{m.recipient}<CopyButton value={m.recipient} label={s.columns.recipient} /></span>],
    [s.columns.userId, m.userId ? <CopyableId key="u" value={m.userId} label={s.columns.userId} /> : null],
    [s.columns.subject, m.subject],
    [s.columns.status, <Badge key="s" tone={STATUS_TONES[m.status]}>{s.statuses[m.status] || m.status}</Badge>],
    [s.columns.provider, m.provider],
    [s.detail.providerMessageId, m.providerMessageId],
    [s.columns.attempts, String(m.attempts || 0)],
    [s.detail.nextAttempt, when(m.nextAttemptUtc)],
    [s.detail.lastError, m.lastError ? <span key="e" className="field-error">{m.lastError}</span> : null],
    [s.columns.created, when(m.createdUtc)],
    [s.detail.sent, when(m.sentUtc)],
    [s.detail.delivered, when(m.deliveredUtc)],
    [s.detail.failed, when(m.failedUtc)],
    [s.detail.updated, when(m.updatedUtc)],
    [strings.metadata.labels, (m.labels || []).join(', ')],
    [strings.metadata.tags, Object.entries(m.tags || {}).map(([k, v]) => k + '=' + v).join(', ')]
  ] : [];
  return <RecordDetail title={m ? m.subject || m.template : s.detail.messageTitle} record={m} rows={rows} onClose={onClose} onJson={() => onJson(m)} loading={q.loading} error={q.error} onRetry={q.reload} />;
}

function MessagesTab({ templates }) {
  const { api, client } = useAdminAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const table = useTableState({ storageKey: 'tk.admin.emailMessages', filterKeys: EMAIL_MESSAGE_FILTERS, defaultSort: '' });
  const [jsonRow, setJsonRow] = useState(null);
  const detailId = searchParams.get('message');
  const query = useMemo(() => buildEmailMessagesQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.meta]);
  const fetcher = useCallback((signal) => api.listEmailMessages(query, signal), [api, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;

  const openDetail = (id) => {
    const next = new URLSearchParams(searchParams);
    if (id) next.set('message', id);
    else next.delete('message');
    setSearchParams(next);
  };

  const columns = [
    { key: 'created', label: s.columns.created, className: 'nowrap', render: (row) => <span title={formatDateTime(row.createdUtc)}>{formatShortDateTime(row.createdUtc)}</span> },
    { key: 'template', label: s.columns.template, render: (row) => <code className="mono">{row.template}</code> },
    { key: 'recipient', label: s.columns.recipient, render: (row) => <span className="truncate">{row.recipient}</span> },
    { key: 'subject', label: s.columns.subject, render: (row) => <span className="truncate">{row.subject}</span> },
    { key: 'status', label: s.columns.status, render: (row) => <Badge tone={STATUS_TONES[row.status]} title={row.lastError || undefined}>{s.statuses[row.status] || row.status}</Badge> },
    { key: 'attempts', label: s.columns.attempts, className: 'num', render: (row) => row.attempts || 0 },
    { key: 'provider', label: s.columns.provider, render: (row) => row.provider || <span className="muted">-</span> },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{s.columns.actions}</span>,
      render: (row) => (
        <ActionMenu
          label={c.rowActions(row.recipient)}
          items={[
            { label: c.view, icon: 'eye', onSelect: () => openDetail(row.id) },
            { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) }
          ]}
        />
      )
    }
  ];

  const filterFields = [
    { key: 'search', label: s.filters.search, placeholder: s.filters.searchPlaceholder, grow: true },
    { key: 'status', label: s.columns.status, type: 'select', options: optionsFrom(s.statuses, Object.values(EmailStatus)) },
    { key: 'template', label: s.columns.template, type: 'select', options: (templates || []).map((t) => ({ value: t.key, label: t.key })) },
    { key: 'category', label: s.columns.category, type: 'select', options: optionsFrom(s.categories, Object.values(EmailCategory)) },
    { key: 'recipient', label: s.columns.recipient, placeholder: s.filters.recipientPlaceholder },
    { key: 'userId', label: s.columns.userId, placeholder: 'usr_', mono: true },
    { key: 'order', label: s.filters.order, type: 'select', options: optionsFrom(s.orderings, EmailMessageOrderings) }
  ];

  return (
    <>
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={client} targetType="EmailMessage" />
      </section>
      <section className="table-section">
        <p className="muted small">{s.messagesNote}</p>
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading} pageSizes={ADMIN_PAGE_SIZES} />
        <DataTable
          caption={s.tabs.messages}
          columns={columns}
          rows={page ? page.items : []}
          rowKey={(row) => row.id}
          loading={list.loading}
          error={list.error ? c.unavailable(s.tabs.messages) + ' ' + errorMessage(list.error, '') : null}
          onRetry={list.reload}
          emptyMessage={table.hasFilters ? s.messagesEmptyFiltered : s.messagesEmpty}
          onRowClick={(row) => openDetail(row.id)}
          rowClassName={(row) => (row.status === 'Failed' ? 'row-failed' : '')}
        />
      </section>
      {detailId && <MessageDetail key={detailId} id={detailId} onClose={() => openDetail(null)} onJson={setJsonRow} />}
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.id} value={jsonRow} />
    </>
  );
}

function AddSuppressionModal({ onClose, onAdded }) {
  const { api, client } = useAdminAuth();
  const [email, setEmail] = useState('');
  const [note, setNote] = useState('');
  const [metadata, setMetadata] = useState({ labels: [], tags: {} });
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const submit = async (ev) => {
    ev.preventDefault();
    const local = {};
    if (!validEmail(email.trim())) local.email = s.suppressions.emailInvalid;
    if (note.length > 500) local.note = s.suppressions.noteTooLong;
    setErrors(local);
    if (Object.keys(local).length) return;
    setBusy(true);
    setError('');
    try {
      const saved = await api.addEmailSuppression({ email: email.trim(), note: note.trim() || null, labels: metadata.labels, tags: metadata.tags });
      onAdded(saved);
    } catch (err) {
      const fe = getFieldErrors(err);
      setErrors(Object.fromEntries(Object.entries(fe).map(([k, v]) => [k, String(v)])));
      setError(errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };
  return (
    <Modal
      open
      size="md"
      onClose={busy ? () => {} : onClose}
      title={s.suppressions.addTitle}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="suppression-form" className="button button-primary" disabled={busy}>{busy ? s.suppressions.adding : s.suppressions.add}</button>
        </>
      )}
    >
      <form id="suppression-form" className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} />
        <p className="muted small">{s.suppressions.addHint}</p>
        <div className="field">
          <label className="label-text" htmlFor="suppression-email">{s.suppressions.email}</label>
          <input id="suppression-email" type="email" value={email} onChange={(ev) => setEmail(ev.target.value)} disabled={busy} aria-invalid={Boolean(errors.email)} autoComplete="off" />
          {errors.email && <span className="field-error" role="alert">{errors.email}</span>}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="suppression-note">{s.suppressions.note}</label>
          <textarea id="suppression-note" rows={2} value={note} onChange={(ev) => setNote(ev.target.value)} disabled={busy} aria-invalid={Boolean(errors.note)} maxLength={600} />
          {errors.note && <span className="field-error" role="alert">{errors.note}</span>}
        </div>
        <MetadataEditor value={metadata} onChange={(next) => setMetadata(next)} isStaff client={client} targetType="EmailSuppression" disabled={busy} />
      </form>
    </Modal>
  );
}

function SuppressionsTab() {
  const { api, client } = useAdminAuth();
  const toast = useToast();
  const table = useTableState({ storageKey: 'tk.admin.emailSuppressions', filterKeys: EMAIL_SUPPRESSION_FILTERS, defaultSort: '' });
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState(null);
  const [detail, setDetail] = useState(null);
  const [jsonRow, setJsonRow] = useState(null);
  const [busy, setBusy] = useState(false);
  const query = useMemo(() => buildEmailSuppressionsQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.meta]);
  const fetcher = useCallback((signal) => api.listEmailSuppressions(query, signal), [api, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;

  const remove = async () => {
    setBusy(true);
    try {
      await api.removeEmailSuppression(removing.id);
      toast.success(s.suppressions.removed(removing.email));
      setRemoving(null);
      list.reload();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  const columns = [
    { key: 'email', label: s.suppressions.email, render: (row) => <span className="copy-row"><span className="truncate">{row.email}</span><CopyButton value={row.email} label={s.suppressions.email} /></span> },
    { key: 'reason', label: s.suppressions.reason, render: (row) => <Badge tone={row.reason === 'Manual' ? 'neutral' : 'warning'}>{s.reasons[row.reason] || row.reason}</Badge> },
    { key: 'source', label: s.suppressions.source, render: (row) => row.source || <span className="muted">-</span> },
    { key: 'note', label: s.suppressions.note, render: (row) => <span className="truncate small">{row.note || ''}</span> },
    { key: 'created', label: s.columns.created, className: 'nowrap', render: (row) => <span title={formatDateTime(row.createdUtc)}>{formatShortDateTime(row.createdUtc)}</span> },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{s.columns.actions}</span>,
      render: (row) => (
        <ActionMenu
          label={c.rowActions(row.email)}
          items={[
            { label: c.view, icon: 'eye', onSelect: () => setDetail(row) },
            { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) },
            { label: s.suppressions.remove, icon: 'trash', danger: true, onSelect: () => setRemoving(row) }
          ]}
        />
      )
    }
  ];

  const filterFields = [
    { key: 'search', label: s.filters.search, placeholder: s.suppressions.searchPlaceholder, grow: true },
    { key: 'reason', label: s.suppressions.reason, type: 'select', options: optionsFrom(s.reasons, Object.values(EmailSuppressionReason)) },
    { key: 'order', label: s.filters.order, type: 'select', options: optionsFrom(s.orderings, EmailSuppressionOrderings) }
  ];

  const detailRows = detail ? [
    [s.suppressions.email, detail.email],
    [s.suppressions.reason, s.reasons[detail.reason] || detail.reason],
    [s.suppressions.source, detail.source],
    [s.suppressions.note, detail.note],
    [s.columns.created, formatDateTime(detail.createdUtc)],
    [strings.metadata.labels, (detail.labels || []).join(', ')],
    [strings.metadata.tags, Object.entries(detail.tags || {}).map(([k, v]) => k + '=' + v).join(', ')]
  ] : [];

  return (
    <>
      <div className="row-between">
        <p className="muted small">{s.suppressions.hint}</p>
        <button type="button" className="button button-primary button-sm" onClick={() => setAdding(true)}><Icon name="plus" size={14} /><span>{s.suppressions.addTitle}</span></button>
      </div>
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={client} targetType="EmailSuppression" />
      </section>
      <section className="table-section">
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading} pageSizes={ADMIN_PAGE_SIZES} />
        <DataTable
          caption={s.tabs.suppressions}
          columns={columns}
          rows={page ? page.items : []}
          rowKey={(row) => row.id}
          loading={list.loading}
          error={list.error ? c.unavailable(s.tabs.suppressions) + ' ' + errorMessage(list.error, '') : null}
          onRetry={list.reload}
          emptyMessage={table.hasFilters ? s.suppressions.emptyFiltered : s.suppressions.empty}
          onRowClick={(row) => setDetail(row)}
        />
      </section>
      {adding && <AddSuppressionModal onClose={() => setAdding(false)} onAdded={(saved) => { toast.success(s.suppressions.added(saved.email)); setAdding(false); list.reload(); }} />}
      {detail && <RecordDetail title={detail.email} record={detail} rows={detailRows} onClose={() => setDetail(null)} onJson={() => setJsonRow(detail)} />}
      <ConfirmModal open={Boolean(removing)} title={removing ? s.suppressions.removeTitle(removing.email) : ''} message={s.suppressions.removeBody} confirmLabel={s.suppressions.remove} busy={busy} onConfirm={remove} onCancel={() => setRemoving(null)} />
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.id} value={jsonRow} />
    </>
  );
}

/**
 * /admin/email: administrative email templates (overrides with preview and test sends), staff
 * notification events, the message log, and suppressions. The tab lives in the URL (`?tab=`).
 */
function EmailView() {
  const [searchParams, setSearchParams] = useSearchParams();
  const tab = TABS.includes(searchParams.get('tab')) ? searchParams.get('tab') : 'templates';
  const select = (id) => setSearchParams(id === 'templates' ? {} : { tab: id });
  const templatesQuery = useTemplates();
  const [testOpen, setTestOpen] = useState(false);
  const templates = (templatesQuery.data && templatesQuery.data.objects) || [];
  const route = strings.admin.routes.email;
  return (
    <div className="view">
      <PageHeader
        title={route.label}
        summary={route.summary}
        actions={<button type="button" className="button button-secondary button-sm" onClick={() => setTestOpen(true)}><Icon name="message" size={14} /><span>{s.test.title}</span></button>}
      />
      <div className="segmented tab-bar" role="tablist" aria-label={s.tabsLabel}>
        {TABS.map((id) => (
          <button key={id} type="button" role="tab" id={'email-tab-' + id} aria-selected={tab === id} aria-controls={'email-panel-' + id} className={'segment' + (tab === id ? ' active' : '')} onClick={() => select(id)}>{s.tabs[id]}</button>
        ))}
      </div>
      <div role="tabpanel" id={'email-panel-' + tab} aria-labelledby={'email-tab-' + tab} className="tab-panel">
        {tab === 'templates' && <TemplatesTab templatesQuery={templatesQuery} />}
        {tab === 'staffEvents' && <StaffEventsTab />}
        {tab === 'messages' && <MessagesTab templates={templates} />}
        {tab === 'suppressions' && <SuppressionsTab />}
      </div>
      {testOpen && <SendTestModal templates={templates} onClose={() => setTestOpen(false)} />}
    </div>
  );
}

export default EmailView;
