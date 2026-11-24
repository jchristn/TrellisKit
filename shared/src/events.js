/**
 * Events helpers (docs/PHASE4_CONTRACT.md section 4 plus resolved decisions 10 and 11): time
 * display in the event's zone with the viewer's zone as a second line, recurrence rule building and
 * plain English summaries, RSVP state for the RSVP control, calendar month grids, and category
 * lookups. Times use `Intl.DateTimeFormat` with IANA zones; nothing here names a property.
 */

/** Event formats. */
export const EventFormats = Object.freeze(['InPerson', 'Online', 'Hybrid']);
/** Event statuses (`Proposed` and `Rejected` belong to member proposals). */
export const EventStatuses = Object.freeze(['Draft', 'Proposed', 'Published', 'Canceled', 'Rejected', 'Withdrawn']);
/** RSVP statuses. */
export const RsvpStatus = Object.freeze({ Going: 'Going', Interested: 'Interested', Waitlisted: 'Waitlisted' });
/** Attendee list visibility. */
export const AttendeeVisibilities = Object.freeze(['Everyone', 'Attendees', 'Staff']);
/** Event list windows (`filters.when`). */
export const EventWhen = Object.freeze({ Upcoming: 'Upcoming', Past: 'Past', All: 'All' });
/** Recurrence frequencies. */
export const RecurrenceFrequency = Object.freeze({ Daily: 'Daily', Weekly: 'Weekly', Monthly: 'Monthly', Yearly: 'Yearly' });
/** Weekday codes in ISO order (Monday first). */
export const Weekdays = Object.freeze(['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']);
/** Limits a client checks before sending (sections 2.5 and 4.4). */
export const EVENT_LIMITS = Object.freeze({
  titleMin: 3, titleMax: 140, summaryMax: 300, descriptionMax: 10000, maxReminders: 3, reminderMinMinutes: 5, reminderMaxMinutes: 20160,
  maxGuests: 10, capacityMax: 100000, cancelReasonMin: 3, cancelReasonMax: 500, reviewNoteMin: 3, reviewNoteMax: 1000,
  intervalMax: 99, countMax: 200
});

const WEEKDAY_NAMES = Object.freeze({ Mon: 'Monday', Tue: 'Tuesday', Wed: 'Wednesday', Thu: 'Thursday', Fri: 'Friday', Sat: 'Saturday', Sun: 'Sunday' });
const ORDINALS = Object.freeze({ 1: 'first', 2: 'second', 3: 'third', 4: 'fourth', '-1': 'last' });
const UNITS = Object.freeze({ Daily: ['day', 'days'], Weekly: ['week', 'weeks'], Monthly: ['month', 'months'], Yearly: ['year', 'years'] });
const MONTHS = Object.freeze(['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December']);

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function safeZone(zone) {
  if (!zone) return 'UTC';
  try {
    new Intl.DateTimeFormat('en-US', { timeZone: zone }).format(0);
    return zone;
  } catch {
    return 'UTC';
  }
}

function fmt(date, zone, options, locale = 'en-US') {
  return new Intl.DateTimeFormat(locale, { timeZone: safeZone(zone), ...options }).format(date);
}

/** The local calendar date (`YYYY-MM-DD`) of an instant in a zone. */
export function dateKeyInZone(value, zone) {
  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: safeZone(zone), year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(date);
  const get = (t) => (parts.find((p) => p.type === t) || {}).value;
  return get('year') + '-' + get('month') + '-' + get('day');
}

/** Local wall time (`YYYY-MM-DDTHH:mm`) of an instant in a zone, for a `datetime-local` input. */
export function localInputValue(value, zone) {
  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: safeZone(zone), year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(date);
  const get = (t) => (parts.find((p) => p.type === t) || {}).value;
  return get('year') + '-' + get('month') + '-' + get('day') + 'T' + get('hour') + ':' + get('minute');
}

