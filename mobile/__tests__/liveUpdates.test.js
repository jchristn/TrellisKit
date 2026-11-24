/**
 * Live updates follow-ups (docs/LIVE_UPDATES_CONTRACT.md resolved decisions 11, 13 to 17):
 * install id, runtime channel and install id headers, key rotation certificates, publish tool
 * update groups and source maps, critical message, and the "new version available" hint.
 */
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import * as SecureStore from 'expo-secure-store';
import fs from 'fs';
import os from 'os';
import path from 'path';
import { Alert, AppState, Linking, Text } from 'react-native';
import { identityHeaders } from '../src/api/identity';
import { PropertyProvider } from '../src/property';
import strings from '../src/strings';
import {
  applyUpdateRequestHeaders,
  BuildChannelKey,
  criticalMessageFor,
  DismissedVersionKey,
  getInstallId,
  InstallIdKey,
  isBinaryBelowLatest,
  latestBinaryVersionFor,
  NewVersionHint,
  resolveBuildChannel,
  UpdatesController,
  UpdatesProvider,
  useNewVersionHint
} from '../src/updates';
import { resetInstallIdCache } from '../src/updates/installId';
import { binaryKey } from '../src/updates/requestHeaders';
import { resetEnvironment } from '../test-utils/fakeServer';
import { property } from '../test-utils/property';

const MOBILE_ROOT = path.resolve(__dirname, '..');
const ENGINE_ROOT = path.resolve(MOBILE_ROOT, '..');
const PLAIN_PROPERTY = path.join(MOBILE_ROOT, 'test-utils/fixtures/plain/property.json');

function fakeUpdates(overrides = {}) {
  return {
    isEnabled: true,
    isEmbeddedLaunch: true,
    updateId: 'embedded-1',
    checkForUpdateAsync: jest.fn(async () => ({ isAvailable: false, isRollBackToEmbedded: false })),
    fetchUpdateAsync: jest.fn(async () => ({ isNew: false, isRollBackToEmbedded: false })),
    reloadAsync: jest.fn(async () => {}),
    setUpdateRequestHeadersOverride: jest.fn(),
    ...overrides
  };
}

function constantsWith(channel, { appVersion = '1.0.0', build = '7' } = {}) {
  return { nativeAppVersion: appVersion, nativeBuildVersion: build, expoConfig: { extra: channel ? { updates: { channel } } : {} } };
}

function tempDir(prefix) {
  return fs.mkdtempSync(path.join(os.tmpdir(), prefix));
}

beforeEach(() => {
  resetEnvironment();
  resetInstallIdCache();
});

describe('install id (decision 13)', () => {
  test('is generated once, stored in secure storage, and reused', async () => {
    const first = await getInstallId();
    expect(first).toBeTruthy();
    expect(SecureStore.__store.get(InstallIdKey)).toBe(first);
    resetInstallIdCache();
    expect(await getInstallId()).toBe(first);
  });

  test('survives a storage write failure for the session', async () => {
    SecureStore.setItemAsync.mockRejectedValueOnce(new Error('keychain locked'));
    const id = await getInstallId();
    expect(id).toBeTruthy();
    expect(await getInstallId()).toBe(id);
  });

  test('is never sent on API requests, so it is not linked to the member', async () => {
    await getInstallId();
    expect(Object.keys(identityHeaders({ property, os: 'ios', updateId: 'u1' }))).not.toContain('X-TK-Install-Id');
  });
});

