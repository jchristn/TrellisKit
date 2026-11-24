import { useCallback, useMemo, useState } from 'react';
import { BANNER_BODY_MAX, BANNER_FILTER_KEYS, BANNER_PLACEMENTS, BANNER_TITLE_MAX, BannerStyle, bannerProblem } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES } from '../../utils/adminQuery.js';
import { formatDateTime, formatShortDate } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import Modal from '../../components/Modal.jsx';
import Icon from '../../components/Icon.jsx';
import MetadataEditor from '../../components/MetadataEditor.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner } from '../../components/States.jsx';
import { BannerCard } from '../../components/BannerSlot.jsx';
import ContentPublishPanel from './ContentPublishPanel.jsx';
import TargetingRulesEditor, { rowsToRules, rulesToRows } from './TargetingRulesEditor.jsx';
import { fromLocalInput, toLocalInput } from './LiveUpdateDialogs.jsx';
import { contentQuery, contentStateTone } from './appContentAdmin.js';
import '../../components/appContent.css';

const u = strings.admin.liveUpdates;
const ac = strings.appContent.admin;
const bn = ac.banners;
const FILTERS = Object.freeze(['search', ...BANNER_FILTER_KEYS]);
const STYLES = Object.freeze(Object.values(BannerStyle));
const CLIENTS = Object.freeze(['web', 'mobile']);

/** POST /admin/app/banners/enumerate query; `active` and `hasDraftChanges` are booleans. */
export function buildBannersQuery(filters, paging) {
  return contentQuery(filters, FILTERS, paging, { ordering: 'PriorityDescending', booleans: ['active', 'hasDraftChanges'] });
}

/** Editor state from a banner. */
export function bannerForm(banner) {
  const b = banner || {};
  const link = b.link || null;
  return {
    title: b.title || '',
    body: b.body || '',
    style: b.style || BannerStyle.Info,
    placement: b.placement || 'Global',
    dismissible: b.dismissible !== false,
    priority: String(b.priority ?? 0),
    starts: toLocalInput(b.startsUtc),
    ends: toLocalInput(b.endsUtc),
    linkKind: link && link.url ? 'url' : link && link.path ? 'path' : 'none',
    linkTarget: link ? link.url || link.path || '' : '',
    linkLabel: link ? link.label || '' : '',
    imageAssetId: b.imageAssetId || '',
    clients: b.clients || [...CLIENTS],
    rules: rulesToRows(b.rules),
    meta: { labels: b.labels || [], tags: b.tags || {} }
  };
}

/** The create or update body from editor state (`Critical` is never dismissible). */
export function bannerBody(form) {
  const link = form.linkKind === 'none' || !form.linkTarget.trim()
    ? null
    : { label: form.linkLabel.trim(), path: form.linkKind === 'path' ? form.linkTarget.trim() : null, url: form.linkKind === 'url' ? form.linkTarget.trim() : null };
  return {
    title: form.title.trim(),
    body: form.body,
    style: form.style,
    placement: form.placement,
    dismissible: form.style === BannerStyle.Critical ? false : Boolean(form.dismissible),
    priority: Number(form.priority) || 0,
    startsUtc: fromLocalInput(form.starts),
    endsUtc: fromLocalInput(form.ends),
    link,
    imageAssetId: form.imageAssetId.trim() || null,
    clients: form.clients,
    rules: rowsToRules(form.rules),
    labels: form.meta.labels,
    tags: form.meta.tags
  };
}

/** The friendly message of a bannerProblem field, or null. */
export function bannerProblemText(field) {
  return field ? bn.problems[field] || bn.problems.title : null;
}

