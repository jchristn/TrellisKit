#!/usr/bin/env node
/**
 * Publish a live (over the air) update for the active property (docs/TRELLISKIT.md section 8.1).
 *
 *   npm run publish-update -- --channel production --message "Fix the feed header" [options]
 *
 * Options:
 *   --channel <name>     production, preview, or development (required)
 *   --message <text>     Release note shown in the admin console (required)
 *   --platform <p>       ios, android, or all (default all)
 *   --critical           Mark the update critical: the app prompts "Restart to update" at once
 *   --critical-message <text>  Prompt text for a critical update (up to 200 characters; implies
 *                        --critical)
 *   --rollout <percent>  Staged rollout percentage, 1 to 100 (default 100)
 *   --skip-export        Reuse dist/updates/<platform> from a previous run
 *   --update-group-id <uuid>  Join an existing update group (for example to add the other
 *                        platform later). Default: a new UUID shared by every platform of this run
 *   --upload             Send the update to POST /api/v1/admin/updates. Without it this is a dry
 *                        run that prints exactly what would be sent.
 *
 * Environment: TK_PROPERTY_DIR (the property, as for builds), TK_UPDATES_ADMIN_URL (API origin;
 * default https://<domains.api>), TK_ADMIN_TOKEN (bearer token for --upload).
 *
 * For each platform it runs `expo export --platform <p> --source-maps external` into
 * dist/updates/<p> (SDK 58 replaced --dump-sourcemap with --source-maps; `external` writes
 * <bundle>.map next to the bundle without a reference inside it), resolves the runtime version
 * (`expo-updates runtimeversion:resolve`, fingerprint policy), and hashes every file (SHA-256
 * base64url as the protocol `hash`, MD5 hex as the asset `key`).
 *
 * The runtime version does not depend on the channel (the channel header is set at runtime, see
 * scripts/updates.js), so one export serves every channel and the server may promote an update
 * between channels. `expoConfig.extra.updates.channel` is removed from the uploaded config: the app
 * takes its channel from the binary, never from an update.
 *
 * Upload contract (provisional until the backend route lands): one multipart/form-data request
 * per platform to POST /api/v1/admin/updates with headers Authorization: Bearer <token>,
 * X-TK-Property, and X-TK-App, and parts:
 *   - `update`: JSON { platform, runtimeVersion, channel, message, critical, criticalMessage,
 *     rolloutPercentage, gitCommit, createdUtc, updateGroupId, expoConfig, launchAsset, assets,
 *     sourceMaps }, where launchAsset and each asset are { part, path, key, hash, contentType,
 *     fileExtension, size }, and each source map is { part, path, key, hash, contentType, size,
 *     bundleKey } (bundleKey is the launch asset key it belongs to)
 *   - one file part per asset and the launch asset, named by its `part` (the asset key)
 *   - one file part per source map, named by its `part` (`sourcemap-<md5 hex>`). Source maps are
 *     private: the server stores them for symbolicating client error reports and never serves them
 *     to apps
 * Every platform of one run carries the same `updateGroupId` (a UUID), so the server groups the
 * iOS and Android updates of a release without guessing.
 * The server stores the files content-addressed, creates the update record (Draft or Published
 * per its own rules), and signs manifests with TK_UPDATES_SIGNING_KEY. Nothing here needs the
 * private key.
 */
const childProcess = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const { MobileRoot, loadActiveProperty } = require('./property');
const { Channels, ManifestPath } = require('./updates');

const Platforms = ['ios', 'android'];

/** Longest critical prompt text (the server enforces the same limit). */
const MaxCriticalMessageLength = 200;

const UuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

const ContentTypes = {
  js: 'application/javascript',
  hbc: 'application/javascript',
  png: 'image/png',
  jpg: 'image/jpeg',
  jpeg: 'image/jpeg',
  gif: 'image/gif',
  webp: 'image/webp',
  svg: 'image/svg+xml',
  ttf: 'font/ttf',
  otf: 'font/otf',
  woff: 'font/woff',
  woff2: 'font/woff2',
  json: 'application/json',
  mp4: 'video/mp4',
  mp3: 'audio/mpeg',
  wav: 'audio/wav'
};

