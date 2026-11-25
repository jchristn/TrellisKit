import { parseScheduleText } from './directory.js';

/** Empty editable schedule. Times are local to the schedule time zone; Monday is day zero. */
export function emptyVenueSchedule(timeZone = '') {
  return { timeZone, openingHours: [], sessions: [], notes: '' };
}

/** Normalized rows for both clients. Structured schedules take precedence over legacy text. */
export function venueScheduleDisplay(schedule, text) {
  if (!schedule) return { ...parseScheduleText(text), sessions: [], timeZone: null };
  const order = (a, b) => a.dayOfWeek - b.dayOfWeek || a.startTime.localeCompare(b.startTime);
  return {
    hours: [...(schedule.openingHours || [])].sort(order).map(e => ({ from: e.dayOfWeek, to: e.dayOfWeek, open: e.startTime, close: e.endTime, allDay: e.endsNextDay && e.startTime === e.endTime, endsNextDay: e.endsNextDay })),
    sessions: [...(schedule.sessions || [])].sort(order),
    notes: schedule.notes ? [schedule.notes] : [],
    timeZone: schedule.timeZone || null
  };
}

/** Validate the API shape before submission; the server remains authoritative. */
export function validateVenueSchedule(schedule) {
  const errors = {};
  if (schedule == null) return errors;
  if (typeof schedule !== 'object' || Array.isArray(schedule)) return { schedule: 'invalidSchedule' };
  const time = value => typeof value === 'string' && /^(?:[01]\d|2[0-3]):[0-5]\d$/.test(value);
  const minutes = value => Number(value.slice(0, 2)) * 60 + Number(value.slice(3));
  for (const [kind, max] of [['openingHours', 28], ['sessions', 100]]) {
    const rows = schedule[kind];
    if (!Array.isArray(rows) || rows.length > max) { errors['schedule.' + kind] = 'invalidSchedule'; continue; }
    rows.forEach((row, i) => {
      const key = 'schedule.' + kind + '[' + i + ']';
      if (!row || !Number.isInteger(row.dayOfWeek) || row.dayOfWeek < 0 || row.dayOfWeek > 6 || !time(row.startTime) || !time(row.endTime) || typeof row.endsNextDay !== 'boolean') { errors[key] = 'invalidSchedule'; return; }
      const duration = minutes(row.endTime) - minutes(row.startTime) + (row.endsNextDay ? 1440 : 0);
      if (duration <= 0 || duration > 1440) errors[key] = 'invalidSchedule';
      if (kind === 'sessions' && (typeof row.name !== 'string' || !row.name.trim() || row.name.length > 120)) errors[key + '.name'] = 'invalidSchedule';
      if (row.instructor != null && (typeof row.instructor !== 'string' || row.instructor.length > 120)) errors[key + '.instructor'] = 'invalidSchedule';
      if (kind === 'openingHours' && (row.name || row.instructor)) errors[key] = 'invalidSchedule';
    });
  }
  if ((schedule.openingHours?.length || schedule.sessions?.length) && !schedule.timeZone) errors['schedule.timeZone'] = 'invalidSchedule';
  if (schedule.timeZone) {
    try { new Intl.DateTimeFormat('en', { timeZone: schedule.timeZone }); } catch { errors['schedule.timeZone'] = 'invalidSchedule'; }
  }
  if (schedule.notes != null && (typeof schedule.notes !== 'string' || schedule.notes.length > 1000)) errors['schedule.notes'] = 'invalidSchedule';
  return errors;
}
