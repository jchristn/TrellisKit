/**
 * The realtime client shared by the website and the mobile app (docs/PHASE5_CONTRACT.md
 * sections 4 and 5, as-built note A). It is platform neutral: the WebSocket constructor, the
 * timer source, and the random source are injected, so tests drive it deterministically.
 *
 * What it does:
 * - Connects with no subprotocol (as-built note A) and sends `hello` as the first frame.
 * - Application heartbeat: a `ping` every `welcome.heartbeatSeconds` regardless of other
 *   traffic; no inbound frame for two intervals closes the socket and reconnects.
 * - Token refresh on `auth.expiring` (`auth.refresh` frame) and on close `4401` (once).
 * - Every close code of contract section 4.10, exponential backoff with full jitter (1 s
 *   doubling to a 30 s cap, reset after 60 s connected), and immediate reconnects on network
 *   change and foreground.
 * - Gap detection per conversation (highest contiguous `seq`) with a `fillGap` hook, resync over
 *   `POST /conversations/changes/enumerate` with a persisted sync token, and the polling fallback
 *   of section 5.1.
 * - Throttled `typing`, batched `ack`, monotonic `read`.
 *
 * The socket is at most once and best effort; REST stays the source of truth. Listeners get
 * `(data, frame)`; a `'*'` listener gets `(type, data, frame)`.
 */

import { normalizeEnumerationResult } from './enumeration.js';

/** Protocol version sent in `hello` (as-built note A). */
export const REALTIME_PROTOCOL = 'tk.v1';

/** Close codes (contract section 4.10). */
export const CloseCode = Object.freeze({
  Normal: 1000,
  UnsupportedData: 1003,
  TooLarge: 1009,
  ServerError: 1011,
  ServiceRestart: 1012,
  TryAgainLater: 1013,
  HeartbeatTimeout: 4000,
  ProtocolViolation: 4400,
  Unauthorized: 4401,
  MembershipRequired: 4402,
  PropertyNotFound: 4404,
  HelloTimeout: 4408,
  Replaced: 4409,
  RateLimited: 4429,
  FeatureDisabled: 4503
});

/** Server to client frame types (contract section 4.5). */
export const ServerFrameTypes = Object.freeze([
  'welcome', 'pong', 'message.created', 'message.updated', 'message.deleted', 'conversation.created', 'conversation.updated',
  'receipt.delivered', 'receipt.read', 'typing', 'notification.created', 'unread.counts', 'ticket.updated', 'access.changed',
  'feature.disabled', 'auth.expiring', 'resync.required', 'error', 'shutdown'
]);

/** Client lifecycle events emitted in addition to the server frame types. */
export const RealtimeEvent = Object.freeze({
  Connected: 'connected',
  Disconnected: 'disconnected',
  Fallback: 'fallback',
  SignedOut: 'signedOut',
  Sync: 'sync',
  Error: 'clientError'
});

/** Connection status values of `RealtimeClient.status`. */
export const RealtimeStatus = Object.freeze({ Idle: 'Idle', Connecting: 'Connecting', Open: 'Open', Waiting: 'Waiting', Stopped: 'Stopped' });

/** App states of the `state` frame. */
export const AppState = Object.freeze({ Foreground: 'Foreground', Background: 'Background' });

/** Timing defaults (contract sections 4.10, 4.4, 5.1). */
export const REALTIME_DEFAULTS = Object.freeze({
  heartbeatSeconds: 25,
  backoffBaseMs: 1000,
  backoffCapMs: 30000,
  stableAfterMs: 60000,
  featureDisabledRetryMs: 300000,
  failedConnectsBeforeFallback: 3,
  typingMinIntervalMs: 3000,
  ackIntervalMs: 1000,
  pollSeconds: 5,
  pollSlowMs: 30000,
  pollIdleAfterMs: 120000,
  unreadPollMs: 60000,
  listPollMs: 30000,
  maxFillPages: 50,
  maxSyncPages: 100
});

const CONVERSATION_FRAMES = new Set(['message.created', 'message.updated', 'message.deleted']);
const OPEN = 1;

/**
 * Full-jitter exponential backoff: a random delay in [0, min(cap, base * 2^attempt)).
 * @param {number} attempt 0 for the first retry
 * @param {() => number} [random] returns [0, 1)
 * @param {{ baseMs?: number, capMs?: number }} [options]
 * @returns {number} milliseconds
 */
export function backoffDelay(attempt, random = Math.random, { baseMs = REALTIME_DEFAULTS.backoffBaseMs, capMs = REALTIME_DEFAULTS.backoffCapMs } = {}) {
  const exp = Math.min(capMs, baseMs * Math.pow(2, Math.max(0, Math.min(30, attempt))));
  const r = Math.min(0.999999, Math.max(0, Number(random()) || 0));
  return Math.floor(r * exp);
}

