import useCollectionPage from '../../../hooks/useCollectionPage.js';
import TablePagination from '../../../components/TablePagination.jsx';
import { useCallback, useEffect, useState } from 'react';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useToast } from '../../../context/ToastContext.jsx';
import { errorMessage } from '../../../utils/api.js';
import { formatDateTime } from '../../../utils/format.js';
import PageHeader from '../../../components/PageHeader.jsx';
import DataTable from '../../../components/DataTable.jsx';
import Modal from '../../../components/Modal.jsx';
import ConfirmModal from '../../../components/ConfirmModal.jsx';
import { Badge } from '../../../components/Badges.jsx';
import { ErrorBanner } from '../../../components/States.jsx';
import { usd } from '../ai/aiAdminUtils.js';
import { DAYS, describeSchedule, jobBody, jobForm, statusTone } from './jobsUtils.js';

const s = strings.jobsAdmin;
const RUN_STATUSES = ['Queued', 'Running', 'Succeeded', 'PartiallySucceeded', 'Failed', 'Cancelled', 'Skipped'];

function Tile({ label, value }) {
  return <div className="ai-tile"><span className="muted small">{label}</span><strong>{value}</strong></div>;
}

function JobEditor({ job, onClose, onSaved }) {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [form, setForm] = useState(() => jobForm(job));
  const [problem, setProblem] = useState(null);
  const [busy, setBusy] = useState(false);
  const f = s.form;
  const set = (key) => (e) => setForm((v) => ({ ...v, [key]: e.target.type === 'checkbox' ? e.target.checked : e.target.value }));
  const toggleDay = (day) => setForm((v) => ({ ...v, daysOfWeek: v.daysOfWeek.includes(day) ? v.daysOfWeek.filter((d) => d !== day) : [...v.daysOfWeek, day] }));
  const save = async (e) => {
    e.preventDefault();
    const built = jobBody(form);
    if (built.error) {
      setProblem(built);
      return;
    }
    setProblem(null);
    setBusy(true);
    try {
      await client.adminUpdateJob(job.key, built.body);
      toast.success(s.saved);
      onSaved();
    } catch (err) {
      toast.error(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };
  const err = (field) => (problem && problem.error === field ? <span className="field-error small" role="alert">{problem.message}</span> : null);
  const needsValue = ['EveryNMinutes', 'EveryNHours', 'EveryNDays'].includes(form.intervalType);
  const needsTime = ['EveryNDays', 'DaysOfWeek'].includes(form.intervalType);
  return (
    <Modal open onClose={onClose} title={f.title(job.name)} footer={(
      <>
        <button type="button" className="button button-secondary" onClick={onClose}>{f.close}</button>
        <button type="submit" form="job-form" className="button button-primary" disabled={busy}>{f.save}</button>
      </>
    )}>
      <form id="job-form" className="stack-form" onSubmit={save} noValidate>
        <label className="checkbox-row"><input type="checkbox" checked={form.enabled} onChange={set('enabled')} /> {f.enabled}</label>
        <label className="field"><span className="label-text">{f.type}</span>
          <select value={form.intervalType} onChange={set('intervalType')}>
            {Object.keys(f.types).map((t) => <option key={t} value={t}>{f.types[t]}</option>)}
          </select>
        </label>
        {needsValue && <label className="field"><span className="label-text">{f.value}</span><input inputMode="numeric" value={form.intervalValue} onChange={set('intervalValue')} />{err('intervalValue')}</label>}
        {needsTime && <label className="field"><span className="label-text">{f.time}</span><input value={form.time} onChange={set('time')} placeholder="11:00" />{err('time')}</label>}
        {form.intervalType === 'DaysOfWeek' && (
          <fieldset className="field"><legend className="label-text">{f.days}</legend>
            <div className="badge-row">
              {DAYS.map((d) => <label key={d} className="checkbox-row"><input type="checkbox" checked={form.daysOfWeek.includes(d)} onChange={() => toggleDay(d)} /> {d}</label>)}
            </div>
            {err('daysOfWeek')}
          </fieldset>
        )}
        <label className="field"><span className="label-text">{f.concurrency}</span><input inputMode="numeric" value={form.maxConcurrency} onChange={set('maxConcurrency')} />{err('maxConcurrency')}</label>
        <label className="field"><span className="label-text">{f.attempts}</span><input inputMode="numeric" value={form.maxAttempts} onChange={set('maxAttempts')} />{err('maxAttempts')}</label>
        <label className="field"><span className="label-text">{f.budget}</span><input inputMode="decimal" value={form.budgetUsdPerRun} onChange={set('budgetUsdPerRun')} />{err('budgetUsdPerRun')}</label>
      </form>
    </Modal>
  );
}

function RunDetail({ runId, onClose, onChanged }) {
  const pagination = useCollectionPage();
  const { query: pageQuery, setResult } = pagination;
  const { client } = useAdminAuth();
  const toast = useToast();
  const [run, setRun] = useState(null);
  const [items, setItems] = useState([]);
  const [error, setError] = useState(null);
  const [confirm, setConfirm] = useState(false);
  const load = useCallback(async () => {
    try {
      const [r, page] = await Promise.all([client.adminGetJobRun(runId), client.adminEnumerateJobRunItems(runId, pageQuery)]);
      setRun(r);
      setItems(page.objects || []);
      setResult(page);
      setError(null);
    } catch (err) {
      setError(errorMessage(err));
    }
  }, [client, runId, pageQuery, setResult]);
  useEffect(() => { load(); }, [load]);
  const retry = async (item) => {
    try {
      await client.adminRetryJobItem(item.id);
      toast.success(s.retried);
      await load();
      onChanged();
    } catch (err) {
      toast.error(errorMessage(err));
    }
  };
  const cancel = async () => {
    setConfirm(false);
    try {
      await client.adminCancelJobRun(runId);
      toast.success(s.cancelled);
      await load();
      onChanged();
    } catch (err) {
      toast.error(errorMessage(err));
    }
  };
  const c = s.columns;
  const open = run && (run.status === 'Queued' || run.status === 'Running');
  return (
    <Modal open onClose={onClose} size="lg" title={run ? s.runTitle(run.key) : s.runs} subtitle={run ? formatDateTime(run.createdUtc) : null}
      footer={open ? <button type="button" className="button button-danger" onClick={() => setConfirm(true)}>{s.cancel}</button> : null}>
      <ErrorBanner message={error} onRetry={load} />
      {run && (
        <div className="stack">
          <div className="badge-row">
            <Badge tone={statusTone(run.status)}>{run.status}</Badge>
            <span className="muted small">{run.trigger}</span>
            <span className="muted small">{s.itemsOf(run.itemsSucceeded, run.itemsTotal)}</span>
            <span className="muted small">{usd(run.costUsd)}</span>
          </div>
          {run.skippedReason && <p className="muted">{run.skippedReason}</p>}
          {run.error && <p className="field-error">{run.error}</p>}
          <DataTable caption={s.columns.items} rows={items} rowKey={(row) => row.id} emptyMessage={s.none} columns={[
            { key: 'target', label: c.target, render: (row) => <code className="mono">{row.targetKey}</code> },
            { key: 'status', label: c.status, render: (row) => <Badge tone={statusTone(row.status)}>{row.status}</Badge> },
            { key: 'attempts', label: c.attempts, className: 'num', render: (row) => row.attempts + ' / ' + row.maxAttempts },
            { key: 'error', label: c.error, render: (row) => row.error || '' },
            { key: 'actions', label: c.actions, render: (row) => (row.status === 'Dead' || row.status === 'Failed' ? <button type="button" className="button button-secondary button-sm" onClick={() => retry(row)}>{s.retry}</button> : null) }
          ]} />
        </div>
      )}
      <ConfirmModal open={confirm} title={s.cancelTitle} message={s.cancelBody} confirmLabel={s.cancel} onCancel={() => setConfirm(false)} onConfirm={cancel} />
    </Modal>
  );
}

/** Admin, System, Jobs: the job engine's definitions, runs, and dead-letter queue (docs/INTEGRATION.md section 31). */
function JobsView() {
  const jobsPage = useCollectionPage();
  const { query: jobsPageQuery, setResult: jobsPageSetResult } = jobsPage;
  const runsPage = useCollectionPage();
  const { query: runsPageQuery, setResult: runsPageSetResult } = runsPage;
  const deadPageState = useCollectionPage();
  const { query: deadPageStateQuery, setResult: deadPageStateSetResult } = deadPageState;

  const { client } = useAdminAuth();
  const toast = useToast();
  const [overview, setOverview] = useState(null);
  const [jobs, setJobs] = useState(null);
  const [runs, setRuns] = useState({ items: [], loading: true });
  const [dead, setDead] = useState([]);
  const [status, setStatus] = useState('');
  const [editing, setEditing] = useState(null);
  const [runId, setRunId] = useState(null);
  const [error, setError] = useState(null);

  const load = useCallback(async () => {
    try {
      const filters = status ? { status: [status] } : {};
      const [o, list, page, deadPage] = await Promise.all([
        client.adminJobOverview(),
        client.adminListJobs(jobsPageQuery),
        client.adminEnumerateJobRuns({ ...runsPageQuery, filters }),
        client.adminEnumerateJobItems({ ...deadPageStateQuery, filters: { status: ['Dead'] } })
      ]);
      setOverview(o);
      setJobs(list.objects);
      jobsPageSetResult(list); runsPageSetResult(page); deadPageStateSetResult(deadPage);
      setRuns({ items: page.objects || [], loading: false });
      setDead(deadPage.objects || []);
      setError(null);
    } catch (err) {
      setError(errorMessage(err));
      setRuns((r) => ({ ...r, loading: false }));
    }
  }, [client, status, jobsPageQuery, jobsPageSetResult, runsPageQuery, runsPageSetResult, deadPageStateQuery, deadPageStateSetResult]);
  useEffect(() => { load(); }, [load]);

  const run = async (job, dryRun) => {
    try {
      await client.adminRunJob(job.key, dryRun);
      toast.success(s.queued);
      await load();
    } catch (err) {
      toast.error(errorMessage(err));
    }
  };
  const retry = async (item) => {
    try {
      await client.adminRetryJobItem(item.id);
      toast.success(s.retried);
      await load();
    } catch (err) {
      toast.error(errorMessage(err));
    }
  };

  const c = s.columns;
  const t = s.tiles;
  return (
    <section className="stack">
      <PageHeader title={s.title} summary={s.intro} />
      <ErrorBanner message={error} onRetry={load} />
      {overview && (
        <>
          {!overview.enabled && <p className="field-error" role="status">{s.engineOff}</p>}
          {!overview.runsHarnesses && <p className="muted small">{s.harnessOff}</p>}
          <div className="ai-tiles">
            <Tile label={t.month} value={usd(overview.monthCostUsd)} />
            <Tile label={t.budget} value={usd(overview.monthlyBudgetUsd)} />
            <Tile label={t.remaining} value={usd(overview.remainingUsd)} />
            <Tile label={t.openRuns} value={overview.openRuns} />
            <Tile label={t.enabled} value={overview.enabledDefinitions + ' / ' + overview.definitions} />
          </div>
        </>
      )}
      <h2 className="section-title">{s.jobs}</h2>
      <TablePagination {...jobsPage.props} />
      <DataTable caption={s.jobs} rows={jobs || []} rowKey={(row) => row.key} loading={!jobs && !error} emptyMessage={s.none} columns={[
        { key: 'job', label: c.job, render: (row) => (
          <div className="cell-stack">
            <strong>{row.name}</strong>
            <code className="mono small">{row.key}</code>
            <div className="badge-row">
              {row.requiresHarness && <Badge tone="info">{s.harness}</Badge>}
              {!row.handlerAvailable && <Badge tone="danger">{s.missing}</Badge>}
            </div>
          </div>
        ) },
        { key: 'schedule', label: c.schedule, render: (row) => (row.enabled ? describeSchedule(row) : <span className="muted">{s.never}</span>) },
        { key: 'next', label: c.next, render: (row) => (row.nextRunUtc ? formatDateTime(row.nextRunUtc) : <span className="muted">{s.never}</span>) },
        { key: 'latest', label: c.latest, render: (row) => (row.latestRun
          ? <button type="button" className="link-button" onClick={() => setRunId(row.latestRun.id)}><Badge tone={statusTone(row.latestRun.status)}>{row.latestRun.status}</Badge></button>
          : <span className="muted">{s.none}</span>) },
        { key: 'actions', label: c.actions, render: (row) => (
          <div className="badge-row">
            <button type="button" className="button button-secondary button-sm" disabled={!row.handlerAvailable} onClick={() => run(row, false)}>{s.runNow}</button>
            {row.supportsDryRun && <button type="button" className="button button-ghost button-sm" disabled={!row.handlerAvailable} onClick={() => run(row, true)}>{s.dryRun}</button>}
            <button type="button" className="button button-ghost button-sm" onClick={() => setEditing(row)}>{s.edit}</button>
          </div>
        ) }
      ]} />

      <div className="row-between">
        <h2 className="section-title">{s.runs}</h2>
        <label className="inline-field" htmlFor="job-run-status">
          <span className="label-text">{s.statusFilter}</span>
          <select id="job-run-status" value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="">{s.allStatuses}</option>
            {RUN_STATUSES.map((v) => <option key={v} value={v}>{v}</option>)}
          </select>
        </label>
      </div>
      <TablePagination {...runsPage.props} />
      <DataTable caption={s.runs} rows={runs.items} rowKey={(row) => row.id} loading={runs.loading} emptyMessage={s.none} onRowClick={(row) => setRunId(row.id)} columns={[
        { key: 'job', label: c.job, render: (row) => <code className="mono">{row.key}</code> },
        { key: 'trigger', label: c.trigger, render: (row) => row.trigger },
        { key: 'status', label: c.status, render: (row) => <Badge tone={statusTone(row.status)}>{row.status}</Badge> },
        { key: 'items', label: c.items, className: 'num', render: (row) => s.itemsOf(row.itemsSucceeded, row.itemsTotal) + (row.itemsDead ? ' (' + s.deadCount(row.itemsDead) + ')' : '') },
        { key: 'cost', label: c.cost, className: 'num', render: (row) => usd(row.costUsd) },
        { key: 'created', label: c.created, render: (row) => formatDateTime(row.createdUtc) }
      ]} />

      <h2 className="section-title">{s.dead}</h2>
      <TablePagination {...deadPageState.props} />
      <DataTable caption={s.dead} rows={dead} rowKey={(row) => row.id} emptyMessage={s.deadEmpty} columns={[
        { key: 'job', label: c.job, render: (row) => <code className="mono">{row.key}</code> },
        { key: 'target', label: c.target, render: (row) => <code className="mono">{row.targetKey}</code> },
        { key: 'attempts', label: c.attempts, className: 'num', render: (row) => row.attempts },
        { key: 'error', label: c.error, render: (row) => row.error || '' },
        { key: 'actions', label: c.actions, render: (row) => <button type="button" className="button button-secondary button-sm" onClick={() => retry(row)}>{s.retry}</button> }
      ]} />

      {editing && <JobEditor job={editing} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); load(); }} />}
      {runId && <RunDetail runId={runId} onClose={() => setRunId(null)} onChanged={load} />}
    </section>
  );
}

export default JobsView;
