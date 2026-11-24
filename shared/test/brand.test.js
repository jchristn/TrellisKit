import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  ApiClient, BrandColorKeys, BrandContrastPairs, brandPalette, checkPaletteContrast, contrastFieldKey, contrastRatio, displayRatio, failingContrast,
  formatRatio, isLocalDevOrigin, mergePalette, mergeProperty, normalizeEmbedOrigin, normalizeHex, paletteChanges, relativeLuminance, validateEmbedOrigins, validateProperty
} from '../src/index.js';

const sample = JSON.parse(readFileSync(new URL('../../samples/example/property.json', import.meta.url), 'utf8'));

test('contrast math follows WCAG 2.1', () => {
  assert.equal(relativeLuminance('#FFFFFF'), 1);
  assert.equal(relativeLuminance('#000'), 0);
  assert.equal(contrastRatio('#FFFFFF', '#000000'), 21);
  assert.equal(contrastRatio('#777777', '#777777'), 1);
  assert.equal(displayRatio(contrastRatio('#CF3335', '#FBE9E9')), 4.29);
  assert.equal(displayRatio(contrastRatio('#B8282C', '#FBE9E9')), 5.31);
  assert.equal(formatRatio(contrastRatio('#FFFFFF', '#CF3335')), '5.03:1');
  assert.ok(Number.isNaN(contrastRatio('red', '#FFFFFF')));
});

test('display rounding never rounds up to a threshold', () => {
  assert.equal(displayRatio(4.4999), 4.49);
  assert.equal(displayRatio(2.996), 2.99);
  assert.equal(displayRatio(4.506), 4.51);
  assert.equal(displayRatio(NaN), 0);
});

test('normalizeHex accepts #RGB and #RRGGBB only', () => {
  assert.equal(normalizeHex(' #abc '), '#AABBCC');
  assert.equal(normalizeHex('#1f5fbf'), '#1F5FBF');
  for (const bad of ['#12345678', 'red', '#12345', '', null, 12]) assert.equal(normalizeHex(bad), null);
});

test('brandPalette fills every key and defaults onAccentSoft to primary', () => {
  const p = brandPalette({ brand: { colors: { light: { primary: '#123456', accentSoft: '#eeeeee80' }, dark: {} } } });
  assert.deepEqual(Object.keys(p.light), [...BrandColorKeys]);
  assert.equal(p.light.onAccentSoft, '#123456');
  assert.equal(p.light.accentSoft, '#EEEEEE');
  assert.equal(p.dark.primary, '');
});

test('mergePalette and paletteChanges keep only valid changed keys', () => {
  const base = brandPalette(sample);
  const merged = mergePalette(base, { light: { primary: '#abc', bogus: '#000000', accent: 'nope' } });
  assert.equal(merged.light.primary, '#AABBCC');
  assert.equal(merged.light.accent, base.light.accent);
  assert.equal(merged.light.bogus, undefined);
  assert.deepEqual(paletteChanges(base, merged), { light: { primary: '#AABBCC' } });
  assert.deepEqual(paletteChanges(base, base), {});
});

test('checkPaletteContrast mirrors the backend pairs; the sample passes AA', () => {
  const checks = checkPaletteContrast(brandPalette(sample));
  assert.equal(checks.length, BrandContrastPairs.length * 2);
  assert.deepEqual(checks.filter((c) => !c.passes), []);
  const failing = mergePalette(brandPalette(sample), { light: { onAccentSoft: '#CF3335', accentSoft: '#FBE9E9' } });
  const bad = failingContrast(failing);
  assert.deepEqual(bad.map((c) => c.scheme + ' ' + c.pair), ['light onAccentSoft/accentSoft']);
  assert.equal(bad[0].ratio, 4.29);
  assert.equal(bad[0].required, 4.5);
  assert.equal(contrastFieldKey('light', 'onAccentSoft/accentSoft'), 'contrast.light.onAccentSoft/accentSoft');
  const overridden = checkPaletteContrast(failing, [{ scheme: 'light', pair: 'onAccentSoft/accentSoft' }]);
  assert.ok(overridden.find((c) => c.pair === 'onAccentSoft/accentSoft' && c.scheme === 'light').overridden);
  const link = checks.find((c) => c.pair === 'primary/background' && c.scheme === 'dark');
  assert.equal(link.backgroundColor, '#0E141D');
});

