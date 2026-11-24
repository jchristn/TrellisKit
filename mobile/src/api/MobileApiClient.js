import { Platform } from 'react-native';
import { aiMethods, ApiError, appearanceMethods, broadcastsMethods, directMessagesApi, directoryMethods, eventMethods, forumMethods, ideasMethods, libraryMethods, messagingMethods, metricsMethods, parseRetryAfter, publicPageMethods, supportMethods } from '@trelliskit/shared';

/** Value of the X-Client header for every request from the app. */
export const ClientName = 'mobile';

/**
 * Requests that leave out `X-Client-Platform` unless the caller adds it: a new support ticket
 * carries device details only when the member opts in (the diagnostics toggle).
 */
const OPT_IN_PLATFORM_ROUTES = ['POST /tickets'];

/** Refresh this many milliseconds before the access token's stated expiry. */
const EXPIRY_SKEW_MS = 30000;

/**
 * Routes under /auth that require a signed-in member. Every other /auth route is anonymous and a
 * 401 from it means bad credentials, not an expired session, so it never triggers a refresh.
 */
const SIGNED_IN_AUTH_ROUTES = ['/auth/me', '/auth/email/resend'];

/**
 * Whether a 401 from this path should trigger the refresh-and-retry flow.
 * @param {string} path Path relative to /api/v1.
 * @returns {boolean}
 */
export function isRefreshableRoute(path) {
  const clean = path.split('?')[0];
  if (!clean.startsWith('/auth/')) return true;
  return SIGNED_IN_AUTH_ROUTES.includes(clean);
}

/**
 * Fetch-based API client for the mobile app (contract section 1).
 *
 * - Sends `X-Client: mobile`, `X-Client-Version`, and `X-Client-Platform` (`Platform.OS`; the
 *   server records document acceptances as Ios or Android from it) on every request, plus the identification
 *   headers from `identityHeaders` (`X-TK-Property`, `X-TK-App`, `X-Client-Update-Id`; see
 *   src/api/identity.js), read per request.
 * - Keeps the access token in memory and the refresh token in secure storage.
 * - On a 401 from a signed-in route it refreshes once (one shared in-flight refresh for all
 *   concurrent callers) and retries the request once. If the refresh is rejected the session is
 *   cleared and `onSessionExpired` listeners run.
 * - A signed-in route that still answers 401 after a successful refresh, or a 401 while an access
 *   token is held but no refresh token is stored, also ends the session (revoked server side).
 * - Every request times out after `timeoutMs` with `ApiError { status: 0, code: 'Timeout' }`.
 * - A `402 MembershipRequired`, `403 AgeAttestationRequired`, or `403 DocumentAcceptanceRequired`
 *   runs `onAccessRequired` listeners.
 */
export class MobileApiClient {
  /**
   * @param {object} options
   * @param {string} options.baseUrl Absolute origin (scheme, host, and optional port).
   * @param {string} options.appVersion Sent as X-Client-Version.
   * @param {{ getRefreshToken: () => Promise<string|null>, setRefreshToken: (t: string|null) => Promise<void>, clear: () => Promise<void> }} options.tokenStore
   * @param {typeof fetch} [options.fetchImpl]
   * @param {number} [options.timeoutMs]
   * @param {() => number} [options.now] Clock (for tests).
   * @param {Record<string, string>|(() => Record<string, string>)} [options.identityHeaders] Headers added to every request.
   */
  constructor({ baseUrl, appVersion, tokenStore, fetchImpl, timeoutMs = 30000, now = () => Date.now(), identityHeaders = null }) {
    this.baseUrl = (baseUrl || '').replace(/\/+$/, '');
    this.appVersion = appVersion;
    this.tokenStore = tokenStore;
    this.fetchImpl = fetchImpl || ((...args) => globalThis.fetch(...args));
    this.timeoutMs = timeoutMs;
    this.now = now;
    this._identityHeaders = identityHeaders;
    this.accessToken = null;
    this.accessTokenExpiresMs = null;
    this._refreshPromise = null;
    this._expiredListeners = new Set();
    this._sessionListeners = new Set();
    this._accessListeners = new Set();
  }

  /**
   * Run a callback when a request is refused because the member lost access mid-session:
   * `402 MembershipRequired` (the membership ended), `403 AgeAttestationRequired`, or
   * `403 DocumentAcceptanceRequired` (a required document changed). The app reloads Me so its
   * route gates send the member to the paywall, the age step, or the documents step.
   * @param {(error: ApiError) => void} listener
   * @returns {() => void} Unsubscribe.
   */
  onAccessRequired(listener) {
    this._accessListeners.add(listener);
    return () => this._accessListeners.delete(listener);
  }