/** An in-memory sync token store (`{ get, set }`); apps persist the token with their own store. */
export function createMemorySyncTokenStore(initial = null) {
  let token = initial || null;
  return { get: () => token, set: (value) => { token = value || null; } };
}

/**
 * The default gap filler: `enumerateMessages` with `filters.afterSeq` and `SeqAscending`, paging
 * until the end. Resolves the messages in `seq` order.
 * @param {{ enumerateMessages: Function }} api
 * @param {{ maxPages?: number, pageSize?: number }} [options]
 * @returns {(conversationId: string, afterSeq: number) => Promise<object[]>}
 */
export function createGapFiller(api, { maxPages = REALTIME_DEFAULTS.maxFillPages, pageSize = 100 } = {}) {
  return async function fillGap(conversationId, afterSeq) {
    const out = [];
    let token = null;
    for (let page = 0; page < maxPages; page += 1) {
      const query = { ordering: 'SeqAscending', maxResults: pageSize, filters: { afterSeq: Number(afterSeq) || 0 } };
      if (token) query.continuationToken = token;
      const result = normalizeEnumerationResult(await api.enumerateMessages(conversationId, query));
      out.push(...result.objects);
      if (result.endOfResults || !result.continuationToken || result.continuationToken === token) break;
      token = result.continuationToken;
    }
    return out.sort((a, b) => (a.seq || 0) - (b.seq || 0));
  };
}

function seqOf(type, data) {
  if (!data) return null;
  if (type === 'message.deleted') return typeof data.seq === 'number' ? data.seq : null;
  return data.message && typeof data.message.seq === 'number' ? data.message.seq : null;
}

export class RealtimeClient {
  /**
   * @param {object} options
   * @param {string|(() => string|Promise<string>)} options.url `wss://<api domain>/api/v1/ws`
   * @param {Function|null} [options.WebSocketImpl] WebSocket constructor (default `globalThis.WebSocket`); none means polling only
   * @param {Record<string,string>} [options.headers] Upgrade headers (React Native only, for example `X-TK-Property`, `X-TK-App`)
   * @param {() => (string|null|Promise<string|null>)} options.getAccessToken Current access token
   * @param {() => (string|null|Promise<string|null>)} [options.refreshAccessToken] Refresh the session; resolve the new token, or null when signed out
   * @param {string} [options.propertyId]
   * @param {string} [options.client] `Web` or `Mobile`
   * @param {string} [options.clientVersion]
   * @param {string|null} [options.installId]
   * @param {string[]} [options.capabilities] Requested capabilities (default messaging, notifications, tickets)
   * @param {string} [options.appState] `Foreground` (default) or `Background`
   * @param {object} [options.api] ApiClient (default gap fill, sync, polling, REST read fallback)
   * @param {(conversationId: string, afterSeq: number) => Promise<object[]>} [options.fillGap]
   * @param {{ get: () => any, set: (token: string|null) => any }} [options.syncTokenStore]
   * @param {{ setTimeout: Function, clearTimeout: Function, now: () => number }} [options.timers]
   * @param {() => number} [options.random]
   * @param {number} [options.typingMinIntervalMs] 3000
   * @param {number} [options.pollSeconds] `messaging.pollSeconds` (5)
   */
  constructor(options = {}) {
    const o = options || {};
    this._url = o.url;
    this._WebSocket = o.WebSocketImpl === undefined ? (typeof globalThis.WebSocket === 'function' ? globalThis.WebSocket : null) : o.WebSocketImpl;
    this._headers = o.headers || null;
    this._getAccessToken = o.getAccessToken || (() => null);
    this._refreshAccessToken = o.refreshAccessToken || null;
    this._propertyId = o.propertyId || null;
    this._clientName = o.client || 'Web';
    this._clientVersion = o.clientVersion || null;
    this._installId = o.installId || null;
    this._requestedCapabilities = Array.isArray(o.capabilities) ? o.capabilities.slice() : ['messaging', 'notifications', 'tickets'];
    this._appState = o.appState === AppState.Background ? AppState.Background : AppState.Foreground;
    this._activeConversationId = null;
    this._api = o.api || null;
    this._fillGap = o.fillGap || (this._api ? createGapFiller(this._api) : null);
    this._tokenStore = o.syncTokenStore || createMemorySyncTokenStore();
    const t = o.timers || {};
    this._setTimeout = t.setTimeout || ((fn, ms) => globalThis.setTimeout(fn, ms));
    this._clearTimeout = t.clearTimeout || ((id) => globalThis.clearTimeout(id));
    this._now = t.now || (() => Date.now());
    this._random = o.random || Math.random;
    this._typingMinIntervalMs = Number.isFinite(o.typingMinIntervalMs) ? o.typingMinIntervalMs : REALTIME_DEFAULTS.typingMinIntervalMs;
    this._pollMs = (Number.isFinite(o.pollSeconds) ? o.pollSeconds : REALTIME_DEFAULTS.pollSeconds) * 1000;

    this._listeners = new Map();
    this._status = RealtimeStatus.Idle;
    this._wanted = false;
    this._blocked = false;
    this._waitForeground = false;
    this._socket = null;
    this._welcomed = false;
    this._frameCounter = 0;
    this._attempt = 0;
    this._failedConnects = 0;
    this._refreshTried = false;
    this._retryAfterMs = null;
    this._reconnectAfterMs = null;
    this._featureDisabledUntil = 0;
    this._reconnectTimer = null;
    this._heartbeatTimer = null;
    this._stableTimer = null;
    this._lastInbound = 0;
    this._welcome = null;
    this._capabilities = [];
    this._heartbeatSeconds = REALTIME_DEFAULTS.heartbeatSeconds;

    this._seqs = new Map();
    this._queues = new Map();
    this._typingStarts = new Map();
    this._typingActive = new Set();
    this._acks = new Map();
    this._reads = new Map();
    this._syncing = null;
    this._syncAgain = false;

    this._polling = false;
    this._pollTimers = { unread: null, active: null, list: null };
    this._activeIdleSince = 0;
  }

