import { useCallback, useMemo, useState } from 'react';
import { normalizeEnumerationResult } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, WORD_DEFAULT_SORT, WORD_FILTERS, buildWordFilterQuery } from '../../utils/adminQuery.js';
import { optionsFrom } from '../../utils/labels.js';
import { formatShortDate } from '../../utils/format.js';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import Modal from '../../components/Modal.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner } from '../../components/States.jsx';

const s = strings.admin.wordFilter;
const c = strings.admin.common;
const col = s.columns;
export const IMPORT_MAX = 1000;

/** Validate a term form. */
export function validateTerm({ term }) {
  return String(term || '').trim() ? {} : { term: s.termRequired };
}

/** Terms from the import textarea: one per line, trimmed, de-duplicated case-insensitively. */
export function parseImportTerms(text) {
  const seen = new Set();
  const out = [];
  for (const line of String(text || '').split(/\r?\n/)) {
    const term = line.trim();
    if (term && !seen.has(term.toLowerCase())) {
      seen.add(term.toLowerCase());
      out.push(term);
    }
  }
  return out;
}

/**
 * Split text around word filter matches (`{ start, length }`) for highlighting.
 * @returns {Array<{ text: string, match: boolean }>}
 */
export function highlightMatches(text, matches) {
  const parts = [];
  let at = 0;
  for (const m of [...(matches || [])].sort((a, b) => a.start - b.start)) {
    if (m.start < at) continue;
    if (m.start > at) parts.push({ text: text.slice(at, m.start), match: false });
    parts.push({ text: text.slice(m.start, m.start + m.length), match: true });
    at = m.start + m.length;
  }
  if (at < text.length) parts.push({ text: text.slice(at), match: false });
  return parts;
}

