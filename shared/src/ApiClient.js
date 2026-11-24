import { ApiError, parseRetryAfter } from './ApiError.js';
import { engagementMethods } from './engagementApi.js';
import { forumMethods } from './forumsApi.js';
import { platformMethods } from './platform.js';
import { propertyAdminMethods } from './propertyAdminApi.js';
import { appearanceMethods } from './appearanceApi.js';
import { updatesMethods } from './updatesApi.js';
import { aiMethods } from './aiApi.js';
import { libraryMethods } from './libraryApi.js';
import { eventMethods } from './eventsApi.js';
import { directoryMethods } from './directoryApi.js';
import { directMessagesApi, messagingMethods } from './messagingApi.js';
import { supportMethods } from './supportApi.js';
import { ideasMethods } from './ideasApi.js';
import { broadcastsMethods } from './broadcastsApi.js';
import { documentsMethods } from './documentsApi.js';
import { jobsMethods } from './jobsApi.js';
import { metricsMethods } from './metricsApi.js';
import { contentReviewMethods } from './contentReviewApi.js';
import { publicPageMethods } from './publicPages.js';

/**
 * Minimal fetch-based client for the TrellisKit REST API.
 *
 * All routes live under `/api/v1`. On the website the base URL is usually empty (same origin,
 * nginx or Vite proxies `/api` to the backend). The mobile app passes an absolute base URL.
 *
 * The client has no browser-only dependencies so the website and the React Native app can both
 * use it. Session handling (refresh on 401) is delegated to the `onUnauthorized` hook so each
 * app can refresh in its own way (cookie on the web, secure storage on mobile).
 */
export class ApiClient {
  /**
   * @param {object} [options]
   * @param {string} [options.baseUrl] Absolute origin such as `https://api.example.test`, or '' for same origin.
   * @param {() => (string|null|Promise<string|null>)} [options.getAccessToken] Returns the bearer token to send, if any.
   * @param {typeof fetch} [options.fetchImpl] Fetch implementation (defaults to global fetch).
   * @param {number} [options.timeoutMs] Per-request timeout in milliseconds (default 30000).
   * @param {Record<string,string>|(() => Record<string,string>)} [options.headers] Headers added to every request,
   *   for example `{ 'X-Client': 'web', 'X-Client-Version': '1.0.0' }`.
   * @param {(info: { method: string, path: string, error: ApiError }) => (boolean|Promise<boolean>)} [options.onUnauthorized]
   *   Called when a request returns 401. Resolve true after refreshing the session to retry the request once.
   * @param {RequestCredentials} [options.credentials] Fetch credentials mode (default 'include').
   */
  constructor({ baseUrl = '', getAccessToken = null, fetchImpl = null, timeoutMs = 30000, headers = null, onUnauthorized = null, credentials = 'include' } = {}) {
    this.baseUrl = baseUrl.replace(/\/+$/, '');
    this.getAccessToken = getAccessToken;
    this.fetchImpl = fetchImpl || globalThis.fetch.bind(globalThis);
    this.timeoutMs = timeoutMs;
    this.headers = headers;
    this.onUnauthorized = onUnauthorized;
    this.credentials = credentials;
  }

  /**
   * Build a full URL for an API path.
   * @param {string} path Path relative to `/api/v1`, for example `/health`.
   * @param {Record<string, string|number|boolean|Array<string|number>|null|undefined>} [query] Query parameters; null and undefined are omitted, arrays repeat the key.
   * @returns {string}
   */
  url(path, query) {
    const normalized = path.startsWith('/') ? path : '/' + path;
    let result = this.baseUrl + '/api/v1' + normalized;
    if (query) {
      const params = new URLSearchParams();
      for (const [key, value] of Object.entries(query)) {
        // Arrays become repeated parameters (`labels=a&labels=b`, docs/ENUMERATION_AND_METADATA.md).
        if (Array.isArray(value)) {
          for (const item of value) if (item !== null && item !== undefined && item !== '') params.append(key, String(item));
        } else if (value !== null && value !== undefined && value !== '') params.append(key, String(value));
      }
      const text = params.toString();
      if (text) result += '?' + text;
    }
    return result;
  }

  /**
   * Headers sent with every request: the configured defaults plus the bearer token.
   * @returns {Promise<Record<string,string>>}
   */
  async baseHeaders() {
    const configured = typeof this.headers === 'function' ? this.headers() : this.headers;
    const headers = { ...(configured || {}) };
    if (this.getAccessToken) {
      const token = await this.getAccessToken();
      if (token) headers.Authorization = 'Bearer ' + token;
    }
    return headers;
  }