  // ---- State ----

  /** `Idle`, `Connecting`, `Open` (welcomed), `Waiting` (reconnect pending or blocked), `Stopped` (signed out). */
  get status() { return this._status; }
  /** Whether the polling fallback is running. */
  get isPolling() { return this._polling; }
  /** Whether the socket is open and welcomed. */
  get isConnected() { return this._welcomed && Boolean(this._socket); }
  /** Capabilities granted by the server (`welcome`, then `access.changed`). */
  get capabilities() { return this._capabilities.slice(); }
  /** `welcome.heartbeatSeconds`. */
  get heartbeatSeconds() { return this._heartbeatSeconds; }
  /** The last `welcome` data, or null. */
  get welcome() { return this._welcome; }
  get appState() { return this._appState; }
  get activeConversationId() { return this._activeConversationId; }

  // ---- Events ----

  /**
   * Listen to a server frame type (contract 4.5) or a lifecycle event (`connected`,
   * `disconnected`, `fallback`, `signedOut`, `sync`, `clientError`); `'*'` gets every event as
   * `(type, data, frame)`. Returns an unsubscribe function.
   */
  on(type, listener) {
    if (typeof listener !== 'function') return () => {};
    if (!this._listeners.has(type)) this._listeners.set(type, new Set());
    this._listeners.get(type).add(listener);
    return () => this.off(type, listener);
  }

  /** Remove a listener. */
  off(type, listener) {
    const set = this._listeners.get(type);
    if (set) set.delete(listener);
  }

  _emit(type, data, frame) {
    const set = this._listeners.get(type);
    if (set) for (const fn of Array.from(set)) {
      try { fn(data, frame); } catch { /* listener errors never break the client */ }
    }
    const all = this._listeners.get('*');
    if (all) for (const fn of Array.from(all)) {
      try { fn(type, data, frame); } catch { /* ignore */ }
    }
  }

  // ---- Lifecycle ----

  /** Start (or resume) the connection. Clears a `4402`, `4404`, or `4409` hold. */
  connect() {
    this._wanted = true;
    this._blocked = false;
    this._waitForeground = false;
    if (this._status === RealtimeStatus.Stopped) this._refreshTried = false;
    if (this._socket) return;
    this._clearReconnect();
    this._open();
  }

  /** Close the socket (`1000`), stop polling, and stay disconnected until `connect`. */
  disconnect() {
    this._wanted = false;
    this._clearReconnect();
    this._stopPolling(false);
    const socket = this._socket;
    const wasOpen = Boolean(socket);
    this._teardown();
    if (socket) {
      detach(socket);
      try { socket.close(CloseCode.Normal, 'client'); } catch { /* ignore */ }
    }
    this._status = RealtimeStatus.Idle;
    if (wasOpen) this._emit(RealtimeEvent.Disconnected, { code: CloseCode.Normal, reason: 'client', willReconnect: false, delayMs: null, byClient: true });
  }

  /** Reconnect now after a network change (or probe an open socket with a `ping`). */
  networkChanged() {
    if (!this._wanted || this._blocked) return;
    if (this._socket) {
      if (this._welcomed) this._send('ping', {});
      return;
    }
    if (this._now() < this._featureDisabledUntil) return;
    this._clearReconnect();
    this._open();
  }