function zoneOffsetMinutes(date, zone) {
  const parts = new Intl.DateTimeFormat('en-US', { timeZone: safeZone(zone), year: 'numeric', month: 'numeric', day: 'numeric', hour: 'numeric', minute: 'numeric', second: 'numeric', hourCycle: 'h23' }).formatToParts(date);
  const get = (t) => Number((parts.find((p) => p.type === t) || {}).value);
  const asUtc = Date.UTC(get('year'), get('month') - 1, get('day'), get('hour') % 24, get('minute'), get('second'));
  return Math.round((asUtc - Math.floor(date.getTime() / 1000) * 1000) / 60000);
}

/** Whether two zones show the same wall clock at an instant (so the viewer line can be skipped). */
export function sameWallClock(date, zoneA, zoneB) {
  if (safeZone(zoneA) === safeZone(zoneB)) return true;
  return zoneOffsetMinutes(date, zoneA) === zoneOffsetMinutes(date, zoneB);
}

function timeRange(start, end, zone, locale) {
  const time = { hour: 'numeric', minute: '2-digit' };
  const startText = fmt(start, zone, time, locale);
  const sameDay = dateKeyInZone(start, zone) === dateKeyInZone(end, zone);
  if (sameDay) return { text: startText + ' to ' + fmt(end, zone, { ...time, timeZoneName: 'short' }, locale), sameDay };
  return { text: startText + ' to ' + fmt(end, zone, { weekday: 'short', month: 'short', day: 'numeric', ...time, timeZoneName: 'short' }, locale), sameDay };
}

/**
 * Display text for an event's time (section 2.5): the event's zone first, and the viewer's zone as a
 * second line when the wall clocks differ. All-day events show dates only.
 * @param {{ startUtc: string, endUtc: string, timeZone?: string, isAllDay?: boolean }} event
 * @param {string} [viewerZone] the viewer's IANA zone (the device zone)
 * @param {{ locale?: string }} [options]
 * @returns {{ primary: string, secondary: string|null, date: string, time: string, zone: string, isAllDay: boolean, multiDay: boolean }}
 */
