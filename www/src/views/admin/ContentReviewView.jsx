import { useCallback, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import {
  ContentChangeClients, ContentReviewActions, ContentReviewCategories, ContentReviewSeverities, ContentReviewStatuses, contentReviewActionAvailable
} from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import { errorMessage } from '../../utils/api.js';
import { formatDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import DataTable from '../../components/DataTable.jsx';
import Modal from '../../components/Modal.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner } from '../../components/States.jsx';
import { actionBody, contentLink, memberLink, queueFilters, reviewTone, severityTone, snapshotExcerpt } from './contentReviewUtils.js';

const s = strings.contentReview;
const FILTER_KEYS = ['status', 'category', 'severity', 'targetType', 'client', 'actor'];

function Tile({ label, value }) {
  return <div className="ai-tile"><span className="muted small">{label}</span><strong>{value}</strong></div>;
}

function who(person) {
  return person ? person.displayName || person.handle || person.id : s.system;
}

function ActionDialog({ row, action, onClose, onDone }) {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [form, setForm] = useState({ reason: '', note: '', until: '' });
  const [problem, setProblem] = useState(null);
  const [busy, setBusy] = useState(false);
  const set = (key) => (e) => setForm((v) => ({ ...v, [key]: e.target.value }));
  const submit = async (e) => {
    e.preventDefault();
    const built = actionBody(action, form);
    if (built.error) {
      setProblem(built.error);
      return;
    }
    setProblem(null);
    setBusy(true);
    try {
      const result = await client.adminActOnContentChange(row.id, built.body);
      toast.success(s.done(s.actions[action]));
      onDone(result.change);
    } catch (err) {
      toast.error(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };
  return (
    <Modal open onClose={onClose} title={s.actions[action]} subtitle={s.actionHelp[action]} footer={(
      <>
        <button type="button" className="button button-secondary" onClick={onClose}>{s.close}</button>
        <button type="submit" form="content-review-action" className={'button ' + (action === 'Dismiss' ? 'button-primary' : 'button-danger')} disabled={busy}>{s.confirm}</button>
      </>
    )}>
      <form id="content-review-action" className="stack-form" onSubmit={submit} noValidate>
        <div className="field">
          <label className="label-text" htmlFor="content-review-reason">{action === 'Dismiss' ? s.reasonOptional : s.reason}</label>
          <input id="content-review-reason" value={form.reason} onChange={set('reason')} maxLength={500} aria-invalid={problem === 'reason'} aria-describedby={problem === 'reason' ? 'content-review-reason-error' : undefined} />
          {problem === 'reason' && <span id="content-review-reason-error" className="field-error small" role="alert">{s.reasonProblem}</span>}
        </div>
        {action === 'Mute' && (
          <label className="field"><span className="label-text">{s.until}</span>
            <input type="datetime-local" value={form.until} onChange={set('until')} />
          </label>
        )}
        <label className="field"><span className="label-text">{s.internalNote}</span>
          <textarea rows={3} value={form.note} onChange={set('note')} maxLength={2000} />
        </label>
      </form>
    </Modal>
  );
}

function ChangeDetail({ id, onClose, onChanged }) {
  const { client } = useAdminAuth();
  const [row, setRow] = useState(null);
  const [error, setError] = useState(null);
  const [action, setAction] = useState(null);
  const load = useCallback(async () => {
    try {
      setRow(await client.adminGetContentChange(id));
      setError(null);
    } catch (err) {
      setError(errorMessage(err));
    }
  }, [client, id]);
  useEffect(() => { load(); }, [load]);
  const link = row ? contentLink(row) : null;
  const member = row ? memberLink(row) : null;
  const type = row ? (s.targetTypes[row.targetType] || row.targetType) : '';
  return (
    <Modal open onClose={onClose} size="lg" title={row ? s.detailTitle(type) : s.title} subtitle={row ? formatDateTime(row.createdUtc) : null}
      footer={row ? (
        <div className="badge-row">
          {ContentReviewActions.filter((a) => contentReviewActionAvailable(row, a)).map((a) => (
            <button key={a} type="button" className={'button button-sm ' + (a === 'Dismiss' ? 'button-secondary' : 'button-danger')} onClick={() => setAction(a)}>{s.actions[a]}</button>
          ))}
        </div>
      ) : null}>
      <ErrorBanner message={error} onRetry={load} />
      {row && (
        <div className="stack">
          <div className="badge-row">
            <Badge tone={reviewTone(row.reviewStatus)}>{s.statuses[row.reviewStatus] || row.reviewStatus}</Badge>
            {row.severity !== 'None' && <Badge tone={severityTone(row.severity)}>{s.severities[row.severity] || row.severity}</Badge>}
            {(row.categories || []).map((c) => <Badge key={c} tone="danger">{s.categories[c] || c}</Badge>)}
            <span className="muted small">{s.changeActions[row.action] || row.action}</span>
            <span className="muted small">{s.clients[row.client] || row.client}</span>
            <span className="muted small">{who(row.actor || row.author)}</span>
          </div>
          {row.contextType && <p className="muted small">{s.context} {s.targetTypes[row.contextType] || row.contextType} <code className="mono">{row.contextId}</code></p>}
          <div className="badge-row">
            {link && <Link className="button button-ghost button-sm" to={link}>{s.open}</Link>}
            {member && <Link className="button button-ghost button-sm" to={member}>{s.openMember}</Link>}
          </div>
          <h3 className="section-title">{s.snapshot}</h3>
          {row.snapshot ? <pre className="content-review-snapshot">{row.snapshot}</pre> : <p className="muted">{s.noText}</p>}
          {(row.mediaRefs || []).length > 0 && (
            <>
              <h3 className="section-title">{s.media}</h3>
              <p className="muted small">{s.mediaNote}</p>
              <ul className="plain-list">{row.mediaRefs.map((m) => <li key={m}><code className="mono">{m}</code></li>)}</ul>
            </>
          )}
          <h3 className="section-title">{s.review}</h3>
          {row.reviewedUtc && <p className="muted small">{s.reviewedBy(row.reviewedBy || row.reviewerKind || '', formatDateTime(row.reviewedUtc))}</p>}
          {row.reviewerNote && <p><strong>{s.note}:</strong> {row.reviewerNote}</p>}
          {row.reviewAttempts > 0 && <p className="muted small">{s.attempts(row.reviewAttempts)}</p>}
          {row.lastError && <p className="field-error small"><strong>{s.lastError}:</strong> {row.lastError}</p>}
        </div>
      )}
      {action && row && (
        <ActionDialog row={row} action={action} onClose={() => setAction(null)} onDone={(next) => { setAction(null); setRow(next); onChanged(); }} />
      )}
    </Modal>
  );
}

/**
 * Admin, Community, Content review: the content changelog with its Terms of Service review (docs/INTEGRATION.md
 * owner requirement R3). Filters live in the URL; `?id=` opens a row (the ContentFlagged notification links there).
 */
function ContentReviewView() {
  const { client } = useAdminAuth();
  const [params, setParams] = useSearchParams();
  const [summary, setSummary] = useState(null);
  const [page, setPage] = useState({ items: [], token: null, total: null, loading: true });
  const [error, setError] = useState(null);
  const filters = Object.fromEntries(FILTER_KEYS.map((k) => [k, params.get(k) || '']));
  const search = params.get('q') || '';
  const openId = params.get('id');
  const key = JSON.stringify([filters, search]);

  const load = useCallback(async (continuationToken) => {
    try {
      const [form, text] = JSON.parse(key);
      const query = { maxResults: 50, filters: queueFilters(form) };
      if (text.trim()) query.search = text.trim();
      if (continuationToken) query.continuationToken = continuationToken;
      const [sum, result] = await Promise.all([continuationToken ? Promise.resolve(null) : client.adminContentReviewSummary(), client.adminEnumerateContentReview(query)]);
      if (sum) setSummary(sum);
      setPage((p) => ({
        items: continuationToken ? p.items.concat(result.objects || []) : (result.objects || []),
        token: result.endOfResults ? null : result.continuationToken,
        total: result.totalRecords,
        loading: false
      }));
      setError(null);
    } catch (err) {
      setError(errorMessage(err));
      setPage((p) => ({ ...p, loading: false }));
    }
  }, [client, key]);
  useEffect(() => { load(null); }, [load]);

  const setParam = (name, value) => {
    const next = new URLSearchParams(params);
    if (value) next.set(name, value);
    else next.delete(name);
    setParams(next, { replace: true });
  };
  const clear = () => setParams(new URLSearchParams(openId ? { id: openId } : {}), { replace: true });
  const f = s.filters;
  const c = s.columns;
  const t = s.tiles;
  const select = (name, label, values, labels) => (
    <label className="inline-field" htmlFor={'content-review-' + name}>
      <span className="label-text">{label}</span>
      <select id={'content-review-' + name} value={filters[name]} onChange={(e) => setParam(name, e.target.value)}>
        <option value="">{f.any}</option>
        {values.map((v) => <option key={v} value={v}>{labels[v] || v}</option>)}
      </select>
    </label>
  );

  return (
    <section className="stack">
      <PageHeader title={s.title} summary={s.intro} />
      <ErrorBanner message={error} onRetry={() => load(null)} />
      {summary && (
        <>
          {summary.reviewerReady
            ? <p className="muted small" role="status">{s.ready}{summary.lastRunStatus ? ' ' + s.lastRun(summary.lastRunStatus, formatDateTime(summary.lastRunUtc)) : ''}</p>
            : <p className="field-error" role="status"><strong>{s.notReady}</strong> {summary.reviewerMessage}</p>}
          <div className="ai-tiles">
            <Tile label={t.pending} value={summary.pending} />
            <Tile label={t.flagged} value={summary.flagged} />
            <Tile label={t.flaggedDay} value={summary.flaggedLast24Hours} />
            <Tile label={t.failing} value={summary.failing} />
            <Tile label={t.oldest} value={summary.oldestPendingUtc ? formatDateTime(summary.oldestPendingUtc) : '-'} />
          </div>
        </>
      )}
      <div className="filter-bar badge-row">
        {select('status', f.status, ContentReviewStatuses, s.statuses)}
        {select('category', f.category, ContentReviewCategories, s.categories)}
        {select('severity', f.severity, ContentReviewSeverities.filter((v) => v !== 'None'), s.severities)}
        {select('targetType', f.targetType, Object.keys(s.targetTypes), s.targetTypes)}
        {select('client', f.client, ContentChangeClients, s.clients)}
        <label className="inline-field" htmlFor="content-review-actor">
          <span className="label-text">{f.actor}</span>
          <input id="content-review-actor" value={filters.actor} onChange={(e) => setParam('actor', e.target.value)} placeholder="usr_" />
        </label>
        <label className="inline-field" htmlFor="content-review-search">
          <span className="label-text">{f.search}</span>
          <input id="content-review-search" type="search" value={search} onChange={(e) => setParam('q', e.target.value)} />
        </label>
        <button type="button" className="button button-ghost button-sm" onClick={clear}>{f.clear}</button>
      </div>
      {page.total != null && <p className="muted small">{s.total(page.total)}</p>}
      <DataTable caption={s.title} rows={page.items} rowKey={(row) => row.id} loading={page.loading} emptyMessage={s.empty} onRowClick={(row) => setParam('id', row.id)} columns={[
        { key: 'when', label: c.when, render: (row) => formatDateTime(row.createdUtc) },
        { key: 'content', label: c.content, render: (row) => (
          <div className="cell-stack">
            <strong>{s.targetTypes[row.targetType] || row.targetType}</strong>
            <span className="muted small">{snapshotExcerpt(row.snapshot) || s.noText}</span>
          </div>
        ) },
        { key: 'action', label: c.action, render: (row) => s.changeActions[row.action] || row.action },
        { key: 'actor', label: c.actor, render: (row) => who(row.actor || row.author) },
        { key: 'client', label: c.client, render: (row) => s.clients[row.client] || row.client },
        { key: 'status', label: c.status, render: (row) => <Badge tone={reviewTone(row.reviewStatus)}>{s.statuses[row.reviewStatus] || row.reviewStatus}</Badge> },
        { key: 'categories', label: c.categories, render: (row) => (row.categories || []).map((x) => s.categories[x] || x).join(', ') },
        { key: 'severity', label: c.severity, render: (row) => (row.severity && row.severity !== 'None' ? <Badge tone={severityTone(row.severity)}>{s.severities[row.severity] || row.severity}</Badge> : '') }
      ]} />
      {page.token && <button type="button" className="button button-secondary" onClick={() => load(page.token)}>{s.loadMore}</button>}
      {openId && <ChangeDetail id={openId} onClose={() => setParam('id', '')} onChanged={() => load(null)} />}
    </section>
  );
}

export default ContentReviewView;
