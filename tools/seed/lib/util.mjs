// Shared helpers: logging, ids, passwords, text templates, and local times.

import { createHash, randomBytes } from 'node:crypto';

export function makeLog(verbose) {
  return {
    info: (...a) => console.log(...a),
    warn: (...a) => console.warn('WARN', ...a),
    debug: (...a) => { if (verbose) console.log('  .', ...a); },
  };
}

const PASSWORD_ALPHABET = 'ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789';

export function randomPassword(length = 20) {
  const bytes = randomBytes(length);
  let out = '';
  for (let i = 0; i < length; i += 1) out += PASSWORD_ALPHABET[bytes[i] % PASSWORD_ALPHABET.length];
  return out;
}

// A stable UUID-shaped id derived from a string (used for clientMessageId).
export function stableUuid(input) {
  const h = createHash('sha256').update(input).digest('hex');
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-4${h.slice(13, 16)}-a${h.slice(17, 20)}-${h.slice(20, 32)}`;
}

// Replace {@memberKey} with @handle and {member:key} with the display name.
export function expandText(text, ctx) {
  if (typeof text !== 'string') return text;
  return text
    .replace(/\{@([A-Za-z0-9_.-]+)\}/g, (m, key) => {
      const member = ctx.members.get(key) ?? [...ctx.members.values()].find((x) => x.handle === key || x.spec?.handle === key);
      return member?.handle ? `@${member.handle}` : m;
    })
    .replace(/\{member:([A-Za-z0-9_.-]+)\}/g, (m, key) => ctx.members.get(key)?.displayName ?? m)
    .replace(/\{property\.name\}/g, ctx.property?.name ?? '')
    .replace(/\{web\}/g, ctx.webBaseUrl ?? '');
}

const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

// Today's date (YYYY-MM-DD) and weekday in a time zone.
function localToday(timeZone) {
  const parts = new Intl.DateTimeFormat('en-US', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit', weekday: 'short' })
    .formatToParts(new Date());
  const get = (t) => parts.find((p) => p.type === t)?.value;
  return { date: `${get('year')}-${get('month')}-${get('day')}`, weekday: get('weekday') };
}

function addDays(isoDate, days) {
  const d = new Date(`${isoDate}T12:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

// A local date from a relative spec: { inDays } or { weekday, weeksAhead }.
export function resolveLocalDate(spec, timeZone) {
  const today = localToday(timeZone);
  if (spec.date) return spec.date;
  if (spec.weekday) {
    const target = WEEKDAYS.indexOf(spec.weekday);
    if (target < 0) throw new Error(`Unknown weekday ${spec.weekday}`);
    const current = WEEKDAYS.indexOf(today.weekday);
    let delta = (target - current + 7) % 7;
    if (delta === 0 && !spec.allowToday) delta = 7;
    return addDays(today.date, delta + 7 * (spec.weeksAhead ?? 0));
  }
  return addDays(today.date, spec.inDays ?? 0);
}

// "YYYY-MM-DDTHH:mm" local wall time from { inDays | weekday, time } plus an optional duration.
export function resolveLocalTime(spec, timeZone, addMinutes = 0) {
  const date = resolveLocalDate(spec, timeZone);
  const [hh, mm] = (spec.time ?? '12:00').split(':').map(Number);
  const total = hh * 60 + mm + addMinutes;
  const dayShift = Math.floor(total / 1440);
  const minutes = ((total % 1440) + 1440) % 1440;
  const d = dayShift ? addDays(date, dayShift) : date;
  return `${d}T${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
}

export function isoInHours(hours) {
  return new Date(Date.now() + hours * 3600_000).toISOString().replace(/\.\d{3}Z$/, 'Z');
}

export function pick(obj, keys) {
  const out = {};
  for (const k of keys) if (obj[k] !== undefined) out[k] = obj[k];
  return out;
}

export function chunk(list, size) {
  const out = [];
  for (let i = 0; i < list.length; i += size) out.push(list.slice(i, i + size));
  return out;
}