  _notifyAccess(error) {
    if (!error || !(error.status === 402 || (error.status === 403 && (error.code === 'AgeAttestationRequired' || error.code === 'DocumentAcceptanceRequired')))) return;
    for (const listener of Array.from(this._accessListeners)) {
      try {
        listener(error);
      } catch {
        // One failing listener does not stop the others.
      }
    }
  }

  /**
   * Run a callback with every `503` answer (a `FeatureDisabled` kill switch marks the feature
   * killed in the content manifest at once; docs/LIVE_UPDATES_CONTRACT.md section 13.2).
   * @param {(error: ApiError) => void} listener
   * @returns {() => void} Unsubscribe.
   */
  onFeatureDisabled(listener) {
    if (!this._featureListeners) this._featureListeners = new Set();
    this._featureListeners.add(listener);
    return () => this._featureListeners.delete(listener);
  }

  _notifyFeatureDisabled(error) {
    if (!error || error.status !== 503 || !this._featureListeners) return;
    for (const listener of Array.from(this._featureListeners)) {
      try {
        listener(error);
      } catch {
        // One failing listener does not stop the others.
      }
    }
  }

  /**
   * Run a callback when the session can no longer be refreshed.
   * @param {() => void} listener
   * @returns {() => void} Unsubscribe.
   */
  onSessionExpired(listener) {
    this._expiredListeners.add(listener);
    return () => this._expiredListeners.delete(listener);
  }

  /**
   * Run a callback with every AuthResponse the client stores (sign-in and refresh).
   * @param {(auth: object) => void} listener
   * @returns {() => void} Unsubscribe.
   */
  onSession(listener) {
    this._sessionListeners.add(listener);
    return () => this._sessionListeners.delete(listener);
  }

  /**
   * The identification headers sent on every request (also used for media and other requests
   * the app makes outside this client).
   * @returns {Record<string, string>}
   */
  identityHeaders() {
    const value = typeof this._identityHeaders === 'function' ? this._identityHeaders() : this._identityHeaders;
    return value && typeof value === 'object' ? { ...value } : {};
  }

  /** Absolute URL for a path relative to /api/v1, or for a server-relative path such as an avatar. */
  url(path, query) {
    const normalized = path.startsWith('/') ? path : '/' + path;
    let result = normalized.startsWith('/api/') ? this.baseUrl + normalized : this.baseUrl + '/api/v1' + normalized;
    if (query) {
      const params = new URLSearchParams();
      for (const [key, value] of Object.entries(query)) {
        if (value !== null && value !== undefined && value !== '') params.append(key, String(value));
      }
      const text = params.toString();
      if (text) result += '?' + text;
    }
    return result;
  }

  /**
   * Store an AuthResponse: access token in memory, refresh token in secure storage.
   * @param {{ accessToken: string, accessTokenExpiresUtc?: string, refreshToken?: string|null }} auth
   */
  async setSession(auth) {
    this.accessToken = auth.accessToken || null;
    const expires = auth.accessTokenExpiresUtc ? Date.parse(auth.accessTokenExpiresUtc) : NaN;
    this.accessTokenExpiresMs = Number.isFinite(expires) ? expires : null;
    if (auth.refreshToken) await this.tokenStore.setRefreshToken(auth.refreshToken);
    for (const listener of this._sessionListeners) listener(auth);
  }

  /** Forget the access token and delete the stored refresh token. */
  async clearSession() {
    this.accessToken = null;
    this.accessTokenExpiresMs = null;
    await this.tokenStore.clear();
  }

  /** Whether a refresh token is stored (a session can be restored). */
  async hasStoredSession() {
    return Boolean(await this.tokenStore.getRefreshToken());
  }

  /**
   * Exchange the stored refresh token for a new AuthResponse. Concurrent callers share one
   * in-flight request, because refresh tokens rotate and a second use would revoke the session.
   * @returns {Promise<object>} AuthResponse.
   * @throws {ApiError}
   */
  refresh() {
    if (!this._refreshPromise) {
      this._refreshPromise = this._doRefresh().finally(() => {
        this._refreshPromise = null;
      });
    }
    return this._refreshPromise;
  }

  async _doRefresh() {
    const refreshToken = await this.tokenStore.getRefreshToken();
    if (!refreshToken) throw new ApiError('You are not signed in.', { status: 401, code: 'InvalidRefreshToken' });
    let auth;
    try {
      auth = await this._send('POST', '/auth/refresh', { body: { refreshToken }, withAuth: false });
    } catch (error) {
      if (error.status === 400 || error.status === 401 || error.status === 403) await this._expire(error);
      throw error;
    }
    await this.setSession(auth);
    return auth;
  }

