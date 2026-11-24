import strings from '../strings.js';
import { LOCALE } from './format.js';

/**
 * Usage metering helpers (docs/USAGE_METERING_CONTRACT.md): the meter catalog, periods and URL
 * state, custom range validation, display-only period math, chart data shaping, formatting, the
 * "Show data" table, and the one-sentence text alternative of each chart. No React here.
 *
 * The server is authoritative for every period boundary; the period math below is used only to
 * label the page and to validate input before a request is made.
 */

const u = strings.usage;

/**
 * Period ids in picker order (the backend enum names sent as `?period=`). `Range` is the custom
 * range. `PreviousQuarter` is the whole previous calendar quarter and does not end at now.
 */
export const USAGE_PERIODS = Object.freeze(['LastDay', 'LastWeek', 'LastMonth', 'Last90Days', 'PreviousQuarter', 'ThisQuarter', 'LastYear', 'ThisWeek', 'ThisMonth', 'ThisYear']);
export const RANGE_PERIOD = 'Range';
export const DEFAULT_PERIOD = 'LastMonth';
export const USAGE_BUCKETS = Object.freeze(['Hour', 'Day', 'Week', 'Month']);
export const MAX_RANGE_DAYS = 1830;
export const MAX_BUCKETS = 400;
export const EXPORT_FORMATS = Object.freeze(['csv', 'json']);

/** Meter groups in page order (the contract's group names). */
export const USAGE_GROUPS = Object.freeze(['users', 'content', 'interactions', 'media', 'storage', 'platform', 'ai']);

/** Storage classes stacked in the storage chart, bottom to top. */
export const STORAGE_CLASSES = Object.freeze(['storage.originals', 'storage.imageVariants', 'storage.videoRenditions', 'storage.updateAssets', 'storage.exports']);

/**
 * The meter catalog (contract section 2). `chart` is the chart form (`bars` for counts per
 * bucket, `line` or `area` for gauges, null for no chart of its own), `card` puts the meter in
 * the summary cards, and `upIsBad` flips the change tone colors (more errors is worse).
 */
export const USAGE_METERS = Object.freeze([
  { id: 'users.total', group: 'users', kind: 'Gauge', unit: 'count', chart: 'line', card: true },
  { id: 'users.new', group: 'users', kind: 'Count', unit: 'count', chart: 'bars', card: true },
  { id: 'users.active', group: 'users', kind: 'Distinct', unit: 'count', chart: 'bars', card: true },
  { id: 'users.deleted', group: 'users', kind: 'Count', unit: 'count', chart: 'bars', card: true, upIsBad: true },
  { id: 'users.activeWeekly', group: 'users', kind: 'Distinct', unit: 'count', chart: null, card: false },
  { id: 'users.activeMonthly', group: 'users', kind: 'Distinct', unit: 'count', chart: null, card: false },
  { id: 'posts.new', group: 'content', kind: 'Count', unit: 'count', chart: 'bars', card: true },
  { id: 'comments.new', group: 'content', kind: 'Count', unit: 'count', chart: 'bars', card: true },
  { id: 'posts.total', group: 'content', kind: 'Gauge', unit: 'count', chart: 'line', card: true },
  { id: 'comments.total', group: 'content', kind: 'Gauge', unit: 'count', chart: 'line', card: true },
  { id: 'interactions.new', group: 'interactions', kind: 'Count', unit: 'count', chart: 'bars', card: true },
  { id: 'reactions.new', group: 'interactions', kind: 'Count', unit: 'count', chart: 'bars', card: true },
  { id: 'pollVotes.new', group: 'interactions', kind: 'Count', unit: 'count', chart: 'bars', card: true },
  { id: 'follows.new', group: 'interactions', kind: 'Count', unit: 'count', chart: 'bars', card: true },
  { id: 'assistantMessages.new', group: 'interactions', kind: 'Count', unit: 'count', chart: 'bars', card: true },
  { id: 'images.new', group: 'media', kind: 'Count', unit: 'count', chart: 'bars', card: true },
  { id: 'videos.new', group: 'media', kind: 'Count', unit: 'count', chart: 'bars', card: true },
  { id: 'images.bytes', group: 'media', kind: 'Count', unit: 'bytes', chart: 'bars', card: true },
  { id: 'videos.bytes', group: 'media', kind: 'Count', unit: 'bytes', chart: 'bars', card: true },
  { id: 'images.total', group: 'media', kind: 'Gauge', unit: 'count', chart: 'line', card: true },
  { id: 'videos.total', group: 'media', kind: 'Gauge', unit: 'count', chart: 'line', card: true },
  { id: 'storage.total', group: 'storage', kind: 'Gauge', unit: 'bytes', chart: 'area', card: true },
  { id: 'storage.originals', group: 'storage', kind: 'Gauge', unit: 'bytes', chart: null, card: true },
  { id: 'storage.imageVariants', group: 'storage', kind: 'Gauge', unit: 'bytes', chart: null, card: true },
  { id: 'storage.videoRenditions', group: 'storage', kind: 'Gauge', unit: 'bytes', chart: null, card: true },
  { id: 'storage.updateAssets', group: 'storage', kind: 'Gauge', unit: 'bytes', chart: null, card: true },
  { id: 'storage.exports', group: 'storage', kind: 'Gauge', unit: 'bytes', chart: null, card: true },
  { id: 'api.requests', group: 'platform', kind: 'Count', unit: 'count', chart: 'bars', card: true },
  { id: 'api.errors', group: 'platform', kind: 'Count', unit: 'count', chart: 'bars', card: true, upIsBad: true },
  { id: 'push.sent', group: 'platform', kind: 'Count', unit: 'count', chart: 'bars', card: true },
  { id: 'email.sent', group: 'platform', kind: 'Count', unit: 'count', chart: 'bars', card: true },
  { id: 'ai.requests', group: 'ai', kind: 'Count', unit: 'count', chart: 'bars', card: true },
  { id: 'ai.promptTokens', group: 'ai', kind: 'Count', unit: 'tokens', chart: 'bars', card: true },
  { id: 'ai.completionTokens', group: 'ai', kind: 'Count', unit: 'tokens', chart: 'bars', card: true },
  { id: 'ai.costMicros', group: 'ai', kind: 'Count', unit: 'usdMicros', chart: 'bars', card: true }
]);

