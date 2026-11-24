/**
 * App-level live updates controller (docs/TRELLISKIT.md section 8.1).
 *
 * - Checks on launch and when the app returns to the foreground, at most once per interval
 *   (property setting `updates.checkIntervalMinutes`, default 15). Concurrent checks share one
 *   in-flight run.
 * - Downloads a new update in the background. The download is applied on the next launch; the
 *   running JavaScript is never swapped under the member.
 * - An update marked critical (`extra.critical: true` or `metadata.critical: "true"` in the
 *   manifest) moves to `ready` with `critical: true`, and the app shows "Restart to update".
 * - The manifest may carry `extra.criticalMessage` (up to 200 characters): the prompt text for a
 *   critical update, on the state as `criticalMessage` (null otherwise).
 * - Before the first check the optional `prepare` hook runs once (the app sets the runtime update
 *   request headers there: channel and install id, src/updates/requestHeaders.js). A failing
 *   prepare is reported and the check still runs with the headers the binary has.
 * - A `rollBackToEmbedded` directive is downloaded the same way and applies on the next launch.
 * - Failures (offline, server errors, signature rejected) never throw to callers: the state
 *   becomes `error` with the message, and the next allowed check tries again.
 *
 * The expo-updates module is injected so tests can drive it.
 */

/** Default minimum minutes between checks. */
export const DefaultCheckIntervalMinutes = 15;

/** Smallest interval honored, so a bad setting cannot hammer the manifest endpoint. */
export const MinimumCheckIntervalMinutes = 1;

/**
 * Normalize a check interval in minutes.
 * @param {unknown} minutes
 * @returns {number}
 */
export function normalizeIntervalMinutes(minutes) {
  const value = Number(minutes);
  if (!Number.isFinite(value) || value <= 0) return DefaultCheckIntervalMinutes;
  return Math.max(MinimumCheckIntervalMinutes, value);
}

/**
 * Whether a manifest marks its update as critical.
 * @param {object|null|undefined} manifest
 * @returns {boolean}
 */
export function isCriticalManifest(manifest) {
  if (!manifest || typeof manifest !== 'object') return false;
  const extra = manifest.extra && typeof manifest.extra === 'object' ? manifest.extra : {};
  if (extra.critical === true) return true;
  const metadata = manifest.metadata && typeof manifest.metadata === 'object' ? manifest.metadata : {};
  return metadata.critical === true || metadata.critical === 'true';
}

/** Longest critical message shown (the server enforces the same limit). */
export const MaxCriticalMessageLength = 200;

/**
 * The critical prompt text a manifest carries, or null.
 * @param {object|null|undefined} manifest
 * @returns {string|null}
 */
export function criticalMessageFor(manifest) {
  const extra = manifest && typeof manifest === 'object' && manifest.extra && typeof manifest.extra === 'object' ? manifest.extra : {};
  const value = typeof extra.criticalMessage === 'string' ? extra.criticalMessage.trim() : '';
  return value ? value.slice(0, MaxCriticalMessageLength) : null;
}

export class UpdatesController {
  /**
   * @param {object} options
   * @param {object|null} options.updates The expo-updates module (or a fake).
   * @param {boolean} [options.enabled] Extra switch (build settings, development mode).
   * @param {number} [options.intervalMinutes]
   * @param {() => number} [options.now]
   * @param {(event: { type: string, [key: string]: any }) => void} [options.onEvent] Telemetry hook.
   * @param {() => Promise<any>} [options.prepare] Runs once before the first check.
   */
  constructor({ updates, enabled = true, intervalMinutes = DefaultCheckIntervalMinutes, now = () => Date.now(), onEvent, prepare } = {}) {
    this.updates = updates || null;
    this.prepare = prepare || null;
    this._prepared = null;
    this.now = now;
    this.onEvent = onEvent || null;
    this.intervalMs = normalizeIntervalMinutes(intervalMinutes) * 60000;
    this._enabled = Boolean(enabled && this.updates && this.updates.isEnabled);
    this._inFlight = null;
    this._listeners = new Set();
    this._eventListeners = new Set();
    this.state = Object.freeze({
      status: this._enabled ? 'idle' : 'disabled',
      lastCheckMs: null,
      pendingUpdateId: null,
      critical: false,
      criticalMessage: null,
      rollBack: false,
      error: null
    });
  }

  /** Whether checks run in this build. */
  get enabled() {
    return this._enabled;
  }

  /** Change the check interval (the property setting can arrive after launch). */
  setIntervalMinutes(minutes) {
    this.intervalMs = normalizeIntervalMinutes(minutes) * 60000;
  }

  /**
   * Listen for state changes.
   * @param {(state: object) => void} listener
   * @returns {() => void}
   */
  subscribe(listener) {
    this._listeners.add(listener);
    return () => this._listeners.delete(listener);
  }

  _set(patch) {
    this.state = Object.freeze({ ...this.state, ...patch });
    for (const listener of Array.from(this._listeners)) {
      try {
        listener(this.state);
      } catch {
        // A failing listener does not stop the others.
      }
    }
  }

