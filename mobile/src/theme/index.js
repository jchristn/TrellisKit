import { useColorScheme } from 'react-native';
import { DarkTheme, DefaultTheme } from 'expo-router';
import { resolvePalettePreset } from '@trelliskit/shared';
import { useProperty } from '../property/PropertyProvider';
import { loadColorSchemePreference } from './colorSchemePreference';
import { usePalettePreference } from './palettePreference';
import { themeFor } from './tokens';

export * from './tokens';
export { getPalettePreference, setPalettePreference, usePalettePreference } from './palettePreference';
export { ColorSchemeModes, setColorSchemePreference, useColorSchemePreference } from './colorSchemePreference';

// Apply this device's light or dark choice before the first frame.
loadColorSchemePreference();

/**
 * Current theme, following the color scheme (the device's, or the member's light or dark choice
 * on this device, Account, Appearance) and the property's live brand palette
 * (PropertyProvider revalidates GET /api/v1/property with its ETag at launch and on foreground, so
 * an admin's palette change applies without a new build). The member's palette preset (Account,
 * Appearance) replaces the property's colors while the live property still offers it in
 * `brand.presets`; otherwise the property palette applies. Outside a PropertyProvider the bundled
 * property's colors are used.
 * @returns {import('./tokens').lightTheme}
 */
export function useTheme() {
  const scheme = useColorScheme();
  const property = useProperty();
  const presetId = usePalettePreference();
  const preset = resolvePalettePreset(property, presetId);
  const colors = preset ? { light: preset.light, dark: preset.dark } : property && property.brand ? property.brand.colors : null;
  return themeFor(scheme, colors);
}

/**
 * Map app tokens onto a React Navigation theme so headers and tab bars match.
 * @param {typeof lightTheme} theme
 */
export function toNavigationTheme(theme) {
  const base = theme.dark ? DarkTheme : DefaultTheme;
  return {
    ...base,
    dark: theme.dark,
    colors: {
      ...base.colors,
      primary: theme.colors.primary,
      background: theme.colors.background,
      card: theme.colors.tabBar,
      text: theme.colors.text,
      border: theme.colors.border,
      notification: theme.colors.danger
    }
  };
}
