import { getApiClient } from '../api/client';
import { fatalErrorStore, MaxFailureMessageLength, MaxStackTraceLength } from './fatalError';
import { getInstallId } from './installId';

/**
 * Live update adoption events (docs/LIVE_UPDATES_CONTRACT.md section 9.2):
 * `POST /api/v1/updates/events` with `X-TK-Install-Id`, 1 to 20 events per request.
 *
 * What the app reports:
 * - `Downloaded` when a new update finishes downloading (it applies on the next launch).
 * - `Applied` once per update, after a launch of a downloaded update has stayed up for
 *   `stableMs` (so a launch that crashes and is rolled back is not counted), with the library's
 *   `launchDuration`.
 * - `LaunchFailed` when the app launches without the update it downloaded (the library rolled
 *   back after a failed launch) or in an emergency launch, with the message and stack trace of
 *   the fatal JavaScript error that update recorded before it crashed (src/updates/fatalError.js).
 * - `RolledBackToEmbedded` when a rollback directive took effect.
 * - `CheckFailed` and `DownloadFailed` with a bounded error code, and `CriticalPromptShown` and
 *   `CriticalRestarted` from the critical update prompt.
 *
 * Events are queued in a small file (memory when the file system is unavailable) and sent in
 * batches: a short while after launch, on return to the foreground, and a few seconds after a new
 * event. Sends are rate limited on the device (at most `maxSendsPerHour`, at least
 * `minIntervalMs` apart, and longer after a `429`), under the server's per-install limit. Nothing
 * here ever throws, blocks startup, or shows an error: when the device is offline or the server
 * fails, the events stay queued for a later batch; events older than the server accepts (7 days)
 * and batches the server rejects as invalid are dropped silently. The install id is the only
 * identifier sent, and the request never carries the member's token.
 */

/** Event types the server accepts. */
export const UpdateEventType = Object.freeze({
  CheckFailed: 'CheckFailed',
  Downloaded: 'Downloaded',
  DownloadFailed: 'DownloadFailed',
  Applied: 'Applied',
  LaunchFailed: 'LaunchFailed',
  RolledBackToEmbedded: 'RolledBackToEmbedded',
  CriticalPromptShown: 'CriticalPromptShown',
  CriticalRestarted: 'CriticalRestarted'
});

/** Bounded error codes the server accepts. */
export const UpdateErrorCode = Object.freeze({
  Network: 'Network',
  SignatureInvalid: 'SignatureInvalid',
  HashMismatch: 'HashMismatch',
  Storage: 'Storage',
  Timeout: 'Timeout',
  Server: 'Server',
  Unknown: 'Unknown'
});

/** Most events per request (server limit). */
export const MaxEventsPerBatch = 20;

/** Most events kept on the device; the oldest go first. */
export const MaxQueuedEvents = 100;

/** Oldest event the server accepts. */
export const MaxEventAgeMs = 7 * 24 * 60 * 60 * 1000;

/** Queue file name in the document directory. */
export const UpdateEventsFile = 'tk-update-events.json';

/** The id used for the embedded update (a rollback directive). */
export const EmbeddedUpdateId = 'embedded';

const HOUR_MS = 60 * 60 * 1000;
const FAILURE_TYPES = ['LaunchFailed', 'DownloadFailed', 'CheckFailed'];
const VERSION = 1;
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * The bounded error code for an expo-updates failure.
 * @param {unknown} error An Error, or a message.
 * @returns {string}
 */
export function updateErrorCode(error) {
  const text = (typeof error === 'string' ? error : ((error && (String(error.code || '') + ' ' + String(error.message || ''))) || '')).toLowerCase();
  if (!text.trim()) return UpdateErrorCode.Unknown;
  if (/time(d)? ?out/.test(text)) return UpdateErrorCode.Timeout;
  if (/signature|certificate|code ?signing/.test(text)) return UpdateErrorCode.SignatureInvalid;
  if (/hash|integrity|checksum/.test(text)) return UpdateErrorCode.HashMismatch;
  if (/network|offline|internet|connection|unreachable|resolve host|failed to connect|socket/.test(text)) return UpdateErrorCode.Network;
  if (/disk|storage|space|database|sqlite|write/.test(text)) return UpdateErrorCode.Storage;
  if (/server|status code 5\d\d|\b5\d\d\b/.test(text)) return UpdateErrorCode.Server;
  return UpdateErrorCode.Unknown;
}

function cleanUpdateId(id) {
  if (id === EmbeddedUpdateId) return EmbeddedUpdateId;
  return typeof id === 'string' && UUID.test(id) ? id.toLowerCase() : null;
}

function emptyState() {
  return { queue: [], pending: null, lastApplied: null, sends: [], blockedUntil: 0 };
}