export function formatEventTime(event, viewerZone, { locale = 'en-US' } = {}) {
  const e = event || {};
  const start = new Date(e.startUtc);
  const end = new Date(e.endUtc || e.startUtc);
  const zone = safeZone(e.timeZone || viewerZone || 'UTC');
  if (Number.isNaN(start.getTime())) return { primary: '', secondary: null, date: '', time: '', zone, isAllDay: Boolean(e.isAllDay), multiDay: false };
  const dateOptions = { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' };
  const date = fmt(start, zone, dateOptions, locale);
  if (e.isAllDay) {
    // An all-day end is exclusive local midnight; show the last day it covers.
    const lastDay = new Date(Math.max(start.getTime(), end.getTime() - 1));
    const multiDay = dateKeyInZone(start, zone) !== dateKeyInZone(lastDay, zone);
    const primary = multiDay ? date + ' to ' + fmt(lastDay, zone, dateOptions, locale) : date;
    return { primary, secondary: null, date, time: '', zone, isAllDay: true, multiDay };
  }
  const range = timeRange(start, end, zone, locale);
  const primary = date + ', ' + range.text;
  let secondary = null;
  const viewer = viewerZone ? safeZone(viewerZone) : null;
  if (viewer && !sameWallClock(start, zone, viewer)) {
    const viewerRange = timeRange(start, end, viewer, locale);
    secondary = fmt(start, viewer, dateOptions, locale) + ', ' + viewerRange.text;
  }
  return { primary, secondary, date, time: range.text, zone, isAllDay: false, multiDay: !range.sameDay };
}

/** Short card text: "Sat, Dec 5, 6:00 PM" in the event's zone (dates only for all-day events). */
export function formatEventShort(event, { locale = 'en-US' } = {}) {
  const e = event || {};
  const start = new Date(e.startUtc);
  if (Number.isNaN(start.getTime())) return '';
  const zone = safeZone(e.timeZone);
  if (e.isAllDay) return fmt(start, zone, { weekday: 'short', month: 'short', day: 'numeric' }, locale);
  return fmt(start, zone, { weekday: 'short', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit', timeZoneName: 'short' }, locale);
}

/**
 * Event details of a global search result (`SearchResultItem.event`), or null for other results.
 * Occurrences of a recurring series arrive collapsed into one result that opens the next upcoming
 * occurrence; canceled events are marked.
 * @param {object|null} hit
 * @returns {{ when: string, canceled: boolean, past: boolean, recurring: boolean, recurrenceSummary: string|null, occurrenceCount: number }|null}
 */
export function searchEventInfo(hit, { locale = 'en-US' } = {}) {
  const e = hit && hit.type === 'Event' && hit.event;
  if (!e) return null;
  return {
    when: formatEventShort({ startUtc: e.startUtc, timeZone: e.timeZone }, { locale }),
    canceled: Boolean(e.isCanceled),
    past: Boolean(e.isPast),
    recurring: Boolean(e.recurrenceId),
    recurrenceSummary: e.recurrenceSummary || null,
    occurrenceCount: Number(e.occurrenceCount) || 1
  };
}

/** Whether an event has ended (`endUtc` before now). */
export function isPastEvent(event, now = new Date()) {
  const end = new Date(event && (event.endUtc || event.startUtc));
  return !Number.isNaN(end.getTime()) && end.getTime() < now.getTime();
}

// ---- Recurrence (resolved decision 11) ----

/** A blank recurrence editor form for an event starting at `startLocal` (`YYYY-MM-DDTHH:mm` or a date). */
export function defaultRecurrenceForm(startLocal) {
  const day = weekdayOfLocal(startLocal);
  const monthDay = Number(String(startLocal || '').slice(8, 10)) || 1;
  return {
    enabled: false, frequency: RecurrenceFrequency.Weekly, interval: 1, weekdays: day ? [day] : [], monthlyMode: 'Date', monthDay,
    setPosition: setPositionOfLocal(startLocal) || 1, positionWeekday: day || 'Mon', ends: 'Never', count: 10, untilDate: ''
  };
}

/** The weekday code (`Mon` ... `Sun`) of a local date string, or null. */
export function weekdayOfLocal(localDate) {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(localDate || ''));
  if (!m) return null;
  const d = new Date(Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3])));
  return Weekdays[(d.getUTCDay() + 6) % 7];
}

/** Which occurrence of its weekday a local date is in its month (1 to 4, or -1 when it is the last). */
export function setPositionOfLocal(localDate) {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(localDate || ''));
  if (!m) return null;
  const year = Number(m[1]);
  const month = Number(m[2]);
  const day = Number(m[3]);
  const daysInMonth = new Date(Date.UTC(year, month, 0)).getUTCDate();
  if (day + 7 > daysInMonth) return -1;
  return Math.min(4, Math.ceil(day / 7));
}

/**
 * Build the API `recurrence` rule from the editor form, or null when repeating is off.
 * Rule shape: `{ frequency, interval, weekdays, monthDay, setPosition, count, untilDate }`.
 * @param {object} form see `defaultRecurrenceForm`
 */
export function buildRecurrenceRule(form) {
  if (!form || !form.enabled) return null;
  const frequency = Object.values(RecurrenceFrequency).includes(form.frequency) ? form.frequency : RecurrenceFrequency.Weekly;
  const rule = { frequency, interval: Math.max(1, Math.min(EVENT_LIMITS.intervalMax, Math.floor(Number(form.interval) || 1))), weekdays: [], monthDay: null, setPosition: null, count: null, untilDate: null };
  if (frequency === RecurrenceFrequency.Weekly) rule.weekdays = Weekdays.filter((d) => (form.weekdays || []).includes(d));
  if (frequency === RecurrenceFrequency.Monthly) {
    if (form.monthlyMode === 'Position') {
      rule.setPosition = [1, 2, 3, 4, -1].includes(Number(form.setPosition)) ? Number(form.setPosition) : 1;
      rule.weekdays = [Weekdays.includes(form.positionWeekday) ? form.positionWeekday : 'Mon'];
    } else {
      const md = Number(form.monthDay);
      rule.monthDay = md === -1 ? -1 : Math.max(1, Math.min(31, Math.floor(md) || 1));
    }
  }
  if (form.ends === 'After') rule.count = Math.max(1, Math.min(EVENT_LIMITS.countMax, Math.floor(Number(form.count) || 1)));
  if (form.ends === 'On' && /^\d{4}-\d{2}-\d{2}$/.test(String(form.untilDate || ''))) rule.untilDate = form.untilDate;
  return rule;
}