  /**
   * Report the app state and the open conversation (the `state` frame drives the push decision).
   * Foreground reconnects immediately when the socket is down.
   * @param {'Foreground'|'Background'} state
   * @param {string|null} [activeConversationId] omitted keeps the current one
   */
  setAppState(state, activeConversationId) {
    const next = state === AppState.Background ? AppState.Background : AppState.Foreground;
    const changedConversation = activeConversationId !== undefined && activeConversationId !== this._activeConversationId;
    this._appState = next;
    if (activeConversationId !== undefined) this._activeConversationId = activeConversationId || null;
    if (changedConversation) this._resetActivePoll();
    if (this._welcomed) this._sendState();
    if (next === AppState.Foreground && this._wanted && !this._blocked && !this._socket && this._now() >= this._featureDisabledUntil) {
      this._waitForeground = false;
      this._clearReconnect();
      this._open();
    }
  }

  /** Set the open conversation without changing the app state. */
  setActiveConversation(conversationId) { this.setAppState(this._appState, conversationId || null); }

  /** Replace the requested capabilities (used on the next `hello`). */
  setCapabilities(capabilities) { this._requestedCapabilities = Array.isArray(capabilities) ? capabilities.slice() : []; }

  // ---- Outbound frames ----

  /**
   * Send `typing`. `Start` is throttled to one per conversation per `typingMinIntervalMs`;
   * `Stop` is sent only after a `Start`. Returns whether a frame was sent.
   */
  typing(conversationId, state = 'Start') {
    if (!conversationId) return false;
    if (state === 'Stop') {
      if (!this._typingActive.has(conversationId)) return false;
      this._typingActive.delete(conversationId);
      return this._send('typing', { conversationId, state: 'Stop' });
    }
    const now = this._now();
    const last = this._typingStarts.get(conversationId);
    if (last !== undefined && now - last < this._typingMinIntervalMs) return false;
    if (!this._send('typing', { conversationId, state: 'Start' })) return false;
    this._typingStarts.set(conversationId, now);
    this._typingActive.add(conversationId);
    return true;
  }

  /** Call when the member sent a message: stops the typing indicator. */
  messageSent(conversationId) { return this.typing(conversationId, 'Stop'); }

  /** Acknowledge delivery up to `seq` (monotonic; at most one frame per conversation per second). */
  ack(conversationId, seq) {
    const value = Number(seq);
    if (!conversationId || !Number.isFinite(value)) return;
    const s = this._acks.get(conversationId) || { sent: -1, pending: -1, timer: null };
    this._acks.set(conversationId, s);
    if (value <= s.sent || value <= s.pending) return;
    s.pending = value;
    if (!s.timer) this._flushAck(conversationId);
  }

  _flushAck(conversationId) {
    const s = this._acks.get(conversationId);
    if (!s) return;
    s.timer = null;
    if (s.pending <= s.sent) return;
    if (!this._send('ack', { conversationId, seq: s.pending })) return;
    s.sent = s.pending;
    s.timer = this._setTimeout(() => this._flushAck(conversationId), REALTIME_DEFAULTS.ackIntervalMs);
  }

  /**
   * Mark read up to `seq` (monotonic). Sent as a `read` frame when connected, else through
   * `POST /conversations/{id}/read` when an `api` was given. Returns whether anything was sent.
   */
  read(conversationId, seq) {
    const value = Number(seq);
    if (!conversationId || !Number.isFinite(value)) return false;
    const last = this._reads.get(conversationId);
    if (last !== undefined && value <= last) return false;
    this._reads.set(conversationId, value);
    if (this._send('read', { conversationId, seq: value })) return true;
    if (this._api && typeof this._api.markConversationRead === 'function') {
      Promise.resolve().then(() => this._api.markConversationRead(conversationId, value)).catch(() => {});
      return true;
    }
    return false;
  }

  _send(type, data) {
    const socket = this._socket;
    if (!socket || socket.readyState !== OPEN) return false;
    if (!this._welcomed && type !== 'hello') return false;
    this._frameCounter += 1;
    try {
      socket.send(JSON.stringify({ type, id: 'c' + this._frameCounter, data }));
      return true;
    } catch {
      return false;
    }
  }

  _sendState() {
    const data = { appState: this._appState };
    if (this._activeConversationId) data.activeConversationId = this._activeConversationId;
    return this._send('state', data);
  }

  // ---- Sequence tracking ----

