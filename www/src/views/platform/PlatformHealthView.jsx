import { collectEnumeration, metricSeriesFromEnumeration } from '@trelliskit/shared';
import { useCallback, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { usePlatformAuth } from '../../context/PlatformAuthContext.jsx';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import { readMeta, writeMeta } from '../../hooks/useEnumerationUrlState.js';
import { platformMetadataSource } from '../../utils/platformQuery.js';
import useApiQuery from '../../hooks/useApiQuery.js';
import { metricRange } from '../admin/metrics/metricViewUtils.js';
import { TimeSeries, RankedBars, RangePicker } from '../../components/charts/index.js';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import PageHeader from '../../components/PageHeader.jsx';
import TableColumns from '../../components/TableColumns.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import strings from '../../strings.js';
const t = strings.platformMetrics;

const KEYS = ['requests.count', 'requests.errorRate', 'requests.latency', 'users.total', 'users.active', 'storage.total', 'ai.calls', 'ai.cost', 'workers.runs', 'workers.lag'];
/** The query is bounded and shareable; property extensions can use any registered key. */
export function platformMetricBody(params, now) {
  const range = metricRange(params, now);
  if (!range) return null;
  const page = Math.max(1, Math.min(500, Number.parseInt(params.get('page'), 10) || 1));
  const size = [5, 10, 20].includes(Number(params.get('size'))) ? Number(params.get('size')) : 10;
  const status = params.get('status');
  const meta = readMeta(params);
  return { labels: meta.labels, labelMatch: meta.labelMatch, tags: meta.tags, maxResults: size, skip: (page - 1) * size, ordering: params.get('ordering') === 'ValueAscending' ? 'ValueAscending' : 'ValueDescending', search: (params.get('search') || '').slice(0, 200), filters: ['Enabled', 'Disabled'].includes(status) ? { status } : {}, includeSeries: false,
    query: { metric: (params.get('metric') || 'requests.count').slice(0, 100), from: range.from, to: range.to, bucket: range.bucket, compare: range.compare } };
}
/** Platform-authenticated comparisons share the property chart kit. */
export default function PlatformHealthView() {
  const { client, call } = usePlatformAuth();
  const [params, setParams] = useSearchParams();
  const [now, setNow] = useState(() => new Date());
  const metadataSource = useMemo(() => platformMetadataSource(client), [client]);
  const meta = readMeta(params);
  const changeMeta = (next) => setParams((current) => { const updated = writeMeta(current, { ...next, search: current.get('search') || '', ordering: current.get('ordering') || '' }); updated.delete('page'); return updated; });
  const body = useMemo(() => platformMetricBody(params, now), [params, now]);
  const fetcher = useCallback((signal) => body ? call(async () => { const page = await client.platformCompareMetrics(body, signal); const objects = await Promise.all(page.objects.map(async (row) => { if (row.unavailableReason) return row; const points = await collectEnumeration((q, sig) => client.platformQueryMetrics(row.propertyId, { ...q, query: body.query }, sig), { maxResults: 100 }, { signal, maxPages: 1000 }); return { ...row, data: metricSeriesFromEnumeration(points) }; })); return { ...page, objects }; }) : Promise.resolve(null), [call, client, body]);
  const result = useApiQuery(fetcher);
  const rows = result.data?.objects || [];
  const series = rows.flatMap((row) => (row.data?.series || []).map((s) => ({ ...s, key: row.propertyId + ':' + s.key, label: row.name })));
  const previous = rows.flatMap((row) => (row.data?.previous || []).map((s) => ({ ...s, key: row.propertyId + ':' + s.key, label: row.name })));
  function update(values) { const next = new URLSearchParams(params); next.delete('page'); for (const [key, value] of Object.entries(values)) if (value === '') next.delete(key); else next.set(key, String(value)); setParams(next); }
  const range = metricRange(params, now);
  const size = body?.maxResults || 10;
  const page = Math.floor((body?.skip || 0) / size) + 1;
  const total = result.data?.totalRecords || 0;
  return <div className="view"><PageHeader title={t.title} summary={t.summary} actions={<button className="button button-secondary" type="button" onClick={() => setNow(new Date())}>{t.refresh}</button>} />
    <RangePicker range={params.get('range') || 'week'} from={params.get('from') || range?.from} to={params.get('to') || range?.to} compare={params.get('compare') !== 'false'} onChange={(v) => update(v.range === 'custom' ? { ...v, from: range?.from || '', to: range?.to || '' } : v)} />
    <div className="metric-controls"><label>{t.metric}<input list="platform-metric-keys" maxLength={100} value={params.get('metric') || 'requests.count'} onChange={(e) => update({ metric: e.target.value })} /><datalist id="platform-metric-keys">{KEYS.map((key) => <option key={key} value={key} />)}</datalist></label>
      <label>{t.search}<input maxLength={200} value={params.get('search') || ''} onChange={(e) => update({ search: e.target.value })} /></label>
      <label>{t.status}<select value={params.get('status') || ''} onChange={(e) => update({ status: e.target.value })}><option value="">{t.all}</option><option value="Enabled">{t.enabled}</option><option value="Disabled">{t.disabled}</option></select></label>
      <label>{t.ordering}<select value={params.get('ordering') || 'ValueDescending'} onChange={(e) => update({ ordering: e.target.value })}><option value="ValueDescending">{t.descending}</option><option value="ValueAscending">{t.ascending}</option></select></label></div>
    <MetadataFilterBar value={{ ...meta, search: '', ordering: '' }} onChange={changeMeta} client={metadataSource} targetType="Property" />
    <p className="muted small">{t.note}</p>
    {!body && <p role="alert">{t.invalid}</p>}{result.loading && !result.data && <Loading />}{result.error && <ErrorBanner message={t.error + ' ' + result.error.message} onRetry={result.reload} />}
    {result.data && <><TablePagination pageNumber={page} pageSize={size} totalCount={total} totalPages={Math.ceil(total / size)} onPageChange={(n) => update({ page: n })} onPageSizeChange={(n) => update({ size: n })} onRefresh={result.reload} pageSizes={[5, 10, 20]} />
      <TableColumns id="platform.metricsComparison"><div className="table-scroll"><table className="data-table"><caption className="visually-hidden">{t.comparisons}</caption><thead><tr><th>{t.property}</th><th>{t.value}</th><th>{t.aggregation}</th><th>{t.completeness}</th></tr></thead><tbody>{rows.map((row) => <tr key={row.propertyId}><td><Link to={'/platform/properties/' + encodeURIComponent(row.propertyId)}>{row.name}</Link></td><td>{row.value === null ? t.missing : row.value.toLocaleString()} {row.unit}</td><td>{row.aggregation}</td><td>{row.unavailableReason || (row.complete ? t.complete : t.partial)}</td></tr>)}</tbody></table></div></TableColumns>
      <RankedBars title={t.comparisons} rows={rows.map((row) => ({ key: row.propertyId, label: row.name, value: row.value }))} unit={rows[0]?.unit} />
      <TimeSeries title={t.series} series={series} previous={previous} timeZone="UTC" unit={rows[0]?.unit} />
    </>}
  </div>;
}
