import useCollectionPage from '../../hooks/useCollectionPage.js';
import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  DOCUMENT_ACCEPTANCE_FILTER_KEYS, DOCUMENT_CHANGE_SUMMARY_MAX, DOCUMENT_TITLE_MAX, DOCUMENT_VERSION_MAX, DocumentAcceptanceClient, DocumentAcceptanceMethod,
  documentPath, formatRelativeTime, isGatingDocument, normalizeEnumerationResult, validatePublishDocument
} from '@trelliskit/shared';
import strings from '../../strings.js';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, pagedQuery } from '../../utils/adminQuery.js';
import { formatDateTime } from '../../utils/format.js';
import Markdown from '../../views/marketing/markdown.jsx';
import AdminFilterPanel from '../AdminFilterPanel.jsx';
import DataTable from '../DataTable.jsx';
import TablePagination from '../TablePagination.jsx';
import Modal from '../Modal.jsx';
import Icon from '../Icon.jsx';
import { Badge } from '../Badges.jsx';
import { Banner, ErrorBanner, Loading } from '../States.jsx';
import TableColumns from '../TableColumns.jsx';

const d = strings.documents;
const s = d.admin;
const c = strings.admin.common;

/** Filter keys of the acceptances table (`search` plus the collection filters). */
export const ACCEPTANCE_FILTERS = Object.freeze(['search', ...DOCUMENT_ACCEPTANCE_FILTER_KEYS.filter((k) => k !== 'userId')]);
/** Versions fetched per "Load more". */
export const VERSIONS_PAGE = 25;

/** The acceptances enumeration body (`POST .../documents/acceptances/enumerate`). */
export function buildAcceptancesQuery(filters, paging) {
  return pagedQuery(filters, ACCEPTANCE_FILTERS, paging);
}

/**
 * The adapter the manager reads through: the admin console passes the admin routes, the platform
 * console the routes of one property. Every method takes an AbortSignal where it reads.
 * @typedef {{
 *   list: (signal?: AbortSignal) => Promise<{ objects: object[], syncsFromFolder: boolean }>,
 *   get: (key: string, signal?: AbortSignal) => Promise<object>,
 *   enumerateVersions: (key: string, query: object, signal?: AbortSignal) => Promise<object>,
 *   getVersion: (key: string, versionId: string, signal?: AbortSignal) => Promise<object>,
 *   publish: (key: string, body: object) => Promise<object>,
 *   enumerateAcceptances: (query: object, signal?: AbortSignal) => Promise<object>,
 *   metadataClient?: object,
 *   publicLinks?: boolean
 * }} DocumentsSource
 */

function count(value) {
  return typeof value === 'number' ? value : 0;
}

function memberCell(row) {
  if (!row.user) return <span className="muted">{s.acceptances.deletedMember}</span>;
  return (
    <span className="cell-stack">
      <span>{row.user.displayName || row.user.handle || row.user.id}</span>
      {row.user.handle && <span className="muted small">{'@' + row.user.handle}</span>}
    </span>
  );
}

