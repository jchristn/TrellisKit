/**
 * Library helpers (docs/PHASE4_CONTRACT.md sections 2.6 and 5): the configuration from
 * `GET /api/v1/property` `library`, level lookups, a progress reporter that throttles to
 * `progressIntervalSeconds` and computes `playedSeconds` with the anti-skip clamp, a
 * storage-agnostic offline progress queue (mobile replays it on reconnect), and display helpers.
 * Nothing here names a property.
 */

import { findProfileField, profileFieldsOf } from './profileFields.js';

/** Lesson kinds. */
export const LessonKinds = Object.freeze(['Video', 'Article']);
/** Lesson statuses. */
export const LessonStatuses = Object.freeze(['Draft', 'Scheduled', 'Processing', 'Published', 'Archived']);
/** Series statuses. */
export const SeriesStatuses = Object.freeze(['Draft', 'Published', 'Archived']);
/** Lesson resource kinds. */
export const ResourceKinds = Object.freeze(['Document', 'Link', 'Reference']);
/** Progress filter values. */
export const ProgressFilters = Object.freeze(['NotStarted', 'InProgress', 'Completed']);
/** Series orderings. */
export const SeriesOrderings = Object.freeze(['SortOrderAscending', 'TitleAscending', 'PublishedDescending', 'Relevance']);
/** Lesson orderings. */
export const LessonOrderings = Object.freeze(['PublishedDescending', 'TitleAscending', 'DurationAscending', 'Relevance']);
/** Level sources. */
export const LevelSources = Object.freeze(['None', 'ProfileField', 'Custom']);
/** Fixed bounds a client checks before sending (section 2.6). */
export const LIBRARY_LIMITS = Object.freeze({ maxMarkers: 50, markerLabelMax: 80, maxResources: 10, referenceMax: 300, maxCaptions: 10, maxLevels: 10, slugPattern: /^[a-z0-9-]{2,80}$/ });
/** Offline progress entries older than this are dropped (section 5.5). */
export const OFFLINE_PROGRESS_MAX_AGE_DAYS = 7;

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function num(value, fallback) {
  const n = Number(value);
  return value !== null && value !== undefined && value !== '' && Number.isFinite(n) ? n : fallback;
}

/**
 * The property's `library` section with engine defaults: `levelSource`, `levelFieldKey`, `levels`,
 * `downloadsEnabled`, `progressIntervalSeconds` (15), `completionThresholdPercent` (90),
 * `resumeTailSeconds` (10).
 */
export function libraryConfig(property) {
  const l = isObject(property) && isObject(property.library) ? property.library : {};
  return {
    levelSource: LevelSources.includes(l.levelSource) ? l.levelSource : 'None',
    levelFieldKey: l.levelFieldKey || null,
    levels: Array.isArray(l.levels) ? l.levels.filter((x) => x && x.key) : [],
    downloadsEnabled: l.downloadsEnabled === true,
    progressIntervalSeconds: Math.max(5, Math.min(120, num(l.progressIntervalSeconds, 15))),
    completionThresholdPercent: Math.max(50, Math.min(100, num(l.completionThresholdPercent, 90))),
    resumeTailSeconds: Math.max(0, Math.min(120, num(l.resumeTailSeconds, 10)))
  };
}

/**
 * Configured levels `{ key, label, color }` in order: the `library.levels` the server sent, else
 * (for `ProfileField`) the options of the profile field named by `levelFieldKey`.
 */
export function libraryLevels(property) {
  const c = libraryConfig(property);
  if (c.levelSource === 'None') return [];
  if (c.levels.length) return c.levels.map((x) => ({ key: x.key, label: x.label || x.key, color: x.color || null }));
  if (c.levelSource === 'ProfileField' && c.levelFieldKey) {
    const field = findProfileField(profileFieldsOf(property), c.levelFieldKey);
    const options = (field && Array.isArray(field.options) ? field.options : []).filter((o) => o && o.key && !o.isRetired);
    return options.map((o) => ({ key: o.key, label: o.label || o.key, color: o.color || null }));
  }
  return [];
}

