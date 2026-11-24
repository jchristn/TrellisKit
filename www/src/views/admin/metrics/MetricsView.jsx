import useCollectionPage from '../../../hooks/useCollectionPage.js';
import TablePagination from '../../../components/TablePagination.jsx';
import { useCallback, useMemo, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useProperty } from '../../../context/PropertyContext.jsx';
import useApiQuery from '../../../hooks/useApiQuery.js';
import { Loading, ErrorBanner } from '../../../components/States.jsx';
import PageHeader from '../../../components/PageHeader.jsx';
import { TimeSeries, RankedBars, Funnel, CohortGrid, HeatMap, Histogram, UptimeStrip, Swimlanes, StatTile, RangePicker } from '../../../components/charts/index.js';
import { metricDrilldown, metricFilters, metricRange } from './metricViewUtils.js';
import strings from '../../../strings.js';
import { timeLabel, formatValue } from '../../../components/charts/chartData.js';

const FORMS = { rankedbars: RankedBars, funnel: Funnel, cohortgrid: CohortGrid, heatmap: HeatMap, histogram: Histogram, uptimestrip: UptimeStrip, swimlanes: Swimlanes };

/** A registry declaration chooses both the query and the visualization. */
export function MetricChart({ chart, definition, range, params, api }) {
  const navigate = useNavigate();
  const groupBy = definition?.groupings?.includes(params.get('groupBy')) ? params.get('groupBy') : chart.groupBy;
  const filters = useMemo(() => metricFilters(params, definition?.groupings || []), [params, definition]);
  const bucket = definition?.buckets?.includes(range.bucket) ? range.bucket : definition?.buckets?.[0] || range.bucket;
  const fetcher = useCallback((signal) => api.queryMetric( { metric: chart.metric, from: range.from, to: range.to, bucket, compare: range.compare, groupBy, filters, percentile: chart.percentile || .95 }, signal), [api, chart.metric, chart.percentile, range.from, range.to, range.compare, bucket, groupBy, filters]);
  const query = useApiQuery(fetcher);
  const form = String(chart.form || 'Line').replace(/[ -]/g, '').toLowerCase();
  const Component = FORMS[form] || TimeSeries;
  const data = query.data;
  const hrefFor = (point, item) => metricDrilldown({ ...definition, key: chart.metric }, point, { ...item, dimension: groupBy }, filters);
  const click = definition?.drilldown ? (point, item) => { const href = hrefFor(point, item); if (href) navigate(href); } : undefined;
  return <div>{query.loading && !data && <Loading inline />}{query.error && <ErrorBanner message={strings.chartKit.loadError + ' ' + query.error.message} onRetry={query.reload} />}{data && <Component title={chart.title || definition?.title || chart.metric} series={data.series} previous={range.compare ? data.previous : []} type={form === 'stackedarea' ? 'stackedArea' : form === 'stackedbars' ? 'stackedBars' : form} unit={data.unit} timeZone={data.timeZone} completeThroughUtc={data.completeThroughUtc} annotations={(data.annotations || []).filter((annotation) => !params.get('annotationKind') || annotation.kind === params.get('annotationKind'))} threshold={chart.threshold} onPointClick={click} canPointClick={(point, item) => Boolean(hrefFor(point, item))} />}</div>;
}