function normalize(value) {
  const state = emptyState();
  if (!value || typeof value !== 'object' || value.version !== VERSION) return state;
  if (Array.isArray(value.queue)) state.queue = value.queue.filter((e) => e && typeof e.type === 'string' && typeof e.occurredUtc === 'string').slice(-MaxQueuedEvents);
  if (typeof value.pending === 'string') state.pending = value.pending;
  if (typeof value.lastApplied === 'string') state.lastApplied = value.lastApplied;
  if (Array.isArray(value.sends)) state.sends = value.sends.filter((t) => Number.isFinite(t));
  if (Number.isFinite(value.blockedUntil)) state.blockedUntil = value.blockedUntil;
  return state;
}

function defaultFile() {
  const { File, Paths } = require('expo-file-system');
  return new File(Paths.document, UpdateEventsFile);
}

/**
 * The reporter's storage: one JSON file, falling back to memory when the file system is not
 * available (or a read or write fails), so reporting still works for the session.
 * @param {() => object} [fileFactory] Returns an expo-file-system `File`.
 * @returns {{ load: () => Promise<object>, save: (state: object) => Promise<void> }}
 */
export function createEventStore(fileFactory = defaultFile) {
  let memory = null;
  return {
    async load() {
      if (memory) return memory;
      try {
        const file = fileFactory();
        if (file && typeof file.text === 'function' && file.exists !== false) memory = normalize(JSON.parse(await file.text()));
      } catch {
        // Unreadable or missing: start empty.
      }
      if (!memory) memory = emptyState();
      return memory;
    },
    async save(state) {
      memory = state;
      try {
        const file = fileFactory();
        if (!file || typeof file.write !== 'function') return;
        if (file.exists === false && typeof file.create === 'function') file.create();
        await file.write(JSON.stringify({ version: VERSION, ...state }));
      } catch {
        // Kept in memory for this session.
      }
    }
  };
}

/**
 * Send one batch with the shared API client: no bearer token (adoption is device based), the
 * install id header, and the usual identification headers.
 * @param {object[]} events
 * @param {string} installId
 */
export function sendUpdateEvents(events, installId) {
  return getApiClient().request('POST', '/updates/events', { body: { events }, headers: { 'X-TK-Install-Id': installId }, withAuth: false });
}

export class UpdateEventReporter {
  /**
   * @param {object} [options]
   * @param {(events: object[], installId: string) => Promise<any>} [options.send]
   * @param {() => Promise<string>} [options.installId]
   * @param {{ load: () => Promise<object>, save: (state: object) => Promise<void> }} [options.store]
   * @param {() => number} [options.now]
   * @param {number} [options.flushDelayMs] Wait after a new event before sending (batching).
   * @param {number} [options.launchFlushDelayMs] Wait after launch before the first send.
   * @param {number} [options.stableMs] How long a launch must stay up to count as `Applied`.
   * @param {number} [options.minIntervalMs] Shortest gap between two sends.
   * @param {number} [options.maxSendsPerHour]
   * @param {{ read: () => Promise<object|null>, clear: () => void }} [options.crashes] The last fatal error record.
   */
  constructor({
    send = sendUpdateEvents,
    installId = getInstallId,
    store = createEventStore(),
    now = () => Date.now(),
    flushDelayMs = 5000,
    launchFlushDelayMs = 15000,
    stableMs = 10000,
    minIntervalMs = 60000,
    maxSendsPerHour = 10,
    crashes = fatalErrorStore
  } = {}) {
    this._send = send;
    this._installId = installId;
    this._store = store;
    this.now = now;
    this.flushDelayMs = flushDelayMs;
    this.launchFlushDelayMs = launchFlushDelayMs;
    this.stableMs = stableMs;
    this.minIntervalMs = minIntervalMs;
    this.maxSendsPerHour = maxSendsPerHour;
    this._crashes = crashes;
    this._chain = Promise.resolve();
    this._timers = new Set();
    this._flushTimer = null;
    this._flushing = null;
    this._disposed = false;
  }

  /** Run a state change after the previous one (load, change, save); never rejects. */
  _update(change) {
    const run = this._chain.then(async () => {
      const state = await this._store.load();
      const result = change(state);
      await this._store.save(state);
      return result;
    });
    this._chain = run.catch(() => undefined);
    return run.catch(() => undefined);
  }

  _later(fn, ms) {
    if (this._disposed) return null;
    const timer = setTimeout(() => {
      this._timers.delete(timer);
      if (!this._disposed) fn();
    }, Math.max(0, ms));
    this._timers.add(timer);
    return timer;
  }

  _scheduleFlush(ms) {
    if (this._disposed) return;
    if (this._flushTimer) {
      clearTimeout(this._flushTimer);
      this._timers.delete(this._flushTimer);
    }
    this._flushTimer = this._later(() => {
      this._flushTimer = null;
      this.flush();
    }, ms);
  }

