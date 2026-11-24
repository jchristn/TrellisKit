/**
 * Decision 15 (docs/LIVE_UPDATES_CONTRACT.md): the channel is not part of the native fingerprint,
 * so every channel's binary of one commit has the same runtime version and the server can promote
 * an update between channels. Resolves the real runtime version (the same command the publish
 * tool and builds use) for each channel and compares.
 */
import childProcess from 'child_process';
import path from 'path';

const MOBILE_ROOT = path.resolve(__dirname, '..');

function runtimeVersion(platform, channel) {
  const bin = path.join(MOBILE_ROOT, 'node_modules', '.bin', 'expo-updates');
  const env = { ...process.env, APP_VARIANT: channel, TK_UPDATES_CHANNEL: channel };
  const out = childProcess.execFileSync(bin, ['runtimeversion:resolve', '--platform', platform], { cwd: MOBILE_ROOT, env, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
  return JSON.parse(out.trim().split('\n').pop()).runtimeVersion;
}

describe.each(['ios', 'android'])('runtime version on %s', (platform) => {
  test('is the same for production, preview, and development', () => {
    const versions = ['production', 'preview', 'development'].map((channel) => runtimeVersion(platform, channel));
    expect(versions[0]).toMatch(/^[0-9a-f]{40}$/);
    expect(new Set(versions).size).toBe(1);
  });
});

test('the embedded native update config is identical for every channel', () => {
  const { updatesConfig } = require('../scripts/updates');
  const { loadActiveProperty } = require('../scripts/property');
  const sample = loadActiveProperty(path.join(MOBILE_ROOT, '..', 'samples/example'));
  const native = (channel) => updatesConfig({ property: sample.property, propertyDir: sample.dir, projectRoot: MOBILE_ROOT, env: { APP_VARIANT: channel } });
  const production = native('production');
  const preview = native('preview');
  expect(preview.updates).toEqual(production.updates);
  expect(preview.extra.channel).toBe('preview');
  expect(production.extra.channel).toBe('production');
});

test('the android and ios scripts that EAS prebuild rewrites are not part of the fingerprint', () => {
  // EAS Build runs prebuild, which rewrites `expo start --android` to `expo run:android` (and the
  // same for ios). Counting those scripts made the runtime version on EAS differ from the local one.
  const { SourceSkips } = require('expo/fingerprint');
  const config = require('../fingerprint.config');
  expect(config.sourceSkips & SourceSkips.PackageJsonAndroidAndIosScriptsIfNotContainRun).toBeTruthy();
  expect(config.sourceSkips & SourceSkips.ExpoConfigExtraSection).toBeTruthy();
});
