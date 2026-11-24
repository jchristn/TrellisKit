import { collectEnumeration } from '@trelliskit/shared';
import { useCallback, useEffect, useState } from 'react';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useToast } from '../../../context/ToastContext.jsx';
import { errorMessage } from '../../../utils/api.js';
import PageHeader from '../../../components/PageHeader.jsx';
import DataTable from '../../../components/DataTable.jsx';
import { Badge } from '../../../components/Badges.jsx';
import { ErrorBanner } from '../../../components/States.jsx';
import { percent, usePagedList, usd } from './aiAdminUtils.js';

const s = strings.ai.admin.analytics;

function Tile({ label, value }) {
  return <div className="ai-tile"><span className="muted small">{label}</span><strong>{value}</strong></div>;
}

function Bars({ points, label }) {
  const max = Math.max(1, ...points.map((p) => p.value));
  return (
    <div className="ai-bars" role="img" aria-label={label}>
      {points.map((p) => <span key={p.timestampUtc} title={new Date(p.timestampUtc).toLocaleDateString() + ': ' + p.value} style={{ height: Math.max(1, Math.round((p.value / max) * 100)) + '%' }} />)}
    </div>
  );
}

function memberName(row) {
  if (row.subjectType === 'Visitor') return s.visitor + ' ' + row.subjectId;
  return (row.member && row.member.displayName) || row.subjectId;
}