function parseArgs(argv) {
  const args = { channel: '', message: '', platform: 'all', critical: false, criticalMessage: null, rollout: 100, upload: false, skipExport: false, updateGroupId: '' };
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    const next = () => {
      i += 1;
      if (i >= argv.length) throw new Error(arg + ' needs a value.');
      return argv[i];
    };
    if (arg === '--channel') args.channel = next().trim().toLowerCase();
    else if (arg === '--message') args.message = next().trim();
    else if (arg === '--platform') args.platform = next().trim().toLowerCase();
    else if (arg === '--rollout') args.rollout = Number(next());
    else if (arg === '--critical') args.critical = true;
    else if (arg === '--critical-message') {
      args.criticalMessage = next().trim();
      args.critical = true;
    } else if (arg === '--update-group-id') args.updateGroupId = next().trim().toLowerCase();
    else if (arg === '--upload') args.upload = true;
    else if (arg === '--skip-export') args.skipExport = true;
    else if (arg === '--help' || arg === '-h') args.help = true;
    else throw new Error('Unknown argument ' + arg);
  }
  if (args.help) return args;
  if (!Channels.includes(args.channel)) throw new Error('--channel must be one of ' + Channels.join(', ') + '.');
  if (!args.message) throw new Error('--message is required.');
  if (args.platform !== 'all' && !Platforms.includes(args.platform)) throw new Error('--platform must be ios, android, or all.');
  if (!Number.isInteger(args.rollout) || args.rollout < 1 || args.rollout > 100) throw new Error('--rollout must be a whole number from 1 to 100.');
  if (args.criticalMessage !== null && (!args.criticalMessage || args.criticalMessage.length > MaxCriticalMessageLength)) throw new Error('--critical-message must be 1 to ' + MaxCriticalMessageLength + ' characters.');
  if (args.updateGroupId && !UuidPattern.test(args.updateGroupId)) throw new Error('--update-group-id must be a UUID.');
  if (!args.updateGroupId) args.updateGroupId = crypto.randomUUID();
  return args;
}

function run(command, args, options = {}) {
  const result = childProcess.spawnSync(command, args, { cwd: MobileRoot, env: process.env, encoding: 'utf8', ...options });
  if (result.status !== 0) throw new Error(command + ' ' + args.join(' ') + ' failed' + (result.stderr ? ':\n' + result.stderr : '.'));
  return result.stdout || '';
}

function gitCommit() {
  try {
    return childProcess.execFileSync('git', ['rev-parse', 'HEAD'], { cwd: MobileRoot, stdio: ['ignore', 'pipe', 'ignore'] }).toString().trim() || null;
  } catch {
    return null;
  }
}

function describeFile(root, relative, ext) {
  const bytes = fs.readFileSync(path.join(root, relative));
  const key = crypto.createHash('md5').update(bytes).digest('hex');
  return {
    part: key,
    path: relative,
    key,
    hash: crypto.createHash('sha256').update(bytes).digest('base64url'),
    contentType: ContentTypes[ext] || 'application/octet-stream',
    fileExtension: '.' + ext,
    size: bytes.length
  };
}

function exportPlatform(platform, outDir, skip) {
  if (!skip) {
    fs.rmSync(outDir, { recursive: true, force: true });
    console.log('Exporting ' + platform + ' to ' + path.relative(MobileRoot, outDir) + ' ...');
    run('npx', ['expo', 'export', '--platform', platform, '--source-maps', 'external', '--output-dir', outDir], { stdio: ['ignore', 'inherit', 'inherit'] });
  }
  const metadataFile = path.join(outDir, 'metadata.json');
  if (!fs.existsSync(metadataFile)) throw new Error('No export at ' + outDir + ' (missing metadata.json).');
  return JSON.parse(fs.readFileSync(metadataFile, 'utf8'));
}

function buildUpdate({ platform, args, outDir, expoConfig, commit }) {
  const metadata = exportPlatform(platform, outDir, args.skipExport);
  const files = metadata.fileMetadata && metadata.fileMetadata[platform];
  if (!files || !files.bundle) throw new Error('The ' + platform + ' export has no bundle.');
  const resolved = JSON.parse(run('npx', ['expo-updates', 'runtimeversion:resolve', '--platform', platform]).trim().split('\n').pop());
  const bundleExt = path.extname(files.bundle).slice(1) || 'js';
  const launchAsset = { ...describeFile(outDir, files.bundle, bundleExt), contentType: 'application/javascript', fileExtension: '.bundle' };
  const assets = (files.assets || []).map((asset) => describeFile(outDir, asset.path, asset.ext));
  return {
    platform,
    runtimeVersion: resolved.runtimeVersion,
    channel: args.channel,
    message: args.message,
    critical: args.critical,
    criticalMessage: args.critical ? args.criticalMessage : null,
    rolloutPercentage: args.rollout,
    gitCommit: commit,
    createdUtc: new Date().toISOString(),
    updateGroupId: args.updateGroupId,
    expoConfig,
    launchAsset,
    assets,
    sourceMaps: sourceMapsFor(outDir, files.bundle, launchAsset.key)
  };
}

/**
 * The source map files of an export (the bundle's `<bundle>.map`, written by
 * `--source-maps external`), described for upload as private parts.
 * @param {string} outDir
 * @param {string} bundle Bundle path relative to outDir.
 * @param {string} bundleKey The launch asset key.
 */