describe('runtime update request headers (decisions 13 and 15)', () => {
  const store = SecureStore;

  test('an embedded launch takes the channel from the embedded config and saves it for this binary', async () => {
    const channel = await resolveBuildChannel({ updates: { isEmbeddedLaunch: true }, constants: constantsWith('preview'), store });
    expect(channel).toBe('preview');
    expect(JSON.parse(SecureStore.__store.get(BuildChannelKey))).toEqual({ channel: 'preview', binary: '1.0.0(7)' });
  });

  test('a downloaded (or promoted) update keeps the binary channel, whatever its own config says', async () => {
    await resolveBuildChannel({ updates: { isEmbeddedLaunch: true }, constants: constantsWith('production'), store });
    expect(await resolveBuildChannel({ updates: { isEmbeddedLaunch: false }, constants: constantsWith('preview'), store })).toBe('production');
    expect(await resolveBuildChannel({ updates: { isEmbeddedLaunch: false }, constants: constantsWith(null), store })).toBe('production');
  });

  test('a different binary does not reuse the saved channel', async () => {
    await resolveBuildChannel({ updates: { isEmbeddedLaunch: true }, constants: constantsWith('preview'), store });
    const otherBinary = constantsWith(null, { build: '8' });
    expect(binaryKey(otherBinary)).toBe('1.0.0(8)');
    // No channel in the running config: the saved one is the best remaining guess.
    expect(await resolveBuildChannel({ updates: { isEmbeddedLaunch: false }, constants: otherBinary, store })).toBe('preview');
    expect(await resolveBuildChannel({ updates: { isEmbeddedLaunch: false }, constants: constantsWith('development', { build: '8' }), store })).toBe('development');
    SecureStore.__store.clear();
    expect(await resolveBuildChannel({ updates: { isEmbeddedLaunch: false }, constants: constantsWith(null), store })).toBe('production');
  });

  test('applies channel, property, app, and install id with setUpdateRequestHeadersOverride', async () => {
    const updates = fakeUpdates();
    const result = await applyUpdateRequestHeaders({ updates, constants: constantsWith('preview'), store, installId: async () => 'install-1' });
    expect(result.applied).toBe(true);
    expect(updates.setUpdateRequestHeadersOverride).toHaveBeenCalledWith({
      'expo-channel-name': 'preview',
      'X-TK-Property': property.id,
      'X-TK-App': property.mobile.bundleId,
      'X-TK-Install-Id': 'install-1'
    });
  });

  test('every runtime header key is embedded by the build, as expo-updates requires', async () => {
    const { updatesConfig } = require('../scripts/updates');
    const { loadActiveProperty } = require('../scripts/property');
    const sample = loadActiveProperty(path.join(ENGINE_ROOT, 'samples/example'));
    const live = updatesConfig({ property: sample.property, propertyDir: sample.dir, projectRoot: MOBILE_ROOT, env: { APP_VARIANT: 'preview' } });
    const updates = fakeUpdates();
    await applyUpdateRequestHeaders({ updates, constants: constantsWith('preview'), store, installId: async () => 'install-1' });
    const keys = Object.keys(updates.setUpdateRequestHeadersOverride.mock.calls[0][0]).map((k) => k.toLowerCase());
    const embedded = Object.keys(live.requestHeaders).map((k) => k.toLowerCase());
    expect(keys.sort()).toEqual(embedded.sort());
  });

  test('a binary that rejects the override keeps its embedded headers and nothing throws', async () => {
    const updates = fakeUpdates({
      setUpdateRequestHeadersOverride: jest.fn(() => {
        throw new Error('Invalid update requestHeaders override');
      })
    });
    const result = await applyUpdateRequestHeaders({ updates, constants: constantsWith('production'), store, installId: async () => 'install-1' });
    expect(result).toMatchObject({ applied: false, error: 'Invalid update requestHeaders override' });
    expect(await applyUpdateRequestHeaders({ updates: { isEnabled: true }, store })).toMatchObject({ applied: false, error: 'unsupported' });
  });

  test('the controller applies the headers once, before its first check', async () => {
    const order = [];
    const updates = fakeUpdates({ checkForUpdateAsync: jest.fn(async () => order.push('check') && { isAvailable: false }) });
    const prepare = jest.fn(async () => order.push('prepare'));
    const controller = new UpdatesController({ updates, prepare });
    await controller.check({ force: true });
    await controller.check({ force: true });
    expect(order).toEqual(['prepare', 'check', 'check']);
    expect(prepare).toHaveBeenCalledTimes(1);
  });

  test('a failing prepare is reported and the check still runs', async () => {
    const events = [];
    const updates = fakeUpdates();
    const controller = new UpdatesController({ updates, prepare: async () => ({ applied: false, error: 'rejected' }), onEvent: (e) => events.push(e) });
    await controller.check();
    expect(updates.checkForUpdateAsync).toHaveBeenCalledTimes(1);
    expect(events).toContainEqual(expect.objectContaining({ type: 'failure', reason: 'prepare', message: 'rejected' }));
  });
});