function BannerEditor({ banner, etag, client, onClose, onSaved }) {
  const [form, setForm] = useState(() => bannerForm(banner));
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const set = (key) => (e) => setForm((v) => ({ ...v, [key]: e.target.type === 'checkbox' ? e.target.checked : e.target.value }));
  const toggleClient = (name) => setForm((v) => ({ ...v, clients: v.clients.includes(name) ? v.clients.filter((x) => x !== name) : [...v.clients, name] }));
  const body = bannerBody(form);
  const critical = form.style === BannerStyle.Critical;

  const submit = async (e) => {
    e.preventDefault();
    setError('');
    const field = bannerProblem(body);
    if (field) {
      setErrors({ [field]: bannerProblemText(field) });
      return;
    }
    if (!form.clients.length) {
      setErrors({ clients: bn.clientsRequired });
      return;
    }
    if (form.meta.problems && form.meta.problems.length) return;
    setBusy(true);
    try {
      const saved = banner ? await client.updateBanner(banner.id, body, etag) : await client.createBanner(body);
      onSaved(saved);
    } catch (err) {
      setErrors(getFieldErrors(err));
      setError(err && err.status === 412 ? bn.staleEdit : errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };
  const fieldError = (key) => (errors[key] ? <span className="field-error" role="alert">{errors[key]}</span> : null);

  return (
    <Modal
      open
      size="lg"
      onClose={onClose}
      title={banner ? bn.editTitle(banner.title) : bn.createTitle}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="banner-form" className="button button-primary" disabled={busy}>{ac.saveDraft}</button>
        </>
      )}
    >
      <form id="banner-form" onSubmit={submit} noValidate>
        {error && <ErrorBanner message={error} />}
        <div className="field">
          <label className="label-text" htmlFor="banner-title">{bn.title}</label>
          <input id="banner-title" value={form.title} onChange={set('title')} maxLength={BANNER_TITLE_MAX} aria-invalid={Boolean(errors.title)} />
          {fieldError('title')}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="banner-body">{bn.body}</label>
          <textarea id="banner-body" rows={4} value={form.body} onChange={set('body')} maxLength={BANNER_BODY_MAX} aria-invalid={Boolean(errors.body)} aria-describedby="banner-body-hint" />
          <span id="banner-body-hint" className="field-hint">{bn.bodyHint(form.body.length, BANNER_BODY_MAX)}</span>
          {fieldError('body')}
        </div>
        <div className="form-grid">
          <div className="field">
            <label className="label-text" htmlFor="banner-style">{bn.style}</label>
            <select id="banner-style" value={form.style} onChange={set('style')}>{STYLES.map((s) => <option key={s} value={s}>{bn.styles[s] || s}</option>)}</select>
          </div>
          <div className="field">
            <label className="label-text" htmlFor="banner-placement">{bn.placement}</label>
            <select id="banner-placement" value={form.placement} onChange={set('placement')}>{BANNER_PLACEMENTS.map((p) => <option key={p} value={p}>{bn.placements[p] || p}</option>)}</select>
          </div>
          <div className="field">
            <label className="label-text" htmlFor="banner-priority">{bn.priority}</label>
            <input id="banner-priority" type="number" min="-1000" max="1000" step="1" value={form.priority} onChange={set('priority')} aria-invalid={Boolean(errors.priority)} />
            {fieldError('priority')}
          </div>
          <div className="field">
            <label className="toggle"><input type="checkbox" checked={critical ? false : form.dismissible} disabled={critical} onChange={set('dismissible')} /> {bn.dismissible}</label>
            {critical && <span className="field-hint">{bn.criticalHint}</span>}
          </div>
        </div>
        <div className="form-grid">
          <div className="field">
            <label className="label-text" htmlFor="banner-starts">{bn.starts}</label>
            <input id="banner-starts" type="datetime-local" value={form.starts} onChange={set('starts')} />
          </div>
          <div className="field">
            <label className="label-text" htmlFor="banner-ends">{bn.ends}</label>
            <input id="banner-ends" type="datetime-local" value={form.ends} onChange={set('ends')} aria-invalid={Boolean(errors.endsUtc)} />
            {fieldError('endsUtc')}
          </div>
        </div>
        <fieldset className="fieldset">
          <legend>{bn.link}</legend>
          <div className="form-grid">
            <div className="field">
              <label className="label-text" htmlFor="banner-link-kind">{bn.linkKind}</label>
              <select id="banner-link-kind" value={form.linkKind} onChange={set('linkKind')}>{Object.entries(bn.linkKinds).map(([k, label]) => <option key={k} value={k}>{label}</option>)}</select>
            </div>
            {form.linkKind !== 'none' && (
              <>
                <div className="field">
                  <label className="label-text" htmlFor="banner-link-target">{form.linkKind === 'path' ? bn.linkPath : bn.linkUrl}</label>
                  <input id="banner-link-target" value={form.linkTarget} onChange={set('linkTarget')} maxLength={form.linkKind === 'path' ? 300 : 2000} placeholder={form.linkKind === 'path' ? '/app/support' : 'https://'} aria-invalid={Boolean(errors['link.path'] || errors['link.url'] || errors.link)} />
                  {fieldError('link.path')}{fieldError('link.url')}{fieldError('link')}
                </div>
                <div className="field">
                  <label className="label-text" htmlFor="banner-link-label">{bn.linkLabel}</label>
                  <input id="banner-link-label" value={form.linkLabel} onChange={set('linkLabel')} maxLength={40} aria-invalid={Boolean(errors['link.label'])} />
                  {fieldError('link.label')}
                </div>
              </>
            )}
          </div>
        </fieldset>
        <div className="field">
          <label className="label-text" htmlFor="banner-image">{bn.imageAssetId}</label>
          <input id="banner-image" className="mono" value={form.imageAssetId} onChange={set('imageAssetId')} placeholder="ras_" aria-describedby="banner-image-hint" />
          <span id="banner-image-hint" className="field-hint">{bn.imageHint}</span>
          {fieldError('imageAssetId')}
        </div>
        <fieldset className="fieldset">
          <legend>{bn.clients}</legend>
          {CLIENTS.map((name) => (
            <label key={name} className="toggle"><input type="checkbox" checked={form.clients.includes(name)} onChange={() => toggleClient(name)} /> {u.flags.clientNames[name] || name}</label>
          ))}
          {fieldError('clients')}
        </fieldset>
        <TargetingRulesEditor rows={form.rules} onChange={(rules) => setForm((v) => ({ ...v, rules }))} errors={errors} help={bn.rulesHelp} />
        <MetadataEditor value={form.meta} onChange={(meta, problems) => setForm((v) => ({ ...v, meta: { ...meta, problems } }))} isStaff client={client} targetType="Banner" collapsible />
        <section className="field" aria-label={bn.preview}>
          <span className="label-text">{bn.preview}</span>
          <div className="tk-banner-preview" data-testid="banner-preview">
            <BannerCard banner={{ ...body, title: body.title || bn.previewTitle, id: banner ? banner.id : 'preview' }} preview onDismiss={() => {}} />
          </div>
          <span className="field-hint">{bn.previewHint(bn.placements[form.placement] || form.placement)}</span>
        </section>
      </form>
    </Modal>
  );
}

