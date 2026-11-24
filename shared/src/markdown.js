/**
 * Markdown subset for feed posts and comments (docs/PHASE2_CONTRACT.md section 3.1).
 *
 * One parser for every client: it turns a body into a small AST that the website maps to React
 * DOM elements and the mobile app maps to React Native elements. Nothing here produces HTML, and
 * no client renders a body as HTML, so there is nothing to inject. The parser has no DOM or Node
 * dependency.
 *
 * Supported: paragraphs and line breaks; `**bold**`, `*italic*`, `_italic_`, `~~strike~~`;
 * `` `inline code` ``; `- item`, `* item`, `1. item` lists with one nesting level; `> quote`;
 * `# Heading`, `## Heading`, and `### Heading` (posts only); site paths in links (`[Terms](/terms)`); fenced code blocks (```` ``` ```` or
 * `~~~`, optional language); horizontal rules (`---`, `***`, `___`); pipe tables with a `|---|`
 * separator row and optional `:` alignment; `[text](https://...)` links and bare `http(s)://`
 * URLs; `@handle` mentions that match the item's `mentions`; and `\` escapes for
 * `* _ ~ [ ] ( ) # > - @ \ \` |`. Everything else (HTML, images, other link schemes) stays
 * literal text. Code is literal: no markup, links, or mentions inside it.
 *
 * AST nodes:
 * - block: `{ type: 'paragraph', children }`, `{ type: 'heading', level: 1|2|3, children }`,
 *   `{ type: 'list', ordered, start, items: [{ children, sublist: list|null }] }`,
 *   `{ type: 'blockquote', children: block[] }`, `{ type: 'code', lang, value }`, `{ type: 'rule' }`,
 *   `{ type: 'table', align: Array<'left'|'center'|'right'|null>, header: inline[][], rows: inline[][][] }`
 * - inline: `{ type: 'text', value }`, `{ type: 'strong'|'em'|'strike', children }`,
 *   `{ type: 'code', value }`, `{ type: 'link', href, children }`, `{ type: 'mention', handle, userId }`,
 *   `{ type: 'break' }`
 */

/** Characters that a backslash makes literal. */
export const MARKDOWN_ESCAPABLE = '*_~[]()#>-@\\`|';