  /** Set (raise) the known contiguous `seq` of a conversation, for example after a REST page. */
  trackConversation(conversationId, seq) {
    const value = Number(seq);
    if (!conversationId || !Number.isFinite(value)) return;
    const current = this._seqs.get(conversationId);
    if (current === undefined || value > current) this._seqs.set(conversationId, value);
  }

  /** Stop tracking a conversation. */
  untrackConversation(conversationId) { this._seqs.delete(conversationId); }

  /** The highest contiguous `seq` known for a conversation, or null. */
  lastSeqOf(conversationId) {
    const v = this._seqs.get(conversationId);
    return v === undefined ? null : v;
  }

  // ---- Connection internals ----

  async _open() {
    if (!this._wanted || this._socket || this._opening) return;
    if (!this._WebSocket) {
      this._status = RealtimeStatus.Waiting;
      this._startPolling('unavailable');
      return;
    }
    this._opening = true;
    this._status = RealtimeStatus.Connecting;
    let url;
    try {
      url = typeof this._url === 'function' ? await this._url() : this._url;
    } catch {
      url = null;
    }
    this._opening = false;
    if (!this._wanted || this._socket) return;
    let socket;
    try {
      if (!url) throw new Error('No realtime URL.');
      // No subprotocol (as-built note A); React Native accepts upgrade headers as the third argument.
      socket = this._headers ? new this._WebSocket(url, undefined, { headers: { ...this._headers } }) : new this._WebSocket(url);
    } catch {
      this._afterClose(null, CloseCode.ServerError, 'connect failed', false);
      return;
    }
    this._socket = socket;
    this._welcomed = false;
    socket.onopen = () => { this._onOpen(socket); };
    socket.onmessage = (event) => { this._onMessage(socket, event); };
    socket.onerror = () => {};
    socket.onclose = (event) => { this._onClose(socket, event); };
  }

  async _onOpen(socket) {
    if (socket !== this._socket) return;
    this._lastInbound = this._now();
    let accessToken = null;
    let syncToken = null;
    try { accessToken = await this._getAccessToken(); } catch { accessToken = null; }
    try { syncToken = await this._tokenStore.get(); } catch { syncToken = null; }
    if (socket !== this._socket || socket.readyState !== OPEN) return;
    this._send('hello', {
      accessToken: accessToken || null,
      propertyId: this._propertyId,
      client: this._clientName,
      clientVersion: this._clientVersion,
      installId: this._installId,
      appState: this._appState,
      resume: { syncToken: syncToken || null },
      capabilities: this._requestedCapabilities.slice(),
      protocol: REALTIME_PROTOCOL
    });
  }

  _onMessage(socket, event) {
    if (socket !== this._socket) return;
    this._lastInbound = this._now();
    let frame;
    try {
      frame = JSON.parse(typeof event === 'string' ? event : event && event.data);
    } catch {
      return;
    }
    if (!frame || typeof frame !== 'object' || typeof frame.type !== 'string') return;
    const type = frame.type;
    const data = frame.data && typeof frame.data === 'object' ? frame.data : {};
    switch (type) {
      case 'welcome': this._onWelcome(data, frame); return;
      case 'auth.expiring': this._emit(type, data, frame); this._refreshOnSocket(); return;
      case 'access.changed':
        if (Array.isArray(data.capabilities)) this._capabilities = data.capabilities.slice();
        this._emit(type, data, frame);
        return;
      case 'resync.required': this._emit(type, data, frame); this.resync(); return;
      case 'error':
        if (Number.isFinite(data.retryAfterMs)) this._retryAfterMs = data.retryAfterMs;
        this._emit(type, data, frame);
        return;
      case 'shutdown':
        if (Number.isFinite(data.reconnectAfterMs)) this._reconnectAfterMs = data.reconnectAfterMs;
        this._emit(type, data, frame);
        return;
      default:
        if (CONVERSATION_FRAMES.has(type) && data.conversationId) {
          this._enqueue(data.conversationId, () => this._processConversationEvent(type, data, frame));
          return;
        }
        // Unknown server types are ignored (forward compatibility, contract 4.3).
        if (ServerFrameTypes.includes(type)) this._emit(type, data, frame);
    }
  }

  _onWelcome(data, frame) {
    this._welcomed = true;
    this._welcome = data;
    this._status = RealtimeStatus.Open;
    this._failedConnects = 0;
    this._refreshTried = false;
    this._retryAfterMs = null;
    this._reconnectAfterMs = null;
    this._heartbeatSeconds = Number(data.heartbeatSeconds) > 0 ? Number(data.heartbeatSeconds) : REALTIME_DEFAULTS.heartbeatSeconds;
    this._capabilities = Array.isArray(data.capabilities) ? data.capabilities.slice() : [];
    this._scheduleHeartbeat();
    this._clearTimer('_stableTimer');
    this._stableTimer = this._setTimeout(() => { this._stableTimer = null; this._attempt = 0; }, REALTIME_DEFAULTS.stableAfterMs);
    if (this._polling) this._stopPolling(true);
    if (this._activeConversationId || this._appState === AppState.Background) this._sendState();
    for (const id of this._acks.keys()) {
      const s = this._acks.get(id);
      if (s && !s.timer && s.pending > s.sent) this._flushAck(id);
    }
    this._emit('welcome', data, frame);
    this._emit(RealtimeEvent.Connected, data, frame);
    if (!data.resume || data.resume.syncRequired !== false) this.resync();
  }