function BannerHistory({ client, id, onClose }) {
  const fetcher = useCallback((signal) => client.enumerateBannerHistory(id, { maxResults: 50 }, signal), [client, id]);
  const history = useApiQuery(fetcher);
  const rows = history.data ? normalizePage(history.data).items.map((row, i) => ({ ...row, rowIndex: i })) : [];
  const columns = [
    { key: 'version', label: u.flags.version, render: (row) => row.publishedInVersion ?? '-' },
    { key: 'title', label: bn.title, render: (row) => row.title },
    { key: 'style', label: bn.style, render: (row) => bn.styles[row.style] || row.style },
    { key: 'state', label: u.flags.state, render: (row) => u.flags.states[row.state] || row.state },
    { key: 'when', label: ac.when, render: (row) => formatDateTime(row.updatedUtc) }
  ];
  return (
    <Modal open size="lg" onClose={onClose} title={ac.history}>
      <DataTable caption={ac.history} columns={columns} rows={rows} rowKey={(row) => String(row.rowIndex)} loading={history.loading} error={history.error ? errorMessage(history.error, '') : null} onRetry={history.reload} emptyMessage={ac.noHistory} />
    </Modal>
  );
}

/**
 * Banners (docs/LIVE_UPDATES_CONTRACT.md section 14.1): enumerated banners with active, placement,
 * style, state, draft, label, and tag filters; the editor (title, Markdown body, style, placement,
 * dismissible, priority, schedule, link, image, clients, targeting rules, labels, tags) with a style
 * preview; archive, restore, delete, history; and the pending content drafts with Publish.
 */
