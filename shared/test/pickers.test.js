import { test } from 'node:test';
import assert from 'node:assert/strict';
import { COMMON_TIME_ZONES, languageName, languageOptions, timeZoneChoices } from '../src/index.js';

test('time zone choices include UTC, the current value, and the device zone, sorted and distinct', () => {
  const zones = timeZoneChoices('Custom/Zone', 'America/Los_Angeles');
  assert.ok(zones.includes('UTC') && zones.includes('Custom/Zone') && zones.includes('America/Los_Angeles'));
  assert.deepEqual(zones, [...new Set(zones)].sort());
  assert.ok(COMMON_TIME_ZONES.includes('America/New_York'));
});

test('time zone choices fall back to the common list without Intl.supportedValuesOf', () => {
  const original = Intl.supportedValuesOf;
  try {
    Intl.supportedValuesOf = undefined;
    const zones = timeZoneChoices();
    assert.equal(zones.length, new Set([...COMMON_TIME_ZONES, 'UTC']).size);
  } finally {
    Intl.supportedValuesOf = original;
  }
});

test('language options are named, sorted, and keep an unknown stored tag', () => {
  assert.equal(languageName('en'), 'English');
  assert.equal(languageName(''), '');
  const options = languageOptions('tlh');
  assert.ok(options.some((o) => o.code === 'tlh'));
  assert.ok(options.find((o) => o.code === 'es').name.startsWith('Spanish'));
  const names = options.map((o) => o.name);
  assert.deepEqual(names, [...names].sort((a, b) => a.localeCompare(b)));
});