test('validateProperty accepts an optional onAccentSoft and rejects a bad one', () => {
  assert.deepEqual(validateProperty(sample), []);
  const bad = JSON.parse(JSON.stringify(sample));
  bad.brand.colors.light.onAccentSoft = 'red';
  assert.ok(validateProperty(bad).includes('brand.colors.light.onAccentSoft must be a #RRGGBB color'));
  delete bad.brand.colors.light.onAccentSoft;
  assert.deepEqual(validateProperty(bad), []);
});

test('mergeProperty takes the runtime palette', () => {
  const runtime = { brand: { ...sample.brand, colors: { light: { ...sample.brand.colors.light, primary: '#0B3D91' }, dark: sample.brand.colors.dark }, paletteVersion: 3 } };
  const merged = mergeProperty(sample, runtime);
  assert.equal(merged.brand.colors.light.primary, '#0B3D91');
  assert.equal(merged.brand.paletteVersion, 3);
});

test('embed origins: https anywhere, http only for localhost and 127.0.0.1', () => {
  assert.equal(normalizeEmbedOrigin('https://Partner.Example.test/'), 'https://partner.example.test');
  assert.equal(normalizeEmbedOrigin('http://localhost:5500'), 'http://localhost:5500');
  assert.equal(normalizeEmbedOrigin('http://127.0.0.1'), 'http://127.0.0.1');
  for (const bad of ['http://partner.example.test', 'http://192.168.1.2:3000', 'http://localhost.example.test', 'https://a.test/page', '*', 'http://localhost:0', 'http://localhost:70000', 'http://[::1]:3000']) {
    assert.equal(normalizeEmbedOrigin(bad), null, bad);
  }
  assert.ok(isLocalDevOrigin('http://localhost:3000') && isLocalDevOrigin('https://127.0.0.1:8443'));
  assert.ok(!isLocalDevOrigin('https://partner.example.test'));
  assert.deepEqual(validateEmbedOrigins(['https://a.test', 'http://b.test', 'HTTP://LOCALHOST:1/', 'http://localhost:1']), { origins: ['https://a.test', 'http://localhost:1'], problems: { 1: 'http://b.test' } });
});

test('branding and AI client methods map to their routes', async () => {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push([init.method, url.replace('https://api.test/api/v1', ''), init.body ? JSON.parse(init.body) : undefined]);
    return new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } });
  };
  const client = new ApiClient({ baseUrl: 'https://api.test', fetchImpl });
  await client.getPropertyBranding();
  await client.updatePropertyBranding({ light: { primary: '#000000' } });
  await client.revertPropertyBranding('Seed');
  await client.getPropertyAi();
  await client.updatePropertyAi(1);
  await client.getPlatformPropertyBranding('app_x');
  await client.updatePlatformPropertyBranding('app_x', { dark: { accent: '#FFFFFF' } });
  await client.revertPlatformPropertyBranding('app_x', 'Previous', 'undo');
  await client.getPlatformPropertyAi('app_x');
  await client.updatePlatformPropertyAi('app_x', false);
  assert.deepEqual(calls.map((c) => c[0] + ' ' + c[1]), [
    'GET /admin/property/branding', 'PUT /admin/property/branding', 'POST /admin/property/branding/revert', 'GET /admin/property/ai', 'PUT /admin/property/ai',
    'GET /platform/properties/app_x/branding', 'PUT /platform/properties/app_x/branding', 'POST /platform/properties/app_x/branding/revert',
    'GET /platform/properties/app_x/ai', 'PUT /platform/properties/app_x/ai'
  ]);
  assert.deepEqual(calls[2][2], { to: 'Seed' });
  assert.deepEqual(calls[4][2], { enabled: true });
  assert.deepEqual(calls[7][2], { to: 'Previous', note: 'undo' });
});