/** A level `{ key, label, color }` by key (an unknown key keeps its key as the label). */
export function findLevel(property, key) {
  if (!key) return null;
  return libraryLevels(property).find((l) => l.key === key) || { key, label: String(key), color: null };
}

/** Group series (or lessons) by their first level, in configured level order; items without a level come last under `null`. */
export function groupByLevel(property, items) {
  const levels = libraryLevels(property);
  const groups = levels.map((level) => ({ level, items: [] }));
  const other = { level: null, items: [] };
  for (const item of items || []) {
    const keys = (item.levels || []).map((l) => (isObject(l) ? l.key : l));
    const group = groups.find((g) => keys.includes(g.level.key));
    (group || other).items.push(item);
  }
  return [...groups.filter((g) => g.items.length), ...(other.items.length ? [other] : [])];
}

/** "5:42" or "1:02:03" for seconds; empty for a missing value. */
export function formatDuration(seconds) {
  const s = Math.max(0, Math.floor(num(seconds, -1)));
  if (seconds === null || seconds === undefined || num(seconds, -1) < 0) return '';
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const r = s % 60;
  const pad = (v) => String(v).padStart(2, '0');
  return h ? h + ':' + pad(m) + ':' + pad(r) : m + ':' + pad(r);
}

/** "57 min" or "2 h 5 min" for a total duration. */
export function formatTotalDuration(seconds) {
  const s = Math.max(0, Math.floor(num(seconds, 0)));
  const minutes = Math.round(s / 60);
  if (minutes < 60) return Math.max(1, minutes) + ' min';
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m ? h + ' h ' + m + ' min' : h + ' h';
}

/**
 * Where playback should start: the stored `positionSeconds`, or 0 when it is within the last
 * `resumeTailSeconds` of the video (section 5.5), or when the lesson was completed and watched to the end.
 * @param {object|null} progress LessonProgress
 * @param {number} durationSeconds
 * @param {number} [tailSeconds]
 */
export function resumePosition(progress, durationSeconds, tailSeconds = 10) {
  if (!progress) return 0;
  const position = Math.max(0, num(progress.positionSeconds, 0));
  const duration = num(durationSeconds, 0);
  if (duration > 0 && position >= duration - Math.max(0, tailSeconds)) return 0;
  return position;
}

/** Progress percent 0 to 100 for display (the server's value, clamped). */
export function progressPercent(progress) {
  if (!progress) return 0;
  return Math.max(0, Math.min(100, Math.round(num(progress.percent, 0))));
}

/** `NotStarted`, `InProgress`, or `Completed` for a LessonProgress. */
export function progressState(progress) {
  if (!progress) return 'NotStarted';
  if (progress.completed) return 'Completed';
  return num(progress.maxPositionSeconds, 0) > 0 || num(progress.watchedSeconds, 0) > 0 ? 'InProgress' : 'NotStarted';
}

/** App path of a series (website prefixes `/app`). */
export function seriesPath(idOrSlug) {
  return '/series/' + encodeURIComponent(idOrSlug);
}

/** App path of a lesson. */
export function lessonPath(idOrSlug) {
  return '/lessons/' + encodeURIComponent(idOrSlug);
}

/** Markers sorted by `seconds`, and the marker active at `position`. */
export function activeMarker(markers, position) {
  const sorted = (markers || []).slice().sort((a, b) => a.seconds - b.seconds);
  let current = null;
  for (const m of sorted) if (m.seconds <= position) current = m;
  return current;
}

/** A random session id for progress reports (not a secret). */
export function newProgressSessionId() {
  const hex = (n) => Array.from({ length: n }, () => Math.floor(Math.random() * 16).toString(16)).join('');
  return hex(8) + '-' + hex(4) + '-' + hex(4) + '-' + hex(4) + '-' + hex(12);
}