/** The publish form: version, optional title, Markdown prefilled with the current content, change summary, and re-acceptance. */
export function PublishForm({ doc, source, onPublished, onCancel }) {
  const toast = useToast();
  const p = s.publish;
  const gating = isGatingDocument(doc);
  const [values, setValues] = useState({ version: '', title: '', content: doc.content || '', changeSummary: '', requireReacceptance: doc.reacceptOnChange === true });
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);
  const set = (key) => (e) => setValues((v) => ({ ...v, [key]: e.target.type === 'checkbox' ? e.target.checked : e.target.value }));
  const message = (code, max) => (code === 'tooLong' ? p.errors.tooLong(max) : p.errors.required);

  const submit = async (e) => {
    e.preventDefault();
    const problems = validatePublishDocument(values);
    const found = {};
    if (problems.version) found.version = message(problems.version, DOCUMENT_VERSION_MAX);
    if (problems.content) found.content = message(problems.content, 0);
    if (problems.changeSummary) found.changeSummary = message(problems.changeSummary, DOCUMENT_CHANGE_SUMMARY_MAX);
    if (problems.title) found.title = message(problems.title, DOCUMENT_TITLE_MAX);
    setErrors(found);
    if (Object.keys(found).length) return;
    setBusy(true);
    try {
      const body = { version: values.version, content: values.content, changeSummary: values.changeSummary, title: values.title };
      if (gating) body.requireReacceptance = values.requireReacceptance;
      const result = await source.publish(doc.key, body);
      toast.success(p.published(values.version.trim()));
      onPublished(result);
    } catch (err) {
      const fieldErrors = getFieldErrors(err);
      const mapped = {};
      for (const key of ['version', 'title', 'content', 'changeSummary']) {
        if (fieldErrors[key]) mapped[key] = [].concat(fieldErrors[key]).join(' ');
      }
      if (err && err.code === 'VersionExists') mapped.version = p.errors.versionExists;
      setErrors(mapped);
      if (!Object.keys(mapped).length) toast.error(errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };

  const field = (key, label, input, hint) => (
    <div className="field">
      <label className="label-text" htmlFor={'doc-publish-' + key}>{label}</label>
      {input}
      {hint && <span id={'doc-publish-' + key + '-hint'} className="muted small">{hint}</span>}
      {errors[key] && <span id={'doc-publish-' + key + '-error'} className="field-error">{errors[key]}</span>}
    </div>
  );
  const describe = (key, hint) => [hint ? 'doc-publish-' + key + '-hint' : '', errors[key] ? 'doc-publish-' + key + '-error' : ''].filter(Boolean).join(' ') || undefined;

  return (
    <form className="detail-block form-stack" onSubmit={submit} aria-labelledby="doc-publish-heading" noValidate>
      <h3 id="doc-publish-heading">{p.title(doc.title || doc.key)}</h3>
      {field('version', p.version, <input id="doc-publish-version" value={values.version} maxLength={DOCUMENT_VERSION_MAX} onChange={set('version')} disabled={busy} aria-invalid={Boolean(errors.version)} aria-describedby={describe('version', true)} />, p.versionHint)}
      {field('title', p.docTitle, <input id="doc-publish-title" value={values.title} maxLength={DOCUMENT_TITLE_MAX} placeholder={doc.title || ''} onChange={set('title')} disabled={busy} aria-invalid={Boolean(errors.title)} aria-describedby={describe('title', true)} />, p.docTitleHint)}
      {field('content', p.content, <textarea id="doc-publish-content" className="textarea mono" rows={14} value={values.content} onChange={set('content')} disabled={busy} aria-invalid={Boolean(errors.content)} aria-describedby={describe('content', false)} />)}
      {field('changeSummary', p.changeSummary, <textarea id="doc-publish-changeSummary" className="textarea" rows={2} value={values.changeSummary} maxLength={DOCUMENT_CHANGE_SUMMARY_MAX} onChange={set('changeSummary')} disabled={busy} aria-invalid={Boolean(errors.changeSummary)} aria-describedby={describe('changeSummary', true)} />, p.changeSummaryHint)}
      {gating && (
        <label className="checkbox-row"><input type="checkbox" checked={values.requireReacceptance} onChange={set('requireReacceptance')} disabled={busy} /><span>{p.requireReacceptance}</span></label>
      )}
      <div className="form-actions">
        <button type="submit" className="button button-primary button-sm" disabled={busy}>{busy ? p.submitting : p.submit}</button>
        <button type="button" className="button button-secondary button-sm" onClick={onCancel} disabled={busy}>{strings.common.cancel}</button>
      </div>
    </form>
  );
}

/** A bounded list driven by continuation tokens, with "Load more". `fetchPage(token, signal)` must be stable. */
export function useTokenList(fetchPage, reloadKey) {
  const [state, setState] = useState({ rows: [], token: null, done: false, loading: true, error: null });
  const [nonce, setNonce] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setState({ rows: [], token: null, done: false, loading: true, error: null });
    fetchPage(null, controller.signal).then(
      (page) => {
        if (controller.signal.aborted) return;
        const r = normalizeEnumerationResult(page);
        setState({ rows: r.objects, token: r.continuationToken, done: r.endOfResults || !r.continuationToken, loading: false, error: null });
      },
      (error) => { if (!controller.signal.aborted) setState((prev) => ({ ...prev, loading: false, error })); }
    );
    return () => controller.abort();
  }, [fetchPage, reloadKey, nonce]);
  const loadMore = async () => {
    setState((prev) => ({ ...prev, loading: true, error: null }));
    try {
      const r = normalizeEnumerationResult(await fetchPage(state.token));
      setState((prev) => {
        const seen = new Set(prev.rows.map((row) => row.id));
        return { rows: [...prev.rows, ...r.objects.filter((row) => !seen.has(row.id))], token: r.continuationToken, done: r.endOfResults || !r.continuationToken, loading: false, error: null };
      });
    } catch (error) {
      setState((prev) => ({ ...prev, loading: false, error }));
    }
  };
  const reload = useCallback(() => setNonce((n) => n + 1), []);
  return { ...state, loadMore, reload };
}

