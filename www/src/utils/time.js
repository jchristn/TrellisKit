/**
 * Time range presets for request activity charts, matching FRONTEND_ARCHITECTURE.md.
 */

export const RANGES = Object.freeze({
  hour: { id: 'hour', bucketMinutes: 1, bucketCount: 60 },
  day: { id: 'day', bucketMinutes: 15, bucketCount: 96 },
  week: { id: 'week', bucketMinutes: 120, bucketCount: 84 },
  month: { id: 'month', bucketMinutes: 360, bucketCount: 120 }
});

export const RANGE_IDS = Object.freeze(['hour', 'day', 'week', 'month']);

/**
 * Compute the query window for a range. The end is the current time rounded up to the next
 * bucket boundary (UTC) so buckets line up across refreshes.
 * @param {string} rangeId
 * @param {Date} [now]
 * @returns {{ fromUtc: string, toUtc: string, bucketMinutes: number, bucketCount: number }}
 */
export function rangeToParams(rangeId, now = new Date()) {
  const range = RANGES[rangeId] || RANGES.day;
  const bucketMs = range.bucketMinutes * 60000;
  const end = Math.ceil(now.getTime() / bucketMs) * bucketMs;
  const start = end - bucketMs * range.bucketCount;
  return {
    fromUtc: new Date(start).toISOString(),
    toUtc: new Date(end).toISOString(),
    bucketMinutes: range.bucketMinutes,
    bucketCount: range.bucketCount
  };
}

function pad(n) {
  return String(n).padStart(2, '0');
}

/**
 * Convert an ISO UTC string to the value a datetime-local input expects (local time).
 * @param {string} iso
 * @returns {string}
 */
export function isoToLocalInput(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + 'T' + pad(d.getHours()) + ':' + pad(d.getMinutes());
}

/**
 * Convert a datetime-local input value (local time) to an ISO UTC string.
 * @param {string} value
 * @returns {string}
 */
export function localInputToIso(value) {
  if (!value) return '';
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? '' : d.toISOString();
}