  async _refreshOnSocket() {
    let token = null;
    try {
      token = this._refreshAccessToken ? await this._refreshAccessToken() : await this._getAccessToken();
    } catch {
      token = null;
    }
    if (token) this._send('auth.refresh', { accessToken: token });
  }

  _scheduleHeartbeat() {
    this._clearTimer('_heartbeatTimer');
    const interval = this._heartbeatSeconds * 1000;
    this._heartbeatTimer = this._setTimeout(() => {
      this._heartbeatTimer = null;
      if (!this._socket || !this._welcomed) return;
      if (this._now() - this._lastInbound >= 2 * interval) {
        this._dropDead();
        return;
      }
      this._send('ping', {});
      this._scheduleHeartbeat();
    }, interval);
  }

  _dropDead() {
    const socket = this._socket;
    if (!socket) return;
    detach(socket);
    try { socket.close(CloseCode.HeartbeatTimeout, 'heartbeat timeout'); } catch { /* ignore */ }
    this._onClose(socket, { code: CloseCode.HeartbeatTimeout, reason: 'heartbeat timeout' });
  }

  _onClose(socket, event) {
    if (socket !== this._socket) return;
    const code = event && Number.isFinite(event.code) ? event.code : 1006;
    const reason = (event && event.reason) || '';
    const wasWelcomed = this._welcomed;
    this._teardown();
    this._afterClose(socket, code, reason, wasWelcomed);
  }

  _teardown() {
    this._clearTimer('_heartbeatTimer');
    this._clearTimer('_stableTimer');
    this._socket = null;
    this._welcomed = false;
    this._typingActive.clear();
  }

  _afterClose(socket, code, reason, wasWelcomed) {
    const emit = (willReconnect, delayMs) => this._emit(RealtimeEvent.Disconnected, { code, reason, willReconnect, delayMs });
    if (!this._wanted) {
      this._status = RealtimeStatus.Idle;
      emit(false, null);
      return;
    }
    switch (code) {
      case CloseCode.Unauthorized: {
        if (this._refreshTried) {
          this._signOut(code);
          emit(false, null);
          return;
        }
        this._refreshTried = true;
        this._status = RealtimeStatus.Waiting;
        emit(true, 0);
        this._refreshThenReconnect(code);
        return;
      }
      case CloseCode.MembershipRequired:
      case CloseCode.PropertyNotFound:
        this._blocked = true;
        this._status = RealtimeStatus.Waiting;
        emit(false, null);
        return;
      case CloseCode.Replaced:
        if (this._appState !== AppState.Foreground) {
          this._waitForeground = true;
          this._status = RealtimeStatus.Waiting;
          emit(false, null);
          return;
        }
        this._reconnectIn(this._nextBackoff(), emit);
        return;
      case CloseCode.RateLimited: {
        const delay = Number.isFinite(this._retryAfterMs) ? this._retryAfterMs : this._nextBackoff();
        this._retryAfterMs = null;
        this._reconnectIn(delay, emit);
        return;
      }
      case CloseCode.FeatureDisabled:
        this._featureDisabledUntil = this._now() + REALTIME_DEFAULTS.featureDisabledRetryMs;
        this._startPolling('featureDisabled');
        this._reconnectIn(REALTIME_DEFAULTS.featureDisabledRetryMs, emit);
        return;
      case CloseCode.ServiceRestart: {
        const delay = Number.isFinite(this._reconnectAfterMs) ? this._reconnectAfterMs : this._nextBackoff();
        this._reconnectAfterMs = null;
        this._reconnectIn(delay, emit);
        return;
      }
      default: {
        if (!wasWelcomed) {
          this._failedConnects += 1;
          if (this._failedConnects >= REALTIME_DEFAULTS.failedConnectsBeforeFallback) this._startPolling('connectFailed');
        }
        this._reconnectIn(this._nextBackoff(), emit);
      }
    }
  }

