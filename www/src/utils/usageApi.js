import { collectEnumeration, usageSeriesFromEnumeration } from '@trelliskit/shared';
import { browser } from './browser.js';
import { filenameFromDisposition } from './usage.js';

/**
 * Usage data sources for the shared UsageDashboard: a `load(query, signal)` that resolves a
 * UsageSummary and an `exportFile(query, format)` that downloads the export file. The property
 * admin console reads `/admin/usage/*`; the platform console reads
 * `/platform/properties/{id}/usage/*` (docs/USAGE_METERING_CONTRACT.md section 7).
 */

const MIME = { csv: 'text/csv', json: 'application/json' };

/** Offer an export response (`{ text, headers }`) as a file download; resolves the file name. */
export function downloadExport(result, query, format) {
  const ext = format === 'csv' ? 'csv' : 'json';
  const fallback = 'usage-' + (query.period || 'export') + (query.from ? '-' + query.from : '') + (query.to ? '-' + query.to : '') + '.' + ext;
  const headers = result && result.headers;
  const name = filenameFromDisposition(headers && headers.get ? headers.get('content-disposition') : '', fallback);
  browser.download(result ? result.text : '', name, MIME[ext]);
  return name;
}

/** Usage source for the property admin console (the admin API from useAdminAuth). */
export function adminUsageSource(api) {
  return {
    load: (query, signal) => api.getUsageSummary(query, signal),
    exportFile: async (query, format) => downloadExport(await api.exportUsage(query, format), query, format)
  };
}

/**
 * Usage source for one property in the platform console.
 * @param {(fn: (client: object) => Promise<any>) => Promise<any>} call platform call runner
 * @param {string} propertyId
 */
export function platformUsageSource(call, propertyId) {
  const base = '/platform/properties/' + encodeURIComponent(propertyId) + '/usage';
  return {
    load: (filters, signal) => call(async (client) => usageSeriesFromEnumeration(await collectEnumeration((q, sig) => client.platformUsageSummary(propertyId, q, sig), { filters, maxResults: 100 }, { signal, maxPages: 1000 }))),
    exportFile: async (query, format) => {
      const result = await call((client) => client.get(base + '/export', { query: { ...query, format }, responseType: 'textWithHeaders' }));
      return downloadExport(result, query, format);
    }
  };
}

/** POST /platform/usage/properties/enumerate: EnumerationResult<PropertyUsageRow>. */
export function enumerateUsageProperties(call, body, signal) {
  return call((client) => client.post('/platform/usage/properties/enumerate', body, { signal }));
}