const MAX_INLINE_DEPTH = 6;
const MAX_QUOTE_DEPTH = 3;
const HANDLE_CHAR = /[A-Za-z0-9_.]/;
const WORD_CHAR = /[A-Za-z0-9]/;
const LIST_ITEM = /^( *)([-*]|(\d{1,9})\.) +(.*)$/;
const HEADING = /^(#{1,3}) +(.*?)(?: +#+)?\s*$/;
const FENCE = /^ {0,3}(`{3,}|~{3,})\s*([A-Za-z0-9_+.#-]{0,30})\s*$/;
const RULE = /^ {0,3}([-*_])(?: *\1){2,} *$/;
const TABLE_SEPARATOR = /^ *\|? *:?-+:? *(?:\| *:?-+:? *)*\|? *$/;
const MAX_TABLE_COLUMNS = 12;
const MAX_TABLE_ROWS = 200;
const QUOTE = /^ {0,3}> ?(.*)$/;
const URL_START = /^https?:\/\//i;
const URL_UNSAFE = /[\s<>"'`\u0000-\u001f\u007f]/;

/**
 * Whether a link target is allowed: only `http` and `https`, with a host, and none of the
 * characters that cannot appear in a URL typed by a member.
 * @param {string} href
 * @returns {boolean}
 */
export function isSafeHref(href) {
  if (typeof href !== 'string' || !URL_START.test(href) || URL_UNSAFE.test(href)) return false;
  const rest = href.replace(URL_START, '');
  return rest.length > 0 && !rest.startsWith('/');
}

const INTERNAL_HREF = /^\/(?!\/)[A-Za-z0-9\-._~/%?#=&+:@]*$/;

/**
 * Whether a link target is a path on the property's own site (for example `/terms` or
 * `/app/forums/general`): it starts with one slash (never `//`, which would be another host) and
 * holds only URL path, query, and fragment characters. Clients open it inside the website or app.
 * @param {string} href
 * @returns {boolean}
 */
export function isInternalHref(href) {
  return typeof href === 'string' && INTERNAL_HREF.test(href);
}

function normalizeSource(body) {
  return String(body === null || body === undefined ? '' : body).replace(/\r\n?/g, '\n');
}

function mentionIndex(mentions) {
  const map = new Map();
  for (const m of Array.isArray(mentions) ? mentions : []) {
    if (m && typeof m.handle === 'string') map.set(m.handle.toLowerCase(), m);
  }
  return map;
}

// ---- Inline parsing ----

function pushText(nodes, value) {
  if (!value) return;
  const last = nodes[nodes.length - 1];
  if (last && last.type === 'text') last.value += value;
  else nodes.push({ type: 'text', value });
}

function isEscape(text, i) {
  return text[i] === '\\' && i + 1 < text.length && MARKDOWN_ESCAPABLE.includes(text[i + 1]);
}

/** Find the closing delimiter for an emphasis run, skipping escapes. Returns -1 when none. */
function findCloser(text, from, delim) {
  for (let i = from; i < text.length; i += 1) {
    if (isEscape(text, i)) {
      i += 1;
      continue;
    }
    if (text.startsWith(delim, i) && i > from) {
      const before = text[i - 1];
      if (/\s/.test(before)) continue;
      if (delim === '_' && i + 1 < text.length && WORD_CHAR.test(text[i + 1])) continue;
      if (delim === '*' && text[i + 1] === '*') {
        // `**` inside a `*...*` span closes a bold run, not the italic one.
        i += 1;
        continue;
      }
      return i;
    }
  }
  return -1;
}

/** Parse `[text](url)` at `i`. Returns `{ end, label, href }` or null. */
function matchLink(text, i) {
  let j = i + 1;
  while (j < text.length) {
    if (isEscape(text, j)) {
      j += 2;
      continue;
    }
    if (text[j] === ']' || text[j] === '[' || text[j] === '\n') break;
    j += 1;
  }
  if (text[j] !== ']' || j === i + 1 || text[j + 1] !== '(') return null;
  const close = text.indexOf(')', j + 2);
  if (close < 0) return null;
  const href = text.slice(j + 2, close);
  return { end: close + 1, label: text.slice(i + 1, j), href };
}

/** Length of a bare URL starting at `i`, with trailing punctuation removed. */
function bareUrlLength(text, i) {
  let j = i;
  while (j < text.length && !URL_UNSAFE.test(text[j])) j += 1;
  let url = text.slice(i, j);
  for (;;) {
    const last = url[url.length - 1];
    if (!last) break;
    if ('.,;:!?*_~'.includes(last)) {
      url = url.slice(0, -1);
      continue;
    }
    if (last === ')') {
      const opens = (url.match(/\(/g) || []).length;
      const closes = (url.match(/\)/g) || []).length;
      if (closes > opens) {
        url = url.slice(0, -1);
        continue;
      }
    }
    if (last === ']' && !url.includes('[')) {
      url = url.slice(0, -1);
      continue;
    }
    break;
  }
  return isSafeHref(url) ? url.length : 0;
}

function parseInlineText(text, ctx, depth) {
  const nodes = [];
  const noCloser = {};
  let i = 0;
  while (i < text.length) {
    const ch = text[i];

    if (isEscape(text, i)) {
      pushText(nodes, text[i + 1]);
      i += 2;
      continue;
    }

    if (ch === '\n') {
      nodes.push({ type: 'break' });
      i += 1;
      continue;
    }

    // Inline code: a run of backticks closed by the same run; the content is literal.
    if (ch === '`') {
      let run = 1;
      while (text[i + run] === '`') run += 1;
      const fence = '`'.repeat(run);
      const close = text.indexOf(fence, i + run);
      if (close > i + run && text[close + run] !== '`' && !text.slice(i + run, close).includes('\n\n')) {
        let value = text.slice(i + run, close).replace(/\n/g, ' ');
        if (value.length > 1 && value.startsWith(' ') && value.endsWith(' ') && value.trim()) value = value.slice(1, -1);
        nodes.push({ type: 'code', value });
        i = close + run;
        continue;
      }
      pushText(nodes, fence);
      i += run;
      continue;
    }

    // Emphasis: ** (bold), ~~ (strike), * and _ (italic).
    let delim = null;
    if (text.startsWith('**', i)) delim = '**';
    else if (text.startsWith('~~', i)) delim = '~~';
    else if (ch === '*') delim = '*';
    else if (ch === '_' && (i === 0 || !WORD_CHAR.test(text[i - 1]))) delim = '_';
    if (delim && depth < MAX_INLINE_DEPTH && i + delim.length < text.length && !/\s/.test(text[i + delim.length])) {
      const start = i + delim.length;
      const close = noCloser[delim] !== undefined && noCloser[delim] <= start ? -1 : findCloser(text, start, delim);
      if (close < 0) {
        if (noCloser[delim] === undefined || start < noCloser[delim]) noCloser[delim] = start;
      } else {
        const type = delim === '**' ? 'strong' : delim === '~~' ? 'strike' : 'em';
        nodes.push({ type, children: parseInlineText(text.slice(start, close), ctx, depth + 1) });
        i = close + delim.length;
        continue;
      }
    }

    // Links: [text](https://...). Other schemes stay literal text.
    if (ch === '[' && !ctx.inLink) {
      const link = matchLink(text, i);
      if (link) {
        if (isSafeHref(link.href) || isInternalHref(link.href)) {
          nodes.push({ type: 'link', href: link.href, children: parseInlineText(link.label, { ...ctx, inLink: true }, depth + 1) });
        } else {
          pushText(nodes, text.slice(i, link.end));
        }
        i = link.end;
        continue;
      }
    }

    // Bare URLs.
    if ((ch === 'h' || ch === 'H') && !ctx.inLink && (i === 0 || !WORD_CHAR.test(text[i - 1])) && URL_START.test(text.slice(i, i + 8))) {
      const length = bareUrlLength(text, i);
      if (length) {
        const href = text.slice(i, i + length);
        nodes.push({ type: 'link', href, children: [{ type: 'text', value: href }] });
        i += length;
        continue;
      }
    }

    // Mentions: @handle not preceded by a handle character.
    if (ch === '@' && (i === 0 || !HANDLE_CHAR.test(text[i - 1]))) {
      let j = i + 1;
      while (j < text.length && HANDLE_CHAR.test(text[j])) j += 1;
      let handle = text.slice(i + 1, j);
      while (handle.endsWith('.')) handle = handle.slice(0, -1);
      const entry = handle.length >= 3 && handle.length <= 30 && !ctx.inLink ? ctx.mentions.get(handle.toLowerCase()) : null;
      if (entry) {
        nodes.push({ type: 'mention', handle, userId: entry.userId || null });
        i += 1 + handle.length;
        continue;
      }
      pushText(nodes, '@' + handle);
      i += 1 + handle.length;
      continue;
    }

    pushText(nodes, ch);
    i += 1;
  }
  return nodes;
}

/**
 * Parse inline Markdown (one paragraph's text) to inline nodes.
 * @param {string} text
 * @param {{ mentions?: Array<{ handle: string, userId?: string }> }} [options]
 * @returns {Array<object>}
 */
export function parseInlineMarkdown(text, { mentions = [] } = {}) {
  return parseInlineText(normalizeSource(text), { mentions: mentionIndex(mentions), inLink: false }, 0);
}

// ---- Block parsing ----

function parseBlocks(lines, ctx, quoteDepth) {
  const blocks = [];
  let paragraph = [];
  let list = null;

  const flushParagraph = () => {
    if (paragraph.length) blocks.push({ type: 'paragraph', children: parseInlineText(paragraph.join('\n'), ctx, 0) });
    paragraph = [];
  };
  const flushList = () => {
    if (list) blocks.push(list);
    list = null;
  };

  let i = 0;
  while (i < lines.length) {
    const line = lines[i];

    if (!line.trim()) {
      flushParagraph();
      flushList();
      i += 1;
      continue;
    }

    const fence = FENCE.exec(line);
    if (fence) {
      flushParagraph();
      flushList();
      const marker = fence[1];
      const body = [];
      i += 1;
      while (i < lines.length) {
        const closing = /^ {0,3}(`{3,}|~{3,})\s*$/.exec(lines[i]);
        if (closing && closing[1][0] === marker[0] && closing[1].length >= marker.length) {
          i += 1;
          break;
        }
        body.push(lines[i]);
        i += 1;
      }
      blocks.push({ type: 'code', lang: fence[2] || null, value: body.join('\n') });
      continue;
    }

    if (RULE.test(line)) {
      flushParagraph();
      flushList();
      blocks.push({ type: 'rule' });
      i += 1;
      continue;
    }

    const table = line.includes('|') && i + 1 < lines.length ? parseTable(lines, i, ctx) : null;
    if (table) {
      flushParagraph();
      flushList();
      blocks.push(table.block);
      i = table.end;
      continue;
    }

    const quote = quoteDepth < MAX_QUOTE_DEPTH ? QUOTE.exec(line) : null;
    if (quote) {
      flushParagraph();
      flushList();
      const inner = [];
      while (i < lines.length) {
        const m = QUOTE.exec(lines[i]);
        if (!m) break;
        inner.push(m[1]);
        i += 1;
      }
      blocks.push({ type: 'blockquote', children: parseBlocks(inner, ctx, quoteDepth + 1) });
      continue;
    }

    const heading = ctx.allowHeadings ? HEADING.exec(line) : null;
    if (heading && heading[2]) {
      flushParagraph();
      flushList();
      blocks.push({ type: 'heading', level: heading[1].length, children: parseInlineText(heading[2], ctx, 0) });
      i += 1;
      continue;
    }

    const item = LIST_ITEM.exec(line);
    if (item && item[4].trim()) {
      flushParagraph();
      const indent = item[1].length;
      const ordered = Boolean(item[3]);
      const node = { children: parseInlineText(item[4], ctx, 0), sublist: null, raw: [item[4]] };
      const lastItem = list && list.items[list.items.length - 1];
      if (indent >= 2 && lastItem) {
        // One nesting level: deeper items join the previous item's sublist.
        if (!lastItem.sublist) lastItem.sublist = { type: 'list', ordered, start: ordered ? parseInt(item[3], 10) : 1, items: [] };
        lastItem.sublist.items.push(node);
      } else {
        if (!list || list.ordered !== ordered) {
          flushList();
          list = { type: 'list', ordered, start: ordered ? parseInt(item[3], 10) : 1, items: [] };
        }
        list.items.push(node);
      }
      i += 1;
      continue;
    }

    if (list) {
      // Lazy continuation of the last list item.
      const lastItem = list.items[list.items.length - 1];
      const target = lastItem.sublist ? lastItem.sublist.items[lastItem.sublist.items.length - 1] : lastItem;
      target.raw.push(line.trim());
      target.children = parseInlineText(target.raw.join('\n'), ctx, 0);
      i += 1;
      continue;
    }

    paragraph.push(line);
    i += 1;
  }
  flushParagraph();
  flushList();
  return blocks.map(cleanBlock);
}

/** Split a table row on unescaped pipes outside code spans, dropping the outer pipes. */
function splitRow(line) {
  let text = line.trim();
  if (text.startsWith('|')) text = text.slice(1);
  if (text.endsWith('|') && !text.endsWith('\\|')) text = text.slice(0, -1);
  const cells = [];
  let cell = '';
  let inCode = false;
  for (let i = 0; i < text.length; i += 1) {
    const c = text[i];
    if (c === '\\' && text[i + 1] === '|') {
      cell += '\\|';
      i += 1;
      continue;
    }
    if (c === '`') inCode = !inCode;
    if (c === '|' && !inCode) {
      cells.push(cell.trim());
      cell = '';
      continue;
    }
    cell += c;
  }
  cells.push(cell.trim());
  return cells;
}

/** A pipe table at `start` (header row plus a separator row), or null. */
function parseTable(lines, start, ctx) {
  const separator = lines[start + 1];
  if (!TABLE_SEPARATOR.test(separator) || !separator.includes('-') || !(separator.includes('|') || lines[start].trim().startsWith('|'))) return null;
  const header = splitRow(lines[start]);
  const marks = splitRow(separator);
  if (header.length !== marks.length || header.length < 1 || header.length > MAX_TABLE_COLUMNS) return null;
  const align = marks.map((m) => {
    const left = m.startsWith(':');
    const right = m.endsWith(':');
    return left && right ? 'center' : right ? 'right' : left ? 'left' : null;
  });
  const cell = (value) => parseInlineText(value || '', ctx, 0);
  const rows = [];
  let i = start + 2;
  while (i < lines.length && lines[i].trim() && lines[i].includes('|') && rows.length < MAX_TABLE_ROWS) {
    const cells = splitRow(lines[i]);
    rows.push(header.map((_, c) => cell(cells[c])));
    i += 1;
  }
  return { block: { type: 'table', align, header: header.map(cell), rows }, end: i };
}

function cleanItem(item) {
  return { children: item.children, sublist: item.sublist ? cleanBlock(item.sublist) : null };
}

function cleanBlock(block) {
  if (block.type !== 'list') return block;
  return { type: 'list', ordered: block.ordered, start: block.start, items: block.items.map(cleanItem) };
}

/**
 * Parse a post or comment body into an AST.
 * @param {string} body Markdown source as stored by the server.
 * @param {{ mentions?: Array<{ handle: string, userId?: string }>, allowHeadings?: boolean }} [options]
 *   `mentions` from the payload (only listed handles become mention nodes); `allowHeadings` is
 *   true for posts and false for comments (heading lines then render as plain text).
 * @returns {{ type: 'root', children: Array<object> }}
 */
export function parseMarkdown(body, { mentions = [], allowHeadings = true } = {}) {
  const lines = normalizeSource(body).split('\n').map((line) => line.replace(/^\t+/, (tabs) => '  '.repeat(tabs.length)));
  const ctx = { mentions: mentionIndex(mentions), allowHeadings, inLink: false };
  return { type: 'root', children: parseBlocks(lines, ctx, 0) };
}

// ---- Plain text ----

function inlineText(nodes) {
  let out = '';
  for (const node of nodes || []) {
    if (node.type === 'text' || node.type === 'code') out += node.value;
    else if (node.type === 'break') out += ' ';
    else if (node.type === 'mention') out += '@' + node.handle;
    else if (node.children) out += inlineText(node.children);
  }
  return out;
}

function blockText(blocks) {
  const parts = [];
  for (const block of blocks || []) {
    if (block.type === 'list') {
      for (const item of block.items) {
        parts.push(inlineText(item.children));
        if (item.sublist) parts.push(blockText([item.sublist]));
      }
    } else if (block.type === 'blockquote') {
      parts.push(blockText(block.children));
    } else if (block.type === 'code') {
      parts.push(block.value);
    } else if (block.type === 'rule') {
      continue;
    } else if (block.type === 'table') {
      for (const row of [block.header, ...block.rows]) parts.push(row.map(inlineText).join(' '));
    } else {
      parts.push(inlineText(block.children));
    }
  }
  return parts.join(' ');
}

/**
 * Plain text of a body or AST: markup removed, mentions kept as `@handle`, whitespace collapsed.
 * Every `@word` is kept as written (mentions resolve on the server).
 * @param {string|{ type: 'root', children: Array<object> }} source
 * @param {{ allowHeadings?: boolean }} [options]
 * @returns {string}
 */
export function toPlainText(source, { allowHeadings = true } = {}) {
  const ast = typeof source === 'string' || source === null || source === undefined ? parseMarkdown(source, { allowHeadings }) : source;
  return blockText(ast.children).replace(/\s+/g, ' ').trim();
}

/**
 * Excerpt like the server's (section 3.2): plain text cut at a word boundary to `max`
 * characters with `...` when truncated.
 * @param {string} body
 * @param {number} [max]
 * @returns {string}
 */
export function markdownExcerpt(body, max = 200) {
  const text = toPlainText(body);
  const chars = Array.from(text);
  if (chars.length <= max) return text;
  let cut = chars.slice(0, max).join('');
  const space = cut.lastIndexOf(' ');
  if (space > max * 0.5) cut = cut.slice(0, space);
  return cut.replace(/[\s.,;:!?]+$/, '') + '...';
}

/**
 * The first `http` or `https` URL in a body (the one the server builds a preview for), or null.
 * @param {string} body
 * @returns {string|null}
 */
export function firstLinkUrl(body) {
  const ast = parseMarkdown(body, { allowHeadings: true });
  let found = null;
  const visit = (nodes) => {
    for (const node of nodes || []) {
      if (found) return;
      if (node.type === 'link') found = node.href;
      else if (node.children) visit(node.children);
      if (node.items) for (const item of node.items) { visit(item.children); if (item.sublist) visit([item.sublist]); }
    }
  };
  visit(ast.children);
  return found;
}

/**
 * Count Unicode scalar values (the unit the server uses for length limits).
 * @param {string} text
 * @returns {number}
 */
export function scalarLength(text) {
  return Array.from(String(text || '')).length;
}
