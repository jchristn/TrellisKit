/**
 * Property content files (web/content/*.md): simple front matter plus Markdown with container
 * directives. Plain JavaScript with no React or DOM use, so the build scripts and the browser
 * share it.
 *
 * Front matter is a block of `key: value` lines between `---` fences at the top of the file.
 * Dotted keys nest (`book.name: Example` becomes `{ book: { name: 'Example' } }`). Values are
 * strings; `{token}` placeholders such as `{name}` and `{price}` are filled from the property when
 * the page renders.
 *
 * The body is Markdown (see views/marketing/markdown.jsx) with container directives:
 *
 *   ::: kind key=value key2="quoted value"
 *   ...Markdown or nested directives with more colons (:::: outer, ::: inner)...
 *   :::
 *
 * A directive closes on a line of exactly as many colons as opened it.
 */

import { AccessModel, accessOf, purchasesEnabled } from '@trelliskit/shared';

const FENCE = /^(:{3,})\s*([A-Za-z][\w-]*)?\s*(.*)$/;
const ATTR = /([A-Za-z][\w-]*)(?:=(?:"([^"]*)"|'([^']*)'|(\S+)))?/g;

/**
 * Parse `key=value key2="a b" flag` attributes.
 * @param {string} text
 * @returns {Record<string, string>}
 */
export function parseAttributes(text) {
  const attrs = {};
  for (const m of String(text || '').matchAll(ATTR)) {
    const value = m[2] !== undefined ? m[2] : m[3] !== undefined ? m[3] : m[4];
    attrs[m[1]] = value === undefined ? 'true' : value;
  }
  return attrs;
}

function setPath(target, key, value) {
  const parts = key.split('.');
  let node = target;
  for (const part of parts.slice(0, -1)) {
    if (typeof node[part] !== 'object' || node[part] === null) node[part] = {};
    node = node[part];
  }
  node[parts[parts.length - 1]] = value;
}

/**
 * Split front matter from a Markdown document.
 * @param {string} source
 * @returns {{ meta: object, body: string }}
 */
export function parseFrontMatter(source) {
  const text = String(source || '').replace(/\r\n?/g, '\n');
  const match = /^---\n([\s\S]*?)\n---\n?/.exec(text);
  if (!match) return { meta: {}, body: text };
  const meta = {};
  for (const line of match[1].split('\n')) {
    if (!line.trim() || line.trim().startsWith('#')) continue;
    const idx = line.indexOf(':');
    if (idx <= 0) continue;
    const key = line.slice(0, idx).trim();
    let value = line.slice(idx + 1).trim();
    if (/^"(.*)"$/.test(value) || /^'(.*)'$/.test(value)) value = value.slice(1, -1);
    setPath(meta, key, value);
  }
  return { meta, body: text.slice(match[0].length) };
}

/**
 * Split a body into Markdown runs and directive containers.
 * @param {string} body
 * @returns {Array<{ type: 'markdown', text: string } | { type: 'directive', kind: string, attrs: object, children: Array }>}
 */
export function parseDirectives(body) {
  const lines = String(body || '').replace(/\r\n?/g, '\n').split('\n');
  const nodes = [];
  let buffer = [];
  const flush = () => {
    if (buffer.join('\n').trim()) nodes.push({ type: 'markdown', text: buffer.join('\n') });
    buffer = [];
  };
  let i = 0;
  while (i < lines.length) {
    const open = FENCE.exec(lines[i].trim());
    if (open && open[2]) {
      const colons = open[1];
      const inner = [];
      let depth = 0;
      i += 1;
      while (i < lines.length) {
        const t = lines[i].trim();
        const m = FENCE.exec(t);
        if (m && m[1] === colons && m[2]) depth += 1;
        else if (t === colons) {
          if (depth === 0) break;
          depth -= 1;
        }
        inner.push(lines[i]);
        i += 1;
      }
      flush();
      nodes.push({ type: 'directive', kind: open[2], attrs: parseAttributes(open[3]), children: parseDirectives(inner.join('\n')) });
      i += 1;
      continue;
    }
    buffer.push(lines[i]);
    i += 1;
  }
  flush();
  return nodes;
}

/** Route path for a content file: `home.md` is `/`, `about.md` is `/about`, or front matter `path`. */
export function contentPath(file, meta = {}) {
  if (meta.path) return meta.path;
  const slug = file.replace(/\.md$/, '');
  return slug === 'home' || slug === 'index' ? '/' : '/' + slug;
}

/**
 * Parse every content file of a site into pages sorted by `navOrder` then path.
 * @param {{ content?: Record<string, string> }} site
 * @returns {Array<{ file: string, slug: string, path: string, meta: object, body: string }>}
 */
export function contentPages(site) {
  const content = (site && site.content) || {};
  return Object.keys(content).map((file) => {
    const { meta, body } = parseFrontMatter(content[file]);
    return { file, slug: file.replace(/\.md$/, ''), path: contentPath(file, meta), meta, body };
  }).sort((a, b) => (Number(a.meta.navOrder) || 99) - (Number(b.meta.navOrder) || 99) || a.path.localeCompare(b.path));
}