  /**
   * Listen for telemetry events (`check`, `download`, `ready`, `failure` with `stage` `check`,
   * `download`, `prepare`, or `restart`), in addition to `onEvent`.
   * @param {(event: { type: string, [key: string]: any }) => void} listener
   * @returns {() => void}
   */
  subscribeEvents(listener) {
    this._eventListeners.add(listener);
    return () => this._eventListeners.delete(listener);
  }

  _emit(type, data) {
    const event = { type, ...data };
    for (const listener of [this.onEvent, ...Array.from(this._eventListeners)]) {
      if (!listener) continue;
      try {
        listener(event);
      } catch {
        // Telemetry never breaks updates.
      }
    }
  }

  /**
   * Whether a check would run now.
   * @param {{ force?: boolean }} [options]
   */
  isDue({ force = false } = {}) {
    if (!this._enabled) return false;
    if (force || this.state.lastCheckMs === null) return true;
    return this.now() - this.state.lastCheckMs >= this.intervalMs;
  }

  /**
   * Check for an update and download it in the background when one is available.
   * @param {object} [options]
   * @param {'launch'|'foreground'|'manual'} [options.reason]
   * @param {boolean} [options.force] Ignore the interval.
   * @returns {Promise<object>} The state after the run (or the current state when skipped).
   */
  check({ reason = 'manual', force = false } = {}) {
    if (this._inFlight) return this._inFlight;
    if (!this.isDue({ force })) return Promise.resolve(this.state);
    this._inFlight = this._run(reason).finally(() => {
      this._inFlight = null;
    });
    return this._inFlight;
  }

  async _run(reason) {
    const previous = this.state.status;
    this._set({ status: 'checking', lastCheckMs: this.now(), error: null });
    if (this.prepare) await this._prepare();
    this._emit('check', { reason });
    let stage = 'check';
    let downloadingId = null;
    try {
      const result = await this.updates.checkForUpdateAsync();
      const rollBack = Boolean(result && result.isRollBackToEmbedded);
      if (!result || (!result.isAvailable && !rollBack)) {
        this._set({ status: previous === 'ready' ? 'ready' : 'idle' });
        return this.state;
      }
      const manifestId = (result.manifest && result.manifest.id) || null;
      if (!rollBack && manifestId && manifestId === this.state.pendingUpdateId) {
        this._set({ status: 'ready' });
        return this.state;
      }

      stage = 'download';
      downloadingId = manifestId;
      this._set({ status: 'downloading' });
      this._emit('download', { reason, updateId: manifestId, rollBack });
      const fetched = await this.updates.fetchUpdateAsync();
      if (!fetched || (!fetched.isNew && !fetched.isRollBackToEmbedded)) {
        this._set({ status: previous === 'ready' ? 'ready' : 'idle' });
        return this.state;
      }
      const manifest = fetched.manifest || result.manifest || null;
      const critical = !fetched.isRollBackToEmbedded && (isCriticalManifest(manifest) || isCriticalManifest(result.manifest));
      const pendingUpdateId = (manifest && manifest.id) || manifestId;
      const criticalMessage = critical ? criticalMessageFor(manifest) || criticalMessageFor(result.manifest) : null;
      this._set({ status: 'ready', pendingUpdateId, critical, criticalMessage, rollBack: Boolean(fetched.isRollBackToEmbedded) });
      this._emit('ready', { updateId: pendingUpdateId, critical, rollBack: Boolean(fetched.isRollBackToEmbedded) });
      return this.state;
    } catch (error) {
      const message = (error && error.message) || String(error);
      this._set({ status: 'error', error: message });
      this._emit('failure', { reason, stage, updateId: downloadingId, message, code: (error && error.code) || null });
      return this.state;
    }
  }

  /** Run the prepare hook once; a failure is reported, never thrown, and not retried. */
  _prepare() {
    if (!this.prepare) return Promise.resolve();
    if (!this._prepared) {
      this._prepared = Promise.resolve()
        .then(() => this.prepare())
        .then(
          (result) => {
            if (result && result.applied === false && result.error) this._emit('failure', { reason: 'prepare', stage: 'prepare', message: result.error });
          },
          (error) => this._emit('failure', { reason: 'prepare', stage: 'prepare', message: (error && error.message) || String(error) })
        );
    }
    return this._prepared;
  }

  /**
   * Apply the downloaded update now (the critical prompt's "Restart to update").
   * @returns {Promise<boolean>} False when nothing is pending or the reload failed (it then
   *   applies on the next launch).
   */
  async restart() {
    if (!this._enabled || (!this.state.pendingUpdateId && !this.state.rollBack)) return false;
    try {
      await this.updates.reloadAsync();
      return true;
    } catch (error) {
      this._emit('failure', { reason: 'restart', stage: 'restart', message: (error && error.message) || String(error) });
      return false;
    }
  }
}
