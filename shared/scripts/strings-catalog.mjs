#!/usr/bin/env node
/**
 * Generate the engine string catalog (docs/LIVE_UPDATES_CONTRACT.md section 14.3) from the website's and the mobile
 * app's strings modules: every string leaf becomes `{ key, client, default, tokens, maxLength }` with keys
 * `web.<path>` (www/src/strings.js `strings`) and `mobile.<path>` (mobile/src/strings.js `stringTemplates`, plus
 * `placesTemplates` under `mobile.places`). Functions, arrays, and computed values are skipped. The modules are parsed,
 * never executed, so their runtime imports do not matter.
 *
 * Writes shared/src/strings/catalog.json and the backend's embedded copy
 * (backend/src/TrellisKit.Server/Assets/string-catalog.json). `--check` exits 1 when either file is stale.
 *
 * Usage: node shared/scripts/strings-catalog.mjs [--check]
 */

import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parse } from '@babel/parser';

const here = dirname(fileURLToPath(import.meta.url));
const engine = resolve(here, '..', '..');

/** Sources: file, top-level binding name, key prefix. */
export const SOURCES = [
  { file: 'www/src/components/charts/chartStrings.js', binding: 'chartKit', prefix: 'web.chartKit', client: 'web' },
  { file: 'www/src/platformMetricsStrings.js', binding: 'platformMetrics', prefix: 'web.platformMetrics', client: 'web' },
  { file: 'www/src/strings.js', binding: 'strings', prefix: 'web', client: 'web' },
  { file: 'mobile/src/strings.js', binding: 'stringTemplates', prefix: 'mobile', client: 'mobile' },
  { file: 'mobile/src/strings.js', binding: 'placesTemplates', prefix: 'mobile.places', client: 'mobile' },
  { file: 'www/src/views/catalog/catalogStrings.js', binding: 'exerciseCatalog', prefix: 'web.exerciseCatalog', client: 'web' },
  { file: 'www/src/views/admin/catalog/catalogImportStrings.js', binding: 'catalogImport', prefix: 'web.catalogImport', client: 'web' },
  { file: 'mobile/src/catalogStrings.js', binding: 'catalogTemplates', prefix: 'mobile.exerciseCatalog', client: 'mobile' },
  { file: 'www/src/views/ranks/ranksStrings.js', binding: 'ranks', prefix: 'web.ranks', client: 'web' },
  { file: 'mobile/src/ranksStrings.js', binding: 'ranksTemplates', prefix: 'mobile.ranks', client: 'mobile' }
];

const TOKEN = /\{([A-Za-z][A-Za-z0-9]*)\}/g;

function tokensOf(text) {
  const out = [];
  for (const match of text.matchAll(TOKEN)) if (!out.includes(match[1])) out.push(match[1]);
  return out;
}

function keyName(node) {
  if (!node) return null;
  if (node.type === 'Identifier') return node.name;
  if (node.type === 'StringLiteral') return node.value;
  if (node.type === 'NumericLiteral') return String(node.value);
  return null;
}

function stringValue(node) {
  if (!node) return null;
  if (node.type === 'StringLiteral') return node.value;
  if (node.type === 'TemplateLiteral' && node.expressions.length === 0) return node.quasis.map((q) => q.value.cooked).join('');
  return null;
}

function objectOf(node) {
  if (!node) return null;
  if (node.type === 'ObjectExpression') return node;
  if (node.type === 'CallExpression' && node.arguments.length > 0) return objectOf(node.arguments[0]);
  if (node.type === 'TSAsExpression' || node.type === 'TypeCastExpression') return objectOf(node.expression);
  return null;
}

function walk(objectNode, path, out) {
  for (const prop of objectNode.properties) {
    if (prop.type !== 'ObjectProperty' || prop.computed) continue;
    const name = keyName(prop.key);
    if (!name) continue;
    const childPath = path + '.' + name;
    const text = stringValue(prop.value);
    if (text !== null) {
      out.push({ key: childPath, value: text });
      continue;
    }
    const child = objectOf(prop.value);
    if (child) walk(child, childPath, out);
  }
}

function findBinding(ast, binding) {
  for (const statement of ast.program.body) {
    const declaration = statement.type === 'ExportNamedDeclaration' ? statement.declaration : statement;
    if (!declaration || declaration.type !== 'VariableDeclaration') continue;
    for (const d of declaration.declarations) {
      if (d.id && d.id.type === 'Identifier' && d.id.name === binding) return objectOf(d.init);
    }
  }
  return null;
}

/**
 * Catalog entries of one strings module source.
 * @param {string} source Module text.
 * @param {{ binding: string, prefix: string, client: string }} spec
 */
export function entriesOf(source, spec) {
  const ast = parse(source, { sourceType: 'module', plugins: ['jsx'] });
  const root = findBinding(ast, spec.binding);
  if (!root) return [];
  const leaves = [];
  walk(root, spec.prefix, leaves);
  return leaves.map((leaf) => ({
    key: leaf.key,
    client: spec.client,
    default: leaf.value,
    tokens: tokensOf(leaf.value),
    maxLength: Math.min(2000, Math.max(200, leaf.value.length * 2))
  }));
}

/** Build the catalog document from the engine sources. */
export function buildCatalog(root = engine) {
  const entries = [];
  const seen = new Set();
  for (const spec of SOURCES) {
    const file = join(root, spec.file);
    if (!existsSync(file)) continue;
    for (const entry of entriesOf(readFileSync(file, 'utf8'), spec)) {
      if (seen.has(entry.key)) continue;
      seen.add(entry.key);
      entries.push(entry);
    }
  }
  entries.sort((a, b) => (a.key < b.key ? -1 : a.key > b.key ? 1 : 0));
  const sha256 = createHash('sha256').update(JSON.stringify(entries)).digest('hex');
  return { version: 1, sha256, entries };
}

export const OUTPUTS = ['shared/src/strings/catalog.json', 'backend/src/TrellisKit.Server/Assets/string-catalog.json'];

function main() {
  const check = process.argv.includes('--check');
  const text = JSON.stringify(buildCatalog(), null, 1) + '\n';
  let stale = false;
  for (const output of OUTPUTS) {
    const file = join(engine, output);
    const current = existsSync(file) ? readFileSync(file, 'utf8') : '';
    if (current === text) continue;
    stale = true;
    if (!check) {
      mkdirSync(dirname(file), { recursive: true });
      writeFileSync(file, text);
    }
  }
  const count = JSON.parse(text).entries.length;
  if (check && stale) {
    process.stderr.write('String catalog is stale; run npm run strings:catalog --workspace shared\n');
    process.exit(1);
  }
  process.stdout.write((check ? 'String catalog is current' : 'Wrote the string catalog') + ' (' + count + ' keys)\n');
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
