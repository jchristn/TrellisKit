import { AndroidEmulatorDefaultApiUrl, IosSimulatorDefaultApiUrl, resolveApiUrl } from '../src/config';
import { isWideWidth } from '../src/hooks/useLayout';
import { formatUptime } from '../src/strings';

describe('resolveApiUrl', () => {
  test('uses the configured URL and trims trailing slashes', () => {
    expect(resolveApiUrl({ configured: 'https://api.example.com/', os: 'ios' })).toBe('https://api.example.com');
  });

  test('falls back to platform defaults', () => {
    expect(resolveApiUrl({ configured: '', os: 'ios' })).toBe(IosSimulatorDefaultApiUrl);
    expect(resolveApiUrl({ configured: '', os: 'android' })).toBe(AndroidEmulatorDefaultApiUrl);
    expect(IosSimulatorDefaultApiUrl).toBe('http://127.0.0.1:8000');
    expect(AndroidEmulatorDefaultApiUrl).toBe('http://10.0.2.2:8000');
  });
});

describe('layout and formatting helpers', () => {
  test('isWideWidth switches at 768', () => {
    expect(isWideWidth(767)).toBe(false);
    expect(isWideWidth(768)).toBe(true);
  });

  test('formatUptime', () => {
    expect(formatUptime(5.7)).toBe('5s');
    expect(formatUptime(125)).toBe('2m 5s');
    expect(formatUptime(90061)).toBe('1d 1h 1m 1s');
    expect(formatUptime(-1)).toBe('');
  });
});
