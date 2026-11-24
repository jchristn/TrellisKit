/**
 * Build-time property loading (Node only). The website build serves exactly one property, chosen
 * by directory path (docs/TRELLISKIT.md section 1, "Clean separation"):
 *
 *   TK_PROPERTY_DIR   absolute, or relative to the engine root (the parent of this www folder).
 *                     A relative path that does not name a property folder there is also tried
 *                     relative to the current directory and to www/, so
 *                     `TK_PROPERTY_DIR=../some-property npm run build` works from www/.
 *                     Default: samples/example (the neutral sample property).
 *
 * A property folder holds property.json, brand/ images, legal/*.md (or the Markdown files its
 * `documents` list names), and optionally
 * web/content/*.md (marketing pages), web/theme.css (extra CSS after the generated theme), and
 * web/og-image.png (a 1200 x 630 social image; generated from the logo when absent).
 */
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { documentsOf, loadProperty, propertySlug } from '@trelliskit/shared';
import { propertyPaletteCss } from '../src/property/theme.js';

/** The www folder. */
export const WWW_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

/** The engine root: the folder that contains www/, shared/, and samples/. */
export const ENGINE_ROOT = path.resolve(WWW_DIR, '..');

/** Property used when TK_PROPERTY_DIR is unset, relative to the engine root. */
export const DEFAULT_PROPERTY_DIR = 'samples/example';

/**
 * Resolve the active property folder.
 * @param {Record<string, string|undefined>} [env]
 * @returns {string} absolute path
 */
export function resolvePropertyDir(env = process.env) {
  const selected = env.TK_PROPERTY_DIR && env.TK_PROPERTY_DIR.trim() ? env.TK_PROPERTY_DIR.trim() : DEFAULT_PROPERTY_DIR;
  if (path.isAbsolute(selected)) return selected;
  const candidates = [ENGINE_ROOT, process.cwd(), WWW_DIR].map((base) => path.resolve(base, selected));
  return candidates.find((dir) => existsSync(path.join(dir, 'property.json'))) || candidates[0];
}

function readMarkdownFolder(dir) {
  const out = {};
  if (!existsSync(dir) || !statSync(dir).isDirectory()) return out;
  for (const file of readdirSync(dir).filter((f) => f.endsWith('.md')).sort()) {
    out[file] = readFileSync(path.join(dir, file), 'utf8');
  }
  return out;
}

/**
 * The property's `documents` with their build-time Markdown (`content`, read from each
 * document's `file` under the property folder), or null when property.json declares no list.
 * @throws {Error} when a document file is outside the property folder or missing
 */
export function readDocuments(dir, property) {
  if (!Array.isArray(property.documents)) return null;
  const root = path.resolve(dir);
  return documentsOf(property).map((doc) => {
    const file = path.resolve(root, String(doc.file || ''));
    if (!doc.file || !file.startsWith(root + path.sep)) throw new Error('TrellisKit: document ' + doc.key + ' has no file inside ' + dir);
    if (!existsSync(file)) throw new Error('TrellisKit: document ' + doc.key + ': ' + doc.file + ' not found in ' + dir);
    return { ...doc, content: readFileSync(file, 'utf8') };
  });
}

/** Image file extensions copied from a property's web/images/ folder. */
export const IMAGE_EXTENSIONS = ['.png', '.jpg', '.jpeg', '.webp', '.gif', '.svg'];

/**
 * Images in a property's web/images/ folder (top level only), served at /images/<name>.
 * @param {string} folder
 * @returns {Record<string, string>} file name to absolute path
 */
function listImages(folder) {
  if (!existsSync(folder) || !statSync(folder).isDirectory()) return {};
  const out = {};
  for (const name of readdirSync(folder).sort()) {
    const file = path.join(folder, name);
    if (!/^[\w.-]+$/.test(name) || !IMAGE_EXTENSIONS.includes(path.extname(name).toLowerCase()) || !statSync(file).isFile()) continue;
    out[name] = file;
  }
  return out;
}

function optionalFile(file) {
  return existsSync(file) ? file : null;
}

