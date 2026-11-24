/**
 * Brand palettes (docs/TRELLISKIT.md section 2.1): the color keys of each scheme, WCAG 2.1 contrast
 * math, the pairs every palette is checked on, and palette merging. Mirrors the backend's
 * BrandContrast and BrandColorSet exactly (same keys, pairs, page backgrounds, and rounding), so
 * the admin Branding page shows the numbers the server enforces. Engine-neutral: every color
 * comes from the property configuration.
 */

/** Every brand color key, in display order. `onAccentSoft` defaults to `primary` when absent. */
export const BrandColorKeys = Object.freeze(['primary', 'primaryHover', 'onPrimary', 'accent', 'onAccent', 'accentSoft', 'onAccentSoft', 'themeColor']);

/** The color schemes. */
export const BrandSchemes = Object.freeze(['light', 'dark']);

/** The engine page background of each scheme (the `background` of a pair). */
export const BrandBackgrounds = Object.freeze({ light: '#FFFFFF', dark: '#0E141D' });

/** WCAG 2.1 AA requirement levels. */
export const BrandContrastLevel = Object.freeze({ Text: 'Text', LargeText: 'LargeText', Component: 'Component' });

const REQUIRED = Object.freeze({ Text: 4.5, LargeText: 3, Component: 3 });

/** The pairs checked in each scheme, in display order. */
export const BrandContrastPairs = Object.freeze([
  { foreground: 'onPrimary', background: 'primary', level: 'Text', usage: 'Button labels' },
  { foreground: 'onPrimary', background: 'primaryHover', level: 'Text', usage: 'Button labels on hover' },
  { foreground: 'onAccent', background: 'accent', level: 'Text', usage: 'Text on accent surfaces' },
  { foreground: 'onAccentSoft', background: 'accentSoft', level: 'Text', usage: 'Selected chips, active navigation, and avatar initials' },
  { foreground: 'primary', background: 'background', level: 'Text', usage: 'Links and primary text on the page' },
  { foreground: 'primary', background: 'accentSoft', level: 'Component', usage: 'Selected chip borders on the soft accent' },
  { foreground: 'accent', background: 'background', level: 'Component', usage: 'Accent surfaces against the page' }
].map((p) => Object.freeze({ ...p, id: p.foreground + '/' + p.background, required: REQUIRED[p.level] })));

const HEX = /^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$/;

/**
 * Normalize an editable color (#RGB or #RRGGBB, any case) to uppercase #RRGGBB.
 * @param {string} value
 * @returns {string|null} null when the value is not a 3 or 6 digit hex color
 */
export function normalizeHex(value) {
  if (typeof value !== 'string') return null;
  let v = value.trim();
  if (!HEX.test(v)) return null;
  if (v.length === 4) v = '#' + v[1] + v[1] + v[2] + v[2] + v[3] + v[3];
  return v.toUpperCase();
}

function channel(hex) {
  const c = parseInt(hex, 16) / 255;
  return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
}

/**
 * WCAG 2.1 relative luminance of a color.
 * @param {string} hex
 * @returns {number} 0 to 1, or NaN for an invalid color
 */
export function relativeLuminance(hex) {
  const v = normalizeHex(hex);
  if (!v) return NaN;
  return 0.2126 * channel(v.slice(1, 3)) + 0.7152 * channel(v.slice(3, 5)) + 0.0722 * channel(v.slice(5, 7));
}

/**
 * WCAG 2.1 contrast ratio of two colors (1 to 21, unrounded).
 * @param {string} foreground
 * @param {string} background
 * @returns {number} NaN when a color is invalid
 */