/**
 * A progress reporter for one playback session (section 5.5). Feed it player events; it sends
 * `{ positionSeconds, playedSeconds, clientUpdatedUtc, sessionId }` through `send` at most every
 * `intervalSeconds` while playing, and immediately on pause, seek end, end, and `flush` (exit,
 * app background).
 *
 * `playedSeconds` counts only real playback: position advances that are no larger than the
 * elapsed wall-clock time times the playback rate (plus a small tolerance) count, so seeking or
 * skipping ahead never counts, and each report is clamped to 0 to 2 times the interval (the
 * server clamps the same way).
 *
 * @param {object} options
 * @param {(body: object) => (void|Promise<any>)} options.send
 * @param {number} [options.intervalSeconds] default 15 (`library.progressIntervalSeconds`)
 * @param {number} [options.durationSeconds] clamps positions when known
 * @param {() => number} [options.now] milliseconds clock (tests pass a fake)
 * @param {string} [options.sessionId]
 */
export function createProgressReporter({ send, intervalSeconds = 15, durationSeconds = null, now = () => Date.now(), sessionId = newProgressSessionId() } = {}) {
  if (typeof send !== 'function') throw new Error('A progress reporter needs a send function.');
  const interval = Math.max(1, num(intervalSeconds, 15));
  const maxPlayed = 2 * interval;
  let duration = num(durationSeconds, null);
  let position = 0;
  let lastPosition = null;
  let lastTick = null;
  let playing = false;
  let played = 0;
  let lastSentAt = null;
  let lastSentPosition = null;
  let rate = 1;
  let closed = false;

  const clampPosition = (p) => {
    const v = Math.max(0, num(p, 0));
    return duration && duration > 0 ? Math.min(duration, v) : v;
  };

  function report(force) {
    if (closed) return null;
    const t = now();
    const due = lastSentAt === null || t - lastSentAt >= interval * 1000;
    if (!force && !due) return null;
    if (!force && played <= 0 && lastSentPosition === position) return null;
    const body = {
      positionSeconds: Math.round(position * 10) / 10,
      playedSeconds: Math.round(Math.max(0, Math.min(maxPlayed, played)) * 10) / 10,
      clientUpdatedUtc: new Date(t).toISOString(),
      sessionId
    };
    played = 0;
    lastSentAt = t;
    lastSentPosition = position;
    try {
      const result = send(body);
      if (result && typeof result.catch === 'function') result.catch(() => {});
    } catch {
      // A failed send is not retried here; the next report carries the latest position.
    }
    return body;
  }

  function advance(nextPosition) {
    const t = now();
    const p = clampPosition(nextPosition);
    if (playing && lastPosition !== null && lastTick !== null) {
      const delta = p - lastPosition;
      const wall = Math.max(0, (t - lastTick) / 1000);
      // Only forward motion that real playback could produce counts (1 second tolerance for timer jitter).
      if (delta > 0 && delta <= wall * rate + 1) played += delta;
    }
    position = p;
    lastPosition = p;
    lastTick = t;
  }

  return {
    sessionId,
    /** Set the known duration (from the media or the player). */
    setDuration(value) { duration = num(value, duration); },
    /** Set the playback rate (1, 1.5, 2 ...). */
    setRate(value) { rate = Math.max(0.25, Math.min(4, num(value, 1))); },
    /** Playback started or resumed at `positionSeconds`. */
    play(positionSeconds) {
      playing = true;
      position = clampPosition(positionSeconds === undefined ? position : positionSeconds);
      lastPosition = position;
      lastTick = now();
    },
    /** A time update while playing; sends when the interval has passed. Returns the body sent, if any. */
    timeUpdate(positionSeconds) {
      advance(positionSeconds);
      return playing ? report(false) : null;
    },
    /** Playback paused; sends now. */
    pause(positionSeconds) {
      if (positionSeconds !== undefined) advance(positionSeconds);
      playing = false;
      return report(true);
    },
    /** A seek finished at `positionSeconds` (the jump never counts as played); sends now. */
    seeked(positionSeconds) {
      position = clampPosition(positionSeconds);
      lastPosition = position;
      lastTick = now();
      return report(true);
    },
    /** Playback reached the end; sends now. */
    ended(positionSeconds) {
      advance(positionSeconds === undefined ? (duration || position) : positionSeconds);
      playing = false;
      return report(true);
    },
    /** Send whatever is pending (exit, app background, tab hidden). */
    flush() { return report(true); },
    /** Stop reporting (after a final flush). */
    close() {
      if (!closed) report(true);
      closed = true;
    },
    /** Current state (for tests and debugging). */
    get state() { return { position, played, playing, lastSentAt }; }
  };
}

