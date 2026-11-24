import { useCallback, useMemo, useState } from 'react';
import { FLAG_ATTRIBUTES, FLAG_FILTER_KEYS } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, pagedQuery } from '../../utils/adminQuery.js';
import { optionsFrom } from '../../utils/labels.js';
import { formatShortDate } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import Modal from '../../components/Modal.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner } from '../../components/States.jsx';
import { FormDialog, splitList } from './LiveUpdateDialogs.jsx';
import ContentPublishPanel from './ContentPublishPanel.jsx';

const u = strings.admin.liveUpdates;
const fl = u.flags;
const FILTERS = Object.freeze(['search', ...FLAG_FILTER_KEYS]);
const ATTRIBUTES = Object.keys(FLAG_ATTRIBUTES);

/** POST /admin/app/flags/enumerate query; hasDraftChanges is sent as a boolean. */
export function buildFlagsQuery(filters, paging) {
  return { ...pagedQuery(filters, FILTERS, { ...paging, sort: '' }, ({ hasDraftChanges, ...rest }) => ({ ...rest, ...(hasDraftChanges ? { hasDraftChanges: hasDraftChanges === 'true' } : {}) })), ordering: 'KeyAscending' };
}

/** Parse a typed value from its text form. */
export function parseFlagValue(text, valueType) {
  if (valueType === 'Boolean') return text === true || text === 'true';
  if (valueType === 'Integer' || valueType === 'Decimal') return Number(text);
  return String(text ?? '');
}

/** Editor state from a flag. */
export function flagForm(flag) {
  const f = flag || {};
  return {
    key: f.key || '',
    name: f.name || '',
    description: f.description || '',
    kind: f.kind || 'Boolean',
    valueType: f.valueType || 'Boolean',
    variants: (f.variants || []).join(', '),
    defaultValue: f.defaultValue === undefined ? 'false' : String(f.defaultValue),
    offValue: f.offValue === undefined ? 'false' : String(f.offValue),
    enabled: f.enabled !== false,
    clients: f.clients || ['mobile', 'web'],
    rules: (f.rules || []).map((r) => ({
      id: r.id,
      value: String(r.value),
      rolloutPercentage: String(r.rolloutPercentage ?? 100),
      conditions: r.conditions.map((c) => ({ attribute: c.attribute, operator: c.operator, values: c.values.join(', ') }))
    }))
  };
}

const FLAG_KEY = /^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*){0,3}$/;

/** The friendly key problem for a flag key (LIVE_UPDATES_CONTRACT.md feature flags), or null when the key is valid. */
export function flagKeyProblem(key) {
  const value = String(key || '').trim();
  if (value.length < 3 || value.length > 64 || !FLAG_KEY.test(value)) return fl.keyInvalid;
  return null;
}

/** The create or update body from editor state. */
export function flagBody(form) {
  const valueType = form.kind === 'Boolean' ? 'Boolean' : form.kind === 'Variant' ? 'String' : form.valueType;
  return {
    key: form.key.trim(),
    name: form.name.trim(),
    description: form.description.trim() || null,
    kind: form.kind,
    valueType,
    variants: form.kind === 'Variant' ? splitList(form.variants) : [],
    defaultValue: parseFlagValue(form.defaultValue, valueType),
    offValue: parseFlagValue(form.offValue, valueType),
    enabled: form.enabled,
    clients: form.clients,
    rules: form.rules.map((r) => ({
      id: r.id,
      value: parseFlagValue(r.value, valueType),
      rolloutPercentage: Number(r.rolloutPercentage) || 100,
      conditions: r.conditions.map((c) => ({ attribute: c.attribute, operator: c.operator, values: c.operator === 'SemverRange' ? [c.values.trim()] : splitList(c.values) }))
    }))
  };
}