  /** Clear the session and tell listeners it can no longer be used. */
  async _expire(error) {
    await this.clearSession();
    for (const listener of Array.from(this._expiredListeners)) {
      try {
        listener(error);
      } catch {
        // One failing listener does not stop the others.
      }
    }
  }

  /**
   * Send an API request.
   * @param {string} method
   * @param {string} path Path relative to /api/v1.
   * @param {object} [options]
   * @param {unknown} [options.body] JSON body.
   * @param {Blob|ArrayBuffer|Uint8Array} [options.rawBody] Raw body (for example an image).
   * @param {string} [options.contentType] Content-Type for rawBody.
   * @param {Record<string, any>} [options.query]
   * @param {AbortSignal} [options.signal]
   * @returns {Promise<any>}
   * @throws {ApiError}
   */
  async request(method, path, options = {}) {
    try {
      return await this._request(method, path, options);
    } catch (error) {
      this._notifyAccess(error);
      this._notifyFeatureDisabled(error);
      throw error;
    }
  }

  async _request(method, path, options = {}) {
    const refreshable = isRefreshableRoute(path);
    if (refreshable && this.accessToken && this.accessTokenExpiresMs && this.now() >= this.accessTokenExpiresMs - EXPIRY_SKEW_MS) {
      try {
        await this.refresh();
      } catch {
        // Fall through: the request below will 401 and take the normal path.
      }
    }

    try {
      return await this._send(method, path, options);
    } catch (error) {
      if (error.status !== 401 || !refreshable) throw error;
      if (!(await this.hasStoredSession())) {
        if (this.accessToken) await this._expire(error);
        throw error;
      }
      try {
        await this.refresh();
      } catch (refreshError) {
        throw refreshError.status === 0 ? refreshError : error;
      }
      try {
        return await this._send(method, path, options);
      } catch (retryError) {
        if (retryError.status === 401) await this._expire(retryError);
        throw retryError;
      }
    }
  }

  async _send(method, path, { body, rawBody, contentType, query, signal, headers: extraHeaders, responseType, withAuth = true } = {}) {
    const headers = {
      ...this.identityHeaders(),
      Accept: 'application/json',
      'X-Client': ClientName,
      'X-Client-Version': this.appVersion,
      ...(OPT_IN_PLATFORM_ROUTES.includes(method + ' ' + path.split('?')[0]) ? {} : { 'X-Client-Platform': Platform.OS }),
      ...(extraHeaders || {})
    };
    let payload;
    if (rawBody !== undefined) {
      headers['Content-Type'] = contentType || 'application/octet-stream';
      payload = rawBody;
    } else if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
      payload = JSON.stringify(body);
    }
    if (withAuth && this.accessToken) headers.Authorization = 'Bearer ' + this.accessToken;

    const controller = new AbortController();
    let timedOut = false;
    const timer = setTimeout(() => {
      timedOut = true;
      controller.abort();
    }, this.timeoutMs);
    if (signal) {
      if (signal.aborted) controller.abort();
      else signal.addEventListener('abort', () => controller.abort(), { once: true });
    }

    let response;
    try {
      response = await this.fetchImpl(this.url(path, query), { method, headers, body: payload, signal: controller.signal });
    } catch (err) {
      if (timedOut) throw new ApiError('The request timed out.', { status: 0, code: 'Timeout' });
      const aborted = err && err.name === 'AbortError';
      throw new ApiError(aborted ? 'The request was cancelled.' : 'Unable to reach the server.', { status: 0, code: aborted ? 'Cancelled' : 'NetworkError' });
    } finally {
      clearTimeout(timer);
    }

    const etagOf = () => (response.headers && typeof response.headers.get === 'function' ? response.headers.get('etag') : null) || null;
    // Conditional reads (`propertyIfChanged`) treat 304 as success.
    if (responseType === 'jsonWithStatus' && response.status === 304) return { status: 304, data: null, etag: etagOf() };
    if (response.status === 204) return null;
    const type = (response.headers && response.headers.get && response.headers.get('content-type')) || '';
    const parsed = type.includes('json') ? await response.json().catch(() => null) : await response.text().catch(() => null);