/** The editor form for an existing rule (inverse of `buildRecurrenceRule`). */
export function recurrenceFormFromRule(rule, startLocal) {
  const form = defaultRecurrenceForm(startLocal);
  if (!rule) return form;
  form.enabled = true;
  form.frequency = rule.frequency || form.frequency;
  form.interval = rule.interval || 1;
  if (rule.frequency === RecurrenceFrequency.Weekly) form.weekdays = (rule.weekdays || []).slice();
  if (rule.frequency === RecurrenceFrequency.Monthly) {
    if (rule.setPosition) {
      form.monthlyMode = 'Position';
      form.setPosition = rule.setPosition;
      form.positionWeekday = (rule.weekdays && rule.weekdays[0]) || form.positionWeekday;
    } else {
      form.monthlyMode = 'Date';
      form.monthDay = rule.monthDay || form.monthDay;
    }
  }
  if (rule.count) {
    form.ends = 'After';
    form.count = rule.count;
  } else if (rule.untilDate) {
    form.ends = 'On';
    form.untilDate = rule.untilDate;
  }
  return form;
}

/**
 * Validate the recurrence editor form. Returns `{ field: code }`: `weekdaysRequired`,
 * `countInvalid`, `untilRequired`, `untilBeforeStart`, `intervalInvalid`.
 * @param {object} form
 * @param {string} [startLocal]
 */
export function validateRecurrenceForm(form, startLocal) {
  const problems = {};
  if (!form || !form.enabled) return problems;
  const interval = Number(form.interval);
  if (!Number.isInteger(interval) || interval < 1 || interval > EVENT_LIMITS.intervalMax) problems.interval = 'intervalInvalid';
  if (form.frequency === RecurrenceFrequency.Weekly && !(form.weekdays || []).length) problems.weekdays = 'weekdaysRequired';
  if (form.ends === 'After') {
    const count = Number(form.count);
    if (!Number.isInteger(count) || count < 1 || count > EVENT_LIMITS.countMax) problems.count = 'countInvalid';
  }
  if (form.ends === 'On') {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(String(form.untilDate || ''))) problems.untilDate = 'untilRequired';
    else if (startLocal && form.untilDate < String(startLocal).slice(0, 10)) problems.untilDate = 'untilBeforeStart';
  }
  return problems;
}

function joinWords(words) {
  if (words.length <= 1) return words.join('');
  if (words.length === 2) return words[0] + ' and ' + words[1];
  return words.slice(0, -1).join(', ') + ', and ' + words[words.length - 1];
}

function formatUntil(untilDate) {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(untilDate || ''));
  if (!m) return String(untilDate || '');
  return MONTHS[Number(m[2]) - 1] + ' ' + Number(m[3]) + ', ' + m[1];
}

/**
 * Plain English summary of a rule, for example "Every week on Tuesday and Thursday, 12 times",
 * "Every 2 months on the last Friday", "Every month on day 15 until March 1, 2027".
 * @param {object|null} rule
 * @param {{ startLocal?: string }} [options] used for the yearly date
 */