function sourceMapsFor(outDir, bundle, bundleKey) {
  const relative = bundle + '.map';
  if (!fs.existsSync(path.join(outDir, relative))) throw new Error('The export has no source map at ' + relative + '. Run without --skip-export.');
  const file = describeFile(outDir, relative, 'map');
  return [{ part: 'sourcemap-' + file.key, path: file.path, key: file.key, hash: file.hash, contentType: 'application/json', size: file.size, bundleKey }];
}

/**
 * The public config to upload: the build's config without the channel (an update never decides
 * the channel; the binary does, see src/updates/requestHeaders.js).
 * @param {object} config `expo config --type public` output.
 */
function publishedExpoConfig(config) {
  const copy = JSON.parse(JSON.stringify(config));
  if (copy.extra && copy.extra.updates && typeof copy.extra.updates === 'object') delete copy.extra.updates.channel;
  return copy;
}

async function upload({ update, outDir, url, token, property }) {
  const form = new FormData();
  form.append('update', new Blob([JSON.stringify(update)], { type: 'application/json' }), 'update.json');
  for (const file of [update.launchAsset, ...update.assets, ...update.sourceMaps]) {
    form.append(file.part, new Blob([fs.readFileSync(path.join(outDir, file.path))], { type: file.contentType }), path.basename(file.path));
  }
  const appId = update.platform === 'android' ? property.mobile.androidPackage : property.mobile.bundleId;
  const response = await fetch(url, { method: 'POST', headers: { Authorization: 'Bearer ' + token, 'X-TK-Property': property.id, 'X-TK-App': appId }, body: form });
  const text = await response.text();
  if (!response.ok) throw new Error('Upload of ' + update.platform + ' failed: HTTP ' + response.status + ' ' + text);
  console.log('Uploaded ' + update.platform + ': ' + text);
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) {
    console.log(fs.readFileSync(__filename, 'utf8').split('*/')[0]);
    return;
  }
  const { property } = loadActiveProperty();
  const origin = ((process.env.TK_UPDATES_ADMIN_URL || '').trim() || (property.domains.api ? 'https://' + property.domains.api : '')).replace(/\/+$/, '');
  if (!origin) throw new Error('No API origin: set TK_UPDATES_ADMIN_URL or the property domains.api.');
  const url = origin + '/api/v1/admin/updates';
  const token = (process.env.TK_ADMIN_TOKEN || '').trim();
  if (args.upload && !token) throw new Error('--upload needs TK_ADMIN_TOKEN.');

  // APP_VARIANT picks the API URL in the published config (development has none, so development
  // updates are not promotable to other channels). The channel is not native config, so it does not
  // change the runtime version. Other build variables (EXPO_PUBLIC_*, EAS_PROJECT_ID,
  // GOOGLE_SERVICES_JSON) must match the EAS environment of the binaries, or the runtime version
  // will not match them.
  process.env.APP_VARIANT = args.channel;
  process.env.TK_UPDATES_CHANNEL = args.channel;
  const expoConfig = publishedExpoConfig(JSON.parse(run('npx', ['expo', 'config', '--json', '--type', 'public'])));
  const commit = gitCommit();
  const platforms = args.platform === 'all' ? Platforms : [args.platform];
  for (const platform of platforms) {
    const outDir = path.join(MobileRoot, 'dist', 'updates', platform);
    const update = buildUpdate({ platform, args, outDir, expoConfig, commit });
    const totalBytes = [update.launchAsset, ...update.assets].reduce((sum, f) => sum + f.size, 0);
    if (!args.upload) {
      console.log('');
      console.log('Dry run (' + platform + '): would POST ' + url + ' as multipart/form-data');
      console.log('Headers: Authorization: Bearer <TK_ADMIN_TOKEN>, X-TK-Property: ' + property.id + ', X-TK-App: ' + (platform === 'android' ? property.mobile.androidPackage : property.mobile.bundleId));
      console.log('Parts: update (JSON below), ' + (update.assets.length + 1) + ' files (' + totalBytes + ' bytes), and ' + update.sourceMaps.length + ' private source map(s)');
      console.log(JSON.stringify({ ...update, expoConfig: '<' + Object.keys(expoConfig).length + ' keys>' }, null, 2));
      continue;
    }
    await upload({ update, outDir, url, token, property });
  }
  if (!args.upload) console.log('\nNothing was sent. Pass --upload (with TK_ADMIN_TOKEN) once POST /api/v1/admin/updates exists. Apps check ' + ManifestPath + '.');
}

if (require.main === module) {
  main().catch((error) => {
    console.error('publish-update: ' + error.message);
    process.exit(1);
  });
}

module.exports = { parseArgs, buildUpdate, sourceMapsFor, publishedExpoConfig, MaxCriticalMessageLength };
