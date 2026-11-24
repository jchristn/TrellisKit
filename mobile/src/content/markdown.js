/**
 * Markdown subset parser (docs/PHASE2_CONTRACT.md section 3.1). Turns a post or comment body into
 * a small AST that the app maps to native Text and View elements. Nothing is ever rendered as
 * HTML, so there is nothing to inject: unsupported syntax (HTML tags, images, tables, fences,
 * inline code outside the assistant mode) stays as the literal text the member typed.
 *
 * Posts, comments, and assistant replies now render with the shared parser (@trelliskit/shared
 * markdown.js, see components/feed/MarkdownBody.jsx). This copy remains for its URL helpers
 * (isSafeUrl, firstUrl) and older callers; prefer the shared parser for anything new.
 *
 * Block nodes: paragraph { children }, heading { level, children }, list { ordered, start, items:
 * [{ children, list? }] }, quote { children: Block[] }.
 * Inline nodes: text { value }, strong | em | strike { children }, link { href, children },
 * mention { handle, userId, displayName }, break, and (only with the `code` option, the assistant
 * mode of docs/AI_CONTRACT.md section 9.2) code { value }: a single-backtick span shown as
 * monospaced text, with no markup, links, or mentions inside.
 */

const ESCAPABLE = new Set(['*', '_', '~', '[', ']', '(', ')', '#', '>', '-', '@', '\\']);
const HANDLE_CHAR = /[A-Za-z0-9_.]/;
const WORD_CHAR = /[A-Za-z0-9]/;
const URL_AT = /^https?:\/\/[^\s<>]+/i;
const LIST_LINE = /^( *)([-*]|\d{1,9}\.) +(.*)$/;
const HEADING_LINE = /^(#{2,3}) +(.+)$/;
const QUOTE_LINE = /^ {0,3}> ?(.*)$/;
const TRAILING_URL_PUNCTUATION = /[.,;:!?'"]+$/;

/** Whether a link target is allowed (http and https only). */
export function isSafeUrl(href) {
  return typeof href === 'string' && /^https?:\/\/[^\s]+$/i.test(href.trim());
}

function mentionMap(mentions) {
  const map = new Map();
  for (const m of Array.isArray(mentions) ? mentions : []) {
    if (m && typeof m.handle === 'string') map.set(m.handle.toLowerCase(), m);
  }
  return map;
}

function pushText(out, value) {
  if (!value) return;
  const last = out[out.length - 1];
  if (last && last.type === 'text') last.value += value;
  else out.push({ type: 'text', value });
}

/** Index of an unescaped closing delimiter at or after `from`, or -1. */
function findClosing(text, delimiter, from) {
  for (let i = from; i <= text.length - delimiter.length; i += 1) {
    if (text[i] === '\\') {
      i += 1;
      continue;
    }
    if (text.startsWith(delimiter, i)) {
      if (delimiter.length === 1 && text[i + 1] === delimiter) {
        // `**` inside an `*` span closes nothing; skip the pair.
        i += 1;
        continue;
      }
      return i;
    }
  }
  return -1;
}

/** Strip trailing punctuation from a bare URL, keeping a closing parenthesis only when balanced. */
function trimUrl(url) {
  let result = url.replace(TRAILING_URL_PUNCTUATION, '');
  while (result.endsWith(')')) {
    const open = (result.match(/\(/g) || []).length;
    const close = (result.match(/\)/g) || []).length;
    if (close <= open) break;
    result = result.slice(0, -1).replace(TRAILING_URL_PUNCTUATION, '');
  }
  return result;
}

/**
 * Parse inline markup.
 * @param {string} text
 * @param {{ mentions: Map<string, object>, code?: boolean }} ctx Mentions (lowercased handle to
 *   Mention) and whether inline code spans are recognized.
 * @param {number} [depth] Nesting guard.
 * @returns {object[]}
 */
function parseInline(text, ctx, depth = 0) {
  const mentions = ctx.mentions;
  const out = [];
  let i = 0;
  while (i < text.length) {
    const ch = text[i];
    const prev = i > 0 ? text[i - 1] : '';

    if (ch === '\\' && i + 1 < text.length && ESCAPABLE.has(text[i + 1])) {
      pushText(out, text[i + 1]);
      i += 2;
      continue;
    }

    if (ctx.code && ch === '`') {
      const end = text.indexOf('`', i + 1);
      const value = end > i + 1 ? text.slice(i + 1, end) : '';
      if (value && !value.includes('\n')) {
        out.push({ type: 'code', value });
        i = end + 1;
        continue;
      }
    }

    if (ch === '\n') {
      out.push({ type: 'break' });
      i += 1;
      continue;
    }

    if (depth < 6 && (text.startsWith('**', i) || text.startsWith('~~', i))) {
      const delimiter = text.slice(i, i + 2);
      const end = findClosing(text, delimiter, i + 2);
      if (end > i + 2 && !/\s/.test(text[i + 2])) {
        out.push({ type: delimiter === '**' ? 'strong' : 'strike', children: parseInline(text.slice(i + 2, end), ctx, depth + 1) });
        i = end + 2;
        continue;
      }
    }

    if (depth < 6 && (ch === '*' || ch === '_') && !(ch === '_' && WORD_CHAR.test(prev))) {
      const end = findClosing(text, ch, i + 1);
      const after = end >= 0 ? text[end + 1] || '' : '';
      if (end > i + 1 && !/\s/.test(text[i + 1]) && !/\s/.test(text[end - 1]) && !(ch === '_' && WORD_CHAR.test(after))) {
        out.push({ type: 'em', children: parseInline(text.slice(i + 1, end), ctx, depth + 1) });
        i = end + 1;
        continue;
      }
    }

    if (ch === '[' && depth < 6) {
      const close = findClosing(text, ']', i + 1);
      if (close > i + 1 && text[close + 1] === '(') {
        const end = text.indexOf(')', close + 2);
        const href = end > 0 ? text.slice(close + 2, end).trim() : '';
        if (end > 0 && isSafeUrl(href)) {
          out.push({ type: 'link', href, children: parseInline(text.slice(i + 1, close), ctx, depth + 1) });
          i = end + 1;
          continue;
        }
      }
    }

    if ((ch === 'h' || ch === 'H') && !WORD_CHAR.test(prev)) {
      const match = URL_AT.exec(text.slice(i));
      if (match) {
        const url = trimUrl(match[0]);
        if (isSafeUrl(url) && url.length > 8) {
          out.push({ type: 'link', href: url, children: [{ type: 'text', value: url }] });
          i += url.length;
          continue;
        }
      }
    }

    if (ch === '@' && !HANDLE_CHAR.test(prev)) {
      let j = i + 1;
      while (j < text.length && HANDLE_CHAR.test(text[j]) && j - i - 1 < 31) j += 1;
      let handle = text.slice(i + 1, j);
      while (handle.endsWith('.')) handle = handle.slice(0, -1);
      const mention = handle.length >= 3 && handle.length <= 30 ? mentions.get(handle.toLowerCase()) : null;
      if (mention) {
        out.push({ type: 'mention', handle, userId: mention.userId || null, displayName: mention.displayName || null });
        i += 1 + handle.length;
        continue;
      }
      pushText(out, '@' + handle);
      i += 1 + handle.length;
      continue;
    }

    pushText(out, ch);
    i += 1;
  }
  return out;
}

function parseBlocks(lines, options, depth = 0) {
  const blocks = [];
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];
    if (!line.trim()) {
      i += 1;
      continue;
    }

    const heading = options.headings ? HEADING_LINE.exec(line) : null;
    if (heading) {
      blocks.push({ type: 'heading', level: heading[1].length, children: parseInline(heading[2].trim(), options) });
      i += 1;
      continue;
    }

    if (depth < 3 && QUOTE_LINE.test(line)) {
      const inner = [];
      while (i < lines.length && QUOTE_LINE.test(lines[i])) {
        inner.push(QUOTE_LINE.exec(lines[i])[1]);
        i += 1;
      }
      blocks.push({ type: 'quote', children: parseBlocks(inner, options, depth + 1) });
      continue;
    }

    const first = LIST_LINE.exec(line);
    if (first && first[1].length < 2) {
      const ordered = /\d/.test(first[2]);
      const list = { type: 'list', ordered, start: ordered ? parseInt(first[2], 10) : 1, items: [] };
      while (i < lines.length && lines[i].trim()) {
        const m = LIST_LINE.exec(lines[i]);
        if (m && m[1].length < 2 && /\d/.test(m[2]) === ordered) {
          list.items.push({ text: m[3], nested: [] });
        } else if (m && m[1].length >= 2 && list.items.length) {
          list.items[list.items.length - 1].nested.push(m);
        } else if (m) {
          break;
        } else if (list.items.length) {
          const item = list.items[list.items.length - 1];
          if (item.nested.length) break;
          item.text += '\n' + lines[i].trim();
        }
        i += 1;
      }
      blocks.push({
        type: 'list',
        ordered,
        start: list.start,
        items: list.items.map((item) => {
          const node = { children: parseInline(item.text, options) };
          if (item.nested.length) {
            const nestedOrdered = /\d/.test(item.nested[0][2]);
            node.list = {
              type: 'list',
              ordered: nestedOrdered,
              start: nestedOrdered ? parseInt(item.nested[0][2], 10) : 1,
              items: item.nested.map((n) => ({ children: parseInline(n[3], options) }))
            };
          }
          return node;
        })
      });
      continue;
    }

    const paragraph = [];
    while (i < lines.length && lines[i].trim()) {
      if (paragraph.length && ((options.headings && HEADING_LINE.test(lines[i])) || QUOTE_LINE.test(lines[i]) || (LIST_LINE.test(lines[i]) && LIST_LINE.exec(lines[i])[1].length < 2))) break;
      paragraph.push(lines[i]);
      i += 1;
    }
    blocks.push({ type: 'paragraph', children: parseInline(paragraph.join('\n'), options) });
  }
  return blocks;
}