  /**
   * Send a request and parse the response.
   * @param {string} method HTTP method.
   * @param {string} path Path relative to `/api/v1`.
   * @param {object} [options]
   * @param {Record<string, any>} [options.query] Query parameters.
   * @param {unknown} [options.body] JSON body.
   * @param {any} [options.rawBody] Raw body (Blob, ArrayBuffer, string) sent as is; use with `contentType`.
   * @param {string} [options.contentType] Content-Type for `rawBody`.
   * @param {Record<string,string>} [options.headers] Extra headers for this request.
   * @param {'json'|'text'|'blob'|'textWithHeaders'|'jsonWithStatus'} [options.responseType] How to read a successful body (default: by content type).
   *   `jsonWithStatus` resolves `{ status, data, etag }` and treats `304 Not Modified` as success.
   *   `textWithHeaders` resolves `{ text, headers }` so callers can read paging headers such as `X-Continuation-Token`.
   * @param {boolean} [options.retryOnUnauthorized] Set false to skip the `onUnauthorized` hook (default true).
   * @param {AbortSignal} [options.signal] Abort signal.
   * @returns {Promise<any>} Parsed JSON, text, Blob, or null for 204.
   * @throws {ApiError}
   */
  async request(method, path, options = {}) {
    try {
      return await this.send(method, path, options);
    } catch (err) {
      const canRetry = err instanceof ApiError && err.status === 401 && options.retryOnUnauthorized !== false && this.onUnauthorized;
      if (!canRetry) throw err;
      let retry = false;
      try {
        retry = await this.onUnauthorized({ method, path, error: err });
      } catch {
        retry = false;
      }
      if (!retry) throw err;
      return this.send(method, path, options);
    }
  }

  /**
   * Send one request without the 401 retry.
   * @param {string} method
   * @param {string} path
   * @param {object} [options] Same as `request`.
   * @returns {Promise<any>}
   * @throws {ApiError}
   */
  async send(method, path, { query, body, rawBody, contentType, headers: extraHeaders, responseType, signal, cache } = {}) {
    const headers = { Accept: 'application/json', ...(await this.baseHeaders()), ...(extraHeaders || {}) };
    let payload;
    if (rawBody !== undefined) {
      payload = rawBody;
      if (contentType) headers['Content-Type'] = contentType;
    } else if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
      payload = JSON.stringify(body);
    }

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), this.timeoutMs);
    if (signal) {
      if (signal.aborted) controller.abort();
      else signal.addEventListener('abort', () => controller.abort(), { once: true });
    }

    let response;
    try {
      response = await this.fetchImpl(this.url(path, query), {
        method,
        headers,
        body: payload,
        signal: controller.signal,
        credentials: this.credentials,
        // `cache` (a fetch cache mode) lets a read revalidate instead of using a browser-cached copy.
        ...(cache ? { cache } : {})
      });
    } catch (err) {
      const aborted = err && err.name === 'AbortError';
      throw new ApiError(aborted ? 'The request timed out or was cancelled.' : 'Unable to reach the server.', { status: 0 });
    } finally {
      clearTimeout(timer);
    }

    if (response.status === 204) return null;
    // Conditional reads (`propertyIfChanged`) treat 304 as success.
    if (responseType === 'jsonWithStatus' && response.status === 304) return { status: 304, data: null, etag: (response.headers && response.headers.get('etag')) || null };

    const responseContentType = (response.headers && response.headers.get('content-type')) || '';
    if (!response.ok) {
      const parsed = responseContentType.includes('application/json')
        ? await response.json().catch(() => null)
        : await response.text().catch(() => null);
      const message = (parsed && typeof parsed === 'object' && (parsed.message || parsed.Message)) || ('Request failed with status ' + response.status + '.');
      const code = (parsed && typeof parsed === 'object' && (parsed.error || parsed.Error)) || null;
      const retryAfterSeconds = parseRetryAfter(response.headers && response.headers.get('retry-after'));
      throw new ApiError(message, { status: response.status, code, body: parsed, retryAfterSeconds });
    }

    if (responseType === 'textWithHeaders') return { text: await response.text(), headers: response.headers };
    if (responseType === 'jsonWithStatus') {
      return { status: response.status, data: await response.json().catch(() => null), etag: (response.headers && response.headers.get('etag')) || null };
    }
    if (responseType === 'blob') return response.blob();
    if (responseType === 'text') return response.text();
    if (responseType === 'json' || responseContentType.includes('application/json')) return response.json().catch(() => null);
    return response.text().catch(() => null);
  }

  /** @param {string} path @param {object} [options] */
  get(path, options) { return this.request('GET', path, options); }
  /** @param {string} path @param {unknown} body @param {object} [options] */
  post(path, body, options) { return this.request('POST', path, { ...options, body }); }
  /** @param {string} path @param {unknown} body @param {object} [options] */
  put(path, body, options) { return this.request('PUT', path, { ...options, body }); }
  /** @param {string} path @param {object} [options] */
  delete(path, options) { return this.request('DELETE', path, options); }

  /**
   * Read server health.
   * @param {AbortSignal} [signal]
   * @returns {Promise<{ status: string, version: string, startedUtc: string, uptimeSeconds: number, database: string }>}
   */
  health(signal) { return this.get('/health', { signal }); }

  /**
   * Read the public property configuration (anonymous).
   * @param {AbortSignal} [signal]
   */
  // The property document is cacheable (max-age 300) but admins change it live (access model, palette, AI switch):
  // revalidate with its ETag on every load (a 304 when unchanged) instead of reusing a stale browser copy.
  property(signal) { return this.get('/property', { signal, retryOnUnauthorized: false, cache: 'no-cache' }); }

  /**
   * Revalidate the public property configuration with its `ETag` (`If-None-Match`). Resolves
   * `{ notModified: true, etag }` for `304`, else `{ notModified: false, property, etag }`.
   * @param {string|null} etag the `ETag` of the cached copy, if any
   * @param {AbortSignal} [signal]
   * @returns {Promise<{ notModified: boolean, property?: object, etag: string|null }>}
   * @throws {ApiError}
   */
  async propertyIfChanged(etag, signal) {
    const headers = etag ? { 'If-None-Match': etag } : undefined;
    const result = await this.send('GET', '/property', { headers, signal, responseType: 'jsonWithStatus' });
    if (result.status === 304) return { notModified: true, etag: etag || null };
    return { notModified: false, property: result.data, etag: result.etag };
  }
}