/** Catalog entry of a meter id, or null. */
export function meterInfo(id) {
  return USAGE_METERS.find((m) => m.id === id) || null;
}

/** Display label of a meter id (falls back to the id). */
export function meterLabel(id) {
  return u.meters[id] || id;
}

/** The chart form a meter kind gets by default: bars for counts, line for gauges. */
export function chartTypeForKind(kind) {
  return kind === 'Gauge' ? 'line' : 'bars';
}

// ---------------------------------------------------------------------------------------------
// Civil dates and time zones (Intl.DateTimeFormat only; no date library)
// ---------------------------------------------------------------------------------------------

const DATE_RE = /^(\d{4})-(\d{2})-(\d{2})$/;
const DAY_MS = 86400000;
const HOUR_MS = 3600000;
const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const partsFormats = new Map();

function pad2(n) {
  return String(n).padStart(2, '0');
}

function partsFormat(timeZone) {
  let format = partsFormats.get(timeZone);
  if (!format) {
    format = new Intl.DateTimeFormat('en-US', {
      timeZone, hourCycle: 'h23', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit'
    });
    partsFormats.set(timeZone, format);
  }
  return format;
}

/** Whether `timeZone` is an IANA zone id this runtime knows. */
export function isValidTimeZone(timeZone) {
  if (!timeZone || typeof timeZone !== 'string') return false;
  try {
    partsFormat(timeZone);
    return true;
  } catch {
    return false;
  }
}

function safeZone(timeZone) {
  return isValidTimeZone(timeZone) ? timeZone : 'UTC';
}

/** Wall-clock fields of an instant in a time zone. */
export function zonedParts(instant, timeZone) {
  const out = {};
  for (const part of partsFormat(safeZone(timeZone)).formatToParts(new Date(instant))) {
    if (part.type !== 'literal') out[part.type] = Number(part.value);
  }
  if (out.hour === 24) out.hour = 0;
  return out;
}

/** Offset of a time zone from UTC at an instant, in milliseconds (local minus UTC). */
export function zoneOffsetMs(instant, timeZone) {
  const t = new Date(instant).getTime();
  const p = zonedParts(t, timeZone);
  const asUtc = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second);
  return asUtc - (t - (t % 1000));
}

/** Parse `YYYY-MM-DD` into `{ year, month, day }`, or null when it is not a real date. */
export function parseLocalDate(text) {
  const m = DATE_RE.exec(String(text || ''));
  if (!m) return null;
  const year = Number(m[1]);
  const month = Number(m[2]);
  const day = Number(m[3]);
  const check = new Date(Date.UTC(year, month - 1, day));
  if (check.getUTCFullYear() !== year || check.getUTCMonth() !== month - 1 || check.getUTCDate() !== day) return null;
  return { year, month, day };
}

function dateFromUtcMs(ms) {
  const d = new Date(ms);
  return d.getUTCFullYear() + '-' + pad2(d.getUTCMonth() + 1) + '-' + pad2(d.getUTCDate());
}

function civilMs(text) {
  const p = parseLocalDate(text);
  return p ? Date.UTC(p.year, p.month - 1, p.day) : NaN;
}

