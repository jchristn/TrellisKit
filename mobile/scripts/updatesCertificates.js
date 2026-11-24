/**
 * Live updates code signing certificates (Node only): creation for
 * scripts/generate-updates-certificate.js and inspection for scripts/updates.js (app.config.js).
 *
 * What expo-updates 58 supports, and why the files look like this:
 *
 * - A build embeds exactly one certificate (`updates.codeSigningCertificate`, one
 *   `codeSigningMetadata.keyid`). There is no setting for a second embedded certificate and no
 *   runtime API that changes the certificate (EXUpdates/CodeSigning/CodeSigningConfiguration.swift
 *   and android/.../codesigning/CodeSigningConfiguration.kt hold a single
 *   `embeddedCertificateString`). In the default mode a signature whose keyid differs from the
 *   embedded keyid is rejected (KeyIdMismatchError), so a key switch would need a new binary.
 * - With `codeSigningIncludeManifestResponseCertificateChain` on, the client instead verifies the
 *   signature with the leaf of `<certificate_chain part of the manifest response> + <embedded
 *   certificate>`: the chain must link up to the embedded certificate, which must be self-signed
 *   and a CA (basicConstraints CA, keyUsage keyCertSign) when the chain is longer than one; the leaf
 *   needs keyUsage digitalSignature and extended key usage codeSigning. The keyid is not compared.
 *   With no chain part the embedded certificate is the leaf.
 *
 * TrellisKit therefore embeds the active certificate (`certificate.pem`, key id `main`) as a CA and
 * issues the standby certificate (`standby-certificate.pem`, key id `standby`) from it. Builds
 * trust both keys: manifests signed by `main` need no chain; manifests signed by `standby` carry
 * `standby-certificate.pem` in the `certificate_chain` part. The server can switch to the standby
 * key without a new binary. Both certificates are CAs (no path length limit) so later standbys
 * issued by a promoted key still chain back to older binaries' certificates.
 */
const crypto = require('crypto');
const path = require('path');

/** Code signing algorithm (the only one expo-updates implements). */
const Algorithm = 'rsa-v1_5-sha256';

/** node-forge, resolved through expo-updates' own certificate library. */
function forge() {
  const base = path.dirname(require.resolve('expo-updates/package.json'));
  const lib = require.resolve('@expo/code-signing-certificates', { paths: [base] });
  return require(require.resolve('node-forge', { paths: [path.dirname(lib)] }));
}

function serialNumber() {
  const f = forge();
  const hex = f.util.bytesToHex(f.random.getBytesSync(9));
  // Positive integer: clear the top bit.
  return (parseInt(hex[0], 16) & 7).toString(16) + hex.slice(1);
}

function validity(years, notAfterCap) {
  const notBefore = new Date();
  notBefore.setMinutes(notBefore.getMinutes() - 5);
  const notAfter = new Date(notBefore);
  notAfter.setFullYear(notAfter.getFullYear() + years);
  if (notAfterCap && notAfter > notAfterCap) return { notBefore, notAfter: new Date(notAfterCap) };
  return { notBefore, notAfter };
}

function extensions() {
  return [
    { name: 'basicConstraints', critical: true, cA: true },
    { name: 'keyUsage', critical: true, digitalSignature: true, keyCertSign: true },
    { name: 'extKeyUsage', critical: true, codeSigning: true },
    { name: 'subjectKeyIdentifier' }
  ];
}

/**
 * Generate an RSA 2048 key pair.
 * @returns {{ privateKeyPem: string, publicKeyPem: string }}
 */
function generateKeyPair() {
  const { privateKey, publicKey } = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
  return {
    privateKeyPem: privateKey.export({ type: 'pkcs8', format: 'pem' }).toString(),
    publicKeyPem: publicKey.export({ type: 'spki', format: 'pem' }).toString()
  };
}

/**
 * Public key PEM for a private key PEM.
 * @param {string} privateKeyPem
 */
function publicKeyFor(privateKeyPem) {
  return crypto.createPublicKey(privateKeyPem).export({ type: 'spki', format: 'pem' }).toString();
}

/**
 * Create the embedded (anchor) certificate: self-signed, CA, code signing.
 * @param {object} options
 * @param {string} options.privateKeyPem
 * @param {string} options.commonName
 * @param {number} [options.years]
 * @returns {string} Certificate PEM.
 */
function createActiveCertificate({ privateKeyPem, commonName, years = 10 }) {
  const { pki, md } = forge();
  const cert = pki.createCertificate();
  cert.publicKey = pki.publicKeyFromPem(publicKeyFor(privateKeyPem));
  cert.serialNumber = serialNumber();
  const { notBefore, notAfter } = validity(years);
  cert.validity.notBefore = notBefore;
  cert.validity.notAfter = notAfter;
  const attrs = [{ name: 'commonName', value: commonName }];
  cert.setSubject(attrs);
  cert.setIssuer(attrs);
  cert.setExtensions(extensions());
  cert.sign(pki.privateKeyFromPem(privateKeyPem), md.sha256.create());
  return pki.certificateToPem(cert);
}

/**
 * Create a standby certificate issued by the active certificate's key.
 * @param {object} options
 * @param {string} options.publicKeyPem Standby public key.
 * @param {string} options.issuerCertificatePem The active certificate.
 * @param {string} options.issuerPrivateKeyPem The active private key.
 * @param {string} options.commonName
 * @param {number} [options.years] Capped at the issuer's expiry.
 * @returns {string} Certificate PEM.
 */