// Phase 2 feed, comments, reactions, media, notifications, moderation, and member search methods.
Object.assign(ApiClient.prototype, engagementMethods);

// Phase 3 forums, threads, polls, follows, promotion, search, and forum admin methods.
Object.assign(ApiClient.prototype, forumMethods);

// Platform console methods (MultiProperty deployments, platform host only).
Object.assign(ApiClient.prototype, platformMethods);

// Profile fields, member directory, administrative email, property settings, and domains.
Object.assign(ApiClient.prototype, propertyAdminMethods);
// Member appearance settings (palette preset).
Object.assign(ApiClient.prototype, appearanceMethods);
// Live updates and app configuration admin methods.
Object.assign(ApiClient.prototype, updatesMethods);
// AI assistants: member and visitor chat (streaming) and the admin AI console (docs/AI_CONTRACT.md).
Object.assign(ApiClient.prototype, aiMethods);
// Phase 4 library, events, and directory (docs/PHASE4_CONTRACT.md section 16).
Object.assign(ApiClient.prototype, libraryMethods);
Object.assign(ApiClient.prototype, eventMethods);
Object.assign(ApiClient.prototype, directoryMethods);
// Phase 5: direct messages, support tickets, ideas board, broadcasts and announcements (docs/PHASE5_CONTRACT.md).
Object.assign(ApiClient.prototype, messagingMethods);
Object.assign(ApiClient.prototype, supportMethods);
Object.assign(ApiClient.prototype, ideasMethods);
Object.assign(ApiClient.prototype, broadcastsMethods);
// Job engine admin (docs/INTEGRATION.md section 31).
Object.assign(ApiClient.prototype, jobsMethods);
Object.assign(ApiClient.prototype, metricsMethods);
Object.assign(ApiClient.prototype, contentReviewMethods);
// Web discovery admin (docs/INTEGRATION.md sections 11.1 and 11.2).

// Required documents (public pages, acceptance, the admin and platform documents console).
Object.assign(ApiClient.prototype, documentsMethods);
// Public landing pages: the member's opt-in profile page and slug checks (docs/PHASE4_CONTRACT.md section 18).
Object.assign(ApiClient.prototype, publicPageMethods);
// `client.messaging`: the direct message methods under their contract names (assistant chats keep
// `enumerateConversations` and friends on the client itself).
Object.defineProperty(ApiClient.prototype, 'messaging', {
  configurable: true,
  get() {
    if (!this._messagingFacade) Object.defineProperty(this, '_messagingFacade', { value: directMessagesApi(this), enumerable: false });
    return this._messagingFacade;
  }
});
