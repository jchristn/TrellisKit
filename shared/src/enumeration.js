/**
 * Enumeration (docs/ENUMERATION_AND_METADATA.md section 2): every list is requested with an
 * `EnumerationQuery` and answered with an `EnumerationResult<T>`. This module builds queries,
 * serializes them to the POST `/enumerate` body and to the GET query-string form, parses the
 * query-string form back (URL state), and normalizes results.
 */

/** Default and maximum page sizes (section 2.2). */
export const ENUMERATION_DEFAULT_MAX_RESULTS = 25;
export const ENUMERATION_MAX_RESULTS_MEMBER = 100;
export const ENUMERATION_MAX_RESULTS_ADMIN = 500;
export const ENUMERATION_MAX_SKIP = 100000;
export const ENUMERATION_MAX_FILTER_LABELS = 20;
export const ENUMERATION_MAX_FILTER_TAGS = 20;
export const ENUMERATION_SEARCH_MAX = 200;

/** Orderings every collection supports. */
export const Ordering = Object.freeze({ CreatedDescending: 'CreatedDescending', CreatedAscending: 'CreatedAscending' });

/** Label match modes. */
export const LabelMatch = Object.freeze({ All: 'All', Any: 'Any' });

/** Query-string keys that belong to the query itself (everything else is a collection filter). */
const RESERVED = new Set(['maxResults', 'continuationToken', 'skip', 'ordering', 'search', 'createdAfterUtc', 'createdBeforeUtc', 'labels', 'labelMatch']);
const TAG_PREFIX = 'tag.';

function clampInt(value, min, max, fallback) {
  const n = Math.floor(Number(value));
  if (!Number.isFinite(n)) return fallback;
  return Math.min(max, Math.max(min, n));
}

function cleanLabels(labels) {
  const seen = new Set();
  const out = [];
  for (const raw of Array.isArray(labels) ? labels : (labels ? [labels] : [])) {
    const label = String(raw || '').trim();
    if (!label || seen.has(label.toLowerCase())) continue;
    seen.add(label.toLowerCase());
    out.push(label);
    if (out.length >= ENUMERATION_MAX_FILTER_LABELS) break;
  }
  return out;
}

function cleanTags(tags) {
  const out = {};
  let count = 0;
  for (const [rawKey, rawValue] of Object.entries(tags && typeof tags === 'object' ? tags : {})) {
    const key = String(rawKey || '').trim();
    if (!key || count >= ENUMERATION_MAX_FILTER_TAGS) continue;
    out[key] = rawValue === null || rawValue === undefined ? '*' : String(rawValue);
    count += 1;
  }
  return out;
}

/**
 * Collection filters: empty values and boolean `false` (an unchecked toggle) are dropped, and the
 * strings `"true"` and `"false"` (URL state) become JSON booleans. The server accepts booleans and
 * the legacy strings alike, so a value sent either way filters the same.
 */
function cleanFilters(filters) {
  const out = {};
  for (const [key, value] of Object.entries(filters && typeof filters === 'object' ? filters : {})) {
    if (value === null || value === undefined || value === '' || value === false) continue;
    out[key] = booleanFilter(value);
  }
  return out;
}

function booleanFilter(value) {
  if (value === 'true') return true;
  if (value === 'false') return false;
  return value;
}

/**
 * Build a normalized `EnumerationQuery`. Empty values are dropped; `maxResults` is clamped to
 * 1..`maxLimit` (100 for member routes, 500 for admin); `skip` is dropped when a continuation
 * token is present.
 * @param {object} [options]
 * @param {number} [options.maxResults]
 * @param {string|null} [options.continuationToken]
 * @param {number} [options.skip]
 * @param {string} [options.ordering]
 * @param {string} [options.search]
 * @param {string} [options.createdAfterUtc]
 * @param {string} [options.createdBeforeUtc]
 * @param {string[]} [options.labels]
 * @param {'All'|'Any'} [options.labelMatch]
 * @param {Record<string,string>} [options.tags] A value of `*` means "has key"
 * @param {Record<string, any>} [options.filters] Collection-specific filters
 * @param {{ maxLimit?: number }} [limits]
 * @returns {object}
 */
export function createEnumerationQuery(options = {}, { maxLimit = ENUMERATION_MAX_RESULTS_MEMBER } = {}) {
  const q = { maxResults: clampInt(options.maxResults, 1, maxLimit, ENUMERATION_DEFAULT_MAX_RESULTS) };
  if (options.continuationToken) q.continuationToken = String(options.continuationToken);
  else if (options.skip) q.skip = clampInt(options.skip, 0, ENUMERATION_MAX_SKIP, 0);
  if (options.ordering) q.ordering = String(options.ordering);
  const search = String(options.search || '').trim().slice(0, ENUMERATION_SEARCH_MAX);
  if (search) q.search = search;
  if (options.createdAfterUtc) q.createdAfterUtc = options.createdAfterUtc;
  if (options.createdBeforeUtc) q.createdBeforeUtc = options.createdBeforeUtc;
  const labels = cleanLabels(options.labels);
  if (labels.length) {
    q.labels = labels;
    q.labelMatch = options.labelMatch === LabelMatch.Any ? LabelMatch.Any : LabelMatch.All;
  }
  const tags = cleanTags(options.tags);
  if (Object.keys(tags).length) q.tags = tags;
  const filters = cleanFilters(options.filters);
  if (Object.keys(filters).length) q.filters = filters;
  return q;
}