/** Admin, AI, Analytics: tiles, a daily chart, cost by endpoint, assistant, and member, and the feedback queue (AI contract section 14). */
function AiAnalyticsView() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [days, setDays] = useState(30);
  const [metric, setMetric] = useState('requests');
  const [overview, setOverview] = useState(null);
  const [series, setSeries] = useState([]);
  const [error, setError] = useState(null);
  const [unreviewed, setUnreviewed] = useState(true);
  useEffect(() => {
    const controller = new AbortController();
    setError(null);
    client.aiOverview({ days }, controller.signal).then(setOverview, (err) => { if (err.name !== 'AbortError') setError(err); });
    return () => controller.abort();
  }, [client, days]);
  useEffect(() => {
    const controller = new AbortController();
    collectEnumeration((q, signal) => client.aiTimeseries(q, signal), { filters: { days, metric, granularity: days <= 1 ? 'Hour' : 'Day' } }, { signal: controller.signal }).then((view) => setSeries(view.objects), () => setSeries([]));
    return () => controller.abort();
  }, [client, days, metric]);
  const endpoints = usePagedList(useCallback((query, signal) => client.enumerateAiEndpointAnalytics({ ...query, filters: { windowDays: String(days) } }, signal), [client, days]));
  const assistants = usePagedList(useCallback((query, signal) => client.enumerateAiAssistantAnalytics({ ...query, filters: { windowDays: String(days) } }, signal), [client, days]));
  const members = usePagedList(useCallback((query, signal) => client.enumerateAiMemberAnalytics({ ...query, filters: { windowDays: String(days) } }, signal), [client, days]));
  const feedback = usePagedList(useCallback((query, signal) => client.enumerateAiFeedback({ ...query, filters: unreviewed ? { reviewed: 'false' } : {} }, signal), [client, unreviewed]));
  const review = async (row) => {
    try {
      await client.reviewAiFeedback(row.id);
      toast.success(s.reviewed);
      feedback.reload();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };
  const o = overview;
  const tokens = (row) => (row.promptTokens + row.completionTokens).toLocaleString();
  return (
    <div className="admin-page">
      <PageHeader title={s.title} summary={s.summary} actions={(
        <select aria-label={s.window} value={days} onChange={(e) => setDays(Number(e.target.value))}>
          {Object.entries(s.windows).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
        </select>
      )} />
      <ErrorBanner message={error ? errorMessage(error, strings.common.errorGeneric) : ''} />
      {o && (
        <div className="ai-tiles">
          <Tile label={s.tiles.requests} value={o.requests.toLocaleString()} />
          <Tile label={s.tiles.errors} value={percent(o.errorRate)} />
          <Tile label={s.tiles.cost} value={usd(o.costUsd)} />
          <Tile label={s.tiles.messages} value={o.messages.toLocaleString()} />
          <Tile label={s.tiles.members} value={o.activeMembers.toLocaleString()} />
          <Tile label={s.tiles.p95} value={o.latencyP95Ms == null ? '-' : o.latencyP95Ms + ' ms'} />
          <Tile label={s.tiles.ttft} value={o.ttftAverageMs == null ? '-' : o.ttftAverageMs + ' ms'} />
          <Tile label={s.tiles.thumbs} value={percent(o.thumbsUpRate)} />
          <Tile label={s.tiles.fallback} value={percent(o.fallbackRate)} />
          <Tile label={s.tiles.budget} value={usd(o.monthToDateCostUsd) + (o.monthlyBudgetUsd != null ? ' / ' + usd(o.monthlyBudgetUsd) : '')} />
        </div>
      )}
      <section className="panel">
        <label className="field">
          <span className="label-text">{s.metric}</span>
          <select value={metric} onChange={(e) => setMetric(e.target.value)}>{Object.entries(s.metrics).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select>
        </label>
        {series.length === 0 ? <p className="muted">{s.noData}</p> : <Bars points={series} label={s.metrics[metric]} />}
      </section>
      <h2>{s.endpoints}</h2>
      <DataTable caption={s.endpoints} rows={endpoints.items} rowKey={(row) => row.endpoint.id} loading={endpoints.loading} emptyMessage={s.noData} columns={[
        { key: 'name', label: s.columns.name, render: (row) => <>{row.endpoint.name} <code className="mono">{row.endpoint.model}</code>{row.endpoint.deleted && <> <Badge>{s.deleted}</Badge></>}</> },
        { key: 'requests', label: s.columns.requests, className: 'num', render: (row) => row.requests.toLocaleString() },
        { key: 'errorRate', label: s.columns.errorRate, className: 'num', render: (row) => percent(row.errorRate) },
        { key: 'p95', label: s.columns.p95, className: 'num', render: (row) => (row.latencyP95Ms == null ? '-' : row.latencyP95Ms + ' ms') },
        { key: 'tokens', label: s.columns.tokens, className: 'num', render: tokens },
        { key: 'cost', label: s.columns.cost, className: 'num', render: (row) => usd(row.costUsd) }
      ]} />
      <h2>{s.assistants}</h2>
      <DataTable caption={s.assistants} rows={assistants.items} rowKey={(row) => row.assistant.id} loading={assistants.loading} emptyMessage={s.noData} columns={[
        { key: 'name', label: s.columns.name, render: (row) => row.assistant.name },
        { key: 'messages', label: s.columns.messages, className: 'num', render: (row) => row.messages.toLocaleString() },
        { key: 'requests', label: s.columns.requests, className: 'num', render: (row) => row.requests.toLocaleString() },
        { key: 'errorRate', label: s.columns.errorRate, className: 'num', render: (row) => percent(row.errorRate) },
        { key: 'tokens', label: s.columns.tokens, className: 'num', render: tokens },
        { key: 'cost', label: s.columns.cost, className: 'num', render: (row) => usd(row.costUsd) + (row.monthlyBudgetUsd != null ? ' (' + usd(row.monthToDateCostUsd) + ' / ' + usd(row.monthlyBudgetUsd) + ')' : '') }
      ]} />
      <h2>{s.members}</h2>
      <DataTable caption={s.members} rows={members.items} rowKey={(row) => row.subjectType + row.subjectId} loading={members.loading} emptyMessage={s.noData} columns={[
        { key: 'member', label: s.columns.member, render: memberName },
        { key: 'messages', label: s.columns.messages, className: 'num', render: (row) => row.messages.toLocaleString() },
        { key: 'tokens', label: s.columns.tokens, className: 'num', render: tokens },
        { key: 'cost', label: s.columns.cost, className: 'num', render: (row) => usd(row.costUsd) }
      ]} />
      {members.token && <button type="button" className="button button-secondary button-sm" onClick={members.more}>{strings.ai.loadMore}</button>}
      <h2>{s.feedback}</h2>
      <label className="checkbox-inline"><input type="checkbox" checked={unreviewed} onChange={(e) => setUnreviewed(e.target.checked)} /> {s.unreviewedOnly}</label>
      <DataTable caption={s.feedback} rows={feedback.items} rowKey={(row) => row.id} loading={feedback.loading} emptyMessage={s.noData} columns={[
        { key: 'rating', label: s.columns.rating, render: (row) => <Badge tone={row.rating === 'Up' ? 'success' : 'danger'}>{row.rating === 'Up' ? strings.ai.feedbackUp : strings.ai.feedbackDown}</Badge> },
        { key: 'question', label: s.columns.question, render: (row) => row.promptContent },
        { key: 'reply', label: s.columns.reply, render: (row) => row.replyContent },
        { key: 'note', label: s.columns.note, render: (row) => [row.reasons && row.reasons.join(', '), row.note].filter(Boolean).join(': ') || '-' },
        { key: 'member', label: s.columns.member, render: (row) => (row.member && row.member.displayName) || s.visitor },
        { key: 'reviewed', label: s.columns.reviewed, render: (row) => (row.reviewed ? s.reviewed : <button type="button" className="button button-secondary button-sm" onClick={() => review(row)}>{s.review}</button>) }
      ]} />
      {feedback.token && <button type="button" className="button button-secondary button-sm" onClick={feedback.more}>{strings.ai.loadMore}</button>}
    </div>
  );
}

export default AiAnalyticsView;
