import useCollectionPage from '../../hooks/useCollectionPage.js';
import TablePagination from '../../components/TablePagination.jsx';
import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { collectEnumeration, CRITICAL_MESSAGE_MAX, ROLLOUT_STEPS, canPromote, hasRollout, parseRolloutTemplate, rolloutPlanProblem } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import { errorMessage } from '../../utils/api.js';
import { formatPreciseDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import MetadataPanel from '../../components/MetadataPanel.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import { formatBytes, fromLocalInput, toLocalInput } from './LiveUpdateDialogs.jsx';
import { PromoteDialog, RollbackDialog, UpdateStatus } from './UpdatesView.jsx';
import SymbolicatePanel from './SymbolicatePanel.jsx';

const u = strings.admin.liveUpdates;
const d = u.detail;

/** Editable stage rows from a group's plan. */
export function stagesFromPlan(plan) {
  return (plan || []).map((s) => ({ percentage: String(s.percentage), starts: toLocalInput(s.startsUtc), appliedUtc: s.appliedUtc }));
}

/** The rollout-plan request body from editable stage rows and auto-halt fields. */
export function planBody(stages, autoHalt) {
  return {
    stages: stages.map((s) => ({ percentage: Number(s.percentage), startsUtc: fromLocalInput(s.starts) })),
    autoHaltEnabled: Boolean(autoHalt.enabled),
    autoHaltFailurePercent: Number(autoHalt.failurePercent) || undefined,
    autoHaltMinInstalls: Number(autoHalt.minInstalls) || undefined
  };
}

function RolloutPanel({ group, onChange }) {
  const [platform, setPlatform] = useState('');
  const [pending, setPending] = useState(null);
  const editable = hasRollout(group);
  return (
    <section className="panel">
      <h2>{d.rollout}</h2>
      <p className="muted">{d.rolloutHelp}</p>
      <div className="toolbar-left">
        {group.updates.length > 1 && (
          <select aria-label={u.platform} value={platform} onChange={(e) => setPlatform(e.target.value)} disabled={!editable}>
            <option value="">{d.allPlatforms}</option>
            {group.updates.map((x) => <option key={x.platform} value={x.platform}>{x.platform}</option>)}
          </select>
        )}
        {ROLLOUT_STEPS.map((step) => (
          <button key={step} type="button" className={'button button-sm ' + (Number(group.rolloutPercentage) === step ? 'button-primary' : 'button-secondary')} disabled={!editable} onClick={() => setPending(step)}>
            {u.percent(step)}
          </button>
        ))}
      </div>
      <ConfirmModal
        open={pending !== null}
        title={pending !== null ? d.confirmRollout(pending) : ''}
        message={d.confirmRolloutBody}
        danger={false}
        onConfirm={() => { const step = pending; setPending(null); onChange(step, platform || undefined); }}
        onCancel={() => setPending(null)}
      />
    </section>
  );
}

function SchedulePanel({ group, template, onSave }) {
  const [stages, setStages] = useState(() => stagesFromPlan(group.rolloutPlan));
  const [autoHalt, setAutoHalt] = useState({ enabled: group.autoHalt.enabled, failurePercent: String(group.autoHalt.failurePercent), minInstalls: String(group.autoHalt.minInstalls) });
  const [error, setError] = useState('');
  const setStage = (i, key) => (e) => setStages((list) => list.map((s, j) => (j === i ? { ...s, [key]: e.target.value } : s)));
  const applyTemplate = () => {
    try {
      const base = group.publishedUtc ? new Date(group.publishedUtc).getTime() : Date.now();
      setStages(parseRolloutTemplate(template).map((s) => ({ percentage: String(s.percentage), starts: toLocalInput(new Date(base + s.hoursAfterPublish * 3600000).toISOString()), appliedUtc: null })));
    } catch (err) {
      setError(err.message);
    }
  };
  const save = () => {
    const body = planBody(stages, autoHalt);
    const problem = rolloutPlanProblem(body.stages);
    if (problem) {
      setError(problem);
      return;
    }
    setError('');
    onSave(body);
  };
  const a = group.autoHalt;
  return (
    <section className="panel">
      <h2>{d.schedule}</h2>
      <p className="muted">{d.scheduleHelp}</p>
      {error && <ErrorBanner message={error} />}
      <table className="table">
        <thead><tr><th>{d.stagePercent}</th><th>{d.stageStarts}</th><th>{u.status}</th><th><span className="visually-hidden">{d.removeStage}</span></th></tr></thead>
        <tbody>
          {stages.map((s, i) => (
            <tr key={i}>
              <td><input aria-label={d.stagePercent + ' ' + (i + 1)} type="number" min="0.01" max="100" step="0.01" value={s.percentage} onChange={setStage(i, 'percentage')} /></td>
              <td><input aria-label={d.stageStarts + ' ' + (i + 1)} type="datetime-local" value={s.starts} onChange={setStage(i, 'starts')} /></td>
              <td>{s.appliedUtc ? d.stageApplied : d.stagePending}</td>
              <td><button type="button" className="button button-secondary button-sm" onClick={() => setStages((list) => list.filter((_, j) => j !== i))}>{d.removeStage}</button></td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="toolbar-left">
        <button type="button" className="button button-secondary button-sm" onClick={() => setStages((list) => [...list, { percentage: '', starts: toLocalInput(new Date().toISOString()), appliedUtc: null }])} disabled={stages.length >= 10}>{d.addStage}</button>
        {template && <button type="button" className="button button-secondary button-sm" onClick={applyTemplate}>{d.useTemplate(template)}</button>}
      </div>
      <h3>{d.autoHalt}</h3>
      <p className="muted">{d.autoHaltHelp}</p>
      <div className="form-grid">
        <label className="toggle"><input type="checkbox" checked={autoHalt.enabled} onChange={(e) => setAutoHalt((v) => ({ ...v, enabled: e.target.checked }))} /> {d.autoHaltEnabled}</label>
        <div className="field">
          <label className="label-text" htmlFor="ah-percent">{d.autoHaltPercent}</label>
          <input id="ah-percent" type="number" min="1" max="100" value={autoHalt.failurePercent} onChange={(e) => setAutoHalt((v) => ({ ...v, failurePercent: e.target.value }))} />
        </div>
        <div className="field">
          <label className="label-text" htmlFor="ah-min">{d.autoHaltMin}</label>
          <input id="ah-min" type="number" min="1" value={autoHalt.minInstalls} onChange={(e) => setAutoHalt((v) => ({ ...v, minInstalls: e.target.value }))} />
        </div>
      </div>
      <p>
        {d.autoHaltStatus(a.failures ?? 0, a.successes ?? 0, a.failureRatePercent)}
        {a.halted && <> <Badge tone="danger">{d.haltedAt(formatPreciseDateTime(a.haltedUtc), a.haltReason)}</Badge></>}
      </p>
      <button type="button" className="button button-primary button-sm" onClick={save}>{d.saveSchedule}</button>
    </section>
  );
}

function CriticalPanel({ group, onSave }) {
  const [critical, setCritical] = useState(group.critical);
  const [message, setMessage] = useState(group.criticalMessage || '');
  return (
    <section className="panel">
      <h2>{d.critical}</h2>
      <p className="muted">{d.criticalHelp}</p>
      <label className="toggle"><input type="checkbox" checked={critical} onChange={(e) => setCritical(e.target.checked)} /> {d.critical}</label>
      <div className="field">
        <label className="label-text" htmlFor="critical-message">{d.criticalMessage}</label>
        <input id="critical-message" type="text" maxLength={CRITICAL_MESSAGE_MAX} value={message} onChange={(e) => setMessage(e.target.value)} disabled={!critical} />
      </div>
      <button type="button" className="button button-primary button-sm" onClick={() => onSave(critical, critical ? message.trim() : '')}>{d.saveCritical}</button>
    </section>
  );
}

function AdoptionPanel({ client, id }) {
  const pagination = useCollectionPage();
  const { query: pageQuery, setResult } = pagination;
  const fetcher = useCallback(async (signal) => { const page = await client.getUpdateGroupAdoption(id, { ...pageQuery, filters: { days: 14 } }, signal); setResult(page); return page; }, [client, id, pageQuery, setResult]);
  const adoption = useApiQuery(fetcher);
  const versionsPage = useCollectionPage();
  const { query: versionQuery, setResult: setVersions } = versionsPage;
  const versionsFetcher = useCallback(async (signal) => { const page = await client.getUpdateGroupBinaryVersions(id, versionQuery, signal); setVersions(page); return page; }, [client, id, versionQuery, setVersions]);
  const versions = useApiQuery(versionsFetcher);
  if (!adoption.data) return null;
  return (
    <section className="panel">
      <h2>{d.adoption}</h2>
      {(adoption.error || versions.error) && <ErrorBanner message={errorMessage(adoption.error || versions.error)} onRetry={() => { adoption.reload(); versions.reload(); }} />}
      <TablePagination {...pagination.props} />
      <table className="table">
        <thead><tr><th>{u.platform}</th><th>{d.activeInstalls}</th><th>{d.onThisUpdate}</th><th>{d.downloaded}</th><th>{d.applied}</th><th>{d.failed}</th><th>{d.launchFailed}</th></tr></thead>
        <tbody>
          {Object.entries(adoption.data.byPlatform || {}).map(([platform, p]) => (
            <tr key={platform}><td>{platform}</td><td>{p.activeInstalls}</td><td>{p.onThisUpdate} ({p.percentOnThisUpdate}%)</td><td>{p.downloaded}</td><td>{p.applied}</td><td>{p.failed}</td><td>{p.launchFailed}</td></tr>
          ))}
        </tbody>
      </table>
      <table className="table">
        <thead><tr><th>{d.day}</th><th>{d.checks}</th><th>{d.downloaded}</th><th>{d.applied}</th><th>{d.failures}</th></tr></thead>
        <tbody>
          {(adoption.data.objects || []).filter((row) => row.checks || row.downloads || row.applied || row.failures).map((row) => (
            <tr key={row.date}><td>{row.date}</td><td>{row.checks}</td><td>{row.downloads}</td><td>{row.applied}</td><td>{row.failures}</td></tr>
          ))}
        </tbody>
      </table>
      <h3>{d.binaryVersions}</h3>
      <TablePagination {...versionsPage.props} loading={versions.loading} onRefresh={versions.reload} />
      <table className="table"><thead><tr><th>{d.binaryVersion}</th><th>{d.activeInstalls}</th></tr></thead><tbody>
        {(versions.data?.objects || []).map((row) => <tr key={row.key}><td>{row.key}</td><td>{row.count}</td></tr>)}
      </tbody></table>
    </section>
  );
}

/**
 * Update detail: summary, per-platform updates, rollout steps with confirmation, the rollout schedule editor with
 * the automatic halt status, the critical marker, adoption, the asset preview, labels and tags, and lifecycle actions.
 */
function UpdateDetailView() {
  const { id } = useParams();
  const { client } = useAdminAuth();
  const toast = useToast();
  const navigate = useNavigate();
  const fetcher = useCallback((signal) => client.getUpdateGroup(id, signal), [client, id]);
  const query = useApiQuery(fetcher);
  const healthFetcher = useCallback((signal) => client.getUpdatesHealth(signal), [client]);
  const health = useApiQuery(healthFetcher);
  const channelsFetcher = useCallback((signal) => collectEnumeration((q, sig) => client.enumerateUpdateChannels(q, sig), {}, { signal }), [client]);
  const channelList = useApiQuery(channelsFetcher);
  const [group, setGroup] = useState(null);
  const [dialog, setDialog] = useState(null);
  const [version, setVersion] = useState(0);

  useEffect(() => {
    if (query.data) {
      setGroup(query.data);
      setVersion((v) => v + 1);
    }
  }, [query.data]);

  const act = async (fn, message) => {
    try {
      const next = await fn();
      if (next && next.id === id) {
        setGroup(next);
        setVersion((v) => v + 1);
      } else query.reload();
      toast.success(message);
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };

  if (query.error) return <div className="view"><ErrorBanner message={errorMessage(query.error, strings.common.errorGeneric)} onRetry={query.reload} /></div>;
  if (!group) return <div className="view"><Loading /></div>;
  const channels = (channelList.data?.objects || []).map((channel) => channel.name);

  return (
    <div className="view">
      <ErrorBanner message={channelList.error ? errorMessage(channelList.error) : null} onRetry={channelList.reload} />
      <PageHeader
        title={group.message}
        summary={<><UpdateStatus group={group} /> <Badge>{group.channel}</Badge> <CopyableId value={group.id} label={u.columns.id} /></>}
        actions={(
          <>
            <Link className="button button-secondary button-sm" to="/admin/updates">{d.back}</Link>
            {group.status === 'Draft' && <button type="button" className="button button-primary button-sm" onClick={() => act(() => client.publishUpdateGroup(group.id, {}), u.rolloutChanged(group.rolloutPercentage || 100))}>{u.actions.publish}</button>}
            {group.status === 'Published' && <button type="button" className="button button-secondary button-sm" onClick={() => act(() => client.pauseUpdateGroup(group.id), u.paused)}>{u.actions.pause}</button>}
            {group.status === 'Paused' && <button type="button" className="button button-secondary button-sm" onClick={() => act(() => client.resumeUpdateGroup(group.id), u.resumed)}>{u.actions.resume}</button>}
            {canPromote(group) && <button type="button" className="button button-secondary button-sm" onClick={() => setDialog('promote')}>{u.actions.promote}</button>}
            {hasRollout(group) && group.kind !== 'RollBackToEmbedded' && <button type="button" className="button button-danger button-sm" onClick={() => setDialog('rollback')}>{u.actions.rollback}</button>}
            {group.status === 'Draft' && <button type="button" className="button button-danger button-sm" onClick={() => setDialog('delete')}>{u.actions.remove}</button>}
          </>
        )}
      />
      <section className="panel">
        <h2>{d.summary}</h2>
        <dl className="detail-list">
          <dt>{d.commit}</dt><dd><code className="mono">{group.gitCommit || '-'}</code>{group.gitDirty && <> <Badge tone="warning">{d.dirty}</Badge></>}</dd>
          <dt>{d.publishedBy}</dt><dd>{group.createdBy.name} ({group.createdBy.type})</dd>
          <dt>{d.published}</dt><dd>{group.publishedUtc ? formatPreciseDateTime(group.publishedUtc) : '-'}</dd>
          <dt>{u.kind}</dt><dd>{u.kinds[group.kind] || group.kind}</dd>
          {group.updateGroupUuid && <><dt>{d.groupUuid}</dt><dd><code className="mono">{group.updateGroupUuid}</code></dd></>}
          {group.promotedFrom && <><dt>{d.promotedFrom}</dt><dd><Link to={'/admin/updates/' + encodeURIComponent(group.promotedFrom)}>{group.promotedFrom}</Link></dd></>}
          {group.republishOf && <><dt>{d.republishOf}</dt><dd><Link to={'/admin/updates/' + encodeURIComponent(group.republishOf)}>{group.republishOf}</Link></dd></>}
        </dl>
        <h3>{d.updates}</h3>
        <table className="table">
          <thead><tr><th>{u.platform}</th><th>{d.uuid}</th><th>{u.runtimeVersion}</th><th>{d.createdAt}</th><th>{d.size}</th><th>{d.assets}</th><th>{d.total}</th><th>{d.sourceMaps}</th><th>{u.columns.rollout}</th></tr></thead>
          <tbody>
            {group.updates.map((x) => (
              <tr key={x.id}>
                <td>{x.platform}</td><td><code className="mono">{x.uuid}</code></td><td><code className="mono">{x.runtimeVersion.slice(0, 12)}</code></td>
                <td>{formatPreciseDateTime(x.createdAt)}</td><td>{formatBytes(x.launchAssetSizeBytes)}</td><td>{x.assetCount}</td><td>{formatBytes(x.totalBytes)}</td><td>{x.sourceMapCount ?? '-'}</td><td>{u.percent(x.rolloutPercentage)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
      <RolloutPanel group={group} onChange={(step, platform) => act(() => client.setUpdateRollout(group.id, step, platform), u.rolloutChanged(step))} />
      {group.status !== 'RolledBack' && group.status !== 'Superseded' && (
        <SchedulePanel key={'plan-' + version} group={group} template={health.data ? health.data.rolloutPlanTemplate : ''} onSave={(body) => act(() => client.setUpdateRolloutPlan(group.id, body), d.scheduleSaved)} />
      )}
      {group.kind !== 'RollBackToEmbedded' && <CriticalPanel key={'critical-' + version} group={group} onSave={(critical, message) => act(() => client.setUpdateCritical(group.id, critical, message), d.criticalSaved)} />}
      <AdoptionPanel client={client} id={group.id} />
      <SymbolicatePanel client={client} updates={group.updates} />
      {group.assets && group.assets.objects && group.assets.objects.length > 0 && (
        <section className="panel">
          <h2>{d.assetList}</h2>
          <table className="table">
            <thead><tr><th>{d.assetKey}</th><th>{d.assetType}</th><th>{d.assetSize}</th></tr></thead>
            <tbody>
              {group.assets.objects.map((a) => (
                <tr key={a.sha256Hex}><td><code className="mono">{a.key}</code>{a.isLaunchAsset && <> <Badge>{d.launch}</Badge></>}</td><td>{a.contentType}</td><td>{formatBytes(a.sizeBytes)}</td></tr>
              ))}
            </tbody>
          </table>
          {group.assets.totalRecords > group.assets.objects.length && <p className="muted">{d.moreAssets(group.assets.objects.length, group.assets.totalRecords)}</p>}
        </section>
      )}
      <MetadataPanel client={client} targetType="UpdateGroup" targetId={group.id} isStaff initial={group} />
      {dialog === 'promote' && <PromoteDialog group={group} client={client} channels={channels} onClose={() => setDialog(null)} onDone={(created) => { setDialog(null); toast.success(u.promoted); navigate('/admin/updates/' + encodeURIComponent(created.id)); }} />}
      {dialog === 'rollback' && <RollbackDialog group={group} client={client} onClose={() => setDialog(null)} onDone={(created) => { setDialog(null); toast.success(u.rolledBack); navigate('/admin/updates/' + encodeURIComponent(created.id)); }} />}
      <ConfirmModal
        open={dialog === 'delete'}
        title={u.deleteTitle}
        message={u.deleteBody}
        confirmLabel={u.actions.remove}
        onConfirm={async () => {
          setDialog(null);
          try {
            await client.deleteUpdateGroup(group.id);
            toast.success(u.deleted);
            navigate('/admin/updates');
          } catch (err) {
            toast.error(errorMessage(err, strings.common.errorGeneric));
          }
        }}
        onCancel={() => setDialog(null)}
      />
    </div>
  );
}

export default UpdateDetailView;