function FlagEditor({ flag, etag, client, onClose, onSaved }) {
  const [form, setForm] = useState(() => flagForm(flag));
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const set = (key) => (e) => setForm((v) => ({ ...v, [key]: e.target.type === 'checkbox' ? e.target.checked : e.target.value }));
  const setRule = (i, patch) => setForm((v) => ({ ...v, rules: v.rules.map((r, j) => (j === i ? { ...r, ...patch } : r)) }));
  const setCondition = (i, ci, patch) => setRule(i, { conditions: form.rules[i].conditions.map((c, j) => (j === ci ? { ...c, ...patch } : c)) });
  const toggleClient = (client2) => setForm((v) => ({ ...v, clients: v.clients.includes(client2) ? v.clients.filter((x) => x !== client2) : [...v.clients, client2] }));

  const submit = async (e) => {
    e.preventDefault();
    setError('');
    const keyProblem = flag && flag.publishedInVersion ? null : flagKeyProblem(form.key);
    if (keyProblem) {
      setErrors({ key: keyProblem });
      return;
    }
    setBusy(true);
    try {
      const body = flagBody(form);
      const saved = flag ? await client.updateFlag(flag.id, body, etag) : await client.createFlag(body);
      onSaved(saved);
    } catch (err) {
      setErrors(getFieldErrors(err));
      setError(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open
      size="lg"
      onClose={onClose}
      title={flag ? fl.editTitle(flag.key) : fl.createTitle}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="flag-form" className="button button-primary" disabled={busy}>{strings.common.save}</button>
        </>
      )}
    >
      <form id="flag-form" onSubmit={submit} noValidate>
        {error && <ErrorBanner message={error} />}
        <div className="form-grid">
          <div className="field">
            <label className="label-text" htmlFor="flag-key">{fl.key}</label>
            <input id="flag-key" className="mono" value={form.key} onChange={set('key')} maxLength={64} disabled={Boolean(flag && flag.publishedInVersion)} aria-invalid={Boolean(errors.key)} aria-describedby={errors.key ? undefined : 'flag-key-hint'} />
            {errors.key ? <span className="field-error" role="alert">{errors.key}</span> : <span id="flag-key-hint" className="muted small">{fl.keyHint}</span>}
          </div>
          <div className="field">
            <label className="label-text" htmlFor="flag-name">{fl.name}</label>
            <input id="flag-name" value={form.name} onChange={set('name')} maxLength={100} aria-invalid={Boolean(errors.name)} />
          </div>
          <div className="field">
            <label className="label-text" htmlFor="flag-kind">{fl.kind}</label>
            <select id="flag-kind" value={form.kind} onChange={set('kind')}>{optionsFrom(fl.kinds).map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}</select>
          </div>
          {form.kind === 'Value' && (
            <div className="field">
              <label className="label-text" htmlFor="flag-type">{fl.valueType}</label>
              <select id="flag-type" value={form.valueType} onChange={set('valueType')}>{optionsFrom(fl.valueTypes).map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}</select>
            </div>
          )}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="flag-description">{fl.description}</label>
          <input id="flag-description" value={form.description} onChange={set('description')} maxLength={500} />
        </div>
        {form.kind === 'Variant' && (
          <div className="field">
            <label className="label-text" htmlFor="flag-variants">{fl.variants}</label>
            <input id="flag-variants" value={form.variants} onChange={set('variants')} aria-invalid={Boolean(errors.variants)} />
            {errors.variants && <span className="field-error">{errors.variants}</span>}
          </div>
        )}
        <div className="form-grid">
          <div className="field">
            <label className="label-text" htmlFor="flag-default">{fl.defaultValue}</label>
            <input id="flag-default" value={form.defaultValue} onChange={set('defaultValue')} aria-invalid={Boolean(errors.defaultValue)} />
          </div>
          <div className="field">
            <label className="label-text" htmlFor="flag-off">{fl.offValue}</label>
            <input id="flag-off" value={form.offValue} onChange={set('offValue')} />
          </div>
          <label className="toggle"><input type="checkbox" checked={form.enabled} onChange={set('enabled')} /> {fl.enabled}</label>
        </div>
        <fieldset className="fieldset">
          <legend>{fl.clients}</legend>
          {Object.keys(fl.clientNames).map((name) => (
            <label key={name} className="toggle"><input type="checkbox" checked={form.clients.includes(name)} onChange={() => toggleClient(name)} /> {fl.clientNames[name]}</label>
          ))}
        </fieldset>
        <fieldset className="fieldset">
          <legend>{fl.rules}</legend>
          <p className="muted">{fl.rulesHelp}</p>
          {form.rules.map((rule, i) => (
            <div key={i} className="panel">
              {rule.conditions.map((cond, ci) => (
                <div key={ci} className="form-grid">
                  <select aria-label={fl.attribute} value={cond.attribute} onChange={(e) => setCondition(i, ci, { attribute: e.target.value, operator: FLAG_ATTRIBUTES[e.target.value][0] })}>
                    {ATTRIBUTES.map((a) => <option key={a} value={a}>{a}</option>)}
                  </select>
                  <select aria-label={fl.operator} value={cond.operator} onChange={(e) => setCondition(i, ci, { operator: e.target.value })}>
                    {(FLAG_ATTRIBUTES[cond.attribute] || []).map((o) => <option key={o} value={o}>{o}</option>)}
                  </select>
                  <input aria-label={fl.values} value={cond.values} onChange={(e) => setCondition(i, ci, { values: e.target.value })} />
                </div>
              ))}
              <div className="form-grid">
                <input aria-label={fl.ruleValue} value={rule.value} onChange={(e) => setRule(i, { value: e.target.value })} />
                <input aria-label={fl.ruleRollout} type="number" min="0.01" max="100" step="0.01" value={rule.rolloutPercentage} onChange={(e) => setRule(i, { rolloutPercentage: e.target.value })} />
              </div>
              <button type="button" className="button button-secondary button-sm" onClick={() => setRule(i, { conditions: [...rule.conditions, { attribute: 'platform', operator: 'In', values: '' }] })}>{fl.addCondition}</button>
              {' '}
              <button type="button" className="button button-secondary button-sm" onClick={() => setForm((v) => ({ ...v, rules: v.rules.filter((_, j) => j !== i) }))}>{fl.removeRule}</button>
            </div>
          ))}
          <button type="button" className="button button-secondary button-sm" onClick={() => setForm((v) => ({ ...v, rules: [...v.rules, { value: v.kind === 'Boolean' ? 'true' : '', rolloutPercentage: '100', conditions: [{ attribute: 'platform', operator: 'In', values: '' }] }] }))}>{fl.addRule}</button>
          {Object.keys(errors).some((x) => x.startsWith('rules')) && <span className="field-error">{Object.entries(errors).filter(([x]) => x.startsWith('rules')).map(([x, m]) => x + ': ' + m).join(' ')}</span>}
        </fieldset>
      </form>
    </Modal>
  );
}

