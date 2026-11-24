import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  ApiClient, BrandColorKeys, BrandSchemes, MemberPaletteMode, effectivePalette, failingContrast, formatRatio, memberPresetsBody, normalizeHex,
  palettePresets, presetBrandingBody, resolvePalettePreset, withPalettePreset
} from '../src/index.js';

// The engine catalog is defined once, as the backend's embedded asset; the API serves it to clients.
const catalog = JSON.parse(readFileSync(new URL('../../backend/src/TrellisKit.Core/Assets/palette-presets.json', import.meta.url), 'utf8'));
const sample = JSON.parse(readFileSync(new URL('../../samples/example/property.json', import.meta.url), 'utf8'));

test('the preset catalog has 12 to 32 presets with unique ids and names and every key in both schemes', () => {
  assert.equal(catalog.version, 1);
  assert.ok(catalog.presets.length >= 12 && catalog.presets.length <= 32, String(catalog.presets.length));
  assert.equal(new Set(catalog.presets.map((p) => p.id)).size, catalog.presets.length);
  assert.equal(new Set(catalog.presets.map((p) => p.name.toLowerCase())).size, catalog.presets.length);
  for (const preset of catalog.presets) {
    assert.match(preset.id, /^[a-z0-9]+(-[a-z0-9]+)*$/);
    assert.ok(preset.description.length > 0, preset.id);
    for (const scheme of BrandSchemes) {
      for (const key of BrandColorKeys) assert.ok(normalizeHex(preset[scheme][key]), preset.id + ' ' + scheme + '.' + key);
    }
  }
});

test('every preset passes every WCAG 2.1 AA pair in both schemes without an override', () => {
  for (const preset of catalog.presets) {
    const failing = failingContrast({ light: preset.light, dark: preset.dark });
    assert.deepEqual(failing.map((c) => c.scheme + ' ' + c.pair + ' ' + formatRatio(c.ratio)), [], preset.id);
  }
});

test('the member preference resolves only to an allowed preset and falls back to the property palette', () => {
  const ocean = catalog.presets.find((p) => p.id === 'ocean');
  const property = { ...sample, brand: { ...sample.brand, presets: [ocean] } };
  assert.equal(palettePresets(property).length, 1);
  assert.equal(resolvePalettePreset(property, 'ocean').id, 'ocean');
  assert.equal(resolvePalettePreset(property, 'graphite'), null, 'not allowed');
  assert.equal(resolvePalettePreset(property, null), null);
  assert.equal(effectivePalette(property, 'ocean').light.primary, ocean.light.primary.toUpperCase());
  assert.equal(effectivePalette(property, 'graphite').light.primary, normalizeHex(sample.brand.colors.light.primary), 'fallback');
  const applied = withPalettePreset(property, 'ocean');
  assert.equal(applied.brand.colors.dark.accentSoft, ocean.dark.accentSoft.toUpperCase());
  assert.equal(applied.brand.logo, sample.brand.logo, 'logo unchanged');
  assert.equal(withPalettePreset(property, 'graphite'), property, 'same object without a preset');
  assert.deepEqual(palettePresets({ brand: { presets: [{ id: 'x' }, null] } }), []);
});

test('preset and member preset bodies', () => {
  const ocean = catalog.presets.find((p) => p.id === 'ocean');
  const body = presetBrandingBody({ light: sample.brand.colors.light, dark: sample.brand.colors.dark }, ocean);
  assert.equal(body.note, 'Preset: Ocean');
  assert.equal(body.light.primary, ocean.light.primary.toUpperCase());
  assert.equal(presetBrandingBody({ light: ocean.light, dark: ocean.dark }, ocean), null, 'nothing to change');
  assert.deepEqual(memberPresetsBody(MemberPaletteMode.All, ['x']), { mode: 'All' });
  assert.deepEqual(memberPresetsBody('None'), { mode: 'None' });
  assert.deepEqual(memberPresetsBody(MemberPaletteMode.Selected, ['ocean', 'ocean', '', 'graphite']), { mode: 'Selected', presetIds: ['ocean', 'graphite'] });
});

test('appearance and member preset routes', async () => {
  const calls = [];
  const fetch = async (url, init) => {
    calls.push({ url, method: init.method, body: init.body ? JSON.parse(init.body) : null });
    return new Response(JSON.stringify({ palettePreset: null }), { status: 200, headers: { 'Content-Type': 'application/json' } });
  };
  const client = new ApiClient({ baseUrl: 'https://api.example.test', fetchImpl: fetch, getAccessToken: () => 'token' });
  await client.getAppearance();
  await client.updateAppearance('ocean');
  await client.updateAppearance('');
  await client.updatePropertyMemberPresets({ mode: 'None' });
  await client.updatePlatformPropertyMemberPresets('prop_1', { mode: 'All' });
  assert.deepEqual(calls.map((c) => c.method + ' ' + new URL(c.url).pathname), [
    'GET /api/v1/me/appearance', 'PUT /api/v1/me/appearance', 'PUT /api/v1/me/appearance',
    'PUT /api/v1/admin/property/branding/member-presets', 'PUT /api/v1/platform/properties/prop_1/branding/member-presets'
  ]);
  assert.deepEqual(calls[1].body, { palettePreset: 'ocean' });
  assert.deepEqual(calls[2].body, { palettePreset: null });
});