export function WorkerTimeline({ api, range }) {
  const pagination = useCollectionPage();
  const { query: pageQuery, setResult } = pagination;
  const from = range.from;
  const fetcher = useCallback(async (signal) => { const page = await api.metricWorkers({ ...pageQuery, createdAfterUtc: from, createdBeforeUtc: range.to }, signal); setResult(page); return page; }, [api, from, range.to, pageQuery, setResult]);
  const query = useApiQuery(fetcher);
  const workers = new Map();
  for (const run of query.data?.objects || []) { if (!workers.has(run.worker)) workers.set(run.worker, []); workers.get(run.worker).push(run); }
  const series = [...workers].map(([key, runs]) => ({ key, points: runs.map((run) => ({ startUtc: run.startedUtc, endUtc: run.endedUtc || run.startedUtc, value: run.itemsProcessed ?? 0, outcome: run.outcome, intervalSeconds: run.intervalSeconds, complete: Boolean(run.endedUtc), run })) }));
  return <><TablePagination {...pagination.props} loading={query.loading} onRefresh={query.reload} />{query.error && <ErrorBanner message={strings.chartKit.loadError} onRetry={query.reload} />}<Swimlanes title={strings.chartKit.workers} series={series} unit="items" summary={strings.chartKit.recentPasses} tableContent={<table className="data-table"><caption>{strings.chartKit.workers}</caption><thead><tr>{['worker', 'started', 'ended', 'outcome', 'duration', 'interval', 'items', 'error'].map((key) => <th key={key}>{strings.chartKit[key]}</th>)}</tr></thead><tbody>{(query.data?.objects || []).map((run) => <tr key={run.id}><th scope="row">{run.worker}</th><td>{timeLabel(run.startedUtc)}</td><td>{timeLabel(run.endedUtc)}</td><td>{run.outcome}</td><td>{formatValue(Math.max(0, Date.parse(run.endedUtc) - Date.parse(run.startedUtc)) / 1000)} {strings.chartKit.seconds}</td><td>{formatValue(run.intervalSeconds)}</td><td>{run.itemsProcessed}</td><td>{run.error || ''}</td></tr>)}</tbody></table>} /><div className="metric-worker-lag">{[...workers].map(([worker, runs]) => {
    const latest = runs.filter((r) => r.outcome === 'Succeeded' || r.outcome === 'Success' || r.outcome === 'Ok').sort((a, b) => Date.parse(b.endedUtc) - Date.parse(a.endedUtc))[0];
    return <StatTile key={worker} title={worker + ' · ' + strings.chartKit.lag} value={latest ? Math.max(0, (Date.parse(range.to) - Date.parse(latest.endedUtc)) / 1000) : null} unit={strings.chartKit.seconds} />;
  })}</div></>;
}