/** Split a comma separated front matter value into trimmed, non-empty items. */
export function listValue(value) {
  if (Array.isArray(value)) return value;
  return String(value || '').split(',').map((s) => s.trim()).filter(Boolean);
}

// ---- Access-dependent copy ----

/** Tokens that only make sense while the property sells memberships. */
const PURCHASE_TOKEN = /\{(price|priceAmount|interval|trialDays|productName)\}/;
const TRAILING_ATTRS = /\s*\{([^{}]+)\}\s*$/;
const WHEN_TOKEN = /^when=(?:"([^"]*)"|'([^']*)'|(\S+))$/;

/**
 * Evaluate a content condition against the live property access policy. A condition is one or
 * more comma separated names, each optionally negated with `!`, that must all hold:
 * `purchases` (memberships are sold), `signupCodes`, `anonymousBrowsing`, `membersOnly` (the
 * `FullyGated` model), or an access model name (`Open`, `PartiallyGated`, `FullyGated`).
 * Unknown names are false.
 * @param {string} expression
 * @param {object} property
 * @returns {boolean}
 */
export function contentCondition(expression, property) {
  const access = accessOf(property);
  const facts = {
    purchases: access.purchasesEnabled,
    signupcodes: access.signupCodesEnabled,
    anonymousbrowsing: access.anonymousBrowsing,
    membersonly: access.model === AccessModel.FullyGated,
    open: access.model === AccessModel.Open,
    partiallygated: access.model === AccessModel.PartiallyGated,
    fullygated: access.model === AccessModel.FullyGated
  };
  const terms = String(expression || '').split(',').map((s) => s.trim()).filter(Boolean);
  if (!terms.length) return true;
  return terms.every((term) => {
    const negated = term.startsWith('!');
    const name = (negated ? term.slice(1) : term).trim().toLowerCase();
    const value = Object.prototype.hasOwnProperty.call(facts, name) ? facts[name] : false;
    return negated ? !value : value;
  });
}

/** Split a line's trailing `{... when=cond ...}` block: `{ line, when }` with `when` removed. */
function splitWhen(line) {
  const m = TRAILING_ATTRS.exec(line);
  if (!m) return { line, when: null };
  const tokens = m[1].trim().match(/(?:[^\s"']+|"[^"]*"|'[^']*')+/g) || [];
  let when = null;
  const rest = tokens.filter((t) => {
    const w = WHEN_TOKEN.exec(t);
    if (!w) return true;
    when = w[1] !== undefined ? w[1] : w[2] !== undefined ? w[2] : w[3];
    return false;
  });
  if (when === null) return { line, when: null };
  const head = line.slice(0, m.index);
  return { line: rest.length ? head + ' {' + rest.join(' ') + '}' : head, when };
}

/**
 * Apply access-dependent copy to a content body before its tokens are filled, so marketing pages
 * follow the live access policy from `GET /api/v1/property` and not only the build-time copy:
 *
 * - A directive with `when=cond` (for example `::: div when=purchases`) is dropped with its
 *   contents when the condition is false; otherwise the attribute is ignored.
 * - A line ending in an attribute block with `when=cond` (for example
 *   `Members-only community {.eyebrow when=membersOnly}`) is dropped when the condition is false.
 * - When the property does not sell memberships, every other line that uses a purchase token
 *   (`{price}`, `{priceAmount}`, `{interval}`, `{trialDays}`, `{productName}`) is dropped, so no
 *   price or billing copy shows in the `Open` model.
 * @param {string} body
 * @param {object} property
 * @returns {string}
 */
export function applyContentConditions(body, property) {
  const sells = purchasesEnabled(property);
  const lines = String(body || '').split('\n');
  const out = [];
  let i = 0;
  while (i < lines.length) {
    const raw = lines[i];
    const open = FENCE.exec(raw.trim());
    if (open && open[2]) {
      const attrs = parseAttributes(open[3]);
      if (attrs.when !== undefined && !contentCondition(attrs.when, property)) {
        // Skip to the matching closing fence (same colon count, respecting nesting).
        const colons = open[1];
        let depth = 0;
        i += 1;
        while (i < lines.length) {
          const t = lines[i].trim();
          const m = FENCE.exec(t);
          if (m && m[1] === colons && m[2]) depth += 1;
          else if (t === colons) {
            if (depth === 0) break;
            depth -= 1;
          }
          i += 1;
        }
        i += 1;
        continue;
      }
      out.push(raw);
      i += 1;
      continue;
    }
    if (open) {
      out.push(raw);
      i += 1;
      continue;
    }
    const { line, when } = splitWhen(raw);
    if (when !== null) {
      if (contentCondition(when, property)) out.push(line);
    } else if (sells || !PURCHASE_TOKEN.test(raw)) {
      out.push(raw);
    }
    i += 1;
  }
  return out.join('\n');
}

/**
 * Remove sentences that use a purchase token from a one-line text (a meta description) when the
 * property does not sell memberships.
 * @param {string} text
 * @param {object} property
 * @returns {string}
 */
export function stripPurchaseSentences(text, property) {
  const value = String(text || '');
  if (purchasesEnabled(property) || !PURCHASE_TOKEN.test(value)) return value;
  return (value.match(/[^.!?]+[.!?]*/g) || []).filter((s) => !PURCHASE_TOKEN.test(s)).join('').trim();
}
