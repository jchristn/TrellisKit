import { brandPalette } from '@trelliskit/shared';
import { PALETTE_STORAGE_KEY, RUNTIME_PALETTE_STYLE_ID, paletteCss } from './theme.js';

/**
 * Apply a property's live brand palette (GET /api/v1/property brand.colors) to the page without a
 * rebuild: a `<style id="tk-runtime-palette">` at the end of `<head>` overrides the build-time
 * theme's custom properties for light and dark, the theme-color meta follows, and the CSS is kept
 * in localStorage so the inline script in index.html applies it before the first paint of the next
 * page load (prerendered pages otherwise show their build-time colors until hydration).
 * @param {object} property the merged runtime property
 * @param {{ document?: Document, storage?: Storage|null }} [env]
 * @returns {string} the CSS applied ('' when nothing was applied)
 */
export function applyRuntimePalette(property, env = {}) {
  const doc = env.document || (typeof document !== 'undefined' ? document : null);
  if (!doc || !doc.head) return '';
  const palette = brandPalette(property);
  if (!palette.light.primary || !palette.dark.primary) return '';
  const css = paletteCss(palette, 'html:root');
  let style = doc.getElementById(RUNTIME_PALETTE_STYLE_ID);
  if (!style) {
    style = doc.createElement('style');
    style.id = RUNTIME_PALETTE_STYLE_ID;
  }
  if (style.textContent !== css) style.textContent = css;
  // Keep it last so it wins over the build-time theme and any later-loaded stylesheet.
  if (doc.head.lastElementChild !== style) doc.head.appendChild(style);
  const meta = doc.querySelector('meta[name="theme-color"]');
  if (meta && palette.light.themeColor) meta.setAttribute('content', palette.light.themeColor.toLowerCase());
  let storage = env.storage;
  if (storage === undefined) {
    try {
      storage = typeof window !== 'undefined' ? window.localStorage : null;
    } catch {
      storage = null;
    }
  }
  try {
    if (storage) storage.setItem(PALETTE_STORAGE_KEY, css);
  } catch {
    // Storage can be unavailable (private mode); the palette still applies for this page view.
  }
  return css;
}

/**
 * Whether a stored palette CSS string has exactly the shape paletteCss writes (two rule blocks of
 * hex custom properties). The inline script in index.html applies the same check before using it.
 * @param {string} css
 * @returns {boolean}
 */
export function isPaletteCss(css) {
  return typeof css === 'string' && css.length < 4000 && /^(html:root(\[data-theme='dark'\])? \{\n( {2}--[a-z-]+: #[0-9a-f]{6};\n)*\}\n){2}$/.test(css);
}