/** All engine and property pages share this bounded, declarative dashboard. */
export default function MetricsView({ pageId: explicitPage }) {
  const { api } = useAdminAuth();
  const { property } = useProperty();
  const route = useParams();
  const pageId = explicitPage || route.pageId || route.metricPage || 'health';
  const [params, setParams] = useSearchParams();
  const [now, setNow] = useState(() => new Date());
  const [dimension, setDimension] = useState('');
  const [filterValue, setFilterValue] = useState('');
  const range = useMemo(() => metricRange(params, now), [params, now]);
  const pagesFetch = useCallback((signal) => api.metricPages(signal), [api]);
  const definitionsFetch = useCallback((signal) => api.metricRegistry(signal), [api]);
  const pages = useApiQuery(pagesFetch); const definitions = useApiQuery(definitionsFetch);
  const page = pages.data?.objects?.find((p) => p.id === pageId);
  const byKey = new Map((definitions.data?.objects || []).map((d) => [d.key, d]));
  const charts = (page?.charts || []).filter((chart) => { const feature = chart.feature || byKey.get(chart.metric)?.feature; return !feature || property?.features?.[feature] !== false; });
  const dimensions = [...new Set(charts.flatMap((chart) => byKey.get(chart.metric)?.groupings || []))];
  function update(values) { const next = new URLSearchParams(params); for (const [key, value] of Object.entries(values)) if (value === '' || value === null) next.delete(key); else next.set(key, String(value)); setParams(next); }
  return <div className="view"><PageHeader title={page?.title || strings.chartKit.title} summary={page?.question || strings.chartKit.summary} actions={<button type="button" className="button button-secondary" onClick={() => setNow(new Date())}>{strings.chartKit.refresh}</button>} />
    <nav aria-label={strings.chartKit.title} className="metric-controls">{(pages.data?.objects || []).map((p) => <Link aria-current={p.id === pageId ? 'page' : undefined} key={p.id} to={'/admin/metrics/' + encodeURIComponent(p.id) + '?' + params}>{p.title}</Link>)}</nav>
    {(pages.error || definitions.error) && <ErrorBanner message={strings.chartKit.loadError} onRetry={() => { pages.reload(); definitions.reload(); }} />}
    {pages.loading && !pages.data && <Loading />}
    {pages.data && !page && <p role="status">{strings.chartKit.unknownPage}</p>}
    <RangePicker range={params.get('range') || 'week'} from={params.get('from') || range?.from} to={params.get('to') || range?.to} compare={params.get('compare') !== 'false'} onChange={(v) => update(v.range === 'custom' ? { ...v, from: range?.from || '', to: range?.to || '' } : v)} />
    <div className="metric-controls"><label>{strings.chartKit.annotationKind}<input list="metric-annotation-kinds" maxLength={64} placeholder={strings.chartKit.allEvents} value={params.get('annotationKind') || ''} onChange={(e) => update({ annotationKind: e.target.value })} /><datalist id="metric-annotation-kinds">{['Deploy', 'LiveUpdate', 'KillSwitch', 'Broadcast'].map((kind) => <option key={kind} value={kind} />)}</datalist></label><label>{strings.chartKit.group}<select value={params.get('groupBy') || ''} onChange={(e) => update({ groupBy: e.target.value })}><option value="">{strings.chartKit.all}</option>{dimensions.map((d) => <option key={d}>{d}</option>)}</select></label>
      <label>{strings.chartKit.filter}<select value={dimension} onChange={(e) => setDimension(e.target.value)}><option value="">{strings.chartKit.all}</option>{dimensions.map((d) => <option key={d}>{d}</option>)}</select></label><label>{strings.chartKit.filterValue}<input value={filterValue} maxLength={100} onChange={(e) => setFilterValue(e.target.value)} /></label><button type="button" className="button button-secondary" disabled={!dimension} onClick={() => update({ ['filter.' + dimension]: filterValue })}>{strings.chartKit.apply}</button><button type="button" className="button button-secondary" onClick={() => { const next = new URLSearchParams(params); [...next.keys()].filter((k) => k.startsWith('filter.')).forEach((k) => next.delete(k)); setParams(next); }}>{strings.chartKit.clear}</button></div>
    {!range && <p role="alert">{strings.chartKit.invalidRange}</p>}
    {range && pageId === 'workers' && <WorkerTimeline api={api} range={range} />}
    {range && <div className="metric-dashboard-grid">{charts.filter((chart) => byKey.has(chart.metric) && !(pageId === 'workers' && chart.metric === 'workers.runs' && String(chart.form).toLowerCase() === 'swimlanes')).map((chart) => <MetricChart key={chart.id} chart={chart} definition={byKey.get(chart.metric)} api={api} range={range} params={params} />)}</div>}
  </div>;
}

/** Home gets one health and one growth chart; detail stays in the declared pages. */
export function MetricsHomeSummary() {
  const { api } = useAdminAuth();
  const [now] = useState(() => new Date());
  const params = useMemo(() => new URLSearchParams('range=week'), []);
  const range = useMemo(() => metricRange(params, now), [params, now]);
  if (!api?.queryMetric) return null;
  return <section><nav className="metric-controls"><Link to="/admin/metrics/health">{strings.chartKit.health}</Link><Link to="/admin/metrics/growth">{strings.chartKit.growth}</Link></nav><div className="metric-dashboard-grid"><MetricChart chart={{ metric: 'requests.errorRate', title: strings.chartKit.health, form: 'Line', threshold: 2 }} definition={{ drilldown: '/admin/requests' }} api={api} range={range} params={params} /><MetricChart chart={{ metric: 'users.new', title: strings.chartKit.growth, form: 'Bars' }} definition={{ buckets: ['Day', 'Week'], drilldown: '/admin/members' }} api={api} range={range} params={params} /></div></section>;
}
