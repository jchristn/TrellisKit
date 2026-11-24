/**
 * Format a timestamp relative to now, for example "5m", "3h", "2d", or a short date.
 * @param {string|Date} value ISO string or Date.
 * @param {Date} [now]
 * @returns {string}
 */
export function formatRelativeTime(value, now = new Date()) {
  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const seconds = Math.max(0, Math.floor((now.getTime() - date.getTime()) / 1000));
  if (seconds < 60) return 'now';
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return minutes + 'm';
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return hours + 'h';
  const days = Math.floor(hours / 24);
  if (days < 7) return days + 'd';
  return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: date.getFullYear() === now.getFullYear() ? undefined : 'numeric' });
}

/**
 * Format the time until a future timestamp, for example "in 5m", "in 12h", "in 2d"; "now" once it has passed
 * (use for due dates and schedules; `formatRelativeTime` is for past times).
 * @param {string|Date} value ISO string or Date.
 * @param {Date} [now]
 * @returns {string}
 */
export function formatTimeUntil(value, now = new Date()) {
  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const seconds = Math.floor((date.getTime() - now.getTime()) / 1000);
  if (seconds < 60) return 'now';
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return 'in ' + minutes + 'm';
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return 'in ' + hours + 'h';
  return 'in ' + Math.floor(hours / 24) + 'd';
}

/**
 * Format a `Retry-After` wait for a sentence, rounding up: "30 seconds", "1 minute", "12 minutes",
 * "about 2 hours" (whole hours), "about 1 day". Returns null when there is no usable value.
 * @param {number|null|undefined} seconds
 * @returns {string|null}
 */
export function formatRetryAfter(seconds) {
  if (typeof seconds !== 'number' || !Number.isFinite(seconds) || seconds <= 0) return null;
  const plural = (n, unit) => n + ' ' + unit + (n === 1 ? '' : 's');
  if (seconds < 60) return plural(Math.ceil(seconds), 'second');
  const minutes = Math.ceil(seconds / 60);
  if (minutes < 60) return plural(minutes, 'minute');
  const hours = Math.ceil(minutes / 60);
  if (hours < 24) return 'about ' + plural(hours, 'hour');
  return 'about ' + plural(Math.ceil(hours / 24), 'day');
}

/**
 * Format a count compactly: 999, 1.2K, 3.4M.
 * @param {number} value
 * @returns {string}
 */
export function formatCount(value) {
  if (typeof value !== 'number' || !Number.isFinite(value)) return '0';
  if (Math.abs(value) < 1000) return String(value);
  if (Math.abs(value) < 1000000) return (value / 1000).toFixed(value < 10000 ? 1 : 0).replace(/\.0$/, '') + 'K';
  return (value / 1000000).toFixed(1).replace(/\.0$/, '') + 'M';
}