/**
 * A storage-agnostic offline queue for progress reports (section 5.5): mobile stores the reports it
 * could not send and replays them on reconnect with their original `clientUpdatedUtc`. Entries
 * older than 7 days are dropped. `storage` is anything with `getItem`, `setItem`, and
 * `removeItem` (sync or async: AsyncStorage, localStorage, or a Map wrapper in tests).
 *
 * @param {{ storage: { getItem: Function, setItem: Function, removeItem?: Function }, key?: string, maxEntries?: number, now?: () => number }} options
 */
export function createProgressQueue({ storage, key = 'tk.library.progressQueue', maxEntries = 500, now = () => Date.now() } = {}) {
  if (!storage || typeof storage.getItem !== 'function' || typeof storage.setItem !== 'function') throw new Error('A progress queue needs a storage with getItem and setItem.');
  const maxAgeMs = OFFLINE_PROGRESS_MAX_AGE_DAYS * 86400000;
  let chain = Promise.resolve();

  const serial = (fn) => {
    const next = chain.then(fn, fn);
    chain = next.catch(() => {});
    return next;
  };

  async function read() {
    let raw;
    try {
      raw = await storage.getItem(key);
    } catch {
      raw = null;
    }
    if (!raw) return [];
    try {
      const list = JSON.parse(raw);
      return Array.isArray(list) ? list.filter((e) => e && e.lessonId && e.body) : [];
    } catch {
      return [];
    }
  }

  async function write(list) {
    if (!list.length && typeof storage.removeItem === 'function') await storage.removeItem(key);
    else await storage.setItem(key, JSON.stringify(list));
  }

  const fresh = (entry) => {
    const t = new Date(entry.body.clientUpdatedUtc).getTime();
    return Number.isFinite(t) && now() - t <= maxAgeMs;
  };

  return {
    /** Add a report that could not be sent. */
    enqueue(lessonId, body) {
      return serial(async () => {
        const list = (await read()).filter(fresh);
        list.push({ lessonId, body: { ...body, clientUpdatedUtc: body.clientUpdatedUtc || new Date(now()).toISOString() } });
        await write(list.slice(-maxEntries));
        return list.length;
      });
    },
    /** The queued entries (fresh ones only). */
    entries() { return serial(async () => (await read()).filter(fresh)); },
    /** Number of queued fresh entries. */
    size() { return serial(async () => (await read()).filter(fresh).length); },
    /**
     * Replay queued reports in order through `send(lessonId, body)`. A network failure or a `429`
     * or `5xx` stops the replay and keeps the rest; other errors (for example `403`, `404`) drop
     * that entry. Resolves `{ sent, dropped, remaining }`.
     * @param {(lessonId: string, body: object) => Promise<any>} sendFn
     */
    replay(sendFn) {
      return serial(async () => {
        const all = await read();
        const list = all.filter(fresh);
        let dropped = all.length - list.length;
        let sent = 0;
        let index = 0;
        for (; index < list.length; index += 1) {
          const entry = list[index];
          try {
            await sendFn(entry.lessonId, entry.body);
            sent += 1;
          } catch (err) {
            const status = err && typeof err.status === 'number' ? err.status : 0;
            if (status === 0 || status === 429 || status >= 500) break;
            dropped += 1;
          }
        }
        const remaining = list.slice(index);
        await write(remaining);
        return { sent, dropped, remaining: remaining.length };
      });
    },
    /** Remove every queued entry (sign-out, account deletion). */
    clear() { return serial(() => write([])); }
  };
}

/** Text for a lesson's level chips. */
export function levelLabels(property, levels) {
  return (levels || []).map((l) => (isObject(l) ? l.label || findLevel(property, l.key).label : findLevel(property, l).label));
}