function createStandbyCertificate({ publicKeyPem, issuerCertificatePem, issuerPrivateKeyPem, commonName, years = 10 }) {
  const { pki, md } = forge();
  const issuer = pki.certificateFromPem(issuerCertificatePem);
  if (publicKeyFor(issuerPrivateKeyPem) !== new crypto.X509Certificate(issuerCertificatePem).publicKey.export({ type: 'spki', format: 'pem' }).toString()) {
    throw new Error('The active private key does not match the active certificate.');
  }
  const cert = pki.createCertificate();
  cert.publicKey = pki.publicKeyFromPem(publicKeyPem);
  cert.serialNumber = serialNumber();
  const { notBefore, notAfter } = validity(years, issuer.validity.notAfter);
  cert.validity.notBefore = notBefore;
  cert.validity.notAfter = notAfter;
  cert.setSubject([{ name: 'commonName', value: commonName }]);
  cert.setIssuer(issuer.subject.attributes);
  cert.setExtensions([...extensions(), { name: 'authorityKeyIdentifier', keyIdentifier: issuer.generateSubjectKeyIdentifier().getBytes() }]);
  cert.sign(pki.privateKeyFromPem(issuerPrivateKeyPem), md.sha256.create());
  return pki.certificateToPem(cert);
}

const CodeSigningOid = '1.3.6.1.5.5.7.3.3';

/**
 * Check one certificate the way expo-updates does (validity, code signing usage).
 * @param {crypto.X509Certificate} cert
 * @returns {string[]} Problems.
 */
function certificateProblems(cert, label) {
  const problems = [];
  const now = Date.now();
  if (Date.parse(cert.validFrom) > now || Date.parse(cert.validTo) < now) problems.push(label + ' is not currently valid (' + cert.validFrom + ' to ' + cert.validTo + ')');
  if (!(cert.keyUsage || []).includes(CodeSigningOid)) problems.push(label + ' lacks the codeSigning extended key usage');
  return problems;
}

/**
 * Inspect a property's certificates.
 * @param {object} options
 * @param {string} options.activePem
 * @param {string|null} [options.standbyPem]
 * @returns {{ activeIsCa: boolean, activeSha256: string, standby: null|{ sha256: string, issuedByActive: boolean }, problems: string[], rotationReady: boolean }}
 */
function inspectCertificates({ activePem, standbyPem = null }) {
  const active = new crypto.X509Certificate(activePem);
  const problems = certificateProblems(active, 'certificate.pem');
  if (active.subject !== active.issuer || !active.verify(active.publicKey)) problems.push('certificate.pem is not self-signed');
  let standby = null;
  if (standbyPem) {
    const cert = new crypto.X509Certificate(standbyPem);
    const issuedByActive = cert.checkIssued(active) && cert.verify(active.publicKey);
    problems.push(...certificateProblems(cert, 'standby-certificate.pem'));
    if (!issuedByActive) problems.push('standby-certificate.pem is not issued by certificate.pem');
    standby = { sha256: cert.fingerprint256, issuedByActive };
  }
  return {
    activeIsCa: active.ca,
    activeSha256: active.fingerprint256,
    standby,
    problems,
    rotationReady: active.ca && Boolean(standby && standby.issuedByActive) && problems.length === 0
  };
}

/**
 * Verify a signature the way expo-updates does with the certificate chain option on: the chain
 * (leaf first, as the server sends it) plus the embedded certificate must link, the root must be
 * self-signed (and a CA when there is a chain), and the leaf's key must verify the signature.
 * Used by tests and by the certificate script's self-check; the device does the real check.
 * @param {object} options
 * @param {string} options.embeddedPem
 * @param {string[]} [options.chainPems]
 * @param {Buffer|string} options.data
 * @param {string} options.signature Base64.
 * @returns {boolean}
 */
function verifyLikeExpoUpdates({ embeddedPem, chainPems = [], data, signature }) {
  const chain = [...chainPems, embeddedPem].map((pem) => new crypto.X509Certificate(pem));
  const root = chain[chain.length - 1];
  // Self-signed (as expo-updates checks it: subject equals issuer and the own key verifies). Not
  // checkIssued, which also demands keyCertSign, and a lone embedded certificate need not be a CA.
  if (root.subject !== root.issuer || !root.verify(root.publicKey)) return false;
  for (let i = 0; i < chain.length - 1; i += 1) {
    if (!chain[i].checkIssued(chain[i + 1]) || !chain[i].verify(chain[i + 1].publicKey)) return false;
    if (!chain[i + 1].ca) return false;
  }
  const leaf = chain[0];
  if (certificateProblems(leaf, 'leaf').length) return false;
  return crypto.verify('sha256', Buffer.from(data), { key: leaf.publicKey, padding: crypto.constants.RSA_PKCS1_PADDING }, Buffer.from(signature, 'base64'));
}

/**
 * Sign like the server: RSASSA-PKCS1-v1_5 SHA-256, base64.
 * @param {string} privateKeyPem
 * @param {Buffer|string} data
 */
function sign(privateKeyPem, data) {
  return crypto.sign('sha256', Buffer.from(data), { key: privateKeyPem, padding: crypto.constants.RSA_PKCS1_PADDING }).toString('base64');
}

module.exports = {
  Algorithm,
  generateKeyPair,
  publicKeyFor,
  createActiveCertificate,
  createStandbyCertificate,
  inspectCertificates,
  verifyLikeExpoUpdates,
  sign
};
