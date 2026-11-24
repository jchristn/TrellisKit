import { brandPalette, normalizeHex } from '@trelliskit/shared';

/**
 * Brand palette to CSS custom properties (docs/TRELLISKIT.md section 2.1). One mapping serves the
 * build (scripts/property.mjs generates the theme from property.json) and the browser (the live
 * palette from GET /api/v1/property replaces it at runtime without a rebuild). No browser or Node
 * APIs at import time.
 */

/** [CSS custom property, brand color key]. */
export const THEME_VARS = Object.freeze([
  ['--brand-primary', 'primary'],
  ['--brand-primary-hover', 'primaryHover'],
  ['--brand-primary-contrast', 'onPrimary'],
  ['--brand-accent', 'accent'],
  ['--brand-accent-contrast', 'onAccent'],
  ['--color-accent-soft', 'accentSoft'],
  ['--brand-accent-soft-contrast', 'onAccentSoft'],
  ['--brand-theme-color', 'themeColor']
]);

/** id of the style element that carries the live palette. */
export const RUNTIME_PALETTE_STYLE_ID = 'tk-runtime-palette';

/** localStorage key of the last live palette (applied before first paint by index.html). */
export const PALETTE_STORAGE_KEY = 'tk.palette';

function block(selector, set) {
  const lines = [];
  for (const [name, key] of THEME_VARS) {
    const value = normalizeHex(set && set[key]);
    if (value) lines.push('  ' + name + ': ' + value.toLowerCase() + ';\n');
  }
  return selector + ' {\n' + lines.join('') + '}\n';
}

/**
 * CSS for a palette `{ light, dark }`: `<root>` for light and `<root>[data-theme='dark']` for dark.
 * Invalid values are left out, so CSS never carries anything but hex colors. The build uses
 * `:root`; the live palette uses `html:root`, which is more specific, so it wins over the
 * build-time theme wherever the stylesheets end up in `<head>`.
 * @param {{ light: object, dark: object }} palette
 * @param {string} [root] ':root' (default) or 'html:root'
 * @returns {string}
 */
export function paletteCss(palette, root = ':root') {
  return block(root, palette && palette.light) + block(root + "[data-theme='dark']", palette && palette.dark);
}

/** CSS for a property's `brand.colors` (every key filled; onAccentSoft defaults to primary). */
export function propertyPaletteCss(property) {
  return paletteCss(brandPalette(property));
}
