import { matchesMeta, page } from './fakeKit.js';

/** Emit actual daily observations in the same paged shape as aggregate metric routes. */
export function dailyMetricPage(summary, query = {}) {
  const context = {};
  let rows = [];
  for (const [series, value] of Object.entries(summary)) {
    if (Array.isArray(value)) {
      rows.push(...value.map((point) => ({ id: `${series}:${point.date || point.day}`, series, date: point.date || point.day, value: point.count, labels: [], tags: {} })));
    } else context[series] = value;
  }
  const filters = query.filters || {};
  rows = rows.filter((row) => (!filters.series || row.series === filters.series)
    && (!query.createdAfterUtc || row.date + 'T00:00:00Z' >= query.createdAfterUtc)
    && (!query.createdBeforeUtc || row.date + 'T00:00:00Z' < query.createdBeforeUtc)
    && (!query.search || row.series.toLowerCase().includes(query.search.toLowerCase()))
    && matchesMeta(row, query));
  const direction = query.ordering === 'CreatedAscending' ? 1 : -1;
  rows.sort((a, b) => direction * (a.date.localeCompare(b.date) || a.id.localeCompare(b.id)));
  return { ...context, ...page(rows, query, { max: 500 }) };
}