export function contrastRatio(foreground, background) {
  const a = relativeLuminance(foreground);
  const b = relativeLuminance(background);
  if (Number.isNaN(a) || Number.isNaN(b)) return NaN;
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

/**
 * A ratio rounded to two decimals for display, never rounded up to a threshold it misses
 * (4.4999 shows as 4.49), so a value shown as passing always passes.
 * @param {number} ratio
 * @returns {number}
 */
export function displayRatio(ratio) {
  if (!Number.isFinite(ratio)) return 0;
  const rounded = Math.round(ratio * 100 + Number.EPSILON) / 100;
  if ((ratio < 4.5 && rounded >= 4.5) || (ratio < 3 && rounded >= 3)) return Math.floor(ratio * 100) / 100;
  return rounded;
}

/** Format a ratio, for example "4.29:1". */
export function formatRatio(ratio) {
  return displayRatio(ratio).toFixed(2) + ':1';
}

function normalizeSet(set) {
  const source = set && typeof set === 'object' ? set : {};
  const out = {};
  for (const key of BrandColorKeys) {
    const raw = typeof source[key] === 'string' ? source[key].trim() : '';
    // property.json accepts #RRGGBBAA; palettes are opaque, so the alpha is dropped.
    out[key] = normalizeHex(/^#[0-9A-Fa-f]{8}$/.test(raw) ? raw.slice(0, 7) : raw) || '';
  }
  if (!out.onAccentSoft) out.onAccentSoft = out.primary;
  return out;
}

/**
 * A property's palette `{ light, dark }` with every key in both schemes (uppercase #RRGGBB;
 * `onAccentSoft` falls back to `primary`). Reads `property.brand.colors`.
 * @param {object} property
 * @returns {{ light: Record<string, string>, dark: Record<string, string> }}
 */
export function brandPalette(property) {
  const colors = (property && property.brand && property.brand.colors) || {};
  return { light: normalizeSet(colors.light), dark: normalizeSet(colors.dark) };
}

/**
 * Merge a partial palette over a palette. Valid values win; invalid or unknown entries are ignored.
 * @param {{ light?: object, dark?: object }} base
 * @param {{ light?: object, dark?: object }} patch
 * @returns {{ light: Record<string, string>, dark: Record<string, string> }}
 */
export function mergePalette(base, patch) {
  const out = { light: normalizeSet(base && base.light), dark: normalizeSet(base && base.dark) };
  for (const scheme of BrandSchemes) {
    const set = patch && patch[scheme];
    if (!set || typeof set !== 'object') continue;
    for (const key of BrandColorKeys) {
      const v = normalizeHex(set[key]);
      if (v) out[scheme][key] = v;
    }
  }
  return out;
}

/**
 * The keys whose values differ between two palettes, as the `PUT /admin/property/branding` body
 * (`{ light?, dark? }`, only the changed keys).
 * @param {{ light: object, dark: object }} before
 * @param {{ light: object, dark: object }} after
 * @returns {{ light?: Record<string, string>, dark?: Record<string, string> }}
 */
export function paletteChanges(before, after) {
  const body = {};
  for (const scheme of BrandSchemes) {
    const changed = {};
    for (const key of BrandColorKeys) {
      const a = normalizeHex(before && before[scheme] && before[scheme][key]);
      const b = normalizeHex(after && after[scheme] && after[scheme][key]);
      if (b && a !== b) changed[key] = b;
    }
    if (Object.keys(changed).length) body[scheme] = changed;
  }
  return body;
}

/**
 * Check every pair of both schemes (the same result as the backend's contrast list).
 * @param {{ light: object, dark: object }} palette
 * @param {Array<{ scheme: string, pair: string }>} [overrides] pairs saved with an override
 * @returns {Array<{ scheme, pair, foreground, background, foregroundColor, backgroundColor, ratio, required, level, passes, overridden, usage }>}
 */
export function checkPaletteContrast(palette, overrides = []) {
  const saved = Array.isArray(overrides) ? overrides : [];
  const checks = [];
  for (const scheme of BrandSchemes) {
    const set = normalizeSet(palette && palette[scheme]);
    for (const pair of BrandContrastPairs) {
      const fg = set[pair.foreground];
      const bg = pair.background === 'background' ? BrandBackgrounds[scheme] : set[pair.background];
      const exact = contrastRatio(fg, bg);
      const passes = Number.isFinite(exact) && exact >= pair.required;
      checks.push({
        scheme,
        pair: pair.id,
        foreground: pair.foreground,
        background: pair.background,
        foregroundColor: fg,
        backgroundColor: bg,
        ratio: displayRatio(exact),
        required: pair.required,
        level: pair.level,
        passes,
        overridden: !passes && saved.some((o) => o && o.scheme === scheme && o.pair === pair.id),
        usage: pair.usage
      });
    }
  }
  return checks;
}

/** The failing checks of a palette (no override considered). */
export function failingContrast(palette) {
  return checkPaletteContrast(palette).filter((c) => !c.passes);
}

/** The field error key the backend uses for a failing pair, for example `contrast.light.onPrimary/primary`. */
export function contrastFieldKey(scheme, pair) {
  return 'contrast.' + scheme + '.' + pair;
}

// ---- Palette presets (docs/TRELLISKIT.md section 2.1) ----

/** Which presets members may choose (`BrandingView.memberPresets.mode`). */
export const MemberPaletteMode = Object.freeze({ All: 'All', None: 'None', Selected: 'Selected' });

/**
 * The engine palette presets members may choose, from `GET /api/v1/property` `brand.presets`
 * (`[{ id, name, description, light, dark }]`, catalog order; empty when none are allowed).
 * Entries without an id or both schemes are left out.
 * @param {object} property
 * @returns {Array<{ id: string, name: string, description: string, light: object, dark: object }>}
 */
export function palettePresets(property) {
  const list = property && property.brand && Array.isArray(property.brand.presets) ? property.brand.presets : [];
  return list.filter((p) => p && typeof p.id === 'string' && p.id && p.light && p.dark);
}

/**
 * The preset that applies for a member's preference: the preset when the property still offers
 * it, else null (the property's brand palette).
 * @param {object} property
 * @param {string|null|undefined} presetId
 * @returns {object|null}
 */
export function resolvePalettePreset(property, presetId) {
  if (!presetId || typeof presetId !== 'string') return null;
  return palettePresets(property).find((p) => p.id === presetId) || null;
}

/**
 * The palette `{ light, dark }` to show: the member's preset when the property allows it, else
 * the property's brand palette (`brandPalette`). Every key is filled in both schemes.
 * @param {object} property
 * @param {string|null|undefined} presetId
 * @returns {{ light: Record<string, string>, dark: Record<string, string> }}
 */
export function effectivePalette(property, presetId) {
  const preset = resolvePalettePreset(property, presetId);
  if (!preset) return brandPalette(property);
  return { light: normalizeSet(preset.light), dark: normalizeSet(preset.dark) };
}

/**
 * The property with `brand.colors` replaced by the member's preset (when allowed), so code that
 * reads `property.brand.colors` (themes, runtime palettes) follows the member's choice. The logo,
 * icon, and every other field are unchanged. Returns the property itself when no preset applies.
 * @param {object} property
 * @param {string|null|undefined} presetId
 * @returns {object}
 */
export function withPalettePreset(property, presetId) {
  const preset = resolvePalettePreset(property, presetId);
  if (!preset) return property;
  return { ...property, brand: { ...property.brand, colors: effectivePalette(property, presetId) } };
}

/**
 * The `PUT .../branding` body that applies a preset to the property's palette: the changed keys of
 * both schemes and a note naming the preset (null when the palette already matches).
 * @param {{ light: object, dark: object }} current
 * @param {{ name?: string, light: object, dark: object }} preset
 * @returns {object|null}
 */
export function presetBrandingBody(current, preset) {
  const body = paletteChanges(current, { light: normalizeSet(preset && preset.light), dark: normalizeSet(preset && preset.dark) });
  if (!Object.keys(body).length) return null;
  if (preset && preset.name) body.note = ('Preset: ' + preset.name).slice(0, 200);
  return body;
}

/**
 * The `PUT .../branding/member-presets` body: `{ mode }`, plus `presetIds` (deduplicated) with
 * Selected.
 * @param {string} mode MemberPaletteMode value
 * @param {string[]} [presetIds]
 * @returns {{ mode: string, presetIds?: string[] }}
 */
export function memberPresetsBody(mode, presetIds = []) {
  if (mode !== MemberPaletteMode.Selected) return { mode: mode === MemberPaletteMode.None ? MemberPaletteMode.None : MemberPaletteMode.All };
  return { mode, presetIds: [...new Set((presetIds || []).filter((id) => typeof id === 'string' && id))] };
}