/** Add whole days to a local date (`YYYY-MM-DD`). Daylight saving never matters for civil dates. */
export function addDays(text, days) {
  return dateFromUtcMs(civilMs(text) + days * DAY_MS);
}

/** Whole days from local date `a` to local date `b` (b minus a). */
export function daysBetween(a, b) {
  return Math.round((civilMs(b) - civilMs(a)) / DAY_MS);
}

/** Day of the week of a local date (0 Sunday to 6 Saturday). */
export function weekdayOf(text) {
  return new Date(civilMs(text)).getUTCDay();
}

/** The local date (`YYYY-MM-DD`) of an instant in a time zone. */
export function localDateOf(instant, timeZone) {
  const p = zonedParts(instant, timeZone);
  return p.year + '-' + pad2(p.month) + '-' + pad2(p.day);
}

/** The UTC instant of local midnight at the start of a local date in a time zone. */
export function localMidnightUtc(text, timeZone) {
  const wall = civilMs(text);
  let guess = wall - zoneOffsetMs(wall, timeZone);
  guess = wall - zoneOffsetMs(guess, timeZone);
  return new Date(guess);
}

function quarterStart(p) {
  return p.year + '-' + pad2(Math.floor((p.month - 1) / 3) * 3 + 1) + '-01';
}

function previousQuarterStart(p) {
  const month = Math.floor((p.month - 1) / 3) * 3 + 1;
  return month === 1 ? (p.year - 1) + '-10-01' : p.year + '-' + pad2(month - 3) + '-01';
}

/**
 * The local bounds of a period, for display and validation only. Returns `{ fromDate, fromUtc,
 * toDate, toUtc }`: the local date the period starts on and the UTC instant of its start, and
 * the exclusive end (`now` for every period except `PreviousQuarter`, which ends at local
 * midnight of the current quarter's first day). Null for `Range` and unknown periods.
 * @param {string} period
 * @param {{ timeZone?: string, now?: Date|number|string, weekStartsOn?: string }} [options]
 */
export function periodBounds(period, { timeZone = 'UTC', now = new Date(), weekStartsOn = 'Monday' } = {}) {
  const zone = safeZone(timeZone);
  const nowMs = new Date(now).getTime();
  const toNow = { toDate: localDateOf(nowMs, zone), toUtc: new Date(nowMs) };
  if (period === 'LastDay') {
    const fromMs = Math.floor(nowMs / HOUR_MS) * HOUR_MS - 23 * HOUR_MS;
    return { fromDate: localDateOf(fromMs, zone), fromUtc: new Date(fromMs), ...toNow };
  }
  const today = localDateOf(nowMs, zone);
  const p = parseLocalDate(today);
  let fromDate;
  switch (period) {
    case 'LastWeek': fromDate = addDays(today, -6); break;
    case 'LastMonth': fromDate = addDays(today, -29); break;
    case 'Last90Days': fromDate = addDays(today, -89); break;
    case 'PreviousQuarter': {
      const toDate = quarterStart(p);
      fromDate = previousQuarterStart(p);
      return { fromDate, fromUtc: localMidnightUtc(fromDate, zone), toDate, toUtc: localMidnightUtc(toDate, zone) };
    }
    case 'ThisQuarter': fromDate = quarterStart(p); break;
    case 'LastYear': fromDate = addDays(today, -364); break;
    case 'ThisWeek': {
      const start = Math.max(0, WEEKDAYS.indexOf(weekStartsOn));
      fromDate = addDays(today, -((weekdayOf(today) - start + 7) % 7));
      break;
    }
    case 'ThisMonth': fromDate = p.year + '-' + pad2(p.month) + '-01'; break;
    case 'ThisYear': fromDate = p.year + '-01-01'; break;
    default: return null;
  }
  return { fromDate, fromUtc: localMidnightUtc(fromDate, zone), ...toNow };
}

/** The local start of a period (`{ fromDate, fromUtc }`), or null for `Range` and unknown periods. */
export function periodStart(period, options) {
  const bounds = periodBounds(period, options);
  return bounds ? { fromDate: bounds.fromDate, fromUtc: bounds.fromUtc } : null;
}

/**
 * Local start date of every period (`{ LastDay: 'YYYY-MM-DD', ... }`) for a zone and an instant.
 * @param {string} timeZone IANA zone id
 * @param {Date|number|string} now
 * @param {string} [weekStartsOn]
 */
export function periodFromDates(timeZone, now, weekStartsOn = 'Monday') {
  const out = {};
  for (const id of USAGE_PERIODS) out[id] = periodStart(id, { timeZone, now, weekStartsOn }).fromDate;
  return out;
}

