/**
 * Build-time property loading for the mobile app (Node only: app.config.js, metro.config.js,
 * jest.config.js, and the asset generator use it; app code never does).
 *
 * The active property is selected only by directory path: TK_PROPERTY_DIR, absolute or relative
 * to the engine root (the folder that contains this mobile/ folder). When it is unset the neutral
 * sample property at <engine root>/samples/example is used. Nothing here assumes the repository
 * layout beyond "mobile/ sits in the engine root".
 */
const fs = require('fs');
const path = require('path');

/** The mobile project folder. */
const MobileRoot = path.resolve(__dirname, '..');

/** The engine root: the folder that contains mobile/. */
const EngineRoot = path.resolve(MobileRoot, '..');

/** Default property directory, relative to the engine root. */
const DefaultPropertyDir = 'samples/example';

/**
 * Resolve the active property directory.
 * @param {string} [value] Directory (defaults to process.env.TK_PROPERTY_DIR, then the sample).
 * @returns {string} Absolute path.
 */
function resolvePropertyDir(value) {
  const raw = (value !== undefined ? value : process.env.TK_PROPERTY_DIR || '').trim() || DefaultPropertyDir;
  return path.isAbsolute(raw) ? path.normalize(raw) : path.resolve(EngineRoot, raw);
}

/**
 * Read and minimally check property.json from a property directory.
 * @param {string} dir Absolute property directory.
 * @returns {object}
 */
function readProperty(dir) {
  const file = path.join(dir, 'property.json');
  if (!fs.existsSync(file)) {
    throw new Error('Property not found: ' + file + '. Set TK_PROPERTY_DIR to a property folder (absolute, or relative to ' + EngineRoot + ').');
  }
  const property = JSON.parse(fs.readFileSync(file, 'utf8'));
  const missing = ['id', 'name', 'mobile', 'membership', 'domains', 'brand'].filter((key) => !property[key]);
  if (missing.length) throw new Error('Property ' + file + ' is missing: ' + missing.join(', '));
  for (const key of ['bundleId', 'androidPackage', 'scheme']) {
    if (!property.mobile[key]) throw new Error('Property ' + file + ' is missing mobile.' + key);
  }
  if (!PropertyIdPattern.test(property.id)) {
    throw new Error('Property ' + file + ' has an invalid id: expected a generated property id of the form app_{k-sortable}_{random}');
  }
  if (property.slug !== undefined && property.slug !== null && !SlugPattern.test(property.slug)) {
    throw new Error('Property ' + file + ' has an invalid slug: lower case letters, digits, and hyphens (1 to 63 characters)');
  }
  return property;
}

const PropertyIdPattern = /^app_[a-z0-9]{6,16}_[A-Za-z0-9]{8,40}$/;
const SlugPattern = /^[a-z0-9][a-z0-9-]{0,62}$/;

/**
 * The property's cosmetic slug (human-readable, filesystem- and URL-safe names such as the Expo
 * slug and key folders). Mirrors @trelliskit/shared propertySlug and the backend's EffectiveSlug:
 * the configured `slug`, else one derived from shortName (or name).
 * The opaque `id` (app_{k-sortable}_{random}) still identifies the property in requests.
 * @param {object} property
 * @returns {string}
 */
function propertySlug(property) {
  const p = property || {};
  if (typeof p.slug === 'string' && p.slug.trim()) return p.slug.trim();
  const source = String((p.shortName && String(p.shortName).trim()) || p.name || '').toLowerCase();
  let slug = '';
  for (const c of source) {
    if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) slug += c;
    else if (slug.length > 0 && !slug.endsWith('-')) slug += '-';
  }
  slug = slug.replace(/^-+|-+$/g, '');
  if (slug.length > 63) slug = slug.substring(0, 63).replace(/-+$/, '');
  return slug || 'property';
}

/**
 * The public subset bundled into the app (everything except the website-only `web` block),
 * matching what GET /api/v1/property serves.
 * @param {object} property
 * @returns {object}
 */
function publicProperty(property) {
  const copy = { ...property };
  delete copy.web;
  return copy;
}

/**
 * Load the active property.
 * @param {string} [value] Directory override.
 * @returns {{ dir: string, property: object, mobileAssetsDir: string }}
 */
function loadActiveProperty(value) {
  const dir = resolvePropertyDir(value);
  return { dir, property: readProperty(dir), mobileAssetsDir: path.join(dir, 'mobile') };
}

module.exports = { MobileRoot, EngineRoot, DefaultPropertyDir, resolvePropertyDir, readProperty, publicProperty, loadActiveProperty, propertySlug };