    if (!response.ok) {
      const isObject = parsed && typeof parsed === 'object';
      const message = (isObject && parsed.message) || 'Request failed with status ' + response.status + '.';
      const retryAfterSeconds = parseRetryAfter(response.headers && typeof response.headers.get === 'function' ? response.headers.get('retry-after') : null);
      throw new ApiError(message, { status: response.status, code: (isObject && parsed.error) || null, body: parsed, retryAfterSeconds });
    }
    if (responseType === 'jsonWithStatus') return { status: response.status, data: parsed === '' ? null : parsed, etag: etagOf() };
    return parsed === '' ? null : parsed;
  }

  /**
   * Headers for a streamed request made outside `request` (assistant replies, docs/AI_CONTRACT.md
   * section 10): identification, client, and the bearer token, refreshed first when it is about to
   * expire.
   * @returns {Promise<Record<string, string>>}
   */
  async baseHeaders() {
    if (this.accessToken && this.accessTokenExpiresMs && this.now() >= this.accessTokenExpiresMs - EXPIRY_SKEW_MS) {
      try {
        await this.refresh();
      } catch {
        // The stream answers 401 and onUnauthorized handles it.
      }
    }
    const headers = { ...this.identityHeaders(), 'X-Client': ClientName, 'X-Client-Version': this.appVersion, 'X-Client-Platform': Platform.OS };
    if (this.accessToken) headers.Authorization = 'Bearer ' + this.accessToken;
    return headers;
  }

  /**
   * Called by a streamed request that answered 401: refresh once and report whether to retry.
   * @returns {Promise<boolean>}
   */
  async onUnauthorized() {
    if (!(await this.hasStoredSession())) return false;
    try {
      await this.refresh();
      return true;
    } catch {
      return false;
    }
  }

  get(path, options) {
    return this.request('GET', path, options);
  }

  post(path, body, options) {
    return this.request('POST', path, { ...options, body });
  }

  put(path, body, options) {
    return this.request('PUT', path, { ...options, body });
  }

  delete(path, options) {
    return this.request('DELETE', path, options);
  }

  /**
   * Revalidate the public property configuration with its `ETag` (`If-None-Match`), with the
   * same identification headers as every request. Resolves `{ notModified: true, etag }` for
   * `304`, else `{ notModified: false, property, etag }` (the shared ApiClient contract).
   * @param {string|null} etag The `ETag` of the cached copy, if any.
   * @param {AbortSignal} [signal]
   * @returns {Promise<{ notModified: boolean, property?: object, etag: string|null }>}
   * @throws {ApiError}
   */
  async propertyIfChanged(etag, signal) {
    const headers = etag ? { 'If-None-Match': etag } : undefined;
    const result = await this.request('GET', '/property', { headers, signal, responseType: 'jsonWithStatus' });
    if (result && result.status === 304) return { notModified: true, etag: etag || null };
    return { notModified: false, property: result ? result.data : null, etag: (result && result.etag) || null };
  }

  /**
   * POST /members/directory/enumerate: `EnumerationResult<UserSummary>` of active members with
   * search, ordering, labels, tags, and `filters.profile.<key>.<operator>`.
   * @param {object} query EnumerationQuery.
   * @param {AbortSignal} [signal]
   */
  enumerateMemberDirectory(query, signal) {
    return this.post('/members/directory/enumerate', query || {}, signal ? { signal } : undefined);
  }

  /** GET /api/v1/health */
  health(signal) {
    return this.get('/health', { signal });
  }

  /** GET /notifications/unread-count (used by the realtime polling fallback). */
  getUnreadCount(signal) {
    return this.get('/notifications/unread-count', signal ? { signal } : undefined);
  }

  /**
   * The direct message methods under their contract names (docs/PHASE5_CONTRACT.md section 19):
   * `enumerateConversations`, `getConversation`, and the rest, bound to this client. The plain
   * names on the client itself belong to the assistant chats.
   */
  get messaging() {
    if (!this._messagingFacade) this._messagingFacade = directMessagesApi(this);
    return this._messagingFacade;
  }
}

// Phase 3 forum, thread, poll, follow, promotion, and search methods from @trelliskit/shared
// (docs/PHASE3_CONTRACT.md section 19). They use only get, post, put, delete, and request.
Object.assign(MobileApiClient.prototype, forumMethods, aiMethods, libraryMethods, eventMethods, directoryMethods);

// Phase 5 direct messages, support tickets, ideas board, and announcements
// (docs/PHASE5_CONTRACT.md section 19). The assistant chat names stay on aiMethods; the direct
// message twins are `enumerateDirectConversations` and friends, and `client.messaging` is the facade.
Object.assign(MobileApiClient.prototype, messagingMethods, supportMethods, ideasMethods, broadcastsMethods, appearanceMethods);

// Public landing pages: the member's opt-in profile page and slug checks (docs/PHASE4_CONTRACT.md section 18).
Object.assign(MobileApiClient.prototype, publicPageMethods, metricsMethods);
