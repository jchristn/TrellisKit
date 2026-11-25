import test from 'node:test';
import assert from 'node:assert/strict';
import { venueScheduleDisplay, validateVenueSchedule, mergeProperty, loadProperty } from '../src/index.js';
const schedule = { timeZone: 'Pacific/Honolulu', openingHours: [{ dayOfWeek: 6, startTime: '22:00', endTime: '02:00', endsNextDay: true }], classes: [{ dayOfWeek: 0, startTime: '18:00', endTime: '19:00', endsNextDay: false, name: 'Fundamentals', instructor: 'Alex' }], notes: 'Holiday hours vary.' };
test('structured schedules preserve overnight hours, class details and notes', () => {
  assert.deepEqual(validateVenueSchedule(schedule), {});
  const out = venueScheduleDisplay(schedule, 'Old hours');
  assert.equal(out.hours[0].endsNextDay, true);
  assert.equal(out.classes[0].name, 'Fundamentals');
  assert.deepEqual(out.notes, ['Holiday hours vary.']);
  assert.equal(out.timeZone, 'Pacific/Honolulu');
  assert.deepEqual(venueScheduleDisplay(null, 'Appointment only').notes, ['Appointment only']);
});
test('invalid times, zones, days, names, null arrays and excessive entries are rejected', () => {
  for (const patch of [{ endTime: '17:00' }, { startTime: '24:00' }, { dayOfWeek: 7 }, { name: '' }, { endTime: '18:00' }, { endTime: '19:01', endsNextDay: true }]) {
    assert.ok(Object.keys(validateVenueSchedule({ ...schedule, classes: [{ ...schedule.classes[0], ...patch }] })).length);
  }
  for (const patch of [{ timeZone: '' }, { timeZone: 'Not/AZone' }, { openingHours: null }, { classes: [null] }, { classes: Array(101).fill(schedule.classes[0]) }]) assert.ok(Object.keys(validateVenueSchedule({ ...schedule, ...patch })).length);
});

test('directory copy survives an older runtime response and newer responses can replace it', async () => {
  const { readFile } = await import('node:fs/promises');
  const base = loadProperty(await readFile(new URL('../../samples/example/property.json', import.meta.url), 'utf8'));
  base.directory = { ...base.directory, introduction: 'Find clubs nearby.', listingNotice: 'Listings are informational.' };
  const merged = mergeProperty(base, { directory: { venueLabel: 'Studio' } });
  assert.equal(merged.directory.introduction, base.directory.introduction);
  assert.equal(merged.directory.venueLabel, 'Studio');
  assert.equal(mergeProperty(base, { directory: { listingNotice: 'Updated notice' } }).directory.listingNotice, 'Updated notice');
});