export function recurrenceSummary(rule, { startLocal } = {}) {
  if (!rule || !UNITS[rule.frequency]) return '';
  const interval = Math.max(1, Number(rule.interval) || 1);
  const [one, many] = UNITS[rule.frequency];
  let text = interval === 1 ? (rule.frequency === 'Daily' ? 'Every day' : 'Every ' + one) : 'Every ' + interval + ' ' + many;
  const days = Weekdays.filter((d) => (rule.weekdays || []).includes(d));
  if (rule.frequency === RecurrenceFrequency.Weekly && days.length) {
    text += ' on ' + (days.length === 5 && !days.includes('Sat') && !days.includes('Sun') ? 'weekdays' : joinWords(days.map((d) => WEEKDAY_NAMES[d])));
  }
  if (rule.frequency === RecurrenceFrequency.Monthly) {
    if (rule.setPosition && days.length) text += ' on the ' + ORDINALS[String(rule.setPosition)] + ' ' + WEEKDAY_NAMES[days[0]];
    else if (Number(rule.monthDay) === -1) text += ' on the last day';
    else if (rule.monthDay) text += ' on day ' + rule.monthDay;
  }
  if (rule.frequency === RecurrenceFrequency.Yearly && startLocal) {
    const m = /^\d{4}-(\d{2})-(\d{2})/.exec(String(startLocal));
    if (m) text += ' on ' + MONTHS[Number(m[1]) - 1] + ' ' + Number(m[2]);
  }
  if (rule.count) text += ', ' + rule.count + (Number(rule.count) === 1 ? ' time' : ' times');
  else if (rule.untilDate) text += ' until ' + formatUntil(rule.untilDate);
  return text;
}

/** Whether an event belongs to a recurrence. */
export function isRecurring(event) {
  return Boolean(event && event.recurrence && event.recurrence.id);
}

// ---- RSVP state (section 4.3) ----

/**
 * What the RSVP control should show for the caller.
 * @param {object} event Event or EventSummary with `myRsvp`, counts, and `viewer`
 * @param {Date} [now]
 * @returns {{ status: string|null, position: number|null, full: boolean, spotsLeft: number|null, canceled: boolean, past: boolean,
 *   closed: boolean, canGoing: boolean, canInterested: boolean, joinsWaitlist: boolean, canCancel: boolean, locked: boolean, requires: string|null }}
 */
export function rsvpState(event, now = new Date()) {
  const e = event || {};
  const viewer = e.viewer || {};
  const rsvp = e.myRsvp || null;
  const status = rsvp ? rsvp.status : null;
  const canceled = e.status === 'Canceled' || Boolean(e.canceledUtc);
  const started = new Date(e.startUtc).getTime() <= now.getTime();
  const past = isPastEvent(e, now);
  const opens = e.rsvpOpensUtc ? new Date(e.rsvpOpensUtc).getTime() : null;
  const closes = e.rsvpClosesUtc ? new Date(e.rsvpClosesUtc).getTime() : null;
  const closed = canceled || started || (opens !== null && now.getTime() < opens) || (closes !== null && now.getTime() >= closes);
  const spotsLeft = e.spotsLeft === undefined ? null : e.spotsLeft;
  const full = e.capacity !== null && e.capacity !== undefined && spotsLeft !== null && spotsLeft <= 0;
  const locked = e.locked === true || viewer.canOpen === false;
  const allowed = !locked && viewer.canRsvp !== false && !closed;
  const joinsWaitlist = full && status !== RsvpStatus.Going && Boolean(e.waitlistEnabled) && viewer.canJoinWaitlist !== false;
  return {
    status,
    position: rsvp && rsvp.status === RsvpStatus.Waitlisted ? rsvp.position || null : null,
    full,
    spotsLeft,
    canceled,
    past,
    closed,
    canGoing: allowed && viewer.canRsvpGoing !== false && (!full || status === RsvpStatus.Going || joinsWaitlist),
    canInterested: allowed,
    joinsWaitlist,
    canCancel: Boolean(rsvp) && !canceled && !started,
    locked,
    requires: viewer.requires || null
  };
}

