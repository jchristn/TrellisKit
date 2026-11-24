import test from 'node:test';
import assert from 'node:assert/strict';
import { isHandle, numberedHandle, suggestHandle } from '../src/index.js';

test('suggestHandle joins the words of a name with periods and falls back to the email', () => {
  assert.equal(suggestHandle('Sam Rivera', 'x@example.com'), 'sam.rivera');
  assert.equal(suggestHandle('  José  Ñandú-Smith ', ''), 'jose.nandu.smith');
  assert.equal(suggestHandle('', 'coach_kim+test@example.com'), 'coach_kim.test');
  assert.equal(suggestHandle('Al', ''), 'al.member');
  assert.equal(suggestHandle('!!!', ''), '');
  assert.equal(suggestHandle('A'.repeat(50), '').length, 30);
  assert.ok(isHandle(suggestHandle('Sam Rivera', '')));
});

test('numberedHandle keeps the stem, replaces a trailing number, and stays a valid handle', () => {
  assert.equal(numberedHandle('sam.rivera', () => 0), 'sam.rivera10');
  assert.equal(numberedHandle('sam.rivera10', () => 0.5), 'sam.rivera505');
  assert.ok(isHandle(numberedHandle('a'.repeat(30))));
});

test('mapAttributionHtml turns Markdown links into links and escapes everything else', async () => {
  const { mapAttributionHtml } = await import('../src/index.js');
  assert.equal(mapAttributionHtml('[Tiles](https://tiles.example.test) © Data <b>'), '<a href="https://tiles.example.test" target="_blank" rel="noopener noreferrer">Tiles</a> © Data &lt;b&gt;');
  assert.equal(mapAttributionHtml('[x](javascript:alert(1))'), '[x](javascript:alert(1))');
  assert.equal(mapAttributionHtml('  '), '');
  assert.equal(mapAttributionHtml(null), '');
});

test('parseScheduleText turns an Hours line into grouped rows and keeps the rest as notes', async () => {
  const { parseScheduleText, formatClockTime, parseVenueDescription } = await import('../src/index.js');
  const s = parseScheduleText('Hours: Mon 05:00-22:00; Tue 05:00-22:00; Wed 05:00-22:00; Thu 05:00-22:00; Fri 05:00-22:00; Sat 07:00-20:00; Sun 07:00-20:00\nGroup classes daily.\n\nDrop in for $30.');
  assert.deepEqual(s.hours, [
    { from: 0, to: 4, open: '05:00', close: '22:00', allDay: false },
    { from: 5, to: 6, open: '07:00', close: '20:00', allDay: false }
  ]);
  assert.deepEqual(s.notes, ['Group classes daily.', 'Drop in for $30.']);
  const allDay = parseScheduleText('Hours: Mon 00:00-00:00; Tue 00:00-00:00; Wed 00:00-00:00; Thu 00:00-00:00; Fri 00:00-00:00; Sat 00:00-00:00; Sun 00:00-00:00');
  assert.deepEqual(allDay.hours, [{ from: 0, to: 6, open: '00:00', close: '00:00', allDay: true }]);
  assert.deepEqual(parseScheduleText('Hours: Mon 6:00-12:00; Wed 6:00-12:00').hours.map((h) => [h.from, h.to]), [[0, 0], [2, 2]]);
  const free = parseScheduleText('Open mat Sundays at noon.\nHours: ask at the desk');
  assert.deepEqual(free, { hours: [], notes: ['Open mat Sundays at noon.', 'Hours: ask at the desk'] });
  assert.deepEqual(parseScheduleText('Hours: by appointment'), { hours: [], notes: ['Hours: by appointment'] });
  assert.deepEqual(parseScheduleText(null), { hours: [], notes: [] });
  assert.equal(formatClockTime('05:00'), '5 AM');
  assert.equal(formatClockTime('17:30'), '5:30 PM');
  assert.equal(formatClockTime('00:00'), '12 AM');
  assert.equal(formatClockTime('12:00'), '12 PM');
  assert.deepEqual(parseVenueDescription('Pricing: From $68 a month.\nAmenities: Pool, Sauna , Parking'), { pricing: 'From $68 a month.', amenities: ['Pool', 'Sauna', 'Parking'], body: '' });
  assert.deepEqual(parseVenueDescription('A friendly gym.\n\nFounded 2010.'), { pricing: null, amenities: [], body: 'A friendly gym.\n\nFounded 2010.' });
});
