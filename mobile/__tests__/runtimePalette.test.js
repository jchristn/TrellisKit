import { act, render, screen, waitFor } from '@testing-library/react-native';
import * as ReactNative from 'react-native';
import { AppState, Text } from 'react-native';
import { PropertyProvider } from '../src/property';
import { darkTheme, lightTheme, neutralLightColors, themeFor, useTheme } from '../src/theme';
import { installFakeServer, resetEnvironment } from '../test-utils/fakeServer';
import { property as active } from '../test-utils/property';

function withPrimary(light, dark) {
  return {
    ...active,
    web: undefined,
    brand: {
      ...active.brand,
      colors: { light: { ...active.brand.colors.light, primary: light, onAccentSoft: light }, dark: { ...active.brand.colors.dark, primary: dark } }
    }
  };
}

function Probe() {
  const theme = useTheme();
  return <Text testID="probe">{theme.scheme + ':' + theme.colors.primary + ':' + theme.colors.onAccentSoft}</Text>;
}

describe('runtime brand palette', () => {
  afterEach(() => {
    jest.restoreAllMocks();
    resetEnvironment();
  });

  test('themeFor returns the bundled theme objects for the bundled colors', () => {
    expect(themeFor('light', active.brand.colors)).toBe(lightTheme);
    expect(themeFor('dark', active.brand.colors)).toBe(darkTheme);
    expect(themeFor('light', null)).toBe(lightTheme);
    expect(themeFor('dark', { light: {} })).toBe(darkTheme);
  });

  test('themeFor builds a cached theme from a runtime palette, keeping engine neutrals and ignoring invalid values', () => {
    const colors = withPrimary('#0B3D91', '#9FC2F5').brand.colors;
    const light = themeFor('light', colors);
    expect(light).not.toBe(lightTheme);
    expect(light.colors.primary).toBe('#0B3D91');
    expect(light.colors.onAccentSoft).toBe('#0B3D91');
    expect(light.colors.background).toBe(lightTheme.colors.background);
    expect(light.spacing).toBe(lightTheme.spacing);
    expect(themeFor('light', colors)).toBe(light);
    expect(themeFor('dark', colors).colors.primary).toBe('#9FC2F5');
    const bad = themeFor('light', { light: { ...active.brand.colors.light, primary: 'red' } });
    expect(bad.colors.primary).toBe(neutralLightColors.primary);
  });

  test('onAccentSoft comes from the property and meets AA on accentSoft', () => {
    expect(lightTheme.colors.onAccentSoft).toBe(active.brand.colors.light.onAccentSoft || lightTheme.colors.onAccentSoft);
    expect(Object.keys(darkTheme.colors)).toContain('onAccentSoft');
  });

  test('useTheme follows the palette from GET /api/v1/property, then the ETag revalidation on foreground', async () => {
    const listeners = [];
    jest.spyOn(AppState, 'addEventListener').mockImplementation((type, fn) => {
      if (type === 'change') listeners.push(fn);
      return { remove: jest.fn() };
    });
    jest.spyOn(ReactNative, 'useColorScheme').mockReturnValue('light');
    const cache = { load: jest.fn(async () => null), save: jest.fn(async () => true) };
    const responses = [
      { status: 200, body: withPrimary('#0B3D91', '#9FC2F5'), headers: { ETag: '"b2"' } },
      { status: 200, body: withPrimary('#7A1F5C', '#E7A3CF'), headers: { ETag: '"b3"' } }
    ];
    const server = installFakeServer({ 'GET /property': () => responses.shift() });
    await render(
      <PropertyProvider cache={cache}>
        <Probe />
      </PropertyProvider>
    );
    await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('light:#0B3D91:#0B3D91'));
    await act(async () => listeners.forEach((fn) => fn('active')));
    await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('light:#7A1F5C:#7A1F5C'));
    expect(server.called('GET /property')[1].headers['If-None-Match']).toBe('"b2"');
  });

  test('a cached runtime palette applies at launch, before the network answers', async () => {
    jest.spyOn(ReactNative, 'useColorScheme').mockReturnValue('dark');
    const cache = { load: jest.fn(async () => ({ property: withPrimary('#0B3D91', '#9FC2F5'), etag: '"b2"' })), save: jest.fn(async () => true) };
    installFakeServer({ 'GET /property': () => new Promise(() => {}) });
    await render(
      <PropertyProvider cache={cache}>
        <Probe />
      </PropertyProvider>
    );
    await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent(/^dark:#9FC2F5:/));
  });
});