/** Whether the join link is shown (only to callers the server returned it to). */
export function joinUrlOf(event) {
  const url = event && event.online && event.online.joinUrl;
  return typeof url === 'string' && /^https:\/\//i.test(url) ? url : null;
}

// ---- Categories ----

/** Configured categories `{ key, label, color }` from `GET /api/v1/property` `events`. */
export function eventCategories(property) {
  const e = isObject(property) && isObject(property.events) ? property.events : {};
  return Array.isArray(e.categories) ? e.categories.filter((c) => c && c.key) : [];
}

/** The property's events section with defaults (`proposals`, `calendarFeedEnabled`, `defaultReminderOffsetsMinutes`). */
export function eventsConfig(property) {
  const e = isObject(property) && isObject(property.events) ? property.events : {};
  return {
    categories: eventCategories(property),
    proposals: e.proposals === true,
    calendarFeedEnabled: e.calendarFeedEnabled !== false,
    defaultReminderOffsetsMinutes: Array.isArray(e.defaultReminderOffsetsMinutes) ? e.defaultReminderOffsetsMinutes.slice(0, 3) : [1440, 120]
  };
}

/** A category's `{ key, label, color }` (an event's own `category` wins). */
export function categoryOf(property, eventOrKey) {
  if (isObject(eventOrKey) && isObject(eventOrKey.category)) return eventOrKey.category;
  const key = isObject(eventOrKey) ? eventOrKey.category : eventOrKey;
  if (!key) return null;
  return eventCategories(property).find((c) => c.key === key) || { key, label: String(key), color: null };
}

/** "1 day before", "2 hours before", "30 minutes before" for a reminder offset. */
export function formatReminderOffset(minutes) {
  const m = Math.max(0, Math.floor(Number(minutes) || 0));
  if (m && m % 1440 === 0) return m / 1440 === 1 ? '1 day before' : m / 1440 + ' days before';
  if (m && m % 60 === 0) return m / 60 === 1 ? '1 hour before' : m / 60 + ' hours before';
  return m === 1 ? '1 minute before' : m + ' minutes before';
}

// ---- Calendar month view ----

/**
 * The weeks of a month view: an array of weeks, each 7 `{ key: 'YYYY-MM-DD', day, inMonth }`
 * cells, starting on Monday (`weekStartsOn: 1`) or Sunday (`0`).
 * @param {number} year
 * @param {number} month 1 to 12
 * @param {{ weekStartsOn?: 0|1 }} [options]
 */
export function monthGrid(year, month, { weekStartsOn = 1 } = {}) {
  const first = new Date(Date.UTC(year, month - 1, 1));
  const lead = (first.getUTCDay() - weekStartsOn + 7) % 7;
  const start = new Date(Date.UTC(year, month - 1, 1 - lead));
  const weeks = [];
  const cursor = new Date(start);
  do {
    const week = [];
    for (let i = 0; i < 7; i += 1) {
      const key = cursor.toISOString().slice(0, 10);
      week.push({ key, day: cursor.getUTCDate(), inMonth: cursor.getUTCMonth() === month - 1 });
      cursor.setUTCDate(cursor.getUTCDate() + 1);
    }
    weeks.push(week);
  } while (cursor.getUTCMonth() === month - 1);
  return weeks;
}

/** The UTC window `{ startsAfterUtc, startsBeforeUtc }` covering a month grid (with a day of slack for zones). */
export function monthWindowUtc(year, month, { weekStartsOn = 1 } = {}) {
  const weeks = monthGrid(year, month, { weekStartsOn });
  const first = weeks[0][0].key;
  const last = weeks[weeks.length - 1][6].key;
  const after = new Date(first + 'T00:00:00Z');
  after.setUTCDate(after.getUTCDate() - 1);
  const before = new Date(last + 'T00:00:00Z');
  before.setUTCDate(before.getUTCDate() + 2);
  return { startsAfterUtc: after.toISOString(), startsBeforeUtc: before.toISOString() };
}

