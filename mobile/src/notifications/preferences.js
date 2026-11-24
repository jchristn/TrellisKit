/**
 * Notification preferences helpers (docs/PHASE2_CONTRACT.md sections 8.5 and 8.6).
 */

/** Categories in display order. Unknown categories from the server are shown after these. */
export const CategoryOrder = Object.freeze(['Messages', 'Replies', 'Mentions', 'Reactions', 'FeedPosts', 'Following', 'Forums', 'Ideas', 'Announcements', 'Moderation', 'Account', 'Support']);

/** Defaults for a new account: every channel on, notification hours 7:00 to 22:00 (contract 8.5). */
export const DefaultHours = Object.freeze({ enabled: true, start: '07:00', end: '22:00' });

/** Step for the notification hours controls, in minutes. */
export const HoursStepMinutes = 30;

/** The device's IANA time zone, or '' when the runtime cannot tell. */
export function deviceTimeZone() {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || '';
  } catch {
    return '';
  }
}

/** Minutes after midnight for `HH:mm`, or null when malformed. */
export function parseTime(value) {
  const match = /^([01]\d|2[0-3]):([0-5]\d)$/.exec(String(value || ''));
  return match ? parseInt(match[1], 10) * 60 + parseInt(match[2], 10) : null;
}

/** `HH:mm` for minutes after midnight (wrapping around the day). */
export function formatTime(minutes) {
  const m = ((Math.round(minutes) % 1440) + 1440) % 1440;
  return String(Math.floor(m / 60)).padStart(2, '0') + ':' + String(m % 60).padStart(2, '0');
}

/** Move a `HH:mm` value by `steps` of HoursStepMinutes, wrapping at midnight. */
export function stepTime(value, steps) {
  const current = parseTime(value);
  const base = current === null ? 0 : current;
  return formatTime(base + steps * HoursStepMinutes);
}

/** A `HH:mm` value as 12-hour clock text, for example "7:00 AM" or "10:30 PM". */
export function displayTime(value) {
  const minutes = parseTime(value);
  if (minutes === null) return String(value || '');
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  const suffix = h < 12 ? 'AM' : 'PM';
  const hour = h % 12 === 0 ? 12 : h % 12;
  return hour + ':' + String(m).padStart(2, '0') + ' ' + suffix;
}

/**
 * Whether a moment falls inside the notification hours (start inclusive, end exclusive; the
 * window may cross midnight; start equal to end, or disabled, means all day).
 * @param {{ enabled: boolean, start: string, end: string }} hours
 * @param {number} minuteOfDay Local minutes after midnight.
 */
export function isWithinHours(hours, minuteOfDay) {
  if (!hours || !hours.enabled) return true;
  const start = parseTime(hours.start);
  const end = parseTime(hours.end);
  if (start === null || end === null || start === end) return true;
  if (start < end) return minuteOfDay >= start && minuteOfDay < end;
  return minuteOfDay >= start || minuteOfDay < end;
}

/**
 * Sort a preferences document's categories into display order.
 * @param {{ categories?: object[] }} prefs
 */
export function orderedCategories(prefs) {
  const list = (prefs && Array.isArray(prefs.categories) ? prefs.categories : []).slice();
  const rank = (c) => {
    const i = CategoryOrder.indexOf(c.category);
    return i < 0 ? CategoryOrder.length : i;
  };
  return list.sort((a, b) => rank(a) - rank(b));
}

/**
 * Apply one toggle locally. Turning in-app off also turns push off (push requires an in-app
 * notification, contract 8.5).
 * @returns {{ prefs: object, change: object }} The new document and the PUT body for this change.
 */
export function toggleCategory(prefs, category, channel, value) {
  const change = { category, [channel]: value };
  const categories = (prefs.categories || []).map((c) => {
    if (c.category !== category) return c;
    const next = { ...c, [channel]: value };
    if (channel === 'inApp' && !value && next.push) {
      next.push = false;
      change.push = false;
    }
    if (channel === 'push' && value && !next.inApp && next.editable && next.editable.inApp) {
      next.inApp = true;
      change.inApp = true;
    }
    return next;
  });
  return { prefs: { ...prefs, categories }, change };
}