function VersionDialog({ docKey, version, source, onClose }) {
  const fetcher = useCallback((signal) => source.getVersion(docKey, version.id, signal), [source, docKey, version.id]);
  const query = useApiQuery(fetcher);
  return (
    <Modal open size="lg" onClose={onClose} title={s.versions.viewTitle(version.version)} subtitle={version.changeSummary || ''}>
      {query.loading && !query.data && <Loading />}
      <ErrorBanner message={query.error ? errorMessage(query.error, '') : ''} onRetry={query.reload} />
      {query.data && <div className="legal-body document-preview"><Markdown source={query.data.content || ''} /></div>}
    </Modal>
  );
}

function VersionsTable({ docKey, source, reloadKey }) {
  const v = s.versions;
  const fetchPage = useCallback((token, signal) => source.enumerateVersions(docKey, token ? { maxResults: VERSIONS_PAGE, continuationToken: token } : { maxResults: VERSIONS_PAGE }, signal), [source, docKey]);
  const list = useTokenList(fetchPage, reloadKey);
  const [viewing, setViewing] = useState(null);
  return (
    <section className="detail-block" aria-labelledby="doc-versions-title">
      <h3 id="doc-versions-title">{v.title}</h3>
      <ErrorBanner message={list.error ? errorMessage(list.error, '') : ''} onRetry={list.reload} />
      {list.rows.length === 0 && !list.loading && !list.error && <p className="muted small">{v.empty}</p>}
      {list.rows.length > 0 && (
        <div className="table-scroll">
          <TableColumns id="tk.documents.DocumentsManager"><table className="data-table">
            <caption className="visually-hidden">{v.caption}</caption>
            <thead><tr>{['version', 'sequence', 'title', 'summary', 'source', 'reaccept', 'accepted', 'created'].map((k) => <th key={k} scope="col">{v.columns[k]}</th>)}<th scope="col"><span className="visually-hidden">{v.view}</span></th></tr></thead>
            <tbody>
              {list.rows.map((row) => (
                <tr key={row.id}>
                  <td className="mono">{row.version}</td>
                  <td className="num">{row.sequence}</td>
                  <td>{row.title}</td>
                  <td>{row.changeSummary || <span className="muted">-</span>}</td>
                  <td><Badge tone="neutral">{d.sources[row.source] || row.source}</Badge></td>
                  <td>{row.requiresReacceptance ? <Badge tone="warning">{v.required}</Badge> : <span className="muted">{v.notRequired}</span>}</td>
                  <td className="num">{count(row.acceptanceCount)}</td>
                  <td className="nowrap"><span title={formatDateTime(row.createdUtc)}>{formatRelativeTime(row.createdUtc)}</span></td>
                  <td><button type="button" className="button button-secondary button-sm" onClick={() => setViewing(row)}>{v.view}</button></td>
                </tr>
              ))}
            </tbody>
          </table></TableColumns>
        </div>
      )}
      {list.loading && <Loading inline label={v.loading} />}
      {!list.done && !list.loading && list.rows.length > 0 && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{v.loadMore}</button>}
      {viewing && <VersionDialog docKey={docKey} version={viewing} source={source} onClose={() => setViewing(null)} />}
    </section>
  );
}