/**
 * Feature flags (docs/LIVE_UPDATES_CONTRACT.md sections 12 and 16): enumerated flags with kind, state, draft,
 * client, label, and tag filters; the editor with a rule builder; Test as; archive, restore, delete; pending content
 * drafts with Publish and Discard; and the version history with Restore.
 */
function FlagsView() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const table = useTableState({ storageKey: 'tk.admin.flags', filterKeys: FILTERS, defaultSort: '' });
  const [editing, setEditing] = useState(null);
  const [dialog, setDialog] = useState(null);
  const [testResult, setTestResult] = useState('');
  const query = useMemo(() => buildFlagsQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.meta]);
  const fetcher = useCallback((signal) => client.enumerateFlags(query, signal), [client, query]);
  const list = useApiQuery(fetcher);
  const draftsFetcher = useCallback((signal) => client.getContentDrafts({ maxResults: 1 }, signal), [client]);
  const drafts = useApiQuery(draftsFetcher);
  const versionsFetcher = useCallback((signal) => client.enumerateContentVersions({ maxResults: 20 }, signal), [client]);
  const versions = useApiQuery(versionsFetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;

  const reloadAll = () => {
    list.reload();
    drafts.reload();
    versions.reload();
  };
  const run = async (fn, message) => {
    try {
      const result = await fn();
      toast.success(typeof message === 'function' ? message(result) : message);
      reloadAll();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };
  const openEditor = async (row) => {
    try {
      const detail = await client.getFlag(row.id);
      setEditing({ flag: detail.flag, etag: detail.flag.eTag });
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };

  const columns = [
    { key: 'key', label: fl.key, render: (row) => <code className="mono">{row.key}</code> },
    { key: 'name', label: fl.name, render: (row) => row.name },
    { key: 'kind', label: fl.kind, render: (row) => fl.kinds[row.kind] || row.kind },
    { key: 'state', label: fl.state, render: (row) => <span className="nowrap"><Badge tone={row.state === 'Published' ? 'success' : row.state === 'Archived' ? 'neutral' : 'info'}>{fl.states[row.state] || row.state}</Badge>{row.hasDraftChanges && <> <Badge tone="warning">{fl.draft}</Badge></>}</span> },
    { key: 'clients', label: fl.clients, render: (row) => row.clients.map((x) => fl.clientNames[x] || x).join(', ') },
    { key: 'updated', label: fl.updated, render: (row) => formatShortDate(row.updatedUtc) },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{u.columns.actions}</span>,
      render: (row) => (
        <ActionMenu
          label={strings.admin.common.rowActions(row.key)}
          items={[
            { label: strings.admin.common.edit, icon: 'edit', onSelect: () => openEditor(row) },
            { label: fl.testAs, icon: 'play', onSelect: () => { setTestResult(''); setDialog({ type: 'test', row }); } },
            ...(row.publishedInVersion || row.state !== 'Draft' ? [row.state === 'Archived'
              ? { label: fl.restore, icon: 'refresh', onSelect: () => run(() => client.restoreFlag(row.id), fl.restored) }
              : { label: fl.archive, icon: 'trash', onSelect: () => run(() => client.archiveFlag(row.id), fl.archived) }] : []),
            ...(row.state === 'Draft' && !row.publishedInVersion ? [{ label: fl.remove, icon: 'trash', danger: true, onSelect: () => run(() => client.deleteFlag(row.id), fl.removed) }] : [])
          ]}
        />
      )
    }
  ];

  const versionColumns = [
    { key: 'version', label: fl.version, render: (row) => row.version },
    { key: 'message', label: u.message, render: (row) => row.message },
    { key: 'kind', label: fl.versionKind, render: (row) => row.kind + (row.restoredFromVersion ? ' (' + row.restoredFromVersion + ')' : '') },
    { key: 'by', label: fl.publishedBy, render: (row) => row.publishedBy },
    { key: 'when', label: u.columns.created, render: (row) => formatShortDate(row.publishedUtc) },
    { key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{u.columns.actions}</span>, render: (row) => <button type="button" className="button button-secondary button-sm" onClick={() => setDialog({ type: 'restore', row })}>{fl.restoreVersion}</button> }
  ];

  const yesNo = [{ value: 'true', label: u.yes }, { value: 'false', label: u.no }];
  const filterFields = [
    { key: 'search', label: fl.searchLabel, placeholder: fl.searchPlaceholder, grow: true },
    { key: 'kind', label: fl.kind, type: 'select', options: optionsFrom(fl.kinds) },
    { key: 'state', label: fl.state, type: 'select', options: optionsFrom(fl.states) },
    { key: 'hasDraftChanges', label: fl.draft, type: 'select', options: yesNo },
    { key: 'client', label: fl.clients, type: 'select', options: optionsFrom(fl.clientNames) }
  ];

  return (
    <div className="view">
      <PageHeader
        title={strings.admin.routes.flags.label}
        summary={strings.admin.routes.flags.summary}
        actions={<button type="button" className="button button-primary button-sm" onClick={() => setEditing({ flag: null })}><Icon name="plus" size={14} /><span>{fl.create}</span></button>}
      />
      <ContentPublishPanel client={client} drafts={drafts} onChanged={() => { list.reload(); versions.reload(); }} />
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={client} targetType="FeatureFlag" />
      </section>
      <section className="table-section">
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading} pageSizes={ADMIN_PAGE_SIZES} />
        <DataTable caption={strings.admin.routes.flags.label} columns={columns} rows={page ? page.items : []} rowKey={(row) => row.id} loading={list.loading} error={list.error ? errorMessage(list.error, '') : null} onRetry={list.reload} emptyMessage={table.hasFilters ? fl.emptyFiltered : fl.empty} />
      </section>
      <section className="table-section">
        <h2>{fl.versions}</h2>
        <DataTable caption={fl.versions} columns={versionColumns} rows={versions.data ? normalizePage(versions.data).items : []} rowKey={(row) => row.id} loading={versions.loading} emptyMessage={fl.noVersions} />
      </section>
      {editing && <FlagEditor flag={editing.flag} etag={editing.etag} client={client} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); toast.success(fl.saved); reloadAll(); }} />}
      {dialog && dialog.type === 'test' && (
        <FormDialog
          open
          title={fl.testAs + ': ' + dialog.row.key}
          submitLabel={fl.testRun}
          fields={[
            { key: 'platform', label: fl.testPlatform, type: 'select', options: [{ value: 'ios', label: 'ios' }, { value: 'android', label: 'android' }, { value: 'web', label: 'web' }] },
            { key: 'appVersion', label: fl.testVersion },
            { key: 'userId', label: fl.testUser },
            { key: 'installId', label: fl.testInstall }
          ]}
          initial={{ platform: 'ios' }}
          onClose={() => setDialog(null)}
          onSubmit={async (v) => {
            const result = await client.evaluateFlag(dialog.row.id, { platform: v.platform, appVersion: v.appVersion || undefined, userId: v.userId || undefined, installId: v.installId || undefined, signedIn: Boolean(v.userId) });
            setTestResult(fl.testResult(JSON.stringify(result.value), result.reason));
          }}
        >
          {testResult && <p role="status"><strong>{testResult}</strong></p>}
        </FormDialog>
      )}
      <ConfirmModal
        open={Boolean(dialog && dialog.type === 'restore')}
        title={dialog && dialog.row ? fl.restoreTitle(dialog.row.version) : ''}
        message={fl.restoreBody}
        confirmLabel={fl.restoreVersion}
        onConfirm={() => { const row = dialog.row; setDialog(null); run(() => client.restoreContentVersion(row.version), (v) => fl.versionRestored(v.version)); }}
        onCancel={() => setDialog(null)}
      />
    </div>
  );
}

export default FlagsView;
