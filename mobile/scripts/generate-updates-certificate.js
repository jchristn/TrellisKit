#!/usr/bin/env node
/**
 * Generate and rotate the live updates code signing keys of a property (docs/TRELLISKIT.md
 * section 8.1, docs/LIVE_UPDATES_CONTRACT.md decision 14).
 *
 * Files in the property (public, committed):
 *   mobile/updates/certificate.pem          active certificate (key id `main`), embedded in builds
 *   mobile/updates/standby-certificate.pem  standby certificate (key id `standby`), issued by the
 *                                           active key; the server sends it as the manifest
 *                                           response `certificate_chain` when it signs with the
 *                                           standby key
 *   mobile/updates/signing.json             optional { "activeKeyId", "standbyKeyId" }, written by
 *                                           --promote-standby (defaults main and standby)
 * Private keys are written outside the repository only (default ~/.trelliskit/keys/<property slug>/,
 * mode 0600) and go to the backend (Secrets Manager or the admin portal), never into Git.
 * See scripts/updatesCertificates.js for why expo-updates needs this shape.
 *
 * Usage:
 *   node scripts/generate-updates-certificate.js [propertyDir] [options]
 *
 *   (no mode)            New property: create the active and the standby key pairs and certificates.
 *   --standby            Create only a new standby pair, issued by the existing active certificate.
 *                        Needs --active-key.
 *   --reissue-active     Re-issue certificate.pem for the same active key as a CA (upgrades a
 *                        certificate made before rotation support; binaries embedding the old
 *                        certificate keep working because the key is unchanged). Needs --active-key.
 *                        Combine with --standby to create the standby in the same run.
 *   --promote-standby    After the server switched signing to the standby key: the standby key
 *                        becomes the active key of the next store release and a new standby is
 *                        created. Needs --standby-key; --new-standby-id <id> (default standby-<date>).
 *
 *   --active-key <file>  Existing active private key (PEM)
 *   --standby-key <file> Existing standby private key (PEM)
 *   --key-dir <dir>      Where new private keys go (default ~/.trelliskit/keys/<property slug>)
 *   --years <n>          Certificate validity, 1 to 30 (default 10)
 *   --force              Overwrite existing certificate or key files
 *
 * propertyDir defaults to TK_PROPERTY_DIR, then the sample property. Relative paths resolve
 * against the engine root (the folder that contains mobile/), like TK_PROPERTY_DIR.
 *
 * Rotation procedure (no new binary needed):
 *   1. Every build already trusts the standby key (its certificate chains to the embedded one).
 *   2. Register the standby key and standby-certificate.pem with the backend in advance.
 *   3. To rotate (planned, or because the active key leaked): switch the property's signing to the
 *      standby key. The server then signs with it and sends standby-certificate.pem as the
 *      certificate_chain part; installed binaries accept those manifests.
 *   4. Before the next store release run --promote-standby with the standby private key. New
 *      binaries embed the promoted key's certificate (and its key id) and a new standby is created;
 *      register the new standby with the backend. Retire the old key on the server once nothing
 *      signs with it (installed old binaries keep working through the chain).
 *
 * Equivalent openssl commands for the initial pair (OpenSSL 1.1.1+):
 *   openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 3650 -subj "/CN=<property slug> updates main" \
 *     -addext "basicConstraints=critical,CA:TRUE" -addext "keyUsage=critical,digitalSignature,keyCertSign" \
 *     -addext "extendedKeyUsage=critical,codeSigning" -keyout main.key -out certificate.pem
 *   openssl req -new -newkey rsa:2048 -nodes -subj "/CN=<property slug> updates standby" -keyout standby.key -out standby.csr
 *   openssl x509 -req -in standby.csr -CA certificate.pem -CAkey main.key -CAcreateserial -sha256 -days 3650 \
 *     -extfile <(printf "basicConstraints=critical,CA:TRUE\nkeyUsage=critical,digitalSignature,keyCertSign\nextendedKeyUsage=critical,codeSigning") \
 *     -out standby-certificate.pem
 */
const childProcess = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { EngineRoot, loadActiveProperty, propertySlug } = require('./property');
const { certificatePath, standbyCertificatePath, signingConfigPath, readSigningConfig } = require('./updates');
const certs = require('./updatesCertificates');