function TermDialog({ term, onClose, onSaved }) {
  const { api } = useAdminAuth();
  const [form, setForm] = useState(() => ({ term: term ? term.term : '', matchType: term ? term.matchType : 'Word', action: term ? term.action : 'Block', note: term ? term.note || '' : '' }));
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const set = (key) => (e) => setForm((f) => ({ ...f, [key]: e.target.value }));
  const submit = async (e) => {
    e.preventDefault();
    const found = validateTerm(form);
    setErrors(found);
    if (Object.keys(found).length) return;
    setBusy(true);
    setError('');
    try {
      const body = { term: form.term.trim(), matchType: form.matchType, action: form.action, note: form.note.trim() || null };
      await (term ? api.updateWordFilterTerm(term.id, body) : api.createWordFilterTerm(body));
      onSaved(term ? s.updated : s.created);
    } catch (err) {
      setError(err && err.status === 409 ? s.duplicate : errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };
  return (
    <Modal open size="sm" onClose={busy ? () => {} : onClose} title={term ? s.editTitle : s.addTitle}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="term-form" className="button button-primary" disabled={busy}>{s.save}</button>
        </>
      )}
    >
      <form id="term-form" className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} />
        <div className="field">
          <label className="label-text" htmlFor="term-term">{s.term}</label>
          <input id="term-term" value={form.term} onChange={set('term')} maxLength={200} aria-invalid={Boolean(errors.term)} disabled={busy} />
          {errors.term && <span className="field-error" role="alert">{errors.term}</span>}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="term-match">{s.matchType}</label>
          <select id="term-match" value={form.matchType} onChange={set('matchType')} disabled={busy}>{Object.entries(s.matchTypes).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select>
        </div>
        <div className="field">
          <label className="label-text" htmlFor="term-action">{s.action}</label>
          <select id="term-action" value={form.action} onChange={set('action')} disabled={busy}>{Object.entries(s.actions).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select>
        </div>
        <div className="field">
          <label className="label-text" htmlFor="term-note">{s.note}</label>
          <input id="term-note" value={form.note} onChange={set('note')} maxLength={500} disabled={busy} />
        </div>
      </form>
    </Modal>
  );
}

function ImportDialog({ onClose, onDone }) {
  const { api } = useAdminAuth();
  const [text, setText] = useState('');
  const [matchType, setMatchType] = useState('Word');
  const [action, setAction] = useState('Block');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const terms = parseImportTerms(text);
  const submit = async (e) => {
    e.preventDefault();
    if (!terms.length) { setError(s.importEmpty); return; }
    if (terms.length > IMPORT_MAX) { setError(s.importTooMany); return; }
    setBusy(true);
    setError('');
    try {
      const result = await api.importWordFilter({ terms, matchType, action });
      onDone(s.importDone(result ? result.added : terms.length, result ? result.skipped : 0));
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };
  return (
    <Modal open size="md" onClose={busy ? () => {} : onClose} title={s.importTitle}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="import-form" className="button button-primary" disabled={busy}>{s.importSubmit}</button>
        </>
      )}
    >
      <form id="import-form" className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} />
        <div className="field">
          <label className="label-text" htmlFor="import-terms">{s.importLabel}</label>
          <textarea id="import-terms" rows={8} value={text} onChange={(e) => setText(e.target.value)} disabled={busy} />
          <span className="muted small">{terms.length} / {IMPORT_MAX}</span>
        </div>
        <div className="hours-fields">
          <label className="field field-compact"><span className="label-text">{s.matchType}</span><select value={matchType} onChange={(e) => setMatchType(e.target.value)}>{Object.entries(s.matchTypes).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select></label>
          <label className="field field-compact"><span className="label-text">{s.action}</span><select value={action} onChange={(e) => setAction(e.target.value)}>{Object.entries(s.actions).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select></label>
        </div>
      </form>
    </Modal>
  );
}

function TestPanel() {
  const { api } = useAdminAuth();
  const [text, setText] = useState('');
  const [result, setResult] = useState(null);
  const [error, setError] = useState('');
  const run = async (e) => {
    e.preventDefault();
    setError('');
    try {
      const body = await api.testWordFilter(text);
      setResult({ text, matches: (body && body.matches) || [] });
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
    }
  };
  return (
    <form className="panel" onSubmit={run} aria-labelledby="wf-test-title">
      <h2 id="wf-test-title">{s.testTitle}</h2>
      <ErrorBanner message={error} />
      <div className="field">
        <label className="label-text" htmlFor="wf-test">{s.testLabel}</label>
        <textarea id="wf-test" rows={2} value={text} onChange={(e) => setText(e.target.value)} placeholder={s.testPlaceholder} />
      </div>
      <div className="form-actions form-actions-start"><button type="submit" className="button button-secondary button-sm" disabled={!text.trim()}>{s.testSubmit}</button></div>
      {result && (
        <div aria-live="polite">
          <p className="small"><strong>{result.matches.length ? s.testMatches(result.matches.length) : s.testNone}</strong></p>
          {result.matches.length > 0 && (
            <p className="wf-highlight">{highlightMatches(result.text, result.matches).map((part, i) => (part.match ? <mark key={i}>{part.text}</mark> : <span key={i}>{part.text}</span>))}</p>
          )}
          <ul className="badge-row">{result.matches.map((m, i) => <li key={i}><Badge tone={m.action === 'Block' ? 'danger' : 'warning'}>{m.term} ({s.actions[m.action] || m.action})</Badge></li>)}</ul>
        </div>
      )}
    </form>
  );
}

/** Word filter list editor (contract section 9.4): search, action filter, add, edit, delete, import, and test. */
function WordFilterTab() {
  const { api, client } = useAdminAuth();
  const toast = useToast();
  const table = useTableState({ storageKey: 'tk.admin.wordfilter', filterKeys: WORD_FILTERS, defaultSort: WORD_DEFAULT_SORT, prefix: 'w.' });
  const [dialog, setDialog] = useState(null);
  const [jsonRow, setJsonRow] = useState(null);
  const [busy, setBusy] = useState(false);
  const query = useMemo(() => buildWordFilterQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.sort, table.meta]);
  const fetcher = useCallback((signal) => api.listWordFilter(query, signal), [api, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const done = (message) => {
    toast.success(message);
    setDialog(null);
    list.reload();
  };
  const loadStarter = async () => {
    setBusy(true);
    try {
      const result = await api.loadWordFilterStarter();
      done(s.importDone(result ? result.added : 0, result ? result.skipped : 0));
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };
  const remove = async () => {
    setBusy(true);
    try {
      await api.deleteWordFilterTerm(dialog.term.id);
      done(s.deleted);
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };
  const columns = [
    { key: 'term', label: col.term, sortField: 'term', render: (row) => <code className="mono">{row.term}</code> },
    { key: 'matchType', label: col.matchType, render: (row) => s.matchTypes[row.matchType] || row.matchType },
    { key: 'action', label: col.action, render: (row) => <Badge tone={row.action === 'Block' ? 'danger' : 'warning'}>{s.actions[row.action] || row.action}</Badge> },
    { key: 'note', label: col.note, className: 'cell-wrap', render: (row) => row.note || <span className="muted">-</span> },
    { key: 'hits', label: col.hits, className: 'num', render: (row) => row.hitCount30d ?? 0 },
    { key: 'createdBy', label: col.createdBy, render: (row) => row.createdByName || '-' },
    { key: 'updated', label: col.updated, sortField: 'updatedUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => formatShortDate(row.updatedUtc || row.createdUtc) },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>,
      render: (row) => <ActionMenu label={c.rowActions(row.term)} items={[
        { label: c.edit, icon: 'edit', onSelect: () => setDialog({ kind: 'edit', term: row }) },
        { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) },
        { label: strings.common.delete, icon: 'trash', danger: true, onSelect: () => setDialog({ kind: 'delete', term: row }) }
      ]} />
    }
  ];
  const fields = [
    { key: 'search', label: s.searchLabel, placeholder: s.searchPlaceholder, grow: true },
    { key: 'action', label: s.action, type: 'select', options: optionsFrom(s.actions) }
  ];
  const rows = page ? page.items : (list.data ? normalizeEnumerationResult(list.data).objects : []);
  return (
    <>
      <p className="muted small">{s.summary}</p>
      <section className="panel">
        <AdminFilterPanel fields={fields} table={table} client={client} targetType="WordFilterTerm" />
      </section>
      <section className="table-section">
        <div className="table-toolbar">
          <div className="toolbar-left" />
          <div className="toolbar-right">
            <button type="button" className="button button-secondary button-sm" onClick={() => setDialog({ kind: 'starter' })}><Icon name="shield" size={14} /><span>{s.starterButton}</span></button>
            <button type="button" className="button button-secondary button-sm" onClick={() => setDialog({ kind: 'import' })}><Icon name="upload" size={14} /><span>{s.importButton}</span></button>
            <button type="button" className="button button-primary button-sm" onClick={() => setDialog({ kind: 'add' })}><Icon name="plus" size={14} /><span>{s.add}</span></button>
          </div>
        </div>
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading} pageSizes={ADMIN_PAGE_SIZES} />
        <DataTable caption={s.title} columns={columns} rows={rows} rowKey={(row) => row.id} loading={list.loading} error={list.error ? c.unavailable(s.title) + ' ' + errorMessage(list.error, '') : null} onRetry={list.reload} emptyMessage={table.hasFilters ? s.emptyFiltered : s.empty} onRowClick={(row) => setDialog({ kind: 'edit', term: row })} sort={table.sort} onSortChange={table.setSort} />
      </section>
      <TestPanel />
      <p className="muted small">{s.disabledNote}</p>
      {dialog && (dialog.kind === 'add' || dialog.kind === 'edit') && <TermDialog term={dialog.term || null} onClose={() => setDialog(null)} onSaved={done} />}
      {dialog && dialog.kind === 'import' && <ImportDialog onClose={() => setDialog(null)} onDone={done} />}
      <ConfirmModal open={Boolean(dialog && dialog.kind === 'starter')} title={s.starterTitle} message={s.starterBody} confirmLabel={s.starterConfirm} danger={false} busy={busy} onCancel={() => setDialog(null)} onConfirm={loadStarter} />
      <ConfirmModal open={Boolean(dialog && dialog.kind === 'delete')} title={dialog && dialog.term ? s.deleteTitle(dialog.term.term) : ''} message={s.deleteBody} confirmLabel={strings.common.delete} busy={busy} onCancel={() => setDialog(null)} onConfirm={remove} />
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.id} value={jsonRow} />
    </>
  );
}

export default WordFilterTab;