  /** Stop every timer (the provider unmounted). Queued events stay stored. */
  dispose() {
    this._disposed = true;
    for (const timer of this._timers) clearTimeout(timer);
    this._timers.clear();
    this._flushTimer = null;
  }

  /** Copy of the stored state (tests and diagnostics). */
  async snapshot() {
    await this._chain;
    const state = await this._store.load();
    return JSON.parse(JSON.stringify(state));
  }

  /**
   * Queue one event and send it with the next batch.
   * @param {string} type An UpdateEventType value.
   * @param {{ updateId?: string|null, errorCode?: string|null, launchDurationMs?: number|null, message?: string|null, stackTrace?: string|null }} [data]
   *   `message` and `stackTrace` are kept on failure events only.
   * @returns {Promise<void>}
   */
  record(type, { updateId = null, errorCode = null, launchDurationMs = null, message = null, stackTrace = null } = {}) {
    if (!Object.values(UpdateEventType).includes(type)) return Promise.resolve();
    const event = { type, occurredUtc: new Date(this.now()).toISOString() };
    const id = cleanUpdateId(updateId);
    if (id) event.updateId = id;
    if (errorCode) event.errorCode = Object.values(UpdateErrorCode).includes(errorCode) ? errorCode : UpdateErrorCode.Unknown;
    if (Number.isFinite(launchDurationMs) && launchDurationMs >= 0) event.launchDurationMs = Math.round(launchDurationMs);
    if (FAILURE_TYPES.includes(type) && typeof message === 'string' && message) event.message = message.slice(0, MaxFailureMessageLength);
    if (FAILURE_TYPES.includes(type) && typeof stackTrace === 'string' && stackTrace) event.stackTrace = stackTrace.slice(0, MaxStackTraceLength);
    const done = this._update((state) => {
      state.queue.push(event);
      if (state.queue.length > MaxQueuedEvents) state.queue.splice(0, state.queue.length - MaxQueuedEvents);
    });
    this._scheduleFlush(this.flushDelayMs);
    return done;
  }

  /**
   * A new update finished downloading: `Downloaded`, and remember it so the next launch can tell
   * whether it ran. A rollback directive is remembered without an event.
   * @param {{ updateId?: string|null, rollBack?: boolean }} info
   */
  noteDownloaded({ updateId = null, rollBack = false } = {}) {
    const id = rollBack ? EmbeddedUpdateId : cleanUpdateId(updateId);
    if (!id) return Promise.resolve();
    const done = this._update((state) => {
      state.pending = id;
    });
    if (rollBack) return done;
    return this.record(UpdateEventType.Downloaded, { updateId: id });
  }

  /**
   * Map an UpdatesController telemetry event to adoption events.
   * @param {{ type: string, [key: string]: any }} event
   */
  handleControllerEvent(event) {
    if (!event || this._disposed) return;
    if (event.type === 'ready') {
      this.noteDownloaded({ updateId: event.updateId, rollBack: Boolean(event.rollBack) });
    } else if (event.type === 'failure' && (event.stage === 'check' || event.stage === 'download')) {
      const code = updateErrorCode({ code: event.code, message: event.message });
      if (event.stage === 'check') this.record(UpdateEventType.CheckFailed, { errorCode: code });
      else this.record(UpdateEventType.DownloadFailed, { updateId: event.updateId, errorCode: code });
    }
  }

  /**
   * Reconcile this launch with what was downloaded before (runs in the background; the returned
   * promise is only for tests).
   * @param {object|null} updates The expo-updates module.
   * @returns {Promise<void>}
   */
  onLaunch(updates) {
    if (!updates || !updates.isEnabled) return Promise.resolve();
    const running = updates.isEmbeddedLaunch ? EmbeddedUpdateId : cleanUpdateId(updates.updateId);
    const emergency = Boolean(updates.isEmergencyLaunch);
    const duration = Number.isFinite(updates.launchDuration) ? updates.launchDuration : null;
    const work = this._update((state) => {
      const events = [];
      const pending = state.pending;
      if (emergency) {
        events.push({ type: UpdateEventType.LaunchFailed, updateId: pending && pending !== EmbeddedUpdateId ? pending : null, errorCode: UpdateErrorCode.Unknown });
        state.pending = null;
      } else if (pending === EmbeddedUpdateId) {
        if (running === EmbeddedUpdateId) events.push({ type: UpdateEventType.RolledBackToEmbedded, updateId: EmbeddedUpdateId });
        state.pending = null;
      } else if (pending && running !== pending) {
        // The downloaded update did not run: the library rolled back after a failed launch.
        events.push({ type: UpdateEventType.LaunchFailed, updateId: pending, errorCode: UpdateErrorCode.Unknown });
        state.pending = null;
      }
      const applied = !emergency && running && running !== EmbeddedUpdateId && running !== state.lastApplied ? running : null;
      return { events, applied };
    }).then(async (result) => {
      if (!result) return;
      for (const e of result.events) await this.record(e.type, await this._withCrash(e));
      if (result.applied) {
        this._later(() => this._confirmApplied(result.applied, duration), this.stableMs);
      }
      this._scheduleFlush(this.launchFlushDelayMs);
    });
    return work;
  }