function parseArgs(argv) {
  const args = { dir: undefined, mode: 'initial', standby: false, activeKey: '', standbyKey: '', keyDir: '', newStandbyId: '', years: 10, force: false };
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    const next = () => {
      i += 1;
      if (i >= argv.length) throw new Error(arg + ' needs a value.');
      return argv[i];
    };
    if (arg === '--force') args.force = true;
    else if (arg === '--standby') args.standby = true;
    else if (arg === '--reissue-active') args.mode = 'reissue';
    else if (arg === '--promote-standby') args.mode = 'promote';
    else if (arg === '--active-key') args.activeKey = next();
    else if (arg === '--standby-key') args.standbyKey = next();
    else if (arg === '--key-dir') args.keyDir = next();
    else if (arg === '--new-standby-id') args.newStandbyId = next().trim();
    else if (arg === '--years') args.years = Number(next());
    else if (arg === '--help' || arg === '-h') args.help = true;
    else if (arg.startsWith('--')) throw new Error('Unknown option ' + arg);
    else args.dir = arg;
  }
  if (args.mode === 'initial' && args.standby) args.mode = 'standby';
  if (!Number.isInteger(args.years) || args.years < 1 || args.years > 30) throw new Error('--years must be a whole number from 1 to 30.');
  if (args.newStandbyId && !/^[A-Za-z0-9._-]{1,64}$/.test(args.newStandbyId)) throw new Error('--new-standby-id may use letters, digits, dot, dash, and underscore (1 to 64).');
  return args;
}

/** The Git work tree containing a path, or null. */
function gitRoot(dir) {
  try {
    return childProcess.execFileSync('git', ['rev-parse', '--show-toplevel'], { cwd: dir, stdio: ['ignore', 'pipe', 'ignore'] }).toString().trim() || null;
  } catch {
    return null;
  }
}

function isInside(child, parent) {
  const rel = path.relative(parent, child);
  return rel === '' || (!rel.startsWith('..') && !path.isAbsolute(rel));
}

function readKey(file, label) {
  if (!file) throw new Error(label + ' is required for this mode.');
  const resolved = path.resolve(file);
  if (!fs.existsSync(resolved)) throw new Error(label + ' ' + resolved + ' does not exist.');
  return fs.readFileSync(resolved, 'utf8');
}

function sameKey(privateKeyPem, certificatePem) {
  const crypto = require('crypto');
  return certs.publicKeyFor(privateKeyPem) === new crypto.X509Certificate(certificatePem).publicKey.export({ type: 'spki', format: 'pem' }).toString();
}

/**
 * Plan and write the files for one run.
 * @param {object} args Parsed arguments.
 * @param {{ dir: string, property: object }} active The property.
 * @param {string} keyDir Absolute directory for new private keys.
 * @returns {{ written: string[], keys: Record<string, string>, activeKeyId: string, standbyKeyId: string }}
 */