/**
 * Parse a body into block nodes.
 * @param {string} body Markdown source (already normalized by the server).
 * @param {object} [options]
 * @param {object[]} [options.mentions] Mention objects from the payload; only these handles link.
 * @param {boolean} [options.headings] Allow `##` and `###` (posts true, comments false).
 * @param {boolean} [options.code] Recognize inline code spans (assistant replies only).
 * @returns {object[]}
 */
export function parseMarkdown(body, { mentions = [], headings = true, code = false } = {}) {
  const text = String(body || '').replace(/\r\n?/g, '\n');
  if (!text.trim()) return [];
  return parseBlocks(text.split('\n'), { mentions: mentionMap(mentions), headings, code: Boolean(code) });
}

function inlineText(nodes) {
  return nodes
    .map((n) => {
      if (n.type === 'text') return n.value;
      if (n.type === 'break') return ' ';
      if (n.type === 'mention') return '@' + n.handle;
      if (n.type === 'code') return n.value;
      return inlineText(n.children || []);
    })
    .join('');
}

function blockText(blocks) {
  return blocks
    .map((b) => {
      if (b.type === 'quote') return blockText(b.children);
      if (b.type === 'list') return b.items.map((item) => inlineText(item.children) + (item.list ? ' ' + blockText([item.list]) : '')).join(' ');
      return inlineText(b.children);
    })
    .join(' ');
}

/**
 * Plain text of a body: markup removed, mentions kept as @handle, whitespace collapsed. Used for
 * accessibility labels and composer previews.
 * @param {string} body
 * @param {object} [options] Same as parseMarkdown.
 */
export function toPlainText(body, options) {
  return blockText(parseMarkdown(body, options)).replace(/\s+/g, ' ').trim();
}

/**
 * The first http or https URL in a body (for the composer's link preview request).
 * @param {string} body
 * @returns {string|null}
 */
export function firstUrl(body) {
  const find = (nodes) => {
    for (const n of nodes) {
      if (n.type === 'link') return n.href;
      const inner = n.children ? find(n.children) : null;
      if (inner) return inner;
      if (n.items) {
        for (const item of n.items) {
          const hit = find(item.children) || (item.list ? find([item.list]) : null);
          if (hit) return hit;
        }
      }
    }
    return null;
  };
  return find(parseMarkdown(body));
}
