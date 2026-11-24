import { normalizeEnumerationResult, toEnumerationParams } from '@trelliskit/shared';
import strings from '../strings.js';
import { formatShortDateTime } from './format.js';

/** Filter keys accepted by the request history endpoints. */
export const REQUEST_FILTER_KEYS = Object.freeze(['method', 'statusCode', 'pathContains', 'routeTemplate', 'statusClass', 'errorCode', 'clientApp', 'clientPlatform', 'clientVersion', 'fromUtc', 'toUtc']);

export const HTTP_METHODS = Object.freeze(['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'HEAD', 'OPTIONS']);

export const PAGE_SIZES = Object.freeze([10, 25, 50, 100, 250, 500, 1000]);
export const DEFAULT_PAGE_SIZE = 25;

/** An empty filter object. */
export function emptyFilters() {
  return Object.fromEntries(REQUEST_FILTER_KEYS.map((key) => [key, '']));
}

/**
 * Keep only non-empty filter values, trimmed.
 * @param {Record<string, string>} filters
 * @returns {Record<string, string>}
 */
export function cleanFilters(filters) {
  const result = {};
  for (const key of REQUEST_FILTER_KEYS) {
    const raw = filters && filters[key];
    const value = typeof raw === 'string' ? raw.trim() : raw;
    if (value !== '' && value !== null && value !== undefined) result[key] = String(value);
  }
  return result;
}

/**
 * Build the query object for GET /request-history.
 * @param {Record<string, string>} filters
 * @param {{ pageNumber: number, pageSize: number }} paging
 */
export function buildRequestHistoryQuery(filters, { pageNumber = 1, pageSize = DEFAULT_PAGE_SIZE, meta = null } = {}) {
  const { fromUtc, toUtc, ...rest } = cleanFilters(filters);
  const size = Math.min(500, pageSize);
  return toEnumerationParams({
    maxResults: size,
    skip: (Math.max(1, pageNumber) - 1) * size,
    createdAfterUtc: fromUtc,
    createdBeforeUtc: toUtc,
    labels: meta ? meta.labels : [],
    labelMatch: meta ? meta.labelMatch : undefined,
    tags: meta ? meta.tags : {},
    filters: rest
  }, { maxLimit: 500 });
}

/**
 * Build the query object for GET /request-history/summary. The chart window replaces any
 * from/to filters so the chart always covers the selected range.
 */
export function buildSummaryQuery(filters, { fromUtc, toUtc, bucketMinutes }) {
  const base = cleanFilters(filters);
  delete base.fromUtc;
  delete base.toUtc;
  return { ...base, fromUtc, toUtc, bucketMinutes };
}

/** Read filters from URL search params. */
export function filtersFromSearchParams(params) {
  const filters = emptyFilters();
  for (const key of REQUEST_FILTER_KEYS) {
    const value = params.get(key);
    if (value) filters[key] = value;
  }
  return filters;
}

/** Write filters (and optional paging) into a URLSearchParams-compatible object. */
export function filtersToSearchParams(filters, extra = {}) {
  const params = new URLSearchParams();
  const clean = cleanFilters(filters);
  for (const [key, value] of Object.entries(clean)) params.set(key, value);
  for (const [key, value] of Object.entries(extra)) {
    if (value !== null && value !== undefined && value !== '') params.set(key, String(value));
  }
  return params;
}

/** Describe active filters in plain English for confirmation dialogs. */
export function describeFilters(filters) {
  const clean = cleanFilters(filters);
  const d = strings.admin.requests.describe;
  const parts = [];
  if (clean.method) parts.push(d.method(clean.method));
  if (clean.statusCode) parts.push(d.statusCode(clean.statusCode));
  if (clean.pathContains) parts.push(d.pathContains(clean.pathContains));
  for (const key of ['routeTemplate', 'statusClass', 'errorCode', 'clientApp', 'clientPlatform', 'clientVersion']) if (clean[key]) parts.push(strings.chartKit.requestFilters[key] + ': ' + clean[key]);
  if (clean.fromUtc) parts.push(d.fromUtc(formatShortDateTime(clean.fromUtc)));
  if (clean.toUtc) parts.push(d.toUtc(formatShortDateTime(clean.toUtc)));
  return parts.length ? parts.join(', ') : strings.admin.requests.noFilters;
}

/** Whether any filter is set. */
export function hasFilters(filters) {
  return Object.keys(cleanFilters(filters)).length > 0;
}

/**
 * Normalize a list response for a page-number table. Accepts an `EnumerationResult` (`objects`,
 * `skip`, `maxResults`, `totalRecords`) and, from servers not yet converted, the Phase 1 page shape.
 */
export function normalizePage(response, fallbackPageSize = DEFAULT_PAGE_SIZE) {
  const r = normalizeEnumerationResult(response);
  const items = r.objects;
  const pageSize = r.maxResults || fallbackPageSize;
  const totalCount = typeof r.totalRecords === 'number' ? r.totalRecords : r.skip + items.length + (r.endOfResults ? 0 : 1);
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  return { items, pageNumber: Math.floor(r.skip / pageSize) + 1, pageSize, totalCount, totalPages };
}