function DocumentDrawer({ summary, source, onClose, onChanged }) {
  const t = s.detail;
  const fetcher = useCallback((signal) => source.get(summary.key, signal), [source, summary.key]);
  const query = useApiQuery(fetcher);
  const [publishing, setPublishing] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const doc = query.data ? { ...summary, ...query.data } : null;
  const path = documentPath(summary);

  const published = () => {
    setPublishing(false);
    setReloadKey((k) => k + 1);
    query.reload();
    onChanged();
  };

  return (
    <Modal open variant="drawer" size="lg" onClose={onClose} title={summary.title || summary.key} subtitle={<code className="mono">{summary.key}</code>}>
      {query.loading && !doc && <Loading label={t.loading} />}
      {query.error && <ErrorBanner message={t.loadError + ' ' + errorMessage(query.error, '')} onRetry={query.reload} />}
      {doc && (
        <div className="detail-stack">
          <section className="detail-block">
            <div className="badge-row">
              <Badge tone="info">{d.kinds[doc.kind] || doc.kind}</Badge>
              <Badge tone={isGatingDocument(doc) ? 'warning' : 'neutral'}>{d.acceptance[doc.acceptance] || doc.acceptance}</Badge>
              {doc.separateCheckbox && <Badge tone="neutral">{s.separateCheckbox}</Badge>}
              {doc.reacceptOnChange && <Badge tone="neutral">{s.reacceptOnChange}</Badge>}
            </div>
            <dl className="kv-grid">
              <div className="kv-item"><dt>{t.key}</dt><dd className="mono">{doc.key}</dd></div>
              <div className="kv-item"><dt>{t.slug}</dt><dd>{path ? (source.publicLinks ? <a href={path} target="_blank" rel="noreferrer">{path}</a> : <code className="mono">{path}</code>) : '-'}</dd></div>
              <div className="kv-item"><dt>{s.columns.version}</dt><dd className="mono">{doc.version}</dd></div>
              <div className="kv-item"><dt>{t.sequence}</dt><dd>{doc.sequence}</dd></div>
              <div className="kv-item"><dt>{t.versionCount}</dt><dd>{count(doc.versionCount)}</dd></div>
              {isGatingDocument(doc) && typeof doc.requiredSequence === 'number' && <div className="kv-item"><dt>{t.requiredSequence}</dt><dd>{t.requiredSequenceValue(doc.requiredSequence)}</dd></div>}
              <div className="kv-item"><dt>{s.columns.acceptedCurrent}</dt><dd>{count(doc.acceptedCurrentCount)}</dd></div>
              <div className="kv-item"><dt>{s.columns.acceptedAny}</dt><dd>{count(doc.acceptedAnyCount)}</dd></div>
              <div className="kv-item"><dt>{s.columns.updated}</dt><dd>{doc.updatedUtc ? formatDateTime(doc.updatedUtc) : '-'}</dd></div>
              {doc.changeSummary && <div className="kv-item"><dt>{t.changeSummary}</dt><dd>{doc.changeSummary}</dd></div>}
            </dl>
            {!publishing && (
              <div className="row-actions">
                <button type="button" className="button button-primary button-sm" onClick={() => setPublishing(true)}><Icon name="upload" size={14} /><span>{t.publish}</span></button>
              </div>
            )}
          </section>
          {publishing && <PublishForm doc={doc} source={source} onPublished={published} onCancel={() => setPublishing(false)} />}
          <section className="detail-block" aria-labelledby="doc-content-title">
            <h3 id="doc-content-title">{t.contentTitle}</h3>
            <div className="legal-body document-preview"><Markdown source={doc.content || ''} /></div>
          </section>
          <VersionsTable docKey={doc.key} source={source} reloadKey={reloadKey} />
        </div>
      )}
    </Modal>
  );
}

function DocumentsTab({ source, list, onChanged }) {
  const [open, setOpen] = useState(null);
  const data = list.data || null;
  const rows = data && Array.isArray(data.objects) ? data.objects : [];
  const columns = [
    { key: 'title', label: s.columns.title, pinned: true, render: (row) => <span className="cell-stack"><span>{row.title}</span><span className="muted small mono">{row.key}</span></span> },
    { key: 'kind', label: s.columns.kind, render: (row) => d.kinds[row.kind] || row.kind },
    { key: 'acceptance', label: s.columns.acceptance, render: (row) => <Badge tone={isGatingDocument(row) ? 'warning' : 'neutral'}>{d.acceptance[row.acceptance] || row.acceptance}</Badge> },
    { key: 'version', label: s.columns.version, render: (row) => <span className="mono">{row.version}</span> },
    { key: 'updated', label: s.columns.updated, className: 'nowrap', render: (row) => (row.updatedUtc ? <span title={formatDateTime(row.updatedUtc)}>{formatRelativeTime(row.updatedUtc)}</span> : '-') },
    { key: 'acceptedCurrent', label: s.columns.acceptedCurrent, className: 'num', render: (row) => (isGatingDocument(row) ? count(row.acceptedCurrentCount) : <span className="muted">-</span>) },
    { key: 'acceptedAny', label: s.columns.acceptedAny, className: 'num', render: (row) => (isGatingDocument(row) ? count(row.acceptedAnyCount) : <span className="muted">-</span>) }
  ];
  return (
    <>
      {data && data.syncsFromFolder && <Banner tone="info" icon="info">{s.syncsFromFolder}</Banner>}
      <section className="table-section">
        <DataTable caption={s.caption} columns={columns} rows={rows} rowKey={(row) => row.key} loading={list.loading} error={list.error ? s.loadError + ' ' + errorMessage(list.error, '') : null} onRetry={list.reload} emptyMessage={s.empty} onRowClick={setOpen} />
      </section>
      {open && <DocumentDrawer key={open.key} summary={open} source={source} onClose={() => setOpen(null)} onChanged={onChanged} />}
    </>
  );
}

