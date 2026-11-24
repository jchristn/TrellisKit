/**
 * Make a reviewable mobile build directory from an engine and an external property.
 * Only runtime sources and public property assets are copied. Credentials, generated native
 * projects, dependency directories and unrelated property files never enter the staging tree.
 * Run npm ci in the returned mobileDir; use EAS_NO_VCS=1 and EAS_PROJECT_ROOT=the staging root.
 * The selected EAS environment must set TK_PROPERTY_DIR to the returned relative property path.
 */
const fs = require('fs');
const os = require('os');
const path = require('path');
const { EngineRoot, resolvePropertyDir, readProperty } = require('./property');

const MobileFiles = ['package.json', 'package-lock.json', 'app.config.js', 'eas.json', 'metro.config.js', 'react-native.config.js', 'fingerprint.config.js', '.gitignore'];
const MobileDirectories = ['app', 'src', 'assets', 'plugins', 'scripts', 'modules'];
const SourceExtensions = new Set(['.js', '.jsx', '.mjs', '.cjs', '.ts', '.tsx', '.json', '.swift', '.podspec', '.kt', '.java', '.gradle', '.xml', '.h', '.m', '.mm', '.xcprivacy', '.png', '.jpg', '.jpeg', '.webp', '.gif', '.svg', '.ttf', '.otf', '.woff', '.woff2']);
const AssetExtensions = new Set(['.png', '.jpg', '.jpeg', '.webp', '.gif', '.svg', '.ttf', '.otf', '.woff', '.woff2']);
const ExcludedDirectories = new Set(['node_modules', 'Pods', 'build', 'dist', 'drafts', '__tests__', 'test', 'tests', 'secrets']);

/** Copy one ordinary file, rejecting symlinks rather than following them outside the source. */
function copyFile(source, destination) {
  if (!fs.lstatSync(source).isFile()) throw new Error('Build inputs must be ordinary files: ' + source);
  fs.mkdirSync(path.dirname(destination), { recursive: true });
  fs.copyFileSync(source, destination);
}

/** Copy approved runtime extensions, without hidden files, private credentials or build output. */
function copyTree(source, destination, extensions) {
  if (!fs.existsSync(source)) return;
  if (!fs.lstatSync(source).isDirectory()) throw new Error('Build input must be an ordinary directory: ' + source);
  for (const entry of fs.readdirSync(source, { withFileTypes: true })) {
    if (entry.name.startsWith('.') || ExcludedDirectories.has(entry.name) || (entry.isFile() && /(?:^|[.-])(?:local|private|secret|credentials?)(?:[.-]|$)/i.test(entry.name))) continue;
    const from = path.join(source, entry.name);
    const to = path.join(destination, entry.name);
    if (entry.isSymbolicLink()) throw new Error('Symlinks are not permitted in build inputs: ' + from);
    if (entry.isDirectory()) copyTree(from, to, extensions);
    else if (entry.isFile() && extensions.has(path.extname(entry.name).toLowerCase()) && !/^(?:google-services|GoogleService-Info)\./.test(entry.name)) copyFile(from, to);
  }
}

/**
 * Stage a build without changing the checkout, property, EAS profile, or app version.
 * @param {{ engineDir?: string, propertyDir?: string, outputDir?: string }} options
 * @returns {{ root: string, mobileDir: string, propertyDir: string, propertyPath: string }}
 */
function stageBuild({ engineDir = EngineRoot, propertyDir = resolvePropertyDir(), outputDir } = {}) {
  engineDir = path.resolve(engineDir);
  propertyDir = path.resolve(propertyDir);
  readProperty(propertyDir);
  let root = outputDir ? path.resolve(outputDir) : fs.mkdtempSync(path.join(os.tmpdir(), 'trelliskit-mobile-'));
  if (root === engineDir || root.startsWith(engineDir + path.sep) || root === propertyDir || root.startsWith(propertyDir + path.sep)) throw new Error('Build staging must be outside the engine and property sources.');
  if (fs.existsSync(root) && fs.readdirSync(root).length) throw new Error('Build staging directory must be empty: ' + root);
  fs.mkdirSync(root, { recursive: true });
  // EAS computes the remote project directory relative to this path. macOS resolves cwd
  // through /tmp -> /private/tmp, so the root must use the same canonical spelling.
  root = fs.realpathSync(root);
  // Stable names keep native config asset paths and EAS property selection unchanged when
  // the engine and property move between standalone checkouts or change directory casing.
  const engineName = 'trelliskit';
  const propertyBase = path.basename(propertyDir).toLowerCase();
  const propertyName = propertyBase === engineName ? 'property' : propertyBase;
  const engineDestination = path.join(root, engineName);
  const mobileDir = path.join(engineDestination, 'mobile');
  const propertyDestination = path.join(root, propertyName);
  for (const file of MobileFiles) {
    const source = path.join(engineDir, 'mobile', file);
    if (fs.existsSync(source)) copyFile(source, path.join(mobileDir, file));
  }
  for (const directory of MobileDirectories) copyTree(path.join(engineDir, 'mobile', directory), path.join(mobileDir, directory), SourceExtensions);
  copyFile(path.join(engineDir, 'shared', 'package.json'), path.join(engineDestination, 'shared', 'package.json'));
  for (const directory of ['src', 'scripts']) copyTree(path.join(engineDir, 'shared', directory), path.join(engineDestination, 'shared', directory), SourceExtensions);
  copyFile(path.join(propertyDir, 'property.json'), path.join(propertyDestination, 'property.json'));
  copyTree(path.join(propertyDir, 'mobile'), path.join(propertyDestination, 'mobile'), AssetExtensions);
  for (const file of ['certificate.pem', 'standby-certificate.pem', 'signing.json']) {
    const source = path.join(propertyDir, 'mobile', 'updates', file);
    if (!fs.existsSync(source)) continue;
    if (file.endsWith('.pem')) {
      const content = fs.readFileSync(source, 'utf8');
      if (!content.includes('-----BEGIN CERTIFICATE-----') || content.includes('PRIVATE KEY')) throw new Error('Only public certificates may be staged: ' + source);
    }
    copyFile(source, path.join(propertyDestination, 'mobile', 'updates', file));
  }
  fs.writeFileSync(path.join(root, '.easignore'), '**/node_modules/\n**/.expo/\n**/dist/\n**/.env*\n**/*.key\n**/*.p8\n**/*.p12\n**/*.jks\n');
  return { root, mobileDir, propertyDir: propertyDestination, propertyPath: '../' + propertyName };
}

if (require.main === module) {
  const args = process.argv.slice(2);
  function argument(name) { const index = args.indexOf(name); return index < 0 ? undefined : args[index + 1]; }
  try {
    if (args.some((value, index) => index % 2 === 0 && !['--property', '--out'].includes(value)) || args.length % 2) throw new Error('Usage: node scripts/stage-build.js [--property /path/to/property] [--out /empty/staging/path]');
    console.log(JSON.stringify(stageBuild({ propertyDir: argument('--property') || resolvePropertyDir(), outputDir: argument('--out') }), null, 2));
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}

module.exports = { stageBuild };
