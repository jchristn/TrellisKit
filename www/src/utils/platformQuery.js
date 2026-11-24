import { DomainKind, PLATFORM_AUDIT_FILTER_KEYS, PROPERTY_FILTER_KEYS, PlatformRequestScope, stripeWebhookPath } from '@trelliskit/shared';
import { pagedQuery } from './adminQuery.js';
import { writeDelegation } from './delegation.js';
import { browser } from './browser.js';

/**
 * EnumerationQuery builders for the platform console tables (canonical POST .../enumerate form).
 * `ordering` is a table filter here (the collections support only their listed orderings, so the
 * tables offer a choice instead of free column sorting).
 */

export const PROPERTY_TABLE_FILTERS = Object.freeze(['search', ...PROPERTY_FILTER_KEYS, 'ordering']);
export const PLATFORM_ADMIN_TABLE_FILTERS = Object.freeze(['search', 'ordering']);
export const PLATFORM_AUDIT_TABLE_FILTERS = Object.freeze(['search', ...PLATFORM_AUDIT_FILTER_KEYS, 'fromUtc', 'toUtc']);
export const PLATFORM_REQUEST_TABLE_FILTERS = Object.freeze(['search', 'propertyId', 'method', 'statusCode', 'pathContains', 'userId']);
export const PROPERTY_METRICS_TABLE_FILTERS = Object.freeze(['search', 'status', 'ordering']);

function withOrdering(filters, keys, paging) {
  const { ordering, ...rest } = filters || {};
  return pagedQuery(rest, keys.filter((k) => k !== 'ordering'), { ...paging, sort: ordering || '' });
}

/** POST /platform/properties/enumerate body. */
export function buildPropertiesQuery(filters, paging) {
  return withOrdering(filters, PROPERTY_TABLE_FILTERS, paging);
}

/** POST /platform/admins/enumerate body. */
export function buildPlatformAdminsQuery(filters, paging) {
  return withOrdering(filters, PLATFORM_ADMIN_TABLE_FILTERS, paging);
}

/** POST /platform/audit/enumerate body (`propertyId` empty reads the platform realm's audit). */
export function buildPlatformAuditQuery(filters, paging) {
  return pagedQuery(filters, PLATFORM_AUDIT_TABLE_FILTERS, { ...paging, sort: '' });
}

/**
 * POST /platform/request-history/enumerate body. One realm per call: `propertyId` is a property
 * id, `platform` (the default when empty), or `unresolved`.
 */
export function buildPlatformRequestHistoryQuery(filters, paging) {
  const scoped = { ...(filters || {}), propertyId: (filters && filters.propertyId) || PlatformRequestScope.Platform };
  return pagedQuery(scoped, PLATFORM_REQUEST_TABLE_FILTERS, { ...paging, sort: '' });
}

/** POST /platform/metrics/properties/enumerate body (filter `status`; orderings PropertyMetricsOrderings). */
export function buildPropertyMetricsQuery(filters, paging) {
  return withOrdering(filters, PROPERTY_METRICS_TABLE_FILTERS, paging);
}

/**
 * Label and tag autocomplete over the platform discovery routes, in the shape `metadataSuggesters`
 * reads (`enumerateLabels`, `enumerateTagKeys`, `enumerateTagValues`). The target type
 * (`Property` or `PlatformAdmin`) is passed through by the suggester.
 * @param {object|null} client platform ApiClient
 */
export function platformMetadataSource(client) {
  if (!client) return null;
  return {
    enumerateLabels: (query, signal) => client.enumeratePlatformLabels(query, signal),
    enumerateTagKeys: (query, signal) => client.enumeratePlatformTagKeys(query, signal),
    enumerateTagValues: (query, signal) => client.enumeratePlatformTagValues(query, signal)
  };
}

/** The accept-invitation link for a one-time platform invitation token, on this platform host. */
export function platformInviteLink(token, origin = window.location.origin) {
  return origin + '/platform/accept-invite?token=' + encodeURIComponent(String(token || ''));
}

/** The public Stripe webhook URL of a property record: its Api domain, else its Web domain, else this origin. */
export function recordStripeWebhookUrl(record, origin = window.location.origin) {
  const domains = (record && record.domains) || [];
  const api = domains.find((d) => d.kind === DomainKind.Api) || domains.find((d) => d.kind === DomainKind.Web);
  return (api ? 'https://' + api.host : origin) + stripeWebhookPath(record && record.id);
}

/**
 * Start a delegated session for a property and open its admin console in this tab (on this
 * platform host; the admin console sends the token with X-TK-Property).
 * @param {(fn: (client: object) => Promise<any>) => Promise<any>} call platform call runner
 * @param {{ id: string, name?: string }} record
 */
export async function startDelegation(call, record) {
  const session = await call((client) => client.createDelegatedSession(record.id));
  writeDelegation(session, record.name);
  browser.assign('/admin/home');
  return session;
}