describe('critical message (decision 17)', () => {
  test('reads extra.criticalMessage, trimmed and capped at 200 characters', () => {
    expect(criticalMessageFor({ extra: { criticalMessage: '  Fixes sign in.  ' } })).toBe('Fixes sign in.');
    expect(criticalMessageFor({ extra: { criticalMessage: 'x'.repeat(300) } })).toHaveLength(200);
    expect(criticalMessageFor({ extra: { criticalMessage: '' } })).toBeNull();
    expect(criticalMessageFor({ extra: {} })).toBeNull();
    expect(criticalMessageFor(null)).toBeNull();
  });

  test('the critical prompt shows the message, and falls back to the default text', async () => {
    jest.spyOn(AppState, 'addEventListener').mockImplementation(() => ({ remove: jest.fn() }));
    jest.spyOn(Alert, 'alert').mockImplementation(() => {});
    const manifestFor = (id, extra) => ({ id, extra, metadata: {} });
    const run = async (manifest) => {
      const updates = fakeUpdates({
        checkForUpdateAsync: jest.fn(async () => ({ isAvailable: true, isRollBackToEmbedded: false, manifest })),
        fetchUpdateAsync: jest.fn(async () => ({ isNew: true, isRollBackToEmbedded: false, manifest }))
      });
      const controller = new UpdatesController({ updates });
      await act(async () => {
        await render(
          <PropertyProvider property={property}>
            <UpdatesProvider controller={controller}>
              <Text>app</Text>
            </UpdatesProvider>
          </PropertyProvider>
        );
      });
      return controller;
    };
    const first = await run(manifestFor('c1', { critical: true, criticalMessage: 'Fixes a crash when posting.' }));
    await waitFor(() => expect(Alert.alert).toHaveBeenCalledTimes(1));
    expect(Alert.alert.mock.calls[0][1]).toBe('Fixes a crash when posting.');
    expect(first.state.criticalMessage).toBe('Fixes a crash when posting.');
    await screen.unmount();
    await run(manifestFor('c2', { critical: true }));
    await waitFor(() => expect(Alert.alert).toHaveBeenCalledTimes(2));
    expect(Alert.alert.mock.calls[1][1]).toBe(strings.updates.readyMessage);
    jest.restoreAllMocks();
  });
});

describe('newer binary hint (decision 17)', () => {
  const withMobile = (mobile) => ({ ...property, mobile: { ...property.mobile, ...mobile } });

  test('reads mobile.latestBinaryVersion per platform or as one string', () => {
    const split = withMobile({ latestBinaryVersion: { ios: '2.0.0', android: '2.1.0' } });
    expect(latestBinaryVersionFor(split, 'android')).toBe('2.1.0');
    expect(latestBinaryVersionFor(withMobile({ latestBinaryVersion: '3.0' }), 'ios')).toBe('3.0');
    expect(latestBinaryVersionFor(property, 'ios')).toBeNull();
    expect(isBinaryBelowLatest(split, { os: 'ios', binaryVersion: '1.9.0' })).toBe(true);
    expect(isBinaryBelowLatest(split, { os: 'ios', binaryVersion: '2.0.0' })).toBe(false);
    expect(isBinaryBelowLatest(property, { os: 'ios', binaryVersion: '0.1.0' })).toBe(false);
  });

  function Host({ os: platform, binaryVersion }) {
    const hint = useNewVersionHint({ os: platform, binaryVersion });
    return (
      <>
        <NewVersionHint hint={hint} />
        <Text>the app</Text>
      </>
    );
  }

  const mount = (prop, platform, binaryVersion) =>
    act(async () => {
      await render(
        <PropertyProvider property={prop}>
          <Host os={platform} binaryVersion={binaryVersion} />
        </PropertyProvider>
      );
    });

  test('an older binary sees a dismissible hint next to the app, not the blocking screen', async () => {
    const open = jest.spyOn(Linking, 'openURL').mockResolvedValue(true);
    await mount(withMobile({ latestBinaryVersion: '2.0.0', appStoreId: '123' }), 'ios', '1.0.0');
    expect(await screen.findByTestId('new-version-hint')).toBeTruthy();
    expect(screen.getByText(strings.updates.newVersionMessage)).toBeTruthy();
    expect(screen.getByText('the app')).toBeTruthy();
    expect(screen.queryByTestId('update-required')).toBeNull();
    await fireEvent.press(screen.getByTestId('new-version-hint-store'));
    expect(open).toHaveBeenCalledWith('https://apps.apple.com/app/id123');
    await fireEvent.press(screen.getByTestId('new-version-hint-dismiss'));
    expect(screen.queryByTestId('new-version-hint')).toBeNull();
    await waitFor(() => expect(SecureStore.__store.get(DismissedVersionKey)).toBe('2.0.0'));
    open.mockRestore();
  });

  test('a dismissal holds for that version only', async () => {
    SecureStore.__store.set(DismissedVersionKey, '2.0.0');
    await mount(withMobile({ latestBinaryVersion: '2.0.0' }), 'ios', '1.0.0');
    await act(async () => {});
    expect(screen.queryByTestId('new-version-hint')).toBeNull();
    await screen.unmount();
    await mount(withMobile({ latestBinaryVersion: '2.1.0' }), 'ios', '1.0.0');
    expect(await screen.findByTestId('new-version-hint')).toBeTruthy();
  });

  test('a current binary, or one below the minimum (blocked instead), gets no hint', async () => {
    await mount(withMobile({ latestBinaryVersion: '2.0.0' }), 'ios', '2.0.0');
    await act(async () => {});
    expect(screen.queryByTestId('new-version-hint')).toBeNull();
    await screen.unmount();
    await mount(withMobile({ latestBinaryVersion: '3.0.0', minimumBinaryVersion: '2.0.0' }), 'ios', '1.0.0');
    await act(async () => {});
    expect(screen.queryByTestId('new-version-hint')).toBeNull();
  });
});