/**
 * The JSON body for `POST /{collection}/enumerate`.
 * @param {object} query EnumerationQuery (normalized or raw)
 * @param {{ maxLimit?: number }} [limits]
 */
export function toEnumerationBody(query, limits) {
  return createEnumerationQuery(query, limits);
}

/**
 * The GET query-string form: an object for `ApiClient` (`labels` is an array, sent as repeated
 * `labels=` parameters), with `tag.<key>=<value>` per tag and collection filters at the top level.
 * @param {object} query
 * @param {{ maxLimit?: number }} [limits]
 * @returns {Record<string, string|number|string[]>}
 */
export function toEnumerationParams(query, limits) {
  const q = createEnumerationQuery(query, limits);
  const params = {};
  for (const key of ['maxResults', 'continuationToken', 'skip', 'ordering', 'search', 'createdAfterUtc', 'createdBeforeUtc']) {
    if (q[key] !== undefined) params[key] = q[key];
  }
  if (q.labels) {
    params.labels = q.labels.slice();
    params.labelMatch = q.labelMatch;
  }
  for (const [key, value] of Object.entries(q.tags || {})) params[TAG_PREFIX + key] = value;
  for (const [key, value] of Object.entries(q.filters || {})) {
    if (!RESERVED.has(key) && !key.startsWith(TAG_PREFIX)) params[key] = value;
  }
  return params;
}

/**
 * Serialize the GET form to a query string (`labels=a&labels=b&tag.level=beginner`).
 * @param {object} query
 * @param {{ maxLimit?: number }} [limits]
 */
export function toEnumerationSearch(query, limits) {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(toEnumerationParams(query, limits))) {
    if (Array.isArray(value)) for (const v of value) search.append(key, String(v));
    else search.append(key, String(value));
  }
  return search.toString();
}

/**
 * Parse the query-string form back into an EnumerationQuery (for URL state). Keys other than the
 * reserved ones and `tag.*` become `filters` when listed in `filterKeys` (all when omitted).
 * Paging keys (`maxResults`, `continuationToken`, `skip`) are kept only when `paging` is true.
 * @param {URLSearchParams|string|Record<string,string|string[]>} source
 * @param {{ filterKeys?: string[], paging?: boolean, prefix?: string }} [options]
 */
export function parseEnumerationParams(source, { filterKeys, paging = true, prefix = '' } = {}) {
  const params = source instanceof URLSearchParams ? source : new URLSearchParams(typeof source === 'string' ? source : flatten(source));
  const out = { labels: [], tags: {}, filters: {} };
  for (const [rawKey, value] of params.entries()) {
    if (prefix && !rawKey.startsWith(prefix)) continue;
    const key = prefix ? rawKey.slice(prefix.length) : rawKey;
    if (key === 'labels') out.labels.push(value);
    else if (key.startsWith(TAG_PREFIX)) out.tags[key.slice(TAG_PREFIX.length)] = value;
    else if (key === 'labelMatch') out.labelMatch = value === LabelMatch.Any ? LabelMatch.Any : LabelMatch.All;
    else if (['ordering', 'search', 'createdAfterUtc', 'createdBeforeUtc'].includes(key)) out[key] = value;
    else if (['maxResults', 'skip'].includes(key)) { if (paging) out[key] = Number(value); }
    else if (key === 'continuationToken') { if (paging) out.continuationToken = value; }
    else if (!filterKeys || filterKeys.includes(key)) out.filters[key] = value;
  }
  out.labels = cleanLabels(out.labels);
  return out;
}

function flatten(record) {
  const pairs = [];
  for (const [key, value] of Object.entries(record || {})) {
    if (Array.isArray(value)) for (const v of value) pairs.push([key, String(v)]);
    else if (value !== undefined && value !== null && value !== '') pairs.push([key, String(value)]);
  }
  return pairs;
}

/**
 * Normalize a list response to the `EnumerationResult` shape. Accepts an `EnumerationResult`,
 * and, for servers that predate the conversion, a Phase 2 `CursorPage` (`items`, `nextCursor`) or
 * a Phase 1 page (`items`, `pageNumber`, `pageSize`, `totalCount`). Extra top-level fields (for
 * example `unreadCount` or `commentCount`) are kept in `extra`.
 * @param {object} response
 * @returns {{ objects: any[], continuationToken: string|null, endOfResults: boolean, totalRecords: number|null, recordsRemaining: number|null, maxResults: number|null, skip: number, extra: object }}
 */
