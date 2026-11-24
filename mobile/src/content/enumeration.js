/**
 * EnumerationQuery and EnumerationResult (docs/ENUMERATION_AND_METADATA.md section 2): every list
 * the app shows is requested with `POST /api/v1/{collection}/enumerate` and paged with the opaque
 * `continuationToken`. The app never asks for "all": `maxResults` is always set and clamped to the
 * member maximum.
 *
 * @trelliskit/shared has no enumeration helpers yet, so they live here; they have no React Native
 * dependency and can move to shared unchanged.
 */

/** Member route maximum (section 2.2, `enumeration.maxResultsMember`). */
export const MaxResultsMember = 100;

/** Default page size (section 2.2). */
export const DefaultMaxResults = 25;

/** Limits on a query's metadata filters (section 2.2). */
export const QueryLimits = Object.freeze({ labels: 20, tags: 20, search: 200 });

/** Orderings every collection supports. */
export const Ordering = Object.freeze({ CreatedDescending: 'CreatedDescending', CreatedAscending: 'CreatedAscending' });

/** Clamp a page size to 1..MaxResultsMember (default DefaultMaxResults). */
export function clampMaxResults(value) {
  const n = Number.isFinite(value) ? Math.floor(value) : DefaultMaxResults;
  return Math.max(1, Math.min(MaxResultsMember, n));
}

/** An empty filter state for a list screen. */
export function emptyFilters(ordering = null) {
  return { search: '', ordering, labels: [], labelMatch: 'All', tags: [] };
}

/** Number of active filters (shown on the filter button). Ordering is not a filter. */
export function activeFilterCount(filters) {
  if (!filters) return 0;
  let n = 0;
  if (filters.search && filters.search.trim()) n += 1;
  n += (filters.labels || []).length;
  n += (filters.tags || []).filter((t) => t && t.key && t.key.trim()).length;
  return n;
}

/**
 * Query fields for a filter state: `search`, `ordering`, `labels` with `labelMatch`, and `tags`
 * (a "has key" row sends `*`). Empty fields are left out.
 */
export function filtersToQuery(filters) {
  const query = {};
  if (!filters) return query;
  const search = (filters.search || '').trim().slice(0, QueryLimits.search);
  if (search) query.search = search;
  if (filters.ordering) query.ordering = filters.ordering;
  const labels = (filters.labels || []).filter(Boolean).slice(0, QueryLimits.labels);
  if (labels.length) {
    query.labels = labels;
    query.labelMatch = filters.labelMatch === 'Any' ? 'Any' : 'All';
  }
  const tags = {};
  for (const row of (filters.tags || []).slice(0, QueryLimits.tags)) {
    const key = row && row.key ? row.key.trim() : '';
    if (!key) continue;
    tags[key] = row.any ? '*' : row.value || '';
  }
  if (Object.keys(tags).length) query.tags = tags;
  return query;
}

/**
 * Build an EnumerationQuery.
 * @param {object} options
 * @param {string|null} [options.continuationToken]
 * @param {number} [options.maxResults]
 * @param {object} [options.filters] Filter state (emptyFilters shape).
 * @param {string} [options.ordering] Overrides the filter state's ordering.
 * @param {object} [options.collectionFilters] The collection's typed `filters`.
 */
export function buildEnumerationQuery({ continuationToken = null, maxResults, filters, ordering, collectionFilters } = {}) {
  const query = { maxResults: clampMaxResults(maxResults), ...filtersToQuery(filters) };
  if (ordering) query.ordering = ordering;
  if (continuationToken) query.continuationToken = continuationToken;
  if (collectionFilters) {
    const typed = {};
    for (const [key, value] of Object.entries(collectionFilters)) if (value !== undefined && value !== null && value !== '') typed[key] = value;
    if (Object.keys(typed).length) query.filters = typed;
  }
  return query;
}

/**
 * Normalize an EnumerationResult: `{ items, nextCursor }` where nextCursor is the continuation
 * token, or null at the end of results.
 * @param {any} result
 */
export function readEnumeration(result) {
  if (!result || typeof result !== 'object') return { items: [], nextCursor: null };
  const items = Array.isArray(result.objects) ? result.objects : [];
  const token = typeof result.continuationToken === 'string' && result.continuationToken ? result.continuationToken : null;
  return { items, nextCursor: result.endOfResults === true ? null : token };
}

/** Remove `staff:` labels and tag keys (members never see staff metadata). */
export function isStaffKey(value) {
  return typeof value === 'string' && value.trim().toLowerCase().startsWith('staff:');
}
