/** Property-neutral metrics collection APIs. Every method returns one server enumeration page. */
import { toEnumerationBody } from './enumeration.js';
const enc = encodeURIComponent;
const body = (request) => toEnumerationBody(request || {}, { maxLimit: 500 });
const metricBody = (request = {}) => ({ ...body(request), query: request.query || {} });
export const metricsMethods = {
  adminRequestHistorySummary(query = {}, signal) { return this.post('/request-history/summary/enumerate', body(query), { signal }); },
  adminEngagementMetrics(query = {}, signal) { return this.post('/admin/metrics/engagement/enumerate', body(query), { signal }); },
  adminUsageSummary(query = {}, signal) { return this.post('/admin/usage/summary/enumerate', body(query), { signal }); },
  platformUsageSummary(propertyId, query = {}, signal) { return this.post('/platform/properties/' + enc(propertyId) + '/usage/summary/enumerate', body(query), { signal }); },
  adminMetricRegistry(query = {}, signal) { return this.post('/admin/metrics/enumerate', body(query), { signal }); },
  adminMetricPages(query = {}, signal) { return this.post('/admin/metrics/pages/enumerate', body(query), { signal }); },
  adminQueryMetrics(request = {}, signal) { return this.post('/admin/metrics/query', metricBody(request), { signal }); },
  adminMetricWorkers(query = {}, signal) { return this.post('/admin/metrics/workers/enumerate', body(query), { signal }); },
  adminMetricAnnotations(query = {}, signal) { return this.post('/admin/metrics/annotations/enumerate', body(query), { signal }); },
  platformCompareMetrics(request = {}, signal) {
    const page = toEnumerationBody({ ...request, maxResults: request.maxResults ?? 20, ordering: request.ordering || 'ValueDescending' }, { maxLimit: 20 });
    page.maxResults = Math.min(20, page.maxResults);
    return this.post('/platform/metrics/comparisons/enumerate', { ...page, query: request.query || {}, includeSeries: request.includeSeries === true }, { signal });
  },
  platformQueryMetrics(propertyId, request = {}, signal) { return this.post('/platform/properties/' + enc(propertyId) + '/metrics/query', metricBody(request), { signal }); }
};

/** Reconstruct chart series only after the caller has retrieved the window's observation pages. */
export function metricSeriesFromEnumeration(result, annotations = []) {
  const series = new Map();
  const previous = new Map();
  for (const row of result.objects || []) {
    const group = row.period === 'Previous' ? previous : series;
    if (!group.has(row.seriesKey)) group.set(row.seriesKey, { key: row.seriesKey, points: [] });
    group.get(row.seriesKey).points.push({ startUtc: row.startUtc, endUtc: row.endUtc, value: row.value, complete: row.complete, suppressed: row.suppressed, samples: row.samples });
  }
  return { metric: result.metric, unit: result.unit, timeZone: result.timeZone, completeThroughUtc: result.completeThroughUtc, series: [...series.values()], previous: [...previous.values()], annotations };
}

/** Adapt complete daily-observation pages for the existing chart components. */
export function legacyMetricSeriesFromEnumeration(result) {
  const view = { ...result };
  for (const row of result.objects || []) {
    if (!Object.hasOwn(view, row.series)) view[row.series] = [];
    view[row.series].push({ date: row.date, count: row.value });
  }
  for (const name of new Set((result.objects || []).map((row) => row.series))) view[name].sort((a, b) => a.date.localeCompare(b.date));
  return view;
}

/** Rebuild aligned meter/bucket arrays from complete usage observation pages. */
export function usageSeriesFromEnumeration(result) {
  const buckets = new Map();
  const meters = new Map();
  for (const row of result.objects || []) {
    buckets.set(row.startUtc, { startUtc: row.startUtc, endUtc: row.endUtc, label: row.label });
    if (!meters.has(row.meter)) meters.set(row.meter, { meter: row.meter, group: row.group, kind: row.kind, unit: row.unit, total: row.total, previousTotal: row.previousTotal, change: row.change, exact: row.exact, observations: new Map() });
    meters.get(row.meter).observations.set(row.startUtc, row.value);
  }
  const ordered = [...buckets.values()].sort((a, b) => a.startUtc.localeCompare(b.startUtc));
  return { ...result, buckets: ordered, meters: [...meters.values()].map(({ observations, ...meter }) => ({ ...meter, values: ordered.map((bucket) => observations.has(bucket.startUtc) ? observations.get(bucket.startUtc) : null) })) };
}