describe('key rotation certificates (decision 14)', () => {
  const certs = require('../scripts/updatesCertificates');
  const { run } = require('../scripts/generate-updates-certificate');
  const { updatesConfig, signingConfig, standbyCertificatePath, certificatePath } = require('../scripts/updates');

  function tempProperty() {
    const dir = tempDir('tk-prop-');
    fs.copyFileSync(PLAIN_PROPERTY, path.join(dir, 'property.json'));
    const prop = JSON.parse(fs.readFileSync(PLAIN_PROPERTY, 'utf8'));
    return { dir, property: prop, keyDir: tempDir('tk-keys-') };
  }

  const args = (overrides) => ({ mode: 'initial', standby: false, activeKey: '', standbyKey: '', newStandbyId: '', years: 10, force: false, ...overrides });
  const read = (file) => fs.readFileSync(file, 'utf8');

  test('a new property gets an embedded CA certificate (main) and a standby (standby) issued by it', () => {
    const p = tempProperty();
    const result = run(args(), p, p.keyDir);
    expect(result).toMatchObject({ activeKeyId: 'main', standbyKeyId: 'standby' });
    const report = certs.inspectCertificates({ activePem: read(certificatePath(p.dir)), standbyPem: read(standbyCertificatePath(p.dir)) });
    expect(report).toMatchObject({ activeIsCa: true, rotationReady: true, problems: [] });
    expect(fs.statSync(result.keys.main).mode & 0o777).toBe(0o600);
    expect(() => run(args(), p, p.keyDir)).toThrow(/already exists/);
  });

  test('builds trust both keys: main without a chain, standby with standby-certificate.pem as the chain', () => {
    const p = tempProperty();
    const { keys } = run(args(), p, p.keyDir);
    const embeddedPem = read(certificatePath(p.dir));
    const standbyPem = read(standbyCertificatePath(p.dir));
    const body = '{"id":"update-1"}';
    expect(certs.verifyLikeExpoUpdates({ embeddedPem, data: body, signature: certs.sign(read(keys.main), body) })).toBe(true);
    expect(certs.verifyLikeExpoUpdates({ embeddedPem, chainPems: [standbyPem], data: body, signature: certs.sign(read(keys.standby), body) })).toBe(true);
    // Standby without its certificate, or a foreign key, is rejected.
    expect(certs.verifyLikeExpoUpdates({ embeddedPem, data: body, signature: certs.sign(read(keys.standby), body) })).toBe(false);
    const stranger = certs.generateKeyPair().privateKeyPem;
    const strangerPem = certs.createActiveCertificate({ privateKeyPem: stranger, commonName: 'stranger' });
    expect(certs.verifyLikeExpoUpdates({ embeddedPem, chainPems: [strangerPem], data: body, signature: certs.sign(stranger, body) })).toBe(false);
  });

  test('the build embeds certificate.pem as keyid main, turns on the certificate chain option, and stays quiet when rotation is ready', () => {
    const p = tempProperty();
    run(args(), p, p.keyDir);
    const prop = { ...p.property, domains: { ...p.property.domains, api: 'api.example.test' } };
    const live = updatesConfig({ property: prop, propertyDir: p.dir, projectRoot: MOBILE_ROOT, env: {} });
    expect(live.enabled).toBe(true);
    expect(live.includeCertificateChain).toBe(true);
    expect(live.rotationWarning).toBeNull();
    expect(live.updates.codeSigningMetadata).toEqual({ keyid: 'main', alg: 'rsa-v1_5-sha256' });
  });

  test('an older non-CA certificate warns and is upgraded in place with the same key', () => {
    const p = tempProperty();
    const lib = require(require.resolve('@expo/code-signing-certificates', { paths: [path.dirname(require.resolve('expo-updates/package.json'))] }));
    const keyPair = lib.generateKeyPair();
    const legacy = lib.generateSelfSignedCodeSigningCertificate({ keyPair, validityNotBefore: new Date(Date.now() - 60000), validityNotAfter: new Date(Date.now() + 365 * 86400000), commonName: 'legacy' });
    fs.mkdirSync(path.dirname(certificatePath(p.dir)), { recursive: true });
    fs.writeFileSync(certificatePath(p.dir), lib.convertCertificateToCertificatePEM(legacy));
    const keyFile = path.join(p.keyDir, 'legacy.pem');
    fs.writeFileSync(keyFile, lib.convertKeyPairToPEM(keyPair).privateKeyPEM);
    expect(signingConfig(p.dir)).toMatchObject({ includeCertificateChain: false, warning: expect.stringMatching(/not a CA certificate/) });
    expect(() => run(args({ mode: 'standby', activeKey: keyFile }), p, p.keyDir)).toThrow(/--reissue-active/);
    run(args({ mode: 'reissue', standby: true, activeKey: keyFile }), p, p.keyDir);
    expect(signingConfig(p.dir)).toMatchObject({ includeCertificateChain: true, standby: true, warning: null });
    // Same key, so binaries embedding the legacy certificate still verify main signatures.
    const body = 'm';
    const signature = certs.sign(read(keyFile), body);
    expect(certs.verifyLikeExpoUpdates({ embeddedPem: lib.convertCertificateToCertificatePEM(legacy), data: body, signature })).toBe(true);
    expect(certs.verifyLikeExpoUpdates({ embeddedPem: read(certificatePath(p.dir)), data: body, signature })).toBe(true);
  });

  test('a missing standby warns that rotation is not possible', () => {
    const p = tempProperty();
    run(args(), p, p.keyDir);
    fs.rmSync(standbyCertificatePath(p.dir));
    expect(signingConfig(p.dir).warning).toMatch(/no standby certificate/);
  });

  test('promotion: the standby becomes active for new binaries, and old binaries still verify through the chain', () => {
    const p = tempProperty();
    const first = run(args(), p, p.keyDir);
    const oldEmbedded = read(certificatePath(p.dir));
    const oldStandbyCert = read(standbyCertificatePath(p.dir));
    const promoted = run(args({ mode: 'promote', standbyKey: first.keys.standby, newStandbyId: 'standby2' }), p, p.keyDir);
    expect(promoted).toMatchObject({ activeKeyId: 'standby', standbyKeyId: 'standby2' });
    expect(JSON.parse(read(path.join(p.dir, 'mobile/updates/signing.json')))).toEqual({ activeKeyId: 'standby', standbyKeyId: 'standby2' });
    const prop = { ...p.property, domains: { ...p.property.domains, api: 'api.example.test' } };
    expect(updatesConfig({ property: prop, propertyDir: p.dir, projectRoot: MOBILE_ROOT, env: {} }).updates.codeSigningMetadata.keyid).toBe('standby');

    const body = 'promoted';
    const signedByStandby = certs.sign(read(first.keys.standby), body);
    // New binaries: the promoted key is embedded, no chain.
    expect(certs.verifyLikeExpoUpdates({ embeddedPem: read(certificatePath(p.dir)), data: body, signature: signedByStandby })).toBe(true);
    // Old binaries (keyid main): chain is the old standby certificate.
    expect(certs.verifyLikeExpoUpdates({ embeddedPem: oldEmbedded, chainPems: [oldStandbyCert], data: body, signature: signedByStandby })).toBe(true);
    // After a second rotation, old binaries still verify with a longer chain.
    const signedByStandby2 = certs.sign(read(promoted.keys.standby2), body);
    expect(certs.verifyLikeExpoUpdates({ embeddedPem: oldEmbedded, chainPems: [read(standbyCertificatePath(p.dir)), oldStandbyCert], data: body, signature: signedByStandby2 })).toBe(true);
  });

  test('the sample property ships an embedded CA and a standby issued by it', () => {
    const sampleDir = path.join(ENGINE_ROOT, 'samples/example');
    expect(signingConfig(sampleDir)).toMatchObject({ keyIds: { activeKeyId: 'main', standbyKeyId: 'standby' }, includeCertificateChain: true, standby: true, warning: null });
  });

  test('the certificate chain plugin is registered only when the embedded certificate is a CA', () => {
    const config = require('../app.config.js')();
    expect(config.plugins).toContain('./plugins/withUpdatesCertificateChain');
    const index = (name) => config.plugins.findIndex((entry) => (Array.isArray(entry) ? entry[0] : entry) === name);
    expect(index('./plugins/withUpdatesCertificateChain')).toBeLessThan(index('expo-updates'));
  });
});

