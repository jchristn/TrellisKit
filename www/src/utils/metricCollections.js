import { collectEnumeration, legacyMetricSeriesFromEnumeration } from '@trelliskit/shared';
export async function loadDailyMetrics(fetchPage, filters, signal) {
  const page = await collectEnumeration(fetchPage, { filters, maxResults: 100 }, { signal, maxPages: 1000 });
  return legacyMetricSeriesFromEnumeration(page);
}