/** Approximate length in days of a period or custom range (used for bucket choices only). */
export function periodLengthDays(state, { timeZone = 'UTC', now = new Date(), weekStartsOn = 'Monday' } = {}) {
  if (state.period === RANGE_PERIOD) {
    if (!parseLocalDate(state.from) || !parseLocalDate(state.to)) return 0;
    return daysBetween(state.from, state.to) + 1;
  }
  const bounds = periodBounds(state.period, { timeZone, now, weekStartsOn });
  if (!bounds) return 0;
  return Math.max(0, (bounds.toUtc.getTime() - bounds.fromUtc.getTime()) / DAY_MS);
}

/** The server's default bucket for a period length in days (contract section 3.3; Day up to 93 days, so a whole quarter is Day). */
export function defaultBucket(lengthDays) {
  if (lengthDays <= 2) return 'Hour';
  if (lengthDays <= 93) return 'Day';
  if (lengthDays <= 400) return 'Week';
  return 'Month';
}

/** Whether a bucket override is allowed for a period length (at most 400 buckets; Hour at most 14 days). */
export function bucketAllowed(bucket, lengthDays) {
  const days = Math.max(0, lengthDays);
  if (bucket === 'Hour') return days <= 14;
  if (bucket === 'Day') return Math.ceil(days) <= MAX_BUCKETS;
  if (bucket === 'Week') return Math.ceil(days / 7) <= MAX_BUCKETS;
  return bucket === 'Month';
}

// ---------------------------------------------------------------------------------------------
// URL state and the API query
// ---------------------------------------------------------------------------------------------

/**
 * Read the usage page state from URL search params: `period` (default LastMonth), `from` and
 * `to` (Range only), and `bucket` (optional override).
 * @param {URLSearchParams|string} params
 * @returns {{ period: string, from: string, to: string, bucket: string }}
 */
export function parseUsageState(params) {
  const sp = typeof params === 'string' ? new URLSearchParams(params) : params;
  const raw = sp.get('period') || '';
  const period = raw === RANGE_PERIOD || USAGE_PERIODS.includes(raw) ? raw : DEFAULT_PERIOD;
  const bucket = USAGE_BUCKETS.includes(sp.get('bucket')) ? sp.get('bucket') : '';
  const from = period === RANGE_PERIOD ? (sp.get('from') || '') : '';
  const to = period === RANGE_PERIOD ? (sp.get('to') || '') : '';
  return { period, from, to, bucket };
}

/**
 * Write the usage page state into URL search params, keeping unrelated params (for example
 * `tab`). The default period is left out so a plain URL stays plain.
 * @param {{ period: string, from?: string, to?: string, bucket?: string }} state
 * @param {URLSearchParams|string} [base]
 * @returns {URLSearchParams}
 */
export function serializeUsageState(state, base = '') {
  const sp = new URLSearchParams(base);
  for (const key of ['period', 'from', 'to', 'bucket']) sp.delete(key);
  if (state.period && state.period !== DEFAULT_PERIOD) sp.set('period', state.period);
  if (state.period === RANGE_PERIOD) {
    if (state.from) sp.set('from', state.from);
    if (state.to) sp.set('to', state.to);
  }
  if (state.bucket) sp.set('bucket', state.bucket);
  return sp;
}

/** The summary and export query for a page state (`from` and `to` only for Range). */
export function usageQuery(state) {
  const query = { period: state.period || DEFAULT_PERIOD };
  if (query.period === RANGE_PERIOD) {
    query.from = state.from;
    query.to = state.to;
  }
  if (state.bucket) query.bucket = state.bucket;
  return query;
}

/**
 * Validate a custom range of local dates. `today` is the local date of now in the property's
 * zone. Returns an object of error codes (`required`, `invalid`, `order`, `future`, `tooLong`)
 * keyed by `from` and `to`; empty when the range is valid.
 * @param {string} from
 * @param {string} to
 * @param {string} today
 */
export function validateRange(from, to, today) {
  const errors = {};
  if (!from) errors.from = 'required';
  else if (!parseLocalDate(from)) errors.from = 'invalid';
  if (!to) errors.to = 'required';
  else if (!parseLocalDate(to)) errors.to = 'invalid';
  if (errors.from || errors.to) return errors;
  if (today && daysBetween(today, from) > 0) errors.from = 'future';
  if (today && daysBetween(today, to) > 0) errors.to = 'future';
  if (errors.from || errors.to) return errors;
  if (daysBetween(from, to) < 0) errors.from = 'order';
  else if (daysBetween(from, to) + 1 > MAX_RANGE_DAYS) errors.to = 'tooLong';
  return errors;
}

// ---------------------------------------------------------------------------------------------
// Chart data shaping (UsageSummary to uPlot arrays)
// ---------------------------------------------------------------------------------------------

