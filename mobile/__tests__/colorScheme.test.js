import { Appearance } from 'react-native';
import { getColorSchemePreference, loadColorSchemePreference, resetColorSchemePreference, setColorSchemePreference } from '../src/theme/colorSchemePreference';

function fakeFile(initial) {
  const file = { exists: initial !== undefined, content: initial, textSync: () => file.content, create: jest.fn(() => { file.exists = true; }), write: jest.fn((text) => { file.content = text; }) };
  return file;
}

describe('color scheme preference', () => {
  let setScheme;
  beforeEach(() => {
    setScheme = jest.spyOn(Appearance, 'setColorScheme').mockImplementation(() => {});
  });
  afterEach(() => {
    setScheme.mockRestore();
    resetColorSchemePreference();
  });

  test('defaults to the device setting and overrides nothing', async () => {
    const file = fakeFile();
    resetColorSchemePreference(() => file);
    expect(await loadColorSchemePreference()).toBe('system');
    expect(setScheme).not.toHaveBeenCalled();
  });

  test('a stored choice is applied at launch', async () => {
    resetColorSchemePreference(() => fakeFile(JSON.stringify({ version: 1, mode: 'dark' })));
    expect(await loadColorSchemePreference()).toBe('dark');
    expect(setScheme).toHaveBeenCalledWith('dark');
  });

  test('choosing a mode applies and saves it, and system removes the override', () => {
    const file = fakeFile();
    resetColorSchemePreference(() => file);
    setColorSchemePreference('light');
    expect(getColorSchemePreference()).toBe('light');
    expect(setScheme).toHaveBeenLastCalledWith('light');
    expect(JSON.parse(file.content)).toEqual({ version: 1, mode: 'light' });
    setColorSchemePreference('system');
    expect(setScheme).toHaveBeenLastCalledWith('auto');
    expect(JSON.parse(file.content).mode).toBe('system');
  });

  test('an unreadable file or an unknown value means the device setting', async () => {
    resetColorSchemePreference(() => fakeFile('not json'));
    expect(await loadColorSchemePreference()).toBe('system');
    setColorSchemePreference('sepia');
    expect(getColorSchemePreference()).toBe('system');
  });
  test('a pending storage read cannot overwrite a newer user choice', async () => {
    let complete;
    const file = { exists: true, text: () => new Promise((resolve) => { complete = resolve; }), write: jest.fn() };
    resetColorSchemePreference(() => file);
    const loading = loadColorSchemePreference();
    setColorSchemePreference('light');
    complete(JSON.stringify({ version: 1, mode: 'dark' }));
    expect(await loading).toBe('light');
    expect(getColorSchemePreference()).toBe('light');
    expect(setScheme).toHaveBeenLastCalledWith('light');
  });

  test('storage failures still apply the selected mode', () => {
    resetColorSchemePreference(() => { throw new Error('Storage unavailable'); });
    setColorSchemePreference('dark');
    expect(getColorSchemePreference()).toBe('dark');
    expect(setScheme).toHaveBeenLastCalledWith('dark');
  });

});
