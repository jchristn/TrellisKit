import { observed } from '../../../components/charts/chartData.js';
import { METRIC_RANGES } from '../../../components/charts/RangePicker.jsx';

/** Parse only bounded time ranges and declared URL controls. */
export function metricRange(params, now = new Date()) {
  const range = params.get('range') || 'week';
  const to = range === 'custom' ? new Date(params.get('to') || '') : now;
  const from = range === 'custom' ? new Date(params.get('from') || '') : new Date(to.getTime() - (METRIC_RANGES[range] || METRIC_RANGES.week));
  const duration = to - from;
  if (!Number.isFinite(duration) || duration <= 0 || duration > 366 * 86400000) return null;
  return { range, from: from.toISOString(), to: to.toISOString(), compare: params.get('compare') !== 'false', bucket: duration <= 7 * 86400000 ? 'Hour' : duration <= 90 * 86400000 ? 'Day' : 'Week' };
}
export function metricFilters(params, allowed) {
  const out = {};
  for (const [key, value] of params) if (key.startsWith('filter.') && allowed.includes(key.slice(7)) && value.length <= 100) out[key.slice(7)] = value;
  return out;
}
/** Only link to source views that can faithfully apply this bucket and grouping. */
export function metricDrilldown(definition, point, item, filters = {}) {
  if (!definition?.drilldown || observed(point) === null || !point.startUtc || !point.endUtc || !Number.isFinite(Date.parse(point.startUtc)) || !Number.isFinite(Date.parse(point.endUtc)) || Date.parse(point.endUtc) <= Date.parse(point.startUtc)) return null;
  let path = definition.drilldown; let prefix = ''; let dimensions = {}; const query = new URLSearchParams();
  if (path === '/admin/requests' && String(definition.key || '').startsWith('requests.')) {
    dimensions = { route: 'routeTemplate', statusClass: 'statusClass', errorCode: 'errorCode', app: 'clientApp', platform: 'clientPlatform', version: 'clientVersion' };
    if (definition.key === 'requests.errorRate') query.set('statusClass', '5xx');
    if (definition.key === 'requests.rateLimited') query.set('statusCode', '429');
  } else if (definition.key === 'forums.posts' && ['/admin/forums', '/admin/threads'].includes(path)) {
    path = '/admin/threads'; dimensions = { forum: 'forumId' };
  } else if (definition.key === 'moderation.actions' && path === '/admin/moderation') {
    prefix = 'a.'; query.set('tab', 'actions'); dimensions = { action: 'action' };
  } else return null;
  const selected = { ...filters };
  if (item?.dimension) {
    if (!dimensions[item.dimension] || /^(other|__other__)$/i.test(String(item.key))) return null;
    selected[item.dimension] = item.key;
  }
  for (const [dimension, value] of Object.entries(selected)) {
    const field = dimensions[dimension];
    if (!field || value == null || String(value).length > (field === 'routeTemplate' ? 300 : 100)) return null;
    if (field === 'statusClass' && !/^[1-5]xx$/.test(String(value))) return null;
    query.set(prefix + field, String(value));
  }
  query.set(prefix + 'fromUtc', point.startUtc);
  query.set(prefix + 'toUtc', point.endUtc);
  return path + '?' + query;
}
