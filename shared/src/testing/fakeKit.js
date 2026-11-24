/**
 * Building blocks shared by the Phase 5 in-memory fakes: EnumerationResult paging, error and
 * success results, label and tag matching, injected failures, a fetch adapter over a
 * `handle({ method, path, query, body, headers })` function, and `combineFakes` to serve several
 * fakes from one `ApiClient`.
 */

export function clone(value) {
  return value === undefined ? undefined : JSON.parse(JSON.stringify(value));
}

/** An EnumerationResult page over `objects` with offset tokens (`o:<n>`). */
export function page(objects, query = {}, { max = 100, tokenPrefix = 'o:' } = {}) {
  const size = Math.max(1, Math.min(max, Math.floor(Number(query.maxResults) || 25)));
  const token = query.continuationToken;
  const offset = token && String(token).startsWith(tokenPrefix) && /^\d+$/.test(String(token).slice(tokenPrefix.length)) ? Number(String(token).slice(tokenPrefix.length)) : Math.max(0, Math.floor(Number(query.skip) || 0));
  const slice = objects.slice(offset, offset + size);
  const end = offset + size >= objects.length;
  return {
    success: true, maxResults: size, skip: offset, continuationToken: end ? null : tokenPrefix + (offset + size), endOfResults: end,
    totalRecords: objects.length, recordsRemaining: Math.max(0, objects.length - offset - slice.length), timestampUtc: '2026-11-12T00:00:00Z', objects: clone(slice)
  };
}

export function error(status, code, message, fieldErrors) {
  return { status, body: { error: code, message: message || code, ...(fieldErrors ? { fieldErrors } : {}) } };
}

export function ok(body, status = 200) {
  return { status, body: clone(body) };
}

export function noContent() {
  return { status: 204, body: null };
}

export function asList(value) {
  if (value === undefined || value === null || value === '') return [];
  return Array.isArray(value) ? value : String(value).split(',');
}

export function truthy(value) {
  return value === true || value === 'true';
}

/** Whether an object matches the query's `labels` (with `labelMatch`) and `tags` (`*` means has key). */
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

/** A UserSummary. */
export function summaryUser(id, displayName, extra = {}) {
  return { id, displayName, handle: String(displayName).toLowerCase().replace(/[^a-z0-9]+/g, ''), avatarUrl: null, isStaff: false, isDeleted: false, ...extra };
}

/**
 * Wrap a route handler with call recording, injected failures, and a fetch adapter.
 * `fail({ method?, path (string prefix or RegExp), status, code?, times?, retryAfterSeconds? })`
 * makes matching calls fail (status 0 throws a network error from `fetchImpl`).
 * @param {(call: { method: string, path: string, parts: string[], query: object, body: any, headers: object }) => ({ status: number, body: any }|undefined)} route
 */
export function createFakeServer(route) {
  const calls = [];
  const failures = [];

  function matches(rule, method, path) {
    if (rule.method && rule.method !== method) return false;
    if (rule.path instanceof RegExp) return rule.path.test(path);
    return !rule.path || path.startsWith(rule.path);
  }

  function handle({ method = 'GET', path = '', query = {}, body, headers = {} }) {
    const clean = String(path || '').split('?')[0].replace(/^\/api\/v1/, '');
    const m = String(method).toUpperCase();
    const b = body && typeof body === 'object' ? body : {};
    const h = {};
    for (const [k, v] of Object.entries(headers || {})) h[k.toLowerCase()] = v;
    calls.push({ method: m, path: clean, query, body: b, headers: h });
    const rule = failures.find((r) => r.times > 0 && matches(r, m, clean));
    if (rule) {
      rule.times -= 1;
      if (rule.status === 0) return { status: 0, body: null, network: true };
      const result = error(rule.status, rule.code || 'Injected', rule.message || 'Injected failure.');
      if (rule.retryAfterSeconds) result.headers = { 'retry-after': String(rule.retryAfterSeconds) };
      return result;
    }
    return route({ method: m, path: clean, parts: clean.split('/').filter(Boolean), query, body: b, headers: h });
  }

  async function fetchImpl(url, init = {}) {
    const parsed = new URL(url, 'http://localhost');
    let body = init.body;
    if (typeof body === 'string') {
      try { body = JSON.parse(body); } catch { body = undefined; }
    }
    const result = handle({ method: init.method || 'GET', path: parsed.pathname, query: Object.fromEntries(parsed.searchParams.entries()), body, headers: init.headers || {} })
      || error(404, 'NotFound', 'No fake route for ' + parsed.pathname);
    return toResponse(result);
  }

  function fail(rule) {
    failures.push({ times: 1, ...rule });
  }

  return { calls, handle, fetchImpl, fail, failures };
}

/** A fetch Response from a fake result (status 0 throws like a network failure). */
export function toResponse(result) {
  if (result.network || result.status === 0) throw new TypeError('Network request failed');
  if (result.retryAfter) result = { ...result, headers: { 'retry-after': String(result.retryAfter), ...(result.headers || {}) } };
  if (result.status === 204) return new Response(null, { status: 204 });
  if (typeof result.body === 'string') return new Response(result.body, { status: result.status, headers: { 'content-type': result.contentType || 'text/plain', ...(result.headers || {}) } });
  return new Response(JSON.stringify(result.body), { status: result.status, headers: { 'content-type': 'application/json', ...(result.headers || {}) } });
}

/**
 * One fetch over several fakes (objects with `handle`, or bare handle functions): the first fake that
 * answers wins; unknown routes 404. The result is a fetch implementation that also carries `handle`
 * and `fetchImpl` (itself), so it works both as `fetchImpl: combineFakes(a, b)` and as
 * `combineFakes(a, b).fetchImpl`.
 * @param {...({ handle: Function }|Function)} fakes
 */
export function combineFakes(...fakes) {
  const handlers = fakes.map((f) => (typeof f === 'function' ? f : f.handle));
  function handle(call) {
    for (const h of handlers) {
      const result = h(call);
      if (result !== undefined && result !== null) return result;
    }
    return undefined;
  }
  async function fetchImpl(url, init = {}) {
    const parsed = new URL(url, 'http://localhost');
    let body = init.body;
    if (typeof body === 'string') {
      try { body = JSON.parse(body); } catch { body = undefined; }
    }
    const result = handle({ method: init.method || 'GET', path: parsed.pathname, query: Object.fromEntries(parsed.searchParams.entries()), body, headers: init.headers || {} })
      || error(404, 'NotFound', 'No fake route for ' + parsed.pathname);
    return toResponse(result);
  }
  fetchImpl.handle = handle;
  fetchImpl.fetchImpl = fetchImpl;
  return fetchImpl;
}
