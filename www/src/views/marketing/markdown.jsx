/**
 * Tiny, dependency-free Markdown renderer for the property's legal and content pages. It renders to
 * React elements (never raw HTML), so the output is safe to server render and hydrate. Supported
 * syntax is the subset those documents use:
 *
 * - Blocks: ATX headings (#), paragraphs, unordered (- or *) and ordered (1.) lists with nested
 *   content indented under an item, blockquotes (>), horizontal rules (---), and pipe tables.
 * - Inline: **strong**, *emphasis*, `code`, [links](/path or https://...), and
 *   [PLACEHOLDER: text], which renders as visibly marked placeholder copy.
 */
import { Link } from 'react-router-dom';
import strings from '../../strings.js';

const HEADING = /^(#{1,6})\s+(.*?)\s*#*\s*$/;
const HR = /^\s{0,3}([-*_])(\s*\1){2,}\s*$/;
const BULLET = /^(\s*)([-*])\s+(.*)$/;
const ORDERED = /^(\s*)(\d+)\.\s+(.*)$/;
const TABLE_SEPARATOR = /^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$/;

/** URL fragment for a heading, for example "Under 18" becomes "under-18". */
export function slugify(text) {
  return String(text)
    .toLowerCase()
    .replace(/[^a-z0-9\s-]/g, '')
    .trim()
    .replace(/\s+/g, '-')
    .replace(/-+/g, '-');
}

export function plainText(text) {
  return text.replace(/\[PLACEHOLDER:\s*([^\]]*)\]/g, '$1').replace(/\[([^\]]+)\]\([^)]*\)/g, '$1').replace(/[*`]/g, '');
}

function splitRow(line) {
  let row = line.trim();
  if (row.startsWith('|')) row = row.slice(1);
  if (row.endsWith('|')) row = row.slice(0, -1);
  return row.split('|').map((cell) => cell.trim());
}

function isBlockStart(line, next) {
  return HEADING.test(line) || HR.test(line) || BULLET.test(line) || ORDERED.test(line)
    || /^\s*>/.test(line) || (line.trim().startsWith('|') && next !== undefined && TABLE_SEPARATOR.test(next));
}

function indentOf(line) {
  return line.match(/^\s*/)[0].length;
}

/**
 * Parse Markdown into a list of block nodes.
 * @param {string} source
 * @returns {Array<object>}
 */
export function parseMarkdown(source) {
  const lines = String(source || '').replace(/\r\n?/g, '\n').split('\n');
  const blocks = [];
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];
    if (!line.trim()) { i += 1; continue; }

    const heading = line.match(HEADING);
    if (heading) {
      const text = heading[2];
      blocks.push({ type: 'heading', level: heading[1].length, text, id: slugify(plainText(text)) });
      i += 1;
      continue;
    }

    if (HR.test(line)) { blocks.push({ type: 'hr' }); i += 1; continue; }

    if (/^\s*>/.test(line)) {
      const inner = [];
      while (i < lines.length && /^\s*>/.test(lines[i])) {
        inner.push(lines[i].replace(/^\s*>\s?/, ''));
        i += 1;
      }
      blocks.push({ type: 'blockquote', blocks: parseMarkdown(inner.join('\n')) });
      continue;
    }

    if (line.trim().startsWith('|') && i + 1 < lines.length && TABLE_SEPARATOR.test(lines[i + 1])) {
      const header = splitRow(line);
      const rows = [];
      i += 2;
      while (i < lines.length && lines[i].trim().startsWith('|')) {
        rows.push(splitRow(lines[i]));
        i += 1;
      }
      blocks.push({ type: 'table', header, rows });
      continue;
    }

    const listMatch = line.match(BULLET) || line.match(ORDERED);
    if (listMatch) {
      const ordered = !line.match(BULLET);
      const pattern = ordered ? ORDERED : BULLET;
      const baseIndent = listMatch[1].length;
      const items = [];
      let current = null;
      while (i < lines.length) {
        const l = lines[i];
        const m = l.match(pattern);
        if (m && m[1].length === baseIndent) {
          current = [m[3]];
          items.push(current);
          i += 1;
          continue;
        }
        if (!l.trim()) {
          // A blank line continues the item only if the next non-blank line is indented under it.
          let j = i + 1;
          while (j < lines.length && !lines[j].trim()) j += 1;
          if (j < lines.length && indentOf(lines[j]) > baseIndent) {
            current.push('');
            i += 1;
            continue;
          }
          break;
        }
        if (indentOf(l) > baseIndent) {
          current.push(l.slice(Math.min(indentOf(l), baseIndent + 2)));
          i += 1;
          continue;
        }
        if (!isBlockStart(l, lines[i + 1]) && current.length && current[current.length - 1] !== '') {
          // Lazy continuation of the item's paragraph.
          current[current.length - 1] += ' ' + l.trim();
          i += 1;
          continue;
        }
        break;
      }
      blocks.push({ type: 'list', ordered, items: items.map((item) => parseMarkdown(item.join('\n'))) });
      continue;
    }

    const para = [line.trim()];
    i += 1;
    while (i < lines.length && lines[i].trim() && !isBlockStart(lines[i], lines[i + 1])) {
      para.push(lines[i].trim());
      i += 1;
    }
    blocks.push({ type: 'paragraph', text: para.join(' ') });
  }
  return blocks;
}

const INLINE = [
  { type: 'placeholder', re: /\[PLACEHOLDER:\s*([^\]]*)\]/ },
  { type: 'link', re: /\[([^\]]+)\]\(([^)\s]+)\)/ },
  { type: 'code', re: /`([^`]+)`/ },
  { type: 'strong', re: /\*\*(.+?)\*\*/ },
  { type: 'em', re: /\*([^*\s](?:[^*]*[^*\s])?)\*/ }
];

/**
 * Parse inline Markdown into tokens.
 * @param {string} text
 * @returns {Array<object>}
 */
export function parseInline(text) {
  const tokens = [];
  let rest = String(text || '');
  while (rest) {
    let best = null;
    for (const rule of INLINE) {
      const m = rest.match(rule.re);
      if (m && (best === null || m.index < best.m.index)) best = { rule, m };
    }
    if (!best) { tokens.push({ type: 'text', value: rest }); break; }
    const { rule, m } = best;
    if (m.index > 0) tokens.push({ type: 'text', value: rest.slice(0, m.index) });
    if (rule.type === 'link') tokens.push({ type: 'link', href: m[2], children: parseInline(m[1]) });
    else if (rule.type === 'code') tokens.push({ type: 'code', value: m[1] });
    else if (rule.type === 'placeholder') tokens.push({ type: 'placeholder', value: m[1].trim() });
    else tokens.push({ type: rule.type, children: parseInline(m[1]) });
    rest = rest.slice(m.index + m[0].length);
  }
  return tokens;
}

export function renderInline(tokens) {
  return tokens.map((t, i) => {
    switch (t.type) {
      case 'strong': return <strong key={i}>{renderInline(t.children)}</strong>;
      case 'em': return <em key={i}>{renderInline(t.children)}</em>;
      case 'code': return <code key={i}>{t.value}</code>;
      case 'placeholder':
        return (
          <span key={i} className="placeholder-copy md-placeholder">
            <span className="placeholder-tag">{strings.common.placeholderTag}</span>
            {t.value}
          </span>
        );
      case 'link':
        if (t.href.startsWith('/')) return <Link key={i} to={t.href}>{renderInline(t.children)}</Link>;
        if (/^https?:/.test(t.href)) return <a key={i} href={t.href} target="_blank" rel="noopener noreferrer">{renderInline(t.children)}</a>;
        return <a key={i} href={t.href}>{renderInline(t.children)}</a>;
      default: return t.value;
    }
  });
}

function renderBlock(block, key) {
  switch (block.type) {
    case 'heading': {
      const Tag = 'h' + Math.min(Math.max(block.level, 1), 6);
      return <Tag key={key} id={block.id}>{renderInline(parseInline(block.text))}</Tag>;
    }
    case 'paragraph': return <p key={key}>{renderInline(parseInline(block.text))}</p>;
    case 'hr': return <hr key={key} />;
    case 'blockquote': return <blockquote key={key}>{renderBlocks(block.blocks)}</blockquote>;
    case 'list': {
      const Tag = block.ordered ? 'ol' : 'ul';
      return (
        <Tag key={key}>
          {block.items.map((item, i) => (
            <li key={i}>
              {item.length === 1 && item[0].type === 'paragraph'
                ? renderInline(parseInline(item[0].text))
                : renderBlocks(item)}
            </li>
          ))}
        </Tag>
      );
    }
    case 'table':
      return (
        <div key={key} className="md-table-wrap">
          <table>
            <thead>
              <tr>{block.header.map((cell, i) => <th key={i} scope="col">{renderInline(parseInline(cell))}</th>)}</tr>
            </thead>
            <tbody>
              {block.rows.map((row, r) => (
                <tr key={r}>{row.map((cell, c) => <td key={c}>{renderInline(parseInline(cell))}</td>)}</tr>
              ))}
            </tbody>
          </table>
        </div>
      );
    default: return null;
  }
}

/** Render parsed blocks to React elements. */
export function renderBlocks(blocks) {
  return blocks.map((block, i) => renderBlock(block, i));
}

/**
 * Split a legal document into its title (the first level 1 heading), its explicit draft notice (a
 * blockquote beginning with "Draft"), and the remaining blocks.
 * @param {string} source
 */
export function splitLegalDocument(source) {
  const blocks = parseMarkdown(source);
  const titleIndex = blocks.findIndex((b) => b.type === 'heading' && b.level === 1);
  const noticeIndex = blocks.findIndex((b) => b.type === 'blockquote'
    && b.blocks[0]?.type === 'paragraph' && /^draft\b/i.test(plainText(b.blocks[0].text).trim()));
  const title = titleIndex >= 0 ? plainText(blocks[titleIndex].text) : '';
  const notice = noticeIndex >= 0 ? blocks[noticeIndex] : null;
  const body = blocks.filter((_, i) => i !== titleIndex && i !== noticeIndex);
  return { title, notice, blocks: body };
}

/** Render a Markdown string. */
function Markdown({ source, blocks }) {
  return <>{renderBlocks(blocks || parseMarkdown(source))}</>;
}

export default Markdown;