  async _refreshThenReconnect(code) {
    let token = null;
    try {
      token = this._refreshAccessToken ? await this._refreshAccessToken() : null;
    } catch {
      token = null;
    }
    if (!this._wanted) return;
    if (!token) {
      this._signOut(code);
      return;
    }
    this._clearReconnect();
    this._open();
  }

  _signOut(code) {
    this._wanted = false;
    this._status = RealtimeStatus.Stopped;
    this._clearReconnect();
    this._stopPolling(false);
    this._emit(RealtimeEvent.SignedOut, { code });
  }

  _nextBackoff() {
    const delay = backoffDelay(this._attempt, this._random);
    this._attempt += 1;
    return delay;
  }

  _reconnectIn(delayMs, emit) {
    this._status = RealtimeStatus.Waiting;
    this._clearReconnect();
    emit(true, delayMs);
    this._reconnectTimer = this._setTimeout(() => {
      this._reconnectTimer = null;
      this._open();
    }, Math.max(0, delayMs));
  }

  _clearReconnect() { this._clearTimer('_reconnectTimer'); }

  _clearTimer(name) {
    if (this[name] !== null && this[name] !== undefined) {
      this._clearTimeout(this[name]);
      this[name] = null;
    }
  }

  // ---- Conversation events, gaps, and sync ----

  _enqueue(conversationId, task) {
    const previous = this._queues.get(conversationId) || Promise.resolve();
    const next = previous.then(task).catch((error) => this._emit(RealtimeEvent.Error, { error, conversationId }));
    this._queues.set(conversationId, next);
    next.then(() => { if (this._queues.get(conversationId) === next) this._queues.delete(conversationId); });
    return next;
  }

  async _processConversationEvent(type, data, frame) {
    const id = data.conversationId;
    const seq = seqOf(type, data);
    const last = this._seqs.get(id);
    if (seq !== null && last !== undefined && seq > last + 1) await this._fill(id, last);
    this._emit(type, data, frame);
    if (type === 'message.created' && seq !== null) {
      const current = this._seqs.get(id);
      if (current === undefined || seq > current) this._seqs.set(id, seq);
    }
  }

  /** Fill after `afterSeq`; emits each message as `message.created` with `gapFill: true`. Resolves the count of new messages. */
  async _fill(conversationId, afterSeq) {
    if (!this._fillGap) return 0;
    let messages;
    try {
      messages = await this._fillGap(conversationId, afterSeq);
    } catch (error) {
      this._emit(RealtimeEvent.Error, { error, conversationId, during: 'fillGap' });
      return 0;
    }
    const list = (Array.isArray(messages) ? messages : []).slice().sort((a, b) => (a.seq || 0) - (b.seq || 0));
    let count = 0;
    let max = afterSeq;
    for (const message of list) {
      if (typeof message.seq === 'number' && message.seq > afterSeq) count += 1;
      if (typeof message.seq === 'number' && message.seq > max) max = message.seq;
      this._emit('message.created', { conversationId, message, gapFill: true }, null);
    }
    const current = this._seqs.get(conversationId);
    if (current === undefined || max > current) this._seqs.set(conversationId, max);
    // New messages in the open conversation keep the polling fallback at its fast cadence.
    if (count > 0 && conversationId === this._activeConversationId) this._resetActivePoll();
    return count;
  }

  /**
   * Run the REST sync (contract 5.2) from the stored token to the end, store the final token,
   * emit `sync` with `{ changes, syncToken, reset }`, then gap-fill every tracked conversation
   * that changed. An untracked conversation gets its `lastSeq` as the baseline. Concurrent calls
   * coalesce into one more run.
   * @returns {Promise<{ changes: object[], syncToken: string|null, reset: boolean }|null>}
   */
  resync() {
    if (this._syncing) {
      this._syncAgain = true;
      return this._syncing;
    }
    const run = async () => {
      let result = null;
      do {
        this._syncAgain = false;
        result = await this._runSync();
      } while (this._syncAgain);
      return result;
    };
    this._syncing = run().finally(() => { this._syncing = null; });
    return this._syncing;
  }

