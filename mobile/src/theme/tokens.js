import { getBundledProperty } from '../property/property';

/**
 * Design tokens. Neutral colors (backgrounds, text, status) belong to the engine; brand colors
 * (primary, onPrimary, accent, onAccent, accentSoft, onAccentSoft) come from the property's
 * brand.colors for each scheme: the bundled property at launch, then the live palette from
 * GET /api/v1/property (cached with its ETag) at runtime, so an admin's palette edit reaches the
 * app without a new build (docs/TRELLISKIT.md section 2.1). App icons and the splash screen are
 * generated at build time and do not follow runtime palette changes. Every screen reads colors,
 * spacing, type, and radii from here through useTheme().
 */

/** Spacing scale in density-independent pixels. */
export const spacing = Object.freeze({
  xxs: 2,
  xs: 4,
  sm: 8,
  md: 12,
  lg: 16,
  xl: 24,
  xxl: 32,
  xxxl: 48
});

/** Corner radii. */
export const radii = Object.freeze({
  sm: 6,
  md: 10,
  lg: 16,
  pill: 999
});

/**
 * Type scale. Only font sizes, weights, and line heights are set; the system font is used and
 * text always scales with the user's Dynamic Type or font size setting.
 */
export const typography = Object.freeze({
  display: { fontSize: 32, lineHeight: 40, fontWeight: '700' },
  title: { fontSize: 24, lineHeight: 32, fontWeight: '700' },
  heading: { fontSize: 18, lineHeight: 24, fontWeight: '600' },
  body: { fontSize: 16, lineHeight: 24, fontWeight: '400' },
  label: { fontSize: 16, lineHeight: 22, fontWeight: '600' },
  caption: { fontSize: 13, lineHeight: 18, fontWeight: '400' }
});

/** Layout breakpoints and widths. */
export const layout = Object.freeze({
  /** Screens at least this wide use the tablet (wide) layout. */
  wideBreakpoint: 768,
  /** Maximum width of a single reading column on wide screens. */
  maxContentWidth: 720,
  /** Width of the list pane in a two-pane layout. */
  listPaneWidth: 360,
  /** Wide windows at least this wide show a third (sidebar) pane; narrower ones put it in a drawer. */
  sidebarBreakpoint: 900,
  /** Width of the filter side panel on wide screens. */
  filterPanelWidth: 300,
  /** Narrowest readable detail pane; a list screen with a detail pane drops its filter side panel for the filter sheet below this. */
  minDetailPaneWidth: 420,
  /** Width of the sidebar pane (forums on tablets). */
  sidebarWidth: 280,
  /** Minimum touch target size (Apple HIG and Material guidance). */
  minTouchTarget: 44
});

/** Engine light palette. Text colors meet WCAG AA (4.5:1) against background and surface. */
export const neutralLightColors = Object.freeze({
  background: '#FFFFFF',
  surface: '#F3F4F6',
  surfaceRaised: '#FFFFFF',
  border: '#C9CED6',
  text: '#111418',
  textMuted: '#4A5260',
  primary: '#1F2933',
  onPrimary: '#FFFFFF',
  accent: '#1F2933',
  onAccent: '#FFFFFF',
  accentSoft: '#E5E7EB',
  onAccentSoft: '#111418',
  secondary: '#E5E7EB',
  onSecondary: '#111418',
  disabled: '#E5E7EB',
  onDisabled: '#4A5260',
  success: '#05603A',
  warning: '#93370D',
  danger: '#B42318',
  tabBar: '#FFFFFF',
  tabInactive: '#4A5260'
});

/** Engine dark palette. Text colors meet WCAG AA (4.5:1) against background and surface. */
export const neutralDarkColors = Object.freeze({
  background: '#0E1116',
  surface: '#1A1F27',
  surfaceRaised: '#232A34',
  border: '#3A4350',
  text: '#F2F4F7',
  textMuted: '#AEB6C2',
  primary: '#F2F4F7',
  onPrimary: '#0E1116',
  accent: '#F2F4F7',
  onAccent: '#0E1116',
  accentSoft: '#2C3440',
  onAccentSoft: '#F2F4F7',
  secondary: '#2C3440',
  onSecondary: '#F2F4F7',
  disabled: '#2C3440',
  onDisabled: '#AEB6C2',
  success: '#75E0A7',
  warning: '#FEC84B',
  danger: '#FDA29B',
  tabBar: '#12161C',
  tabInactive: '#AEB6C2'
});

const BRAND_KEYS = ['primary', 'onPrimary', 'accent', 'onAccent', 'accentSoft', 'onAccentSoft'];

/**
 * Overlay a property's brand colors for one scheme on the engine palette.
 * @param {object} neutral Engine palette.
 * @param {object|undefined} brand The property's brand.colors.light or brand.colors.dark.
 * @returns {object}
 */
export function buildColors(neutral, brand) {
  const colors = { ...neutral };
  for (const key of BRAND_KEYS) {
    if (brand && typeof brand[key] === 'string' && /^#[0-9A-Fa-f]{6}$/.test(brand[key])) colors[key] = brand[key];
  }
  return Object.freeze(colors);
}

const brandColors = (getBundledProperty().brand && getBundledProperty().brand.colors) || {};

/** Light palette for the active property. */
export const lightColors = buildColors(neutralLightColors, brandColors.light);

/** Dark palette for the active property. */
export const darkColors = buildColors(neutralDarkColors, brandColors.dark);

/**
 * Build the full theme for a color scheme.
 * @param {'light'|'dark'|null|undefined} scheme System color scheme; anything other than 'dark' yields light.
 * @returns {{ scheme: 'light'|'dark', dark: boolean, colors: typeof lightColors, spacing: typeof spacing, radii: typeof radii, typography: typeof typography, layout: typeof layout }}
 */
export function getTheme(scheme) {
  const dark = scheme === 'dark';
  return {
    scheme: dark ? 'dark' : 'light',
    dark,
    colors: dark ? darkColors : lightColors,
    spacing,
    radii,
    typography,
    layout
  };
}

export const lightTheme = Object.freeze(getTheme('light'));
export const darkTheme = Object.freeze(getTheme('dark'));

function sameBrand(a, b) {
  const x = a || {};
  const y = b || {};
  return BRAND_KEYS.every((key) => String(x[key] || '').toUpperCase() === String(y[key] || '').toUpperCase());
}

const runtimeThemes = new Map();

/**
 * The theme for a scheme and a property's brand.colors (for example the live palette from
 * GET /api/v1/property). Returns the bundled lightTheme or darkTheme object itself when the colors
 * match the bundled property, so components only re-render when the palette really changed;
 * other palettes are built once and cached (a few entries at most).
 * @param {'light'|'dark'|null|undefined} scheme
 * @param {{ light?: object, dark?: object }|null|undefined} colors
 * @returns {typeof lightTheme}
 */
export function themeFor(scheme, colors) {
  const dark = scheme === 'dark';
  const base = dark ? darkTheme : lightTheme;
  const brand = colors && (dark ? colors.dark : colors.light);
  if (!brand || sameBrand(brand, dark ? brandColors.dark : brandColors.light)) return base;
  const key = (dark ? 'dark:' : 'light:') + BRAND_KEYS.map((k) => String(brand[k] || '').toUpperCase()).join(',');
  let theme = runtimeThemes.get(key);
  if (!theme) {
    theme = Object.freeze({ ...base, colors: buildColors(dark ? neutralDarkColors : neutralLightColors, brand) });
    if (runtimeThemes.size >= 8) runtimeThemes.clear();
    runtimeThemes.set(key, theme);
  }
  return theme;
}