describe('publish tool (decisions 11 and 16)', () => {
  const publish = require('../scripts/publish-update');

  test('one updateGroupId per run, shared by every platform; --update-group-id joins a group', () => {
    const a = publish.parseArgs(['--channel', 'preview', '--message', 'm']);
    expect(a.updateGroupId).toMatch(/^[0-9a-f-]{36}$/);
    expect(publish.parseArgs(['--channel', 'preview', '--message', 'm']).updateGroupId).not.toBe(a.updateGroupId);
    const id = '0b6f7a3e-7f8c-4f43-9a4e-2f1d3c4b5a69';
    expect(publish.parseArgs(['--channel', 'preview', '--message', 'm', '--update-group-id', id]).updateGroupId).toBe(id);
    expect(() => publish.parseArgs(['--channel', 'preview', '--message', 'm', '--update-group-id', 'nope'])).toThrow(/UUID/);
  });

  test('--critical-message implies critical and is limited to 200 characters', () => {
    expect(publish.parseArgs(['--channel', 'production', '--message', 'm', '--critical-message', 'Fixes sign in'])).toMatchObject({ critical: true, criticalMessage: 'Fixes sign in' });
    expect(() => publish.parseArgs(['--channel', 'production', '--message', 'm', '--critical-message', 'x'.repeat(201)])).toThrow(/200/);
  });

  test('the uploaded config has no channel, so an update never decides it', () => {
    const config = { name: 'x', extra: { updates: { enabled: true, channel: 'preview', checkIntervalMinutes: 15 } } };
    expect(publish.publishedExpoConfig(config).extra.updates).toEqual({ enabled: true, checkIntervalMinutes: 15 });
    expect(config.extra.updates.channel).toBe('preview');
  });

  test('both platform descriptors carry the same group id and their private source maps', () => {
    const args = publish.parseArgs(['--channel', 'preview', '--message', 'Release', '--skip-export']);
    const updates = ['ios', 'android'].map((platform) => {
      const outDir = tempDir('tk-export-');
      const bundle = '_expo/static/js/' + platform + '/entry-abc.hbc';
      fs.mkdirSync(path.join(outDir, path.dirname(bundle)), { recursive: true });
      fs.mkdirSync(path.join(outDir, 'assets'));
      fs.writeFileSync(path.join(outDir, bundle), 'bundle-' + platform);
      fs.writeFileSync(path.join(outDir, bundle + '.map'), '{"version":3,"sources":[]}');
      fs.writeFileSync(path.join(outDir, 'assets/a1'), 'png');
      fs.writeFileSync(path.join(outDir, 'metadata.json'), JSON.stringify({ fileMetadata: { [platform]: { bundle, assets: [{ path: 'assets/a1', ext: 'png' }] } } }));
      return publish.buildUpdate({ platform, args, outDir, expoConfig: {}, commit: null });
    });
    expect(updates[0].updateGroupId).toBe(args.updateGroupId);
    expect(updates[1].updateGroupId).toBe(args.updateGroupId);
    for (const update of updates) {
      expect(update.sourceMaps).toEqual([expect.objectContaining({ path: expect.stringMatching(/\.hbc\.map$/), contentType: 'application/json', bundleKey: update.launchAsset.key, part: 'sourcemap-' + update.sourceMaps[0].key })]);
      expect(update.assets.map((a) => a.path)).not.toContain(update.sourceMaps[0].path);
    }
    // Same native layer for both channels' binaries: runtimeVersion is resolved per platform only.
    expect(updates[0].runtimeVersion).toMatch(/^[0-9a-f]{40}$/);
  });

  test('a missing source map fails the publish', () => {
    const outDir = tempDir('tk-export-');
    expect(() => publish.sourceMapsFor(outDir, 'bundle.hbc', 'k')).toThrow(/no source map/);
  });
});