/** Group events by local start date in `zone` (the viewer's zone in a month view). */
export function groupEventsByDay(events, zone) {
  const out = {};
  for (const e of events || []) {
    const key = dateKeyInZone(e.startUtc, e.isAllDay ? e.timeZone : zone);
    if (!key) continue;
    (out[key] = out[key] || []).push(e);
  }
  for (const key of Object.keys(out)) out[key].sort((a, b) => String(a.startUtc).localeCompare(String(b.startUtc)));
  return out;
}

/** App path of an event (website prefixes `/app`). */
export function eventPath(idOrSlug) {
  return '/events/' + encodeURIComponent(idOrSlug);
}

/**
 * Validate an admin or proposal event form before sending. Returns `{ field: code }`: `required`,
 * `tooShort`, `tooLong`, `endBeforeStart`, `locationRequired`, `onlineRequired`, `https`,
 * `capacityInvalid`, `tooManyReminders`, `reminderInvalid`.
 * @param {object} form `{ title, summary, format, startLocal, endLocal, timeZone, isAllDay, location, online, capacity, externalUrl, reminderOffsetsMinutes }`
 */
export function validateEventForm(form) {
  const problems = {};
  const f = form || {};
  const title = String(f.title || '').trim();
  if (!title) problems.title = 'required';
  else if (title.length < EVENT_LIMITS.titleMin) problems.title = 'tooShort';
  else if (title.length > EVENT_LIMITS.titleMax) problems.title = 'tooLong';
  if (f.summary && String(f.summary).length > EVENT_LIMITS.summaryMax) problems.summary = 'tooLong';
  if (!f.startLocal) problems.startLocal = 'required';
  if (!f.endLocal) problems.endLocal = 'required';
  if (f.startLocal && f.endLocal && String(f.endLocal) <= String(f.startLocal) && !(f.isAllDay && String(f.endLocal) === String(f.startLocal))) problems.endLocal = 'endBeforeStart';
  if (!f.timeZone) problems.timeZone = 'required';
  const format = f.format || 'InPerson';
  const loc = f.location || {};
  const hasLocation = Boolean(loc.venueId) || Boolean(String(loc.name || '').trim()) || Boolean(loc.address && String(loc.address.locality || loc.address.line1 || '').trim());
  if ((format === 'InPerson' || format === 'Hybrid') && !hasLocation) problems.location = 'locationRequired';
  // A typed address (no venue) needs its country: the server requires location.address.countryCode with an address.
  const address = loc.address || null;
  if ((format === 'InPerson' || format === 'Hybrid') && !loc.venueId && address && String(address.line1 || address.locality || '').trim() && !String(address.countryCode || '').trim()) {
    problems['location.address.countryCode'] = 'countryRequired';
  }
  const online = f.online || {};
  if ((format === 'Online' || format === 'Hybrid') && !String(online.platformLabel || online.joinUrl || '').trim()) problems.online = 'onlineRequired';
  if (online.joinUrl && !/^https:\/\//i.test(online.joinUrl)) problems['online.joinUrl'] = 'https';
  if (f.externalUrl && !/^https:\/\//i.test(f.externalUrl)) problems.externalUrl = 'https';
  if (f.capacity !== null && f.capacity !== undefined && f.capacity !== '') {
    const c = Number(f.capacity);
    if (!Number.isInteger(c) || c < 1 || c > EVENT_LIMITS.capacityMax) problems.capacity = 'capacityInvalid';
  }
  const offsets = f.reminderOffsetsMinutes || [];
  if (offsets.length > EVENT_LIMITS.maxReminders) problems.reminderOffsetsMinutes = 'tooManyReminders';
  else if (offsets.some((m) => !Number.isInteger(Number(m)) || Number(m) < EVENT_LIMITS.reminderMinMinutes || Number(m) > EVENT_LIMITS.reminderMaxMinutes)) problems.reminderOffsetsMinutes = 'reminderInvalid';
  return problems;
}