export function normalizeEnumerationResult(response) {
  const r = response && typeof response === 'object' ? response : {};
  const known = new Set(['success', 'maxResults', 'skip', 'continuationToken', 'endOfResults', 'totalRecords', 'recordsRemaining', 'timestampUtc', 'objects', 'items', 'nextCursor', 'pageNumber', 'pageSize', 'totalCount', 'totalPages']);
  const extra = {};
  for (const [k, v] of Object.entries(r)) if (!known.has(k)) extra[k] = v;
  if (Array.isArray(r.objects)) {
    const token = r.continuationToken || null;
    return {
      objects: r.objects,
      continuationToken: token,
      endOfResults: typeof r.endOfResults === 'boolean' ? r.endOfResults : !token,
      totalRecords: typeof r.totalRecords === 'number' ? r.totalRecords : null,
      recordsRemaining: typeof r.recordsRemaining === 'number' ? r.recordsRemaining : null,
      maxResults: typeof r.maxResults === 'number' ? r.maxResults : null,
      skip: typeof r.skip === 'number' ? r.skip : 0,
      extra
    };
  }
  const items = Array.isArray(r.items) ? r.items : [];
  if ('nextCursor' in r || !('pageNumber' in r)) {
    return { objects: items, continuationToken: r.nextCursor || null, endOfResults: !r.nextCursor, totalRecords: null, recordsRemaining: null, maxResults: null, skip: 0, extra };
  }
  const pageSize = r.pageSize || items.length || ENUMERATION_DEFAULT_MAX_RESULTS;
  const skip = ((r.pageNumber || 1) - 1) * pageSize;
  const total = typeof r.totalCount === 'number' ? r.totalCount : null;
  return {
    objects: items,
    continuationToken: null,
    endOfResults: total === null ? true : skip + items.length >= total,
    totalRecords: total,
    recordsRemaining: total === null ? null : Math.max(0, total - skip - items.length),
    maxResults: pageSize,
    skip,
    extra
  };
}

/**
 * Convert a table sort string (`field` or `-field`) to an ordering name: `-createdUtc` becomes
 * `CreatedDescending`, `displayName` becomes `DisplayNameAscending`. A value that already is an
 * ordering name passes through.
 * @param {string} sort
 * @returns {string}
 */
export function sortToOrdering(sort) {
  const text = String(sort || '').trim();
  if (!text) return '';
  if (/(Ascending|Descending)$/.test(text) || /^[A-Z]/.test(text)) return text;
  const desc = text.startsWith('-');
  const field = (desc ? text.slice(1) : text).replace(/Utc$/, '');
  return field.charAt(0).toUpperCase() + field.slice(1) + (desc ? 'Descending' : 'Ascending');
}

/**
 * Convert an ordering name back to a table sort string (inverse of `sortToOrdering`).
 * @param {string} ordering
 * @param {string[]} [utcFields] field names that end in `Utc` (for example `createdUtc`)
 */
export function orderingToSort(ordering, utcFields = []) {
  const m = /^(.+?)(Ascending|Descending)$/.exec(String(ordering || ''));
  if (!m) return '';
  let field = m[1].charAt(0).toLowerCase() + m[1].slice(1);
  if (utcFields.includes(field + 'Utc')) field += 'Utc';
  return (m[2] === 'Descending' ? '-' : '') + field;
}

/**
 * Follow real continuation pages for bootstrap configuration or a finite chart window.
 * Each request remains bounded. Incomplete/malformed pagination fails explicitly instead of
 * silently dropping records. Interactive lists should display individual pages instead.
 */
export async function collectEnumeration(fetchPage, query = {}, { signal, maxPages = 200 } = {}) {
  const objects = [];
  const seen = new Set();
  let continuationToken;
  let first;
  for (let pageNumber = 0; pageNumber < maxPages; pageNumber += 1) {
    if (signal?.aborted) throw signal.reason || Object.assign(new Error('Request aborted.'), { name: 'AbortError' });
    const page = await fetchPage({ ...query, ...(continuationToken ? { continuationToken, skip: undefined } : {}), maxResults: query.maxResults || 50 }, signal);
    if (!page || !Array.isArray(page.objects)) throw new Error('Invalid enumeration response.');
    first ||= page;
    objects.push(...page.objects);
    if (page.endOfResults === true) return { ...first, objects, continuationToken: null, endOfResults: true, recordsRemaining: 0 };
    const next = page.continuationToken;
    if (!next || seen.has(next)) throw new Error('Enumeration did not provide a new continuation token.');
    seen.add(next);
    continuationToken = next;
  }
  throw new Error('Enumeration exceeded the page limit. Refine the requested range.');
}