function num(value) {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

/** The summary's meter entry by id, or null. */
export function findMeter(summary, id) {
  const meters = (summary && Array.isArray(summary.meters)) ? summary.meters : [];
  return meters.find((m) => m && m.meter === id) || null;
}

/** Bucket start times as Unix seconds (uPlot's x values). */
export function bucketSeconds(summary) {
  const buckets = (summary && Array.isArray(summary.buckets)) ? summary.buckets : [];
  return buckets.map((b) => Math.floor(Date.parse(b && b.startUtc) / 1000));
}

/** A meter's values aligned to the summary buckets (missing or non-numeric values are null). */
export function meterValues(summary, id) {
  const count = (summary && Array.isArray(summary.buckets)) ? summary.buckets.length : 0;
  const meter = findMeter(summary, id);
  const values = meter && Array.isArray(meter.values) ? meter.values : [];
  const out = new Array(count);
  for (let i = 0; i < count; i += 1) out[i] = num(values[i]);
  return out;
}

/** uPlot data for one or more meters: `[xs, values of meter 1, ...]`. */
export function chartData(summary, ids) {
  return [bucketSeconds(summary), ...ids.map((id) => meterValues(summary, id))];
}

/**
 * Stacked area data: `data` is `[xs, cumulative 1, cumulative 2, ...]` where cumulative `i` is
 * the sum of layers 1 to `i` (nulls count as 0; a bucket where every layer is null stays null),
 * and `layers` keeps the raw values for tooltips and tables.
 */
export function stackedData(summary, ids) {
  const xs = bucketSeconds(summary);
  const layers = ids.map((id) => meterValues(summary, id));
  const cumulative = [];
  for (let i = 0; i < layers.length; i += 1) {
    const row = new Array(xs.length);
    for (let k = 0; k < xs.length; k += 1) {
      const anyValue = layers.some((layer) => layer[k] !== null);
      if (!anyValue) row[k] = null;
      else row[k] = (i > 0 && cumulative[i - 1][k] !== null ? cumulative[i - 1][k] : 0) + (layers[i][k] || 0);
    }
    cumulative.push(row);
  }
  return { data: [xs, ...cumulative], layers };
}

// ---------------------------------------------------------------------------------------------
// Formatting
// ---------------------------------------------------------------------------------------------

const countFormat = new Intl.NumberFormat(LOCALE);
const usdFormat = new Intl.NumberFormat(LOCALE, { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 2 });
const usdSmallFormat = new Intl.NumberFormat(LOCALE, { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 4 });
const BYTE_UNITS = ['KB', 'MB', 'GB', 'TB'];

/** A count with thousands separators; null shows a dash. */
export function formatCount(value) {
  const v = num(value);
  return v === null ? '-' : countFormat.format(Math.round(v));
}

/** Bytes in B, KB, MB, GB, or TB (1024 based); null shows a dash. */
export function formatUsageBytes(value) {
  const v = num(value);
  if (v === null) return '-';
  if (Math.abs(v) < 1024) return Math.round(v) + ' B';
  let scaled = v / 1024;
  let unit = 0;
  while (Math.abs(scaled) >= 1024 && unit < BYTE_UNITS.length - 1) {
    scaled /= 1024;
    unit += 1;
  }
  return scaled.toFixed(Math.abs(scaled) < 10 ? 1 : 0) + ' ' + BYTE_UNITS[unit];
}

/** Tokens as a count. */
export function formatTokens(value) {
  return formatCount(value);
}

/** Millionths of a US dollar as dollars ("$1.25"; amounts under a cent keep 4 decimals). */
export function formatUsdMicros(value) {
  const v = num(value);
  if (v === null) return '-';
  const dollars = v / 1e6;
  return dollars !== 0 && Math.abs(dollars) < 0.01 ? usdSmallFormat.format(dollars) : usdFormat.format(dollars);
}

/** A meter value in its unit (`count`, `bytes`, `tokens`, `usdMicros`). */
export function formatMeterValue(value, unit) {
  if (unit === 'bytes') return formatUsageBytes(value);
  if (unit === 'tokens') return formatTokens(value);
  if (unit === 'usdMicros') return formatUsdMicros(value);
  return formatCount(value);
}

/**
 * The change versus the previous period: `{ text, tone }` with tone `up`, `down`, or `flat`.
 * A null change (no previous data) reads "no previous data".
 */
export function formatChange(change) {
  const v = num(change);
  if (v === null) return { text: u.noPrevious, tone: 'flat' };
  const pct = v * 100;
  if (Math.abs(pct) < 0.05) return { text: '0.0%', tone: 'flat' };
  return { text: (pct > 0 ? '+' : '-') + Math.abs(pct).toFixed(1) + '%', tone: pct > 0 ? 'up' : 'down' };
}

const shortDate = new Intl.DateTimeFormat(LOCALE, { month: 'short', day: 'numeric', timeZone: 'UTC' });
const monthYear = new Intl.DateTimeFormat(LOCALE, { month: 'short', year: 'numeric', timeZone: 'UTC' });
const hourLabel = new Intl.DateTimeFormat(LOCALE, { month: 'short', day: 'numeric', hour: 'numeric', timeZone: 'UTC' });

/**
 * A bucket label for display. The server's `label` is already local (`YYYY-MM-DD`,
 * `YYYY-MM`, or `YYYY-MM-DDTHH:00`), so it is formatted as a wall-clock value without
 * converting zones.
 */
export function formatBucketLabel(label, bucket) {
  const text = String(label || '');
  const hour = /^(\d{4})-(\d{2})-(\d{2})T(\d{2})/.exec(text);
  if (hour) return hourLabel.format(new Date(Date.UTC(Number(hour[1]), Number(hour[2]) - 1, Number(hour[3]), Number(hour[4]))));
  const month = /^(\d{4})-(\d{2})$/.exec(text);
  if (month) return monthYear.format(new Date(Date.UTC(Number(month[1]), Number(month[2]) - 1, 1)));
  const day = parseLocalDate(text);
  if (day) {
    const formatted = shortDate.format(new Date(Date.UTC(day.year, day.month - 1, day.day)));
    if (bucket === 'Month') return monthYear.format(new Date(Date.UTC(day.year, day.month - 1, 1)));
    return bucket === 'Week' ? u.weekOf(formatted) : formatted;
  }
  return text;
}

/** A short label of a bucket for the text alternative (no "Week of" prefix). */
function sentenceLabel(label, bucket) {
  return bucket === 'Week' ? formatBucketLabel(label, 'Day') : formatBucketLabel(label, bucket);
}

const zonedDateTimeFormats = new Map();

/** An instant in a time zone, for example "Oct 14, 2026, 12:00 AM". */
export function formatZonedDateTime(value, timeZone) {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const zone = safeZone(timeZone);
  let format = zonedDateTimeFormats.get(zone);
  if (!format) {
    format = new Intl.DateTimeFormat(LOCALE, { timeZone: zone, year: 'numeric', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
    zonedDateTimeFormats.set(zone, format);
  }
  return format.format(date);
}

/** File name from a Content-Disposition header, or the fallback. */
export function filenameFromDisposition(header, fallback) {
  const text = String(header || '');
  const star = /filename\*=(?:UTF-8'')?([^;]+)/i.exec(text);
  if (star) {
    try {
      return decodeURIComponent(star[1].trim().replace(/^"|"$/g, ''));
    } catch {
      // Fall through to the plain parameter.
    }
  }
  const plain = /filename="?([^";]+)"?/i.exec(text);
  return plain ? plain[1].trim() : fallback;
}

// ---------------------------------------------------------------------------------------------
// The "Show data" table and the text alternative
// ---------------------------------------------------------------------------------------------

/**
 * Rows of the accessible data table of a chart: one row per bucket with the bucket label and
 * each meter's formatted value.
 * @param {object} summary UsageSummary
 * @param {string[]} ids meter ids (columns)
 * @param {{ total?: boolean }} [options] add a total column (the stacked storage chart)
 * @returns {{ columns: string[], rows: Array<{ key: string, label: string, cells: string[] }> }}
 */
export function buildDataTable(summary, ids, { total = false } = {}) {
  const buckets = (summary && Array.isArray(summary.buckets)) ? summary.buckets : [];
  const bucket = summary && summary.bucket;
  const series = ids.map((id) => ({ id, unit: (findMeter(summary, id) || meterInfo(id) || {}).unit, values: meterValues(summary, id) }));
  const columns = [u.bucketColumn, ...ids.map(meterLabel)];
  if (total) columns.push(u.totalColumn);
  const rows = buckets.map((b, i) => {
    const cells = series.map((s) => formatMeterValue(s.values[i], s.unit));
    if (total) {
      const present = series.filter((s) => s.values[i] !== null);
      cells.push(present.length ? formatMeterValue(present.reduce((sum, s) => sum + s.values[i], 0), series[0] && series[0].unit) : '-');
    }
    return { key: (b && (b.startUtc || b.label)) || String(i), label: formatBucketLabel(b && b.label, bucket), cells };
  });
  return { columns, rows };
}

function bucketNoun(summary) {
  return u.bucketNouns[summary && summary.bucket] || u.bucketNouns.Day;
}

function endpoints(summary) {
  const buckets = summary.buckets;
  return [sentenceLabel(buckets[0].label, summary.bucket), sentenceLabel(buckets[buckets.length - 1].label, summary.bucket)];
}

/**
 * One-sentence text alternative of a single-meter chart, for example "Posts created per day
 * from Oct 14 to Nov 12: total 120, peak 9 on Nov 3." Counts report the period total and the
 * peak bucket; gauges report the latest value, the low, and the high.
 * @param {object} summary UsageSummary
 * @param {string} id meter id
 * @param {string} [title] chart title (defaults to the meter label)
 */
export function describeChart(summary, id, title = meterLabel(id)) {
  const buckets = (summary && Array.isArray(summary.buckets)) ? summary.buckets : [];
  const meter = findMeter(summary, id);
  const values = meterValues(summary, id);
  const present = values.map((v, i) => ({ v, i })).filter((x) => x.v !== null);
  if (!buckets.length || !meter || !present.length) return u.describe.none(title);
  const unit = meter.unit || (meterInfo(id) || {}).unit;
  const fmt = (v) => formatMeterValue(v, unit);
  const [from, to] = endpoints(summary);
  const noun = bucketNoun(summary);
  const label = (i) => sentenceLabel(buckets[i].label, summary.bucket);
  const kind = meter.kind || (meterInfo(id) || {}).kind;
  if (kind === 'Gauge') {
    let low = present[0];
    let high = present[0];
    for (const x of present) {
      if (x.v < low.v) low = x;
      if (x.v > high.v) high = x;
    }
    const latest = num(meter.total) !== null ? meter.total : present[present.length - 1].v;
    return u.describe.gauge(title, noun, from, to, fmt(latest), fmt(low.v), label(low.i), fmt(high.v), label(high.i));
  }
  let peak = present[0];
  for (const x of present) if (x.v > peak.v) peak = x;
  const total = num(meter.total) !== null ? meter.total : present.reduce((s, x) => s + x.v, 0);
  return u.describe.count(title, noun, from, to, fmt(total), fmt(peak.v), peak.v > 0 ? label(peak.i) : '');
}

/** Text alternative of the stacked storage chart: the latest total and each class's latest value. */
export function describeStacked(summary, ids, title = u.storageByClass) {
  const buckets = (summary && Array.isArray(summary.buckets)) ? summary.buckets : [];
  const series = ids.map((id) => ({ id, values: meterValues(summary, id) }));
  let last = -1;
  for (let k = buckets.length - 1; k >= 0 && last < 0; k -= 1) if (series.some((s) => s.values[k] !== null)) last = k;
  if (!buckets.length || last < 0) return u.describe.none(title);
  const [from, to] = endpoints(summary);
  const total = series.reduce((sum, s) => sum + (s.values[last] || 0), 0);
  const parts = series.filter((s) => s.values[last] !== null).map((s) => meterLabel(s.id) + ' ' + formatUsageBytes(s.values[last])).join(', ');
  return u.describe.stacked(title, bucketNoun(summary), from, to, formatUsageBytes(total), parts);
}

/** Whether a summary has no buckets or no meter values at all. */
export function summaryIsEmpty(summary) {
  if (!summary || !Array.isArray(summary.buckets) || !summary.buckets.length) return true;
  const meters = Array.isArray(summary.meters) ? summary.meters : [];
  return !meters.some((m) => num(m.total) || (Array.isArray(m.values) && m.values.some((v) => num(v))));
}

// ---------------------------------------------------------------------------------------------
// Platform comparison table
// ---------------------------------------------------------------------------------------------

/** Orderings of the platform usage comparison (contract section 7.2). */
export const USAGE_PROPERTY_ORDERINGS = Object.freeze([
  'NameAscending', 'UsersDescending', 'ActiveUsersDescending', 'PostsDescending', 'CommentsDescending', 'InteractionsDescending',
  'ImagesDescending', 'VideosDescending', 'StorageDescending', 'ApiRequestsDescending', 'PushDescending', 'EmailDescending',
  'AiTokensDescending', 'AiCostDescending'
]);

/** PropertyUsageRow value columns with their unit and the ordering that sorts by them. */
export const USAGE_ROW_COLUMNS = Object.freeze([
  { key: 'usersTotal', unit: 'count', ordering: 'UsersDescending' },
  { key: 'usersNew', unit: 'count', ordering: null },
  { key: 'activeUsers', unit: 'count', ordering: 'ActiveUsersDescending' },
  { key: 'posts', unit: 'count', ordering: 'PostsDescending' },
  { key: 'comments', unit: 'count', ordering: 'CommentsDescending' },
  { key: 'interactions', unit: 'count', ordering: 'InteractionsDescending' },
  { key: 'images', unit: 'count', ordering: 'ImagesDescending' },
  { key: 'imageBytes', unit: 'bytes', ordering: null },
  { key: 'videos', unit: 'count', ordering: 'VideosDescending' },
  { key: 'videoBytes', unit: 'bytes', ordering: null },
  { key: 'storageBytes', unit: 'bytes', ordering: 'StorageDescending' },
  { key: 'apiRequests', unit: 'count', ordering: 'ApiRequestsDescending' },
  { key: 'pushSent', unit: 'count', ordering: 'PushDescending' },
  { key: 'emailSent', unit: 'count', ordering: 'EmailDescending' },
  { key: 'aiTokens', unit: 'tokens', ordering: 'AiTokensDescending' },
  { key: 'aiCostMicros', unit: 'usdMicros', ordering: 'AiCostDescending' }
]);

/** Sum each value column over a page of PropertyUsageRow (the "totals of this page" row). */
export function usageRowTotals(rows) {
  const totals = {};
  for (const col of USAGE_ROW_COLUMNS) totals[col.key] = 0;
  for (const row of rows || []) {
    for (const col of USAGE_ROW_COLUMNS) totals[col.key] += num(row && row[col.key]) || 0;
  }
  return totals;
}

/**
 * The POST /platform/usage/properties/enumerate body. Computed orderings page with `skip` only
 * (no continuation token), so the page always maps to `skip`.
 * @param {{ period: string, from?: string, to?: string }} state usage period state
 * @param {{ search?: string, status?: string, ordering?: string }} filters
 * @param {{ pageNumber: number, pageSize: number }} paging
 */
export function buildUsagePropertiesQuery(state, filters, { pageNumber = 1, pageSize = 50 } = {}) {
  const f = { period: state.period || DEFAULT_PERIOD };
  if (f.period === RANGE_PERIOD) {
    f.from = state.from;
    f.to = state.to;
  }
  if (filters && filters.status) f.status = filters.status;
  const body = {
    maxResults: pageSize,
    skip: (Math.max(1, pageNumber) - 1) * pageSize,
    ordering: (filters && filters.ordering) || 'NameAscending',
    filters: f
  };
  const search = filters && typeof filters.search === 'string' ? filters.search.trim() : '';
  if (search) body.search = search;
  const labels = filters && Array.isArray(filters.labels) ? filters.labels.filter(Boolean) : [];
  if (labels.length) {
    body.labels = labels;
    body.labelMatch = filters.labelMatch === 'Any' ? 'Any' : 'All';
  }
  const tags = filters && filters.tags && typeof filters.tags === 'object' ? filters.tags : {};
  if (Object.keys(tags).length) body.tags = { ...tags };
  return body;
}

/** URL keys of the usage overview table state (besides the period and the label and tag filters). */
export const USAGE_OVERVIEW_KEYS = Object.freeze({ search: 'q', status: 'status', ordering: 'order', page: 'page', pageSize: 'size' });

/**
 * The overview table state from the URL: search, status, ordering, page, and page size, so the
 * whole view (with the period and the label and tag filters) is shareable and survives reloads.
 * @param {URLSearchParams} params
 * @param {{ orderings?: readonly string[], pageSizes?: readonly number[], defaultPageSize?: number }} [options]
 */
export function parseUsageOverviewState(params, { orderings = USAGE_PROPERTY_ORDERINGS, pageSizes = [10, 25, 50, 100, 200], defaultPageSize = 50 } = {}) {
  const k = USAGE_OVERVIEW_KEYS;
  const ordering = orderings.includes(params.get(k.ordering)) ? params.get(k.ordering) : 'NameAscending';
  const page = Math.max(1, Math.floor(Number(params.get(k.page)) || 1));
  const size = pageSizes.includes(Number(params.get(k.pageSize))) ? Number(params.get(k.pageSize)) : defaultPageSize;
  return { search: params.get(k.search) || '', status: params.get(k.status) || '', ordering, pageNumber: page, pageSize: size };
}

/**
 * Write overview table changes into a copy of `params`; defaults are left out so a plain URL
 * stays plain. Changing anything but the page resets the page.
 * @param {URLSearchParams} params
 * @param {object} changes any of `{ search, status, ordering, pageNumber, pageSize }`
 * @param {{ defaultPageSize?: number }} [options]
 */
export function writeUsageOverviewState(params, changes, { defaultPageSize = 50 } = {}) {
  const k = USAGE_OVERVIEW_KEYS;
  const next = new URLSearchParams(params);
  const set = (key, value, fallback) => {
    if (value === undefined) return;
    if (value === '' || value === null || value === fallback) next.delete(key);
    else next.set(key, String(value));
  };
  set(k.search, changes.search, '');
  set(k.status, changes.status, '');
  set(k.ordering, changes.ordering, 'NameAscending');
  set(k.pageSize, changes.pageSize, defaultPageSize);
  if (changes.pageNumber !== undefined) set(k.page, changes.pageNumber, 1);
  else next.delete(k.page);
  return next;
}