  async _runSync() {
    const api = this._api;
    if (!api || typeof api.syncConversations !== 'function') return null;
    let token = null;
    try { token = (await this._tokenStore.get()) || null; } catch { token = null; }
    let reset = false;
    let changes = [];
    let finalToken = null;
    let continuationToken = null;
    let complete = false;
    const seenTokens = new Set();
    for (let page = 0; page < REALTIME_DEFAULTS.maxSyncPages; page += 1) {
      let result;
      try {
        result = normalizeEnumerationResult(await api.syncConversations({ maxResults: 100, syncToken: token, continuationToken }));
      } catch (error) {
        if (!reset && error && error.status === 400 && error.code === 'InvalidContinuationToken') {
          reset = true;
          token = null;
          continuationToken = null;
          seenTokens.clear();
          changes = [];
          page = -1;
          continue;
        }
        this._emit(RealtimeEvent.Error, { error, during: 'sync' });
        return null;
      }
      changes.push(...result.objects);
      if (result.endOfResults) {
        finalToken = result.extra.syncToken || null;
        complete = Boolean(finalToken);
        break;
      }
      if (!result.continuationToken || seenTokens.has(result.continuationToken)) break;
      seenTokens.add(result.continuationToken);
      continuationToken = result.continuationToken;
    }
    if (!complete) {
      this._emit(RealtimeEvent.Error, { error: new Error('Conversation sync did not finish. The stored watermark is unchanged.'), during: 'sync' });
      return null;
    }
    try { await this._tokenStore.set(finalToken || null); } catch { /* ignore */ }
    const summary = { changes, syncToken: finalToken || null, reset };
    this._emit(RealtimeEvent.Sync, summary, null);
    for (const change of changes) {
      const conversation = change && change.conversation;
      const id = conversation && conversation.id;
      if (!id) continue;
      if (change.change === 'Removed') {
        this._seqs.delete(id);
        continue;
      }
      const last = this._seqs.get(id);
      const lastSeq = Number(conversation.lastSeq);
      if (last === undefined) {
        if (Number.isFinite(lastSeq)) this._seqs.set(id, lastSeq);
        continue;
      }
      if (Number.isFinite(lastSeq) && lastSeq > last) await this._enqueue(id, () => this._fill(id, last));
    }
    return summary;
  }

  // ---- Polling fallback (contract 5.1) ----

  _startPolling(reason) {
    if (this._polling) return;
    this._polling = true;
    this._activeIdleSince = this._now();
    this._emit(RealtimeEvent.Fallback, { polling: true, reason });
    this._pollUnread();
    this._pollList();
    this._schedulePoll('active', this._pollMs, () => this._pollActive());
  }

  _stopPolling(emit) {
    const was = this._polling;
    this._polling = false;
    for (const key of Object.keys(this._pollTimers)) {
      if (this._pollTimers[key] !== null) this._clearTimeout(this._pollTimers[key]);
      this._pollTimers[key] = null;
    }
    if (was && emit) this._emit(RealtimeEvent.Fallback, { polling: false, reason: 'connected' });
  }

  _schedulePoll(key, delay, fn) {
    if (!this._polling) return;
    if (this._pollTimers[key] !== null) this._clearTimeout(this._pollTimers[key]);
    this._pollTimers[key] = this._setTimeout(() => {
      this._pollTimers[key] = null;
      if (this._polling) fn();
    }, delay);
  }

  _resetActivePoll() {
    this._activeIdleSince = this._now();
    if (this._polling) this._schedulePoll('active', this._pollMs, () => this._pollActive());
  }

  async _pollUnread() {
    const api = this._api;
    if (api && this._appState === AppState.Foreground) {
      const [notifications, messaging] = await Promise.all([
        typeof api.getUnreadCount === 'function' ? Promise.resolve().then(() => api.getUnreadCount()).catch(() => null) : null,
        typeof api.getMessagingUnreadCount === 'function' ? Promise.resolve().then(() => api.getMessagingUnreadCount()).catch(() => null) : null
      ]);
      if (this._polling && (notifications || messaging)) {
        const data = { polled: true };
        if (notifications && typeof notifications.unreadCount === 'number') data.notifications = notifications.unreadCount;
        if (messaging) for (const key of ['conversations', 'messages', 'requests']) if (typeof messaging[key] === 'number') data[key] = messaging[key];
        this._emit('unread.counts', data, null);
      }
    }
    this._schedulePoll('unread', REALTIME_DEFAULTS.unreadPollMs, () => this._pollUnread());
  }

  async _pollList() {
    if (this._appState === AppState.Foreground) await this.resync();
    this._schedulePoll('list', REALTIME_DEFAULTS.listPollMs, () => this._pollList());
  }

  async _pollActive() {
    const id = this._activeConversationId;
    if (id && this._appState === AppState.Foreground) {
      const last = this._seqs.get(id);
      if (last !== undefined) {
        const count = await this._enqueue(id, () => this._fill(id, last));
        if (count > 0) this._activeIdleSince = this._now();
      }
    }
    const idle = this._now() - this._activeIdleSince >= REALTIME_DEFAULTS.pollIdleAfterMs;
    this._schedulePoll('active', idle ? REALTIME_DEFAULTS.pollSlowMs : this._pollMs, () => this._pollActive());
  }
}

function detach(socket) {
  socket.onopen = null;
  socket.onmessage = null;
  socket.onclose = null;
  socket.onerror = null;
}