function BannersView() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const table = useTableState({ storageKey: 'tk.admin.banners', filterKeys: FILTERS, defaultSort: '' });
  const [editing, setEditing] = useState(null);
  const [dialog, setDialog] = useState(null);
  const query = useMemo(() => buildBannersQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.meta]);
  const fetcher = useCallback((signal) => client.enumerateBanners(query, signal), [client, query]);
  const list = useApiQuery(fetcher);
  const draftsFetcher = useCallback((signal) => client.getContentDrafts({ maxResults: 1 }, signal), [client]);
  const drafts = useApiQuery(draftsFetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const route = strings.appContent.routes.banners;

  const reloadAll = () => {
    list.reload();
    drafts.reload();
  };
  const run = async (fn, message) => {
    try {
      await fn();
      toast.success(message);
      reloadAll();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };
  const openEditor = async (row) => {
    try {
      const detail = await client.getBanner(row.id);
      setEditing({ banner: detail.banner, etag: detail.banner.eTag });
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };
  const schedule = (row) => {
    if (!row.startsUtc && !row.endsUtc) return bn.always;
    return (row.startsUtc ? formatShortDate(row.startsUtc) : bn.now) + ' - ' + (row.endsUtc ? formatShortDate(row.endsUtc) : bn.open);
  };

  const columns = [
    { key: 'title', label: bn.title, render: (row) => <button type="button" className="link-button" onClick={() => openEditor(row)}>{row.title}</button> },
    { key: 'style', label: bn.style, render: (row) => <Badge tone={row.style === 'Critical' ? 'danger' : row.style === 'Warning' ? 'warning' : row.style === 'Success' ? 'success' : 'info'}>{bn.styles[row.style] || row.style}</Badge> },
    { key: 'placement', label: bn.placement, render: (row) => bn.placements[row.placement] || row.placement },
    { key: 'priority', label: bn.priority, render: (row) => row.priority },
    { key: 'schedule', label: bn.schedule, render: (row) => schedule(row) },
    { key: 'clients', label: bn.clients, render: (row) => (row.clients || []).map((x) => u.flags.clientNames[x] || x).join(', ') },
    { key: 'state', label: u.flags.state, render: (row) => <span className="nowrap"><Badge tone={contentStateTone(row.state)}>{u.flags.states[row.state] || row.state}</Badge>{row.hasDraftChanges && <> <Badge tone="warning">{u.flags.draft}</Badge></>}</span> },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{u.columns.actions}</span>,
      render: (row) => (
        <ActionMenu
          label={strings.admin.common.rowActions(row.title)}
          items={[
            { label: strings.admin.common.edit, icon: 'edit', onSelect: () => openEditor(row) },
            { label: ac.history, icon: 'clock', onSelect: () => setDialog({ type: 'history', row }) },
            ...(row.publishedInVersion ? [row.state === 'Archived'
              ? { label: u.flags.restore, icon: 'refresh', onSelect: () => run(() => client.restoreBanner(row.id), u.flags.restored) }
              : { label: u.flags.archive, icon: 'trash', onSelect: () => run(() => client.archiveBanner(row.id), u.flags.archived) }] : []),
            ...(!row.publishedInVersion ? [{ label: u.flags.remove, icon: 'trash', danger: true, onSelect: () => setDialog({ type: 'delete', row }) }] : [])
          ]}
        />
      )
    }
  ];
  const yesNo = [{ value: 'true', label: u.yes }, { value: 'false', label: u.no }];
  const filterFields = [
    { key: 'search', label: bn.searchLabel, placeholder: bn.searchPlaceholder, grow: true },
    { key: 'active', label: bn.active, type: 'select', options: yesNo },
    { key: 'placement', label: bn.placement, type: 'select', options: BANNER_PLACEMENTS.map((p) => ({ value: p, label: bn.placements[p] || p })) },
    { key: 'style', label: bn.style, type: 'select', options: STYLES.map((s) => ({ value: s, label: bn.styles[s] || s })) },
    { key: 'state', label: u.flags.state, type: 'select', options: Object.entries(u.flags.states).map(([value, label]) => ({ value, label })) },
    { key: 'hasDraftChanges', label: u.flags.draft, type: 'select', options: yesNo }
  ];

  return (
    <div className="view">
      <PageHeader
        title={route.label}
        summary={route.summary}
        actions={<button type="button" className="button button-primary button-sm" onClick={() => setEditing({ banner: null })}><Icon name="plus" size={14} /><span>{bn.create}</span></button>}
      />
      <ContentPublishPanel client={client} drafts={drafts} onChanged={list.reload} />
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={client} targetType="Banner" />
      </section>
      <section className="table-section">
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading} pageSizes={ADMIN_PAGE_SIZES} />
        <DataTable caption={route.label} columns={columns} rows={page ? page.items : []} rowKey={(row) => row.id} loading={list.loading} error={list.error ? errorMessage(list.error, '') : null} onRetry={list.reload} emptyMessage={table.hasFilters ? bn.emptyFiltered : bn.empty} />
      </section>
      {editing && <BannerEditor banner={editing.banner} etag={editing.etag} client={client} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); toast.success(bn.saved); reloadAll(); }} />}
      {dialog && dialog.type === 'history' && <BannerHistory client={client} id={dialog.row.id} onClose={() => setDialog(null)} />}
      <ConfirmModal
        open={Boolean(dialog && dialog.type === 'delete')}
        title={dialog && dialog.row ? bn.deleteTitle(dialog.row.title) : ''}
        message={bn.deleteBody}
        confirmLabel={u.flags.remove}
        onConfirm={() => { const row = dialog.row; setDialog(null); run(() => client.deleteBanner(row.id), bn.deleted); }}
        onCancel={() => setDialog(null)}
      />
    </div>
  );
}

export default BannersView;
