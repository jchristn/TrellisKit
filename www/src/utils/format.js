/**
 * Formatting helpers. The site is English only, so formatting uses an explicit 'en-US' locale
 * rather than the browser default.
 */

export const LOCALE = 'en-US';

const dateTimeFormat = new Intl.DateTimeFormat(LOCALE, {
  year: 'numeric', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit'
});
const preciseDateTimeFormat = new Intl.DateTimeFormat(LOCALE, {
  year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit'
});
const shortDateTimeFormat = new Intl.DateTimeFormat(LOCALE, {
  month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit'
});
const timeFormat = new Intl.DateTimeFormat(LOCALE, { hour: 'numeric', minute: '2-digit' });
const preciseTimeFormat = new Intl.DateTimeFormat(LOCALE, { hour: '2-digit', minute: '2-digit', second: '2-digit' });
const numberFormat = new Intl.NumberFormat(LOCALE);

function toDate(value) {
  if (value === null || value === undefined || value === '') return null;
  const date = value instanceof Date ? value : new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

/** Timestamp without seconds, for example "Nov 11, 2026, 10:15 AM" (member and admin pages). */
export function formatDateTime(value) {
  const date = toDate(value);
  return date ? dateTimeFormat.format(date) : '';
}

/** Timestamp with seconds, for example "Nov 11, 2026, 10:15:02 AM", where seconds matter (request history, audit, update logs). */
export function formatPreciseDateTime(value) {
  const date = toDate(value);
  return date ? preciseDateTimeFormat.format(date) : '';
}

/** Compact timestamp for tables and chart labels. */
export function formatShortDateTime(value) {
  const date = toDate(value);
  return date ? shortDateTimeFormat.format(date) : '';
}

/** Time of day without seconds, for example "10:15 AM". */
export function formatTime(value) {
  const date = toDate(value);
  return date ? timeFormat.format(date) : '';
}

/** Time of day with seconds, for example "10:15:02 AM" (logs). */
export function formatPreciseTime(value) {
  const date = toDate(value);
  return date ? preciseTimeFormat.format(date) : '';
}

/** Integer with thousands separators. */
export function formatNumber(value) {
  return typeof value === 'number' && Number.isFinite(value) ? numberFormat.format(value) : '0';
}

/** Duration in milliseconds, for example "1.7 ms", "52 ms", "1.25 s". */
export function formatDuration(ms) {
  if (typeof ms !== 'number' || !Number.isFinite(ms)) return '';
  if (ms < 10) return ms.toFixed(1) + ' ms';
  if (ms < 1000) return Math.round(ms) + ' ms';
  return (ms / 1000).toFixed(2) + ' s';
}

/** Byte count, for example "120 B", "4.2 KB". */
export function formatBytes(bytes) {
  if (typeof bytes !== 'number' || !Number.isFinite(bytes)) return '';
  if (bytes < 1024) return bytes + ' B';
  const units = ['KB', 'MB', 'GB'];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return value.toFixed(value < 10 ? 1 : 0) + ' ' + units[unit];
}

/** Ratio (0 to 1) as a percentage, for example "96.0%". */
export function formatPercent(ratio) {
  if (typeof ratio !== 'number' || !Number.isFinite(ratio)) return '';
  return (ratio * 100).toFixed(1) + '%';
}

/** Uptime in seconds, for example "3d 4h", "2h 5m", "45s". */
export function formatUptime(seconds) {
  if (typeof seconds !== 'number' || !Number.isFinite(seconds) || seconds < 0) return '';
  const s = Math.floor(seconds);
  const days = Math.floor(s / 86400);
  const hours = Math.floor((s % 86400) / 3600);
  const minutes = Math.floor((s % 3600) / 60);
  if (days > 0) return days + 'd ' + hours + 'h';
  if (hours > 0) return hours + 'h ' + minutes + 'm';
  if (minutes > 0) return minutes + 'm ' + (s % 60) + 's';
  return s + 's';
}

/** Pretty-print a string as JSON when it parses, otherwise return it unchanged. */
export function prettyBody(text) {
  if (text === null || text === undefined) return '';
  if (typeof text !== 'string') return JSON.stringify(text, null, 2);
  const trimmed = text.trim();
  if (!(trimmed.startsWith('{') || trimmed.startsWith('['))) return text;
  try {
    return JSON.stringify(JSON.parse(trimmed), null, 2);
  } catch {
    return text;
  }
}

const dateFormat = new Intl.DateTimeFormat(LOCALE, { year: 'numeric', month: 'long', day: 'numeric' });
const shortDateFormat = new Intl.DateTimeFormat(LOCALE, { year: 'numeric', month: 'short', day: 'numeric' });

/** Calendar date, for example "March 1, 2027". Date-only strings (YYYY-MM-DD) are read as local dates. */
export function formatDate(value) {
  if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)) {
    const [y, m, d] = value.split('-').map(Number);
    return dateFormat.format(new Date(y, m - 1, d));
  }
  const date = toDate(value);
  return date ? dateFormat.format(date) : '';
}

/** Compact calendar date, for example "Mar 1, 2027". */
export function formatShortDate(value) {
  if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)) {
    const [y, m, d] = value.split('-').map(Number);
    return shortDateFormat.format(new Date(y, m - 1, d));
  }
  const date = toDate(value);
  return date ? shortDateFormat.format(date) : '';
}

/** US dollars, for example "$12.50". */
export function formatUsd(value) {
  return typeof value === 'number' && Number.isFinite(value)
    ? new Intl.NumberFormat(LOCALE, { style: 'currency', currency: 'USD' }).format(value)
    : '';
}
