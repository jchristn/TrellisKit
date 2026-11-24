// HTTP client for the public TrellisKit REST API.
// Retries rate limits (429 with Retry-After) and transient failures (connection
// refused, 502, 503 while a stack restarts), and re-signs a member session on 401.

import { setTimeout as sleep } from 'node:timers/promises';

export class ApiError extends Error {
  constructor(method, path, status, body) {
    const code = body && typeof body === 'object' ? body.error : undefined;
    const message = body && typeof body === 'object' ? body.message : String(body ?? '');
    super(`${method} ${path} -> ${status} ${code ?? ''} ${message ?? ''}`.trim());
    this.method = method;
    this.path = path;
    this.status = status;
    this.code = code;
    this.body = body;
  }
}

export class Api {
  constructor({ baseUrl, adminToken, log, maxRetryAfterSeconds = 120, clientName = 'trelliskit-seed', appId = null, propertyId = null }) {
    this.baseUrl = baseUrl.replace(/\/+$/, '');
    this.adminToken = adminToken;
    this.log = log;
    this.maxRetryAfterSeconds = maxRetryAfterSeconds;
    this.clientName = clientName;
    this.appId = appId;
    this.propertyId = propertyId;
    this.requestCount = 0;
    this.rateLimitWaits = 0;
  }

  url(path) {
    if (/^https?:\/\//.test(path)) return path;
    return `${this.baseUrl}${path.startsWith('/') ? '' : '/'}${path}`;
  }

  // auth: 'admin' (break-glass token), a Session, or null (anonymous)
  async request(method, path, { auth = 'admin', body, raw, contentType, headers = {}, ok = [], query } = {}) {
    let url = this.url(path);
    if (query) {
      const qs = new URLSearchParams();
      for (const [k, v] of Object.entries(query)) {
        if (v === undefined || v === null) continue;
        if (Array.isArray(v)) v.forEach((x) => qs.append(k, String(x)));
        else qs.append(k, String(v));
      }
      const s = qs.toString();
      if (s) url += (url.includes('?') ? '&' : '?') + s;
    }
    let attempt = 0;
    let reauthed = false;
    for (;;) {
      attempt += 1;
      const h = { 'X-Client': 'mobile', 'X-Client-Version': '1.0.0', 'User-Agent': this.clientName, ...(this.appId ? { 'X-TK-App': this.appId } : {}), ...(this.propertyId ? { 'X-TK-Property': this.propertyId } : {}), ...headers };
      let token = null;
      if (auth === 'admin') token = this.adminToken;
      else if (auth && typeof auth === 'object') token = await auth.accessToken(this);
      if (token) h.Authorization = `Bearer ${token}`;
      let payload;
      if (raw !== undefined) {
        payload = raw;
        h['Content-Type'] = contentType ?? 'application/octet-stream';
      } else if (body !== undefined) {
        payload = JSON.stringify(body);
        h['Content-Type'] = 'application/json';
      }
      let res;
      try {
        this.requestCount += 1;
        res = await fetch(url, { method, headers: h, body: payload });
      } catch (err) {
        if (attempt <= 6) {
          this.log.debug(`network error on ${method} ${path}: ${err.message}; retrying`);
          await sleep(2000 * attempt);
          continue;
        }
        throw err;
      }
      const text = await res.text();
      let data = text;
      if (text && (res.headers.get('content-type') ?? '').includes('json')) {
        try { data = JSON.parse(text); } catch { /* keep text */ }
      }
      if (res.status === 429 && attempt <= 12) {
        const retryAfter = Math.min(Number(res.headers.get('retry-after') ?? '5') || 5, this.maxRetryAfterSeconds);
        this.rateLimitWaits += 1;
        this.log.info(`  rate limited on ${method} ${path.split('?')[0]}; waiting ${retryAfter}s`);
        await sleep((retryAfter + 1) * 1000);
        continue;
      }
      if ((res.status === 502 || res.status === 503 || res.status === 504) && attempt <= 5 && !(data && data.error === 'IntegrationNotConfigured')) {
        await sleep(3000 * attempt);
        continue;
      }
      if (res.status === 401 && auth && typeof auth === 'object' && !reauthed) {
        reauthed = true;
        auth.invalidate();
        continue;
      }
      if (res.status >= 200 && res.status < 300) return { status: res.status, data, headers: res.headers };
      if (ok.includes(res.status) || (data && ok.includes(data.error))) return { status: res.status, data, headers: res.headers, error: data && data.error };
      throw new ApiError(method, path, res.status, data);
    }
  }

  async get(path, opts) { return (await this.request('GET', path, opts)).data; }
  async post(path, body, opts = {}) { return (await this.request('POST', path, { ...opts, body })).data; }
  async put(path, body, opts = {}) { return (await this.request('PUT', path, { ...opts, body })).data; }
  async patch(path, body, opts = {}) { return (await this.request('PATCH', path, { ...opts, body })).data; }
  async del(path, opts = {}) { return (await this.request('DELETE', path, opts)).data; }

  // POST <collection>/enumerate and return every object (bounded by maxPages).
  async enumerateAll(path, query = {}, opts = {}, maxPages = 20) {
    const out = [];
    let continuationToken = null;
    for (let page = 0; page < maxPages; page += 1) {
      const body = { maxResults: query.maxResults ?? 100, ...query };
      if (continuationToken) body.continuationToken = continuationToken;
      const data = await this.post(path, body, opts);
      out.push(...(data.objects ?? []));
      if (data.endOfResults || !data.continuationToken) break;
      continuationToken = data.continuationToken;
    }
    return out;
  }
}

// A member session: signs in lazily with email and password.
export class Session {
  constructor({ key, email, password }) {
    this.key = key;
    this.email = email;
    this.password = password;
    this.token = null;
    this.expiresUtc = 0;
    this.user = null;
  }

  invalidate() { this.token = null; }

  async accessToken(api) {
    if (this.token && Date.now() < this.expiresUtc - 60_000) return this.token;
    const res = await api.request('POST', '/api/v1/auth/login', { auth: null, body: { email: this.email, password: this.password } });
    this.adopt(res.data);
    return this.token;
  }

  adopt(authResponse) {
    this.token = authResponse.accessToken;
    this.expiresUtc = Date.parse(authResponse.accessTokenExpiresUtc ?? '') || Date.now() + 30 * 60_000;
    if (authResponse.user) this.user = authResponse.user;
  }

  get userId() { return this.user?.id ?? null; }
}