  /** A `LaunchFailed` for the update that recorded the last fatal error takes its message and stack (the record is then cleared). */
  async _withCrash(event) {
    if (event.type !== UpdateEventType.LaunchFailed || !event.updateId || !this._crashes) return event;
    try {
      const crash = await this._crashes.read();
      if (!crash || typeof crash.updateId !== 'string' || crash.updateId.toLowerCase() !== event.updateId) return event;
      this._crashes.clear();
      return { ...event, message: crash.message || null, stackTrace: crash.stackTrace || null };
    } catch {
      return event;
    }
  }

  /** The launch stayed up: report `Applied` once for this update. */
  _confirmApplied(updateId, launchDurationMs) {
    return this._update((state) => {
      if (state.lastApplied === updateId) return false;
      state.lastApplied = updateId;
      if (state.pending === updateId) state.pending = null;
      return true;
    }).then((fresh) => (fresh ? this.record(UpdateEventType.Applied, { updateId, launchDurationMs }) : undefined));
  }

  /** The app returned to the foreground: send what is queued (rate limits permitting). */
  onForeground() {
    this._scheduleFlush(0);
  }

  /** Whether a send is allowed now, and when to try again otherwise. */
  _allowance(state) {
    const now = this.now();
    state.sends = state.sends.filter((t) => now - t < HOUR_MS);
    if (state.blockedUntil > now) return { ok: false, retryMs: state.blockedUntil - now };
    const last = state.sends.length ? Math.max(...state.sends) : null;
    if (last !== null && now - last < this.minIntervalMs) return { ok: false, retryMs: this.minIntervalMs - (now - last) };
    if (state.sends.length >= this.maxSendsPerHour) return { ok: false, retryMs: HOUR_MS - (now - Math.min(...state.sends)) };
    return { ok: true, retryMs: 0 };
  }

  /**
   * Send one batch of queued events. Never throws; resolves the number of events the server
   * took (0 when nothing was sent).
   * @returns {Promise<number>}
   */
  flush() {
    if (this._flushing) return this._flushing;
    this._flushing = this._flushOnce().catch(() => 0).finally(() => {
      this._flushing = null;
    });
    return this._flushing;
  }

  async _flushOnce() {
    const plan = await this._update((state) => {
      const now = this.now();
      state.queue = state.queue.filter((e) => {
        const at = Date.parse(e.occurredUtc);
        return Number.isFinite(at) && now - at <= MaxEventAgeMs;
      });
      if (!state.queue.length) return null;
      const allowance = this._allowance(state);
      if (!allowance.ok) return { wait: allowance.retryMs };
      state.sends.push(now);
      return { batch: state.queue.slice(0, MaxEventsPerBatch) };
    });
    if (!plan) return 0;
    if (plan.wait !== undefined) {
      this._scheduleFlush(plan.wait);
      return 0;
    }
    let installId;
    try {
      installId = await this._installId();
    } catch {
      return 0;
    }
    if (!installId) return 0;
    let outcome = 'sent';
    let retryAfterMs = 0;
    try {
      await this._send(plan.batch, installId);
    } catch (error) {
      const status = error && Number.isFinite(error.status) ? error.status : 0;
      if (status === 400 || status === 404 || status === 413) outcome = 'rejected';
      else {
        outcome = 'kept';
        if (status === 429) retryAfterMs = Math.max(this.minIntervalMs, ((error && error.retryAfterSeconds) || 0) * 1000, 5 * 60000);
      }
    }
    const sent = plan.batch;
    const remaining = await this._update((state) => {
      if (outcome !== 'kept') {
        const ids = new Set(sent.map((e) => e.type + '|' + e.occurredUtc + '|' + (e.updateId || '')));
        let removed = 0;
        state.queue = state.queue.filter((e) => {
          if (removed < sent.length && ids.has(e.type + '|' + e.occurredUtc + '|' + (e.updateId || ''))) {
            removed += 1;
            return false;
          }
          return true;
        });
      }
      if (retryAfterMs) state.blockedUntil = this.now() + retryAfterMs;
      return state.queue.length;
    });
    // More queued (a long offline spell): the next batch goes after the interval.
    if (remaining && outcome === 'sent') this._scheduleFlush(this.minIntervalMs);
    return outcome === 'sent' ? sent.length : 0;
  }
}
