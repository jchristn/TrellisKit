import { renderHook } from '@testing-library/react-native';
import * as ReactNative from 'react-native';
import { darkColors, getTheme, lightColors, useTheme } from '../src/theme';
import { property } from '../test-utils/property';

function luminance(hex) {
  const value = hex.replace('#', '');
  const channels = [0, 2, 4].map((index) => parseInt(value.slice(index, index + 2), 16) / 255);
  const linear = channels.map((c) => (c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4)));
  return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2];
}

function contrast(a, b) {
  const la = luminance(a);
  const lb = luminance(b);
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

describe('theme tokens', () => {
  afterEach(() => jest.restoreAllMocks());

  test('getTheme selects light and dark palettes', () => {
    expect(getTheme('light').colors).toBe(lightColors);
    expect(getTheme('dark').colors).toBe(darkColors);
    expect(getTheme('dark').dark).toBe(true);
    expect(getTheme(null).scheme).toBe('light');
    expect(getTheme(undefined).scheme).toBe('light');
  });

  test('useTheme follows the system color scheme', async () => {
    const spy = jest.spyOn(ReactNative, 'useColorScheme');
    spy.mockReturnValue('dark');
    expect((await renderHook(() => useTheme())).result.current.colors).toBe(darkColors);
    spy.mockReturnValue('light');
    expect((await renderHook(() => useTheme())).result.current.colors).toBe(lightColors);
  });

  test('brand colors come from the active property', () => {
    expect(lightColors.primary).toBe(property.brand.colors.light.primary);
    expect(lightColors.onPrimary).toBe(property.brand.colors.light.onPrimary);
    expect(darkColors.primary).toBe(property.brand.colors.dark.primary);
    expect(darkColors.accent).toBe(property.brand.colors.dark.accent);
  });

  test('both palettes define the same color keys', () => {
    expect(Object.keys(darkColors).sort()).toEqual(Object.keys(lightColors).sort());
  });

  test.each([
    ['light', lightColors],
    ['dark', darkColors]
  ])('%s palette (engine neutrals plus the property brand colors) meets WCAG AA contrast', (_name, colors) => {
    const pairs = [
      ['text', 'background'],
      ['text', 'surface'],
      ['textMuted', 'background'],
      ['textMuted', 'surface'],
      ['primary', 'background'],
      ['primary', 'surface'],
      ['onPrimary', 'primary'],
      ['onAccent', 'accent'],
      ['onAccentSoft', 'accentSoft'],
      ['onSecondary', 'secondary'],
      ['onDisabled', 'disabled'],
      ['success', 'surface'],
      ['warning', 'surface'],
      ['danger', 'surface'],
      ['tabInactive', 'tabBar']
    ];
    for (const [fg, bg] of pairs) {
      expect({ pair: fg + ' on ' + bg, ratio: contrast(colors[fg], colors[bg]) >= 4.5 }).toEqual({ pair: fg + ' on ' + bg, ratio: true });
    }
  });
});
