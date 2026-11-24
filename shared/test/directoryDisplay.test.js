import assert from 'node:assert/strict';
import test from 'node:test';
import { formatClockTime, mapAttributionHtml, parseScheduleText, parseVenueDescription } from '../src/directory.js';

test('map attribution escapes HTML while allowing explicit http links', () => {
  const html = mapAttributionHtml('<script>bad</script> [Data](https://example.test/?a=1&b=2) [no](javascript:alert)');
  assert.match(html, /^&lt;script&gt;bad&lt;\/script&gt;/);
  assert.match(html, /href="https:\/\/example.test\/\?a=1&amp;b=2"/);
  assert.match(html, /rel="noopener noreferrer"/);
  assert.ok(!html.includes('href="javascript:'));
  assert.equal(mapAttributionHtml(null), '');
});

test('schedule display merges consecutive equal days and preserves notes', () => {
  const result = parseScheduleText('Hours: Tue 05:00-22:00; Mon 05:00-22:00; Thu 00:00-24:00\nCall before visiting.');
  assert.deepEqual(result.hours, [
    { from: 0, to: 1, open: '05:00', close: '22:00', allDay: false },
    { from: 3, to: 3, open: '00:00', close: '24:00', allDay: true }
  ]);
  assert.deepEqual(result.notes, ['Call before visiting.']);
  assert.equal(formatClockTime('17:30'), '5:30 PM');
  assert.equal(formatClockTime('00:00'), '12 AM');
});

test('unrecognized, duplicate, or invalid schedule data remains intact as notes', () => {
  for (const text of ['Open by appointment', 'Hours: Mon 25:00-26:00', 'Hours: Mon 24:30-24:59', 'Hours: Mon 09:00-17:00; Mon 10:00-18:00']) {
    assert.deepEqual(parseScheduleText(text), { hours: [], notes: [text] });
  }
});

test('description display separates optional sections without dropping remaining prose', () => {
  assert.deepEqual(parseVenueDescription('A community space.\nPricing: Ask for a quote\nAmenities: Parking, Showers\nPricing: Other arrangements available'), {
    pricing: 'Ask for a quote', amenities: ['Parking', 'Showers'], body: 'A community space.\nPricing: Other arrangements available'
  });
  assert.deepEqual(parseVenueDescription(null), { pricing: null, amenities: [], body: '' });
});