function run(args, { dir, property }, keyDir) {
  const certFile = certificatePath(dir);
  const standbyFile = standbyCertificatePath(dir);
  const signing = readSigningConfig(dir);
  const writes = [];
  const keys = {};
  const keyFile = (keyId) => path.join(keyDir, keyId === 'main' ? 'updates-private-key.pem' : 'updates-' + keyId + '-private-key.pem');
  const name = (keyId) => propertySlug(property) + ' updates ' + keyId;
  const plan = (file, contents, mode) => {
    if (fs.existsSync(file) && !args.force) throw new Error(file + ' already exists; pass --force to overwrite it.');
    writes.push({ file, contents, mode });
  };
  let { activeKeyId, standbyKeyId } = signing;
  let activePem;
  let activeKeyPem;

  if (args.mode === 'initial') {
    activeKeyPem = certs.generateKeyPair().privateKeyPem;
    activePem = certs.createActiveCertificate({ privateKeyPem: activeKeyPem, commonName: name(activeKeyId), years: args.years });
    plan(certFile, activePem);
    plan(keyFile(activeKeyId), activeKeyPem, 0o600);
    keys[activeKeyId] = keyFile(activeKeyId);
  } else if (args.mode === 'standby' || args.mode === 'reissue') {
    if (!fs.existsSync(certFile)) throw new Error('No certificate at ' + certFile + '. Run without a mode first.');
    activeKeyPem = readKey(args.activeKey, '--active-key');
    const existing = fs.readFileSync(certFile, 'utf8');
    if (!sameKey(activeKeyPem, existing)) throw new Error('--active-key does not match ' + certFile + '.');
    if (args.mode === 'reissue') {
      activePem = certs.createActiveCertificate({ privateKeyPem: activeKeyPem, commonName: name(activeKeyId), years: args.years });
      writes.push({ file: certFile, contents: activePem });
    } else {
      activePem = existing;
      if (!certs.inspectCertificates({ activePem }).activeIsCa) throw new Error(certFile + ' is not a CA certificate, so it cannot issue a standby. Run --reissue-active --standby with the same --active-key first.');
    }
  } else if (args.mode === 'promote') {
    if (!fs.existsSync(standbyFile)) throw new Error('No standby certificate at ' + standbyFile + '.');
    activeKeyPem = readKey(args.standbyKey, '--standby-key');
    if (!sameKey(activeKeyPem, fs.readFileSync(standbyFile, 'utf8'))) throw new Error('--standby-key does not match ' + standbyFile + '.');
    activeKeyId = standbyKeyId;
    standbyKeyId = args.newStandbyId || 'standby-' + new Date().toISOString().slice(0, 10).replace(/-/g, '');
    if (standbyKeyId === activeKeyId) throw new Error('--new-standby-id must differ from the promoted key id ' + activeKeyId + '.');
    activePem = certs.createActiveCertificate({ privateKeyPem: activeKeyPem, commonName: name(activeKeyId), years: args.years });
    writes.push({ file: certFile, contents: activePem });
    writes.push({ file: signingConfigPath(dir), contents: JSON.stringify({ activeKeyId, standbyKeyId }, null, 2) + '\n' });
  }

  if (args.mode !== 'reissue' || args.standby) {
    const standbyKeyPem = certs.generateKeyPair().privateKeyPem;
    const standbyPem = certs.createStandbyCertificate({
      publicKeyPem: certs.publicKeyFor(standbyKeyPem),
      issuerCertificatePem: activePem,
      issuerPrivateKeyPem: activeKeyPem,
      commonName: name(standbyKeyId),
      years: args.years
    });
    if (args.mode === 'promote') writes.push({ file: standbyFile, contents: standbyPem });
    else plan(standbyFile, standbyPem);
    plan(keyFile(standbyKeyId), standbyKeyPem, 0o600);
    keys[standbyKeyId] = keyFile(standbyKeyId);
  }

  // Self-check before anything touches the disk.
  const standbyWrite = writes.find((w) => w.file === standbyFile);
  const standbyPem = standbyWrite ? standbyWrite.contents : fs.existsSync(standbyFile) ? fs.readFileSync(standbyFile, 'utf8') : null;
  const report = certs.inspectCertificates({ activePem, standbyPem });
  if (!report.activeIsCa || (standbyPem && !report.standby.issuedByActive) || report.problems.length) throw new Error('Generated certificates failed the self-check: ' + report.problems.join('; '));

  for (const w of writes) {
    fs.mkdirSync(path.dirname(w.file), { recursive: true, mode: w.mode ? 0o700 : undefined });
    fs.writeFileSync(w.file, w.contents, w.mode ? { mode: w.mode } : undefined);
  }
  return { written: writes.map((w) => w.file), keys, activeKeyId, standbyKeyId };
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) {
    console.log(fs.readFileSync(__filename, 'utf8').split('*/')[0]);
    return;
  }
  const active = loadActiveProperty(args.dir);
  const keyDir = path.resolve(args.keyDir || path.join(os.homedir(), '.trelliskit', 'keys', propertySlug(active.property)));
  const forbidden = [EngineRoot, active.dir, gitRoot(active.dir), gitRoot(EngineRoot)].filter(Boolean);
  for (const root of forbidden) {
    if (isInside(keyDir, root)) throw new Error('Refusing to write private keys inside ' + root + '. Choose a --key-dir outside the repository.');
  }

  const result = run(args, active, keyDir);
  console.log('Written:');
  for (const file of result.written) console.log('  ' + file + (Object.values(result.keys).includes(file) ? '   (private key: never commit)' : ''));
  console.log('');
  console.log('Key ids: active "' + result.activeKeyId + '", standby "' + result.standbyKeyId + '".');
  console.log('Next steps:');
  console.log('1. Commit the certificate files under ' + path.join(active.dir, 'mobile', 'updates') + ' (public by design).');
  for (const [keyId, file] of Object.entries(result.keys)) {
    console.log('2. Register key "' + keyId + '" with the backend for property "' + active.property.id + '" (admin portal signing keys, or Secrets Manager: aws secretsmanager put-secret-value --secret-id <the property updates signing secret> --secret-string file://' + file + ').');
  }
  console.log('3. Delete the local private keys once stored, or keep them only in a password manager.');
  if (args.mode === 'initial' || args.mode === 'reissue' || args.mode === 'promote') console.log('4. Rebuild the store binaries: they embed ' + path.basename(certificatePath(active.dir)) + ' and trust the standby key through it.');
}

if (require.main === module) {
  try {
    main();
  } catch (error) {
    console.error('generate-updates-certificate: ' + error.message);
    process.exit(1);
  }
}

module.exports = { parseArgs, run };
