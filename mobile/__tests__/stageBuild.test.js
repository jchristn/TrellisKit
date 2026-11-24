/** Packaging tests use synthetic properties so no private deployment data enters a fixture. */
const fs = require('fs');
const os = require('os');
const path = require('path');
const { stageBuild } = require('../scripts/stage-build');

describe('mobile build staging', () => {
  let root;
  let engineDir;
  let propertyDir;
  const write = (file, content = 'source') => { fs.mkdirSync(path.dirname(file), { recursive: true }); fs.writeFileSync(file, content); };
  beforeEach(() => {
    root = fs.mkdtempSync(path.join(os.tmpdir(), 'tk-stage-test-'));
    engineDir = path.join(root, 'engine');
    propertyDir = path.join(root, 'external', 'community');
    write(path.join(engineDir, 'mobile', 'package.json'), '{}');
    write(path.join(engineDir, 'mobile', 'eas.json'), JSON.stringify({ build: { production: { autoIncrement: true } } }));
    write(path.join(engineDir, 'mobile', 'src', 'app.js'));
    write(path.join(engineDir, 'mobile', 'modules', 'shared-credentials', 'ios', 'Module.swift'));
    write(path.join(engineDir, 'shared', 'package.json'), '{}');
    write(path.join(engineDir, 'shared', 'src', 'ApiClient.js'));
    write(path.join(propertyDir, 'property.json'), JSON.stringify({ id: 'app_12345678_abcdefgh', name: 'Community', mobile: { bundleId: 'org.example.app', androidPackage: 'org.example.app', scheme: 'example' }, membership: {}, domains: {}, brand: {} }));
    write(path.join(propertyDir, 'mobile', 'icon.png'));
    write(path.join(propertyDir, 'mobile', 'updates', 'certificate.pem'), '-----BEGIN CERTIFICATE-----\npublic\n-----END CERTIFICATE-----');
  });
  afterEach(() => fs.rmSync(root, { recursive: true, force: true }));

  it('packages an external property and native module without deployment secrets, drafts or generated projects', () => {
    for (const file of ['deploy/local.env', 'brand/drafts/icon.png', 'mobile/updates/private.key', 'mobile/drafts/icon.png', 'mobile/google-services.json']) write(path.join(propertyDir, file), 'secret');
    for (const file of ['ios/Generated.swift', 'node_modules/package/index.js', 'src/.env', 'src/secret.json']) write(path.join(engineDir, 'mobile', file), 'secret');
    const result = stageBuild({ engineDir, propertyDir, outputDir: path.join(root, 'stage') });
    expect(result.propertyPath).toBe('../community');
    expect(fs.readFileSync(path.join(result.mobileDir, 'modules/shared-credentials/ios/Module.swift'), 'utf8')).toBe('source');
    expect(fs.existsSync(path.join(result.root, 'trelliskit/shared/src/ApiClient.js'))).toBe(true);
    expect(fs.existsSync(path.join(result.propertyDir, 'mobile/updates/certificate.pem'))).toBe(true);
    for (const file of ['deploy/local.env', 'brand/drafts/icon.png', 'mobile/updates/private.key', 'mobile/drafts/icon.png', 'mobile/google-services.json']) expect(fs.existsSync(path.join(result.propertyDir, file))).toBe(false);
    for (const file of ['ios/Generated.swift', 'node_modules/package/index.js', 'src/.env', 'src/secret.json']) expect(fs.existsSync(path.join(result.mobileDir, file))).toBe(false);
    expect(fs.readFileSync(path.join(result.mobileDir, 'eas.json'), 'utf8')).toBe(fs.readFileSync(path.join(engineDir, 'mobile/eas.json'), 'utf8'));
  });

  it('returns canonical paths when the output parent is a symlink, keeping EAS project paths relative', () => {
    const actual = path.join(root, 'actual');
    const alias = path.join(root, 'alias');
    fs.mkdirSync(actual);
    fs.symlinkSync(actual, alias, 'dir');
    const result = stageBuild({ engineDir, propertyDir, outputDir: path.join(alias, 'stage') });
    expect(result.root).toBe(fs.realpathSync(path.join(actual, 'stage')));
    expect(result.mobileDir).toBe(fs.realpathSync(result.mobileDir));
    expect(path.relative(result.root, fs.realpathSync(result.mobileDir))).toBe(path.join('trelliskit', 'mobile'));
  });

  it('keeps engine and property output names stable across checkout casing and avoids folder collisions', () => {
    const renamedEngine = path.join(root, 'TrellisKit');
    const renamedProperty = path.join(root, 'external', 'Community');
    fs.renameSync(engineDir, renamedEngine);
    fs.renameSync(propertyDir, renamedProperty);
    const result = stageBuild({ engineDir: renamedEngine, propertyDir: renamedProperty, outputDir: path.join(root, 'case-stage') });
    expect(path.relative(result.root, result.mobileDir)).toBe(path.join('trelliskit', 'mobile'));
    expect(result.propertyPath).toBe('../community');
    expect(path.basename(result.propertyDir)).toBe('community');
    const collision = path.join(root, 'external', 'TrellisKit');
    fs.renameSync(renamedProperty, collision);
    const second = stageBuild({ engineDir: renamedEngine, propertyDir: collision, outputDir: path.join(root, 'collision-stage') });
    expect(second.propertyPath).toBe('../property');
    expect(fs.existsSync(path.join(second.mobileDir, 'package.json'))).toBe(true);
    expect(fs.existsSync(path.join(second.propertyDir, 'property.json'))).toBe(true);
  });

  it('rejects an existing output, recursive staging, symlink assets and private material in certificate files', () => {
    expect(() => stageBuild({ engineDir, propertyDir, outputDir: root })).toThrow('must be empty');
    expect(() => stageBuild({ engineDir, propertyDir, outputDir: path.join(propertyDir, 'stage') })).toThrow('outside');
    fs.symlinkSync(path.join(propertyDir, 'property.json'), path.join(propertyDir, 'mobile/leak.png'));
    expect(() => stageBuild({ engineDir, propertyDir, outputDir: path.join(root, 'symlink-stage') })).toThrow('Symlinks');
    fs.unlinkSync(path.join(propertyDir, 'mobile/leak.png'));
    write(path.join(propertyDir, 'mobile/updates/certificate.pem'), '-----BEGIN CERTIFICATE-----\n-----BEGIN PRIVATE KEY-----');
    expect(() => stageBuild({ engineDir, propertyDir, outputDir: path.join(root, 'private-stage') })).toThrow('Only public certificates');
  });
});