/**
 * Load a property folder into the site bundle the website is built from.
 * @param {string} dir absolute property folder
 * @param {{ repositoryUrl?: string }} [engine] engine-level values (not property content)
 * @returns {{ dir: string, site: object, files: object }}
 *   `site` is serializable and shipped to the browser; `files` holds absolute paths for assets.
 *   `site.documents` (present when property.json has `documents`) lists each document with its
 *   build-time Markdown in `content`.
 * @throws {Error} when property.json is missing or invalid (the message names the problem)
 */
export function loadSite(dir, engine = {}) {
  const jsonFile = path.join(dir, 'property.json');
  if (!existsSync(jsonFile)) throw new Error('TrellisKit: no property.json in ' + dir + ' (set TK_PROPERTY_DIR to a property folder)');
  let property;
  try {
    property = loadProperty(readFileSync(jsonFile, 'utf8'));
  } catch (err) {
    throw new Error('TrellisKit: ' + jsonFile + ': ' + err.message, { cause: err });
  }
  const web = property.web || {};
  const brand = property.brand || {};
  const brandFile = (rel) => (rel ? optionalFile(path.join(dir, rel)) : null);
  const files = {
    logo: brandFile(brand.logo),
    // Optional logo for dark backgrounds, by convention beside the logo: brand/logo.png and brand/logo-dark.png.
    logoDark: brandFile(brand.logo ? brand.logo.replace(/(\.[^./]+)$/, '-dark$1') : null),
    icon: brandFile(brand.icon) || brandFile(brand.logo),
    iconIco: brandFile(brand.iconIco),
    ogImage: optionalFile(path.join(dir, 'web', 'og-image.png')),
    images: listImages(path.join(dir, 'web', 'images')),
    themeOverrides: optionalFile(path.join(dir, web.theme || 'web/theme.css'))
  };
  if (!files.logo) throw new Error('TrellisKit: brand.logo not found in ' + dir);
  const documents = readDocuments(dir, property);
  const site = {
    property,
    content: readMarkdownFolder(path.join(dir, web.content || 'web/content')),
    // With a `documents` list the legal pages come from it (`documents`, with each file's
    // Markdown); without one, from the standard files in the legal/ folder.
    legal: documents ? {} : readMarkdownFolder(path.join(dir, web.legal || 'legal')),
    engine: { repositoryUrl: engine.repositoryUrl || '' }
  };
  if (documents) site.documents = documents;
  return { dir, site, files };
}

/** Load the active property for this build (TK_PROPERTY_DIR), plus engine values from the environment. */
export function loadActiveSite(env = process.env) {
  return loadSite(resolvePropertyDir(env), { repositoryUrl: env.TK_REPOSITORY_URL || '' });
}

/**
 * Theme CSS for a property: brand custom properties for light and dark from
 * `brand.colors` (src/property/theme.js; the live palette replaces them at runtime), followed by the property's optional web/theme.css overrides. It is loaded
 * after the engine's neutral defaults in src/index.css, so it wins.
 * @param {object} property
 * @param {string} [overrides] extra CSS appended verbatim
 */
export function buildThemeCss(property, overrides = '') {
  return '/* Generated from property.json (' + propertySlug(property) + ') by the TrellisKit build. Do not edit. */\n'
    + propertyPaletteCss(property)
    + (overrides ? '\n/* Property overrides (web/theme.css) */\n' + overrides.trim() + '\n' : '');
}

/** Read the theme CSS for a loaded site. */
export function themeCssFor(loaded) {
  const overrides = loaded.files.themeOverrides ? readFileSync(loaded.files.themeOverrides, 'utf8') : '';
  return buildThemeCss(loaded.site.property, overrides);
}

/** site.webmanifest for a property. */
export function buildManifest(property) {
  return JSON.stringify({
    name: property.name,
    short_name: property.shortName || property.name,
    description: property.description || undefined,
    icons: [
      { src: '/icon-192.png', sizes: '192x192', type: 'image/png' },
      { src: '/icon-512.png', sizes: '512x512', type: 'image/png' }
    ],
    theme_color: property.brand.colors.light.themeColor,
    background_color: '#ffffff',
    display: 'standalone',
    start_url: '/'
  }, null, 2) + '\n';
}
