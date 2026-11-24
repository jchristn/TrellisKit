/**
 * Job admin helpers: schedule descriptions, status tones, and the edit form mapping.
 * (c) 2026 Lattice AI, LLC. Licensed under the MIT License; see LICENSE.md.
 */
import strings from '../../../strings.js';

const s = strings.jobsAdmin;

/** Days of the week in server order. */
export const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/** Minutes after midnight to HH:MM. */
export function clock(minutes) {
  const m = Math.max(0, Math.min(1439, Number(minutes) || 0));
  return String(Math.floor(m / 60)).padStart(2, '0') + ':' + String(m % 60).padStart(2, '0');
}

/** HH:MM to minutes after midnight, or null. */
export function minutesOf(text) {
  const match = /^(\d{1,2}):(\d{2})$/.exec(String(text || '').trim());
  if (!match) return null;
  const h = Number(match[1]);
  const m = Number(match[2]);
  if (h > 23 || m > 59) return null;
  return h * 60 + m;
}

/** Human description of a job's schedule. */
export function describeSchedule(job) {
  const fn = s.schedule[job.intervalType] || s.schedule.Manual;
  return fn(job.intervalValue, clock(job.timeOfDayMinutesUtc), job.daysOfWeek || []);
}

/** Badge tone of a run or item status. */
export function statusTone(status) {
  switch (status) {
    case 'Succeeded': return 'success';
    case 'PartiallySucceeded': case 'Skipped': case 'Cancelled': return 'warning';
    case 'Failed': case 'Dead': return 'danger';
    case 'Running': case 'Queued': case 'Pending': return 'info';
    default: return 'neutral';
  }
}

/** Edit form state from a job. */
export function jobForm(job) {
  return {
    enabled: !!job.enabled,
    intervalType: job.intervalType,
    intervalValue: String(job.intervalValue),
    time: clock(job.timeOfDayMinutesUtc),
    daysOfWeek: [...(job.daysOfWeek || [])],
    maxConcurrency: String(job.maxConcurrency),
    maxAttempts: String(job.maxAttempts),
    budgetUsdPerRun: job.budgetUsdPerRun == null ? '0' : String(job.budgetUsdPerRun)
  };
}

/** The PUT body from a form, or { error } naming the first invalid field. */
export function jobBody(form) {
  const body = { enabled: form.enabled, intervalType: form.intervalType };
  const int = (v) => (/^\d+$/.test(String(v).trim()) ? Number(v) : NaN);
  if (['EveryNMinutes', 'EveryNHours', 'EveryNDays'].includes(form.intervalType)) {
    const v = int(form.intervalValue);
    const [min, max] = { EveryNMinutes: [5, 1440], EveryNHours: [1, 168], EveryNDays: [1, 90] }[form.intervalType];
    if (!(v >= min && v <= max)) return { error: 'intervalValue', message: min + ' to ' + max + '.' };
    body.intervalValue = v;
  }
  if (['EveryNDays', 'DaysOfWeek'].includes(form.intervalType)) {
    const t = minutesOf(form.time);
    if (t == null) return { error: 'time', message: 'HH:MM, 00:00 to 23:59.' };
    body.timeOfDayMinutesUtc = t;
  }
  if (form.intervalType === 'DaysOfWeek') {
    if (form.daysOfWeek.length === 0) return { error: 'daysOfWeek', message: 'Choose one or more days.' };
    body.daysOfWeek = DAYS.filter((d) => form.daysOfWeek.includes(d));
  }
  const c = int(form.maxConcurrency);
  if (!(c >= 1 && c <= 64)) return { error: 'maxConcurrency', message: '1 to 64.' };
  body.maxConcurrency = c;
  const a = int(form.maxAttempts);
  if (!(a >= 1 && a <= 10)) return { error: 'maxAttempts', message: '1 to 10.' };
  body.maxAttempts = a;
  const b = Number(String(form.budgetUsdPerRun).trim());
  if (!(Number.isFinite(b) && b >= 0 && b <= 100000)) return { error: 'budgetUsdPerRun', message: '0 to 100000.' };
  body.budgetUsdPerRun = b;
  return { body };
}