function AcceptancesTab({ source, documents }) {
  const a = s.acceptances;
  const table = useTableState({ storageKey: 'tk.admin.documentAcceptances', filterKeys: ACCEPTANCE_FILTERS, defaultSort: '-createdUtc', prefix: 'acc.' });
  const query = useMemo(() => buildAcceptancesQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.sort, table.meta]);
  const fetcher = useCallback((signal) => source.enumerateAcceptances(query, signal), [source, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const rows = page ? page.items : [];
  const columns = [
    { key: 'member', label: a.columns.member, pinned: true, render: memberCell },
    { key: 'document', label: a.columns.document, render: (row) => <span className="cell-stack"><span>{row.documentTitle || row.documentKey}</span><span className="muted small mono">{row.documentKey}</span></span> },
    { key: 'version', label: a.columns.version, render: (row) => <span className="mono">{row.version}</span> },
    { key: 'method', label: a.columns.method, render: (row) => <Badge tone={row.method === DocumentAcceptanceMethod.Reacceptance ? 'info' : 'neutral'}>{d.methods[row.method] || row.method}</Badge> },
    { key: 'client', label: a.columns.client, render: (row) => d.clients[row.client] || row.client },
    { key: 'accepted', label: a.columns.accepted, className: 'nowrap', render: (row) => <span title={formatDateTime(row.acceptedUtc)}>{formatRelativeTime(row.acceptedUtc)}</span> }
  ];
  const filterFields = [
    { key: 'search', label: a.filters.search },
    { key: 'documentKey', label: a.filters.document, type: 'select', options: documents.map((doc) => ({ value: doc.key, label: doc.title || doc.key })) },
    { key: 'version', label: a.filters.version },
    { key: 'method', label: a.filters.method, type: 'select', options: Object.values(DocumentAcceptanceMethod).map((value) => ({ value, label: d.methods[value] })) },
    { key: 'client', label: a.filters.client, type: 'select', options: Object.values(DocumentAcceptanceClient).map((value) => ({ value, label: d.clients[value] })) }
  ];
  return (
    <>
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={source.metadataClient || null} targetType="DocumentAcceptance" />
      </section>
      <section className="table-section">
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading} pageSizes={ADMIN_PAGE_SIZES} />
        <DataTable caption={a.caption} columns={columns} rows={rows} rowKey={(row) => row.id} loading={list.loading} error={list.error ? c.unavailable(a.caption) + ' ' + errorMessage(list.error, '') : null} onRetry={list.reload} emptyMessage={table.hasFilters ? a.emptyFiltered : a.empty} />
      </section>
    </>
  );
}

/**
 * The documents console shared by the admin console (/admin/documents) and the platform console
 * (a property's Documents tab): the documents table with the folder-sync notice, a detail drawer
 * (current content, version history with "Load more", and the publish form), and the
 * acceptances table with document, version, method, client, search, and label and tag filters.
 * @param {{ source: DocumentsSource }} props
 */
function DocumentsManager({ source }) {
  const [tab, setTab] = useState('documents');
  const documentsPage = useCollectionPage();
  const { query: documentsPageQuery, setResult: documentsPageSetResult } = documentsPage;
  const fetcher = useCallback(async (signal) => { const page = await source.list(documentsPageQuery, signal); documentsPageSetResult(page); return page; }, [source, documentsPageQuery, documentsPageSetResult]);
  const list = useApiQuery(fetcher);
  const documents = list.data && Array.isArray(list.data.objects) ? list.data.objects : [];
  return (
    <div className="documents-manager">
      <div className="segmented tab-bar" role="tablist" aria-label={s.tabsLabel}>
        {['documents', 'acceptances'].map((id) => <button key={id} type="button" role="tab" aria-selected={tab === id} className={'segment' + (tab === id ? ' active' : '')} onClick={() => setTab(id)}>{s.tabs[id]}</button>)}
      </div>
      <div role="tabpanel" className="tab-panel">
        <TablePagination {...documentsPage.props} onRefresh={list.reload} loading={list.loading} />
        {tab === 'documents' ? <DocumentsTab source={source} list={list} onChanged={list.reload} /> : <AcceptancesTab source={source} documents={documents} />}
      </div>
    </div>
  );
}

export default DocumentsManager;
