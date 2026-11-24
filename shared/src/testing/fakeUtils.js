/**
 * Small helpers shared by the Phase 4 fakes (library, events, directory): EnumerationResult paging
 * with offset continuation tokens, label and tag matching, response builders, and a fetch adapter.
 * Test and prototyping code only.
 */

/** Deep copy through JSON (fakes never hand out their own state). */
export function clone(value) {
  return value === undefined ? undefined : JSON.parse(JSON.stringify(value));
}

/** One EnumerationResult page over `objects` (continuation tokens are `o:<offset>`). */
export function page(objects, query = {}, { max = 100 } = {}) {
  const size = Math.max(1, Math.min(max, Math.floor(Number(query.maxResults) || 25)));
  const token = query.continuationToken;
  const offset = token && /^o:\d+$/.test(token) ? Number(token.slice(2)) : Math.max(0, Math.floor(Number(query.skip) || 0));
  const slice = objects.slice(offset, offset + size);
  const end = offset + size >= objects.length;
  return {
    success: true, maxResults: size, skip: offset, continuationToken: end ? null : 'o:' + (offset + size), endOfResults: end,
    totalRecords: objects.length, recordsRemaining: Math.max(0, objects.length - offset - slice.length), timestampUtc: '2026-11-11T00:00:00Z', objects: clone(slice)
  };
}

/** A filter value as a list (arrays as is, comma strings split). */
export function asList(value) {
  if (value === undefined || value === null || value === '') return [];
  return Array.isArray(value) ? value.map(String) : String(value).split(',');
}

/** Whether a filter value is true (`true` or `'true'`). */
export function truthy(value) {
  return value === true || value === 'true';
}

/** Whether a filter value is false (`false` or `'false'`). */
export function falsy(value) {
  return value === false || value === 'false';
}

/** Label (`All`/`Any`) and tag (`*` means has key) matching per the enumeration specification. */
export function matchesMeta(item, query) {
  const labels = (query.labels || []).map((l) => String(l).toLowerCase());
  if (labels.length) {
    const own = (item.labels || []).map((l) => String(l).toLowerCase());
    const hit = query.labelMatch === 'Any' ? labels.some((l) => own.includes(l)) : labels.every((l) => own.includes(l));
    if (!hit) return false;
  }
  for (const [key, value] of Object.entries(query.tags || {})) {
    const own = item.tags || {};
    const found = Object.keys(own).find((k) => k.toLowerCase() === key.toLowerCase());
    if (!found) return false;
    if (value !== '*' && own[found] !== value) return false;
  }
  return true;
}

/** Case-insensitive search over a few text fields. */
export function matchesSearch(item, search, fields) {
  const term = String(search || '').trim().toLowerCase();
  if (!term) return true;
  return fields.some((f) => String(item[f] || '').toLowerCase().includes(term));
}

/** An error response. */
export function error(status, code, message, fieldErrors) {
  return { status, body: { error: code, message: message || code, ...(fieldErrors ? { fieldErrors } : {}) } };
}

/** A success response (the body is copied). */
export function ok(body, status = 200) {
  return { status, body: clone(body) };
}

/** A 204 response. */
export function noContent() {
  return { status: 204, body: null };
}

/** A text response (`contentType` defaults to text/plain). */
export function textResponse(text, contentType = 'text/plain') {
  return { status: 200, body: text, contentType };
}

/** Split a request path (relative to or including `/api/v1`) into parts. */
export function splitPath(path) {
  return String(path || '').split('?')[0].replace(/^\/api\/v1/, '').split('/').filter(Boolean).map(decodeURIComponent);
}

/**
 * Group map points `{ g: { lat, lng }, ... }` into a simple grid over `box` with about `max` cells:
 * one-point cells stay `point(x)`, others become `Cluster { kind, count, lat, lng, bbox }`.
 */
export function gridClusters(points, box, max, point) {
  const cells = Math.ceil(Math.sqrt(max));
  const height = (box.north - box.south) / cells || 1;
  const width = ((box.east - box.west + 360) % 360 || 360) / cells;
  const groups = {};
  for (const x of points) {
    const key = Math.floor((x.g.lat - box.south) / height) + ':' + Math.floor((((x.g.lng - box.west) + 360) % 360) / width);
    (groups[key] = groups[key] || []).push(x);
  }
  return Object.values(groups).map((g) => {
    if (g.length === 1) return point(g[0]);
    const lats = g.map((x) => x.g.lat);
    const lngs = g.map((x) => x.g.lng);
    return { kind: 'Cluster', count: g.length, lat: lats.reduce((a, b) => a + b, 0) / g.length, lng: lngs.reduce((a, b) => a + b, 0) / g.length, bbox: { south: Math.min(...lats), west: Math.min(...lngs), north: Math.max(...lats), east: Math.max(...lngs) } };
  });
}

/**
 * A fetch implementation over one or more `handle` functions (the first defined answer wins;
 * unknown routes answer 404 NotFound).
 * @param {...((call: object) => ({ status: number, body: any, contentType?: string }|undefined))} handlers
 */
export function fetchOver(...handlers) {
  return async function fetchImpl(url, init = {}) {
    const parsed = new URL(url, 'http://localhost');
    let body = init.body;
    if (typeof body === 'string') {
      try {
        body = JSON.parse(body);
      } catch {
        body = undefined;
      }
    }
    const call = { method: init.method || 'GET', path: parsed.pathname, query: Object.fromEntries(parsed.searchParams.entries()), body };
    let result;
    for (const handle of handlers) {
      result = handle(call);
      if (result) break;
    }
    if (!result) result = error(404, 'NotFound', 'No fake route for ' + parsed.pathname);
    if (result.status === 204) return new Response(null, { status: 204 });
    if (result.contentType) return new Response(String(result.body), { status: result.status, headers: { 'content-type': result.contentType } });
    const headers = { 'content-type': 'application/json' };
    if (result.retryAfter) headers['retry-after'] = String(result.retryAfter);
    return new Response(JSON.stringify(result.body), { status: result.status, headers });
  };
}
