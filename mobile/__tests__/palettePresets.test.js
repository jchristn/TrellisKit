import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import * as ReactNative from 'react-native';
import { Text } from 'react-native';
import { PropertyProvider } from '../src/property';
import strings from '../src/strings';
import { getPalettePreference, setPalettePreference, useTheme } from '../src/theme';
import { loadPalettePreference, resetPalettePreference, subscribePalettePreference } from '../src/theme/palettePreference';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { atWidth } from '../test-utils/phase5';
import { property as active } from '../test-utils/property';

// The engine catalog is one file (the backend's embedded asset); the app gets it from GET /property.
const catalog = require('../../backend/src/TrellisKit.Core/Assets/palette-presets.json').presets;
const ocean = catalog.find((p) => p.id === 'ocean');
const graphite = catalog.find((p) => p.id === 'graphite');
const APP_DIR = path.resolve(__dirname, '../app');

function withPresets(presets) {
  return { ...active, web: undefined, brand: { ...active.brand, presets } };
}

function memoryFile(initial = null) {
  const file = { exists: initial !== null, content: initial, writes: 0 };
  file.textSync = () => file.content;
  file.create = () => { file.exists = true; };
  file.write = (text) => { file.content = text; file.writes += 1; };
  return file;
}

function Probe() {
  const theme = useTheme();
  return <Text testID="probe">{theme.scheme + ':' + theme.colors.primary + ':' + theme.colors.accentSoft}</Text>;
}

describe('palette preference', () => {
  afterEach(() => {
    jest.restoreAllMocks();
    resetPalettePreference();
    resetEnvironment();
  });

  test('stores a valid preset id in its file, clears with null, notifies subscribers, and loads it at launch', async () => {
    const file = memoryFile();
    resetPalettePreference(() => file);
    const seen = [];
    const off = subscribePalettePreference(() => seen.push(getPalettePreference()));
    setPalettePreference('ocean');
    expect(getPalettePreference()).toBe('ocean');
    expect(JSON.parse(file.content)).toEqual({ version: 1, presetId: 'ocean' });
    setPalettePreference('Bad Id');
    expect(getPalettePreference()).toBeNull();
    setPalettePreference(null);
    expect(seen).toEqual(['ocean', null]);
    off();
    resetPalettePreference(() => memoryFile(JSON.stringify({ version: 1, presetId: 'graphite' })));
    await loadPalettePreference();
    expect(getPalettePreference()).toBe('graphite');
    resetPalettePreference(() => memoryFile('not json'));
    await loadPalettePreference();
    expect(getPalettePreference()).toBeNull();
  });

  test('useTheme applies an allowed preset in both schemes and falls back to the property palette otherwise', async () => {
    resetPalettePreference(() => memoryFile());
    const scheme = jest.spyOn(ReactNative, 'useColorScheme').mockReturnValue('light');
    await render(
      <PropertyProvider property={withPresets([ocean])}>
        <Probe />
      </PropertyProvider>
    );
    expect(screen.getByTestId('probe')).toHaveTextContent(new RegExp('^light:' + active.brand.colors.light.primary + ':'));
    await act(async () => setPalettePreference('ocean'));
    expect(screen.getByTestId('probe')).toHaveTextContent('light:' + ocean.light.primary + ':' + ocean.light.accentSoft);
    scheme.mockReturnValue('dark');
    await act(async () => setPalettePreference(null));
    await act(async () => setPalettePreference('ocean'));
    expect(screen.getByTestId('probe')).toHaveTextContent('dark:' + ocean.dark.primary + ':' + ocean.dark.accentSoft);
    await act(async () => setPalettePreference('graphite'));
    expect(screen.getByTestId('probe')).toHaveTextContent(new RegExp('^dark:' + active.brand.colors.dark.primary + ':'));
  });
});

describe('Account, Appearance', () => {
  afterEach(() => {
    resetPalettePreference();
    resetEnvironment();
  });

  function serve(stored) {
    const state = { stored };
    const user = makeUser();
    const server = installFakeServer({
      ...signedInAs(user),
      'GET /property': withPresets([graphite, ocean]),
      'GET /me/appearance': () => ({ palettePreset: state.stored, effectivePreset: state.stored, presetsAvailable: true }),
      'PUT /me/appearance': (call) => {
        state.stored = call.body.palettePreset;
        return { palettePreset: state.stored, effectivePreset: state.stored, presetsAvailable: true };
      }
    });
    return { server, state };
  }

  test('lists the property palette and the allowed presets, saves the choice to the account, and applies it (phone)', async () => {
    resetPalettePreference(() => memoryFile());
    const { server, state } = serve(null);
    await renderRouter(APP_DIR, { initialUrl: '/account/appearance' });
    const def = await screen.findByTestId('appearance-default');
    await waitFor(() => expect(screen.getByTestId('appearance-ocean')).toBeTruthy());
    expect(def.props.accessibilityState.checked).toBe(true);
    expect(screen.getByText(graphite.name)).toBeTruthy();
    expect(screen.getByText(strings.appearance.propertyDefault)).toBeTruthy();
    await act(async () => fireEvent.press(screen.getByTestId('appearance-ocean')));
    await waitFor(() => expect(state.stored).toBe('ocean'));
    expect(server.called('PUT /me/appearance')[0].body).toEqual({ palettePreset: 'ocean' });
    expect(getPalettePreference()).toBe('ocean');
    await waitFor(() => expect(screen.getByTestId('appearance-ocean').props.accessibilityState.checked).toBe(true));
    await act(async () => fireEvent.press(screen.getByTestId('appearance-default')));
    await waitFor(() => expect(state.stored).toBeNull());
    expect(getPalettePreference()).toBeNull();
  });

  test('a preset the community no longer offers falls back to the property palette, with a notice (tablet)', async () => {
    resetPalettePreference(() => memoryFile());
    serve('lavender');
    await atWidth(1024, async () => {
      await renderRouter(APP_DIR, { initialUrl: '/account/appearance' });
      expect(await screen.findByTestId('appearance-disallowed')).toBeTruthy();
      await waitFor(() => expect(screen.getByTestId('appearance-default').props.accessibilityState.checked).toBe(true));
      expect(getPalettePreference()).toBe('lavender');
    });
  });

  test('the account screen links to Appearance', async () => {
    serve(null);
    await renderRouter(APP_DIR, { initialUrl: '/account' });
    await act(async () => fireEvent.press(await screen.findByTestId('account-appearance')));
    expect(await screen.findByTestId('screen-appearance')).toBeTruthy();
  });
});
