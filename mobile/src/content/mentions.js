/**
 * Mention helpers for composers (docs/PHASE2_CONTRACT.md sections 3.3 and 3.4): the handle
 * pattern, the autocomplete trigger at the cursor, and inserting a chosen handle.
 *
 * The contract places these in @trelliskit/shared (mentions.js); this local copy follows the same
 * pattern until it ships.
 */

/** Characters a handle may contain. */
const HANDLE_CHAR = /[A-Za-z0-9_.]/;

/** Mentions in a body, as written (`@` plus 3 to 30 handle characters, not inside a word or email). */
export function extractHandles(body) {
  const text = String(body || '');
  const out = [];
  const pattern = /(^|[^A-Za-z0-9_.])@([A-Za-z0-9_.]{3,30})/g;
  let match;
  while ((match = pattern.exec(text))) {
    const handle = match[2].replace(/\.+$/, '');
    if (handle.length >= 3) out.push(handle);
  }
  return out;
}

/**
 * The mention being typed at the cursor, if any: `@` followed by at least `minLength` handle
 * characters, with nothing but handle characters between it and the cursor.
 * @param {string} text
 * @param {number} cursor Selection end.
 * @param {number} [minLength] Characters after `@` before suggestions start (contract: 1).
 * @returns {{ query: string, start: number, end: number }|null}
 */
export function mentionQueryAt(text, cursor, minLength = 1) {
  const value = String(text || '');
  const end = Math.max(0, Math.min(typeof cursor === 'number' ? cursor : value.length, value.length));
  let i = end - 1;
  while (i >= 0 && HANDLE_CHAR.test(value[i])) i -= 1;
  if (i < 0 || value[i] !== '@') return null;
  const before = i > 0 ? value[i - 1] : '';
  if (before && HANDLE_CHAR.test(before)) return null;
  const query = value.slice(i + 1, end);
  if (query.length < minLength || query.length > 30) return null;
  return { query, start: i, end };
}

/**
 * Replace the mention being typed with `@handle ` and return the new text and cursor.
 * @param {string} text
 * @param {{ start: number, end: number }} range From mentionQueryAt.
 * @param {string} handle
 */
export function insertMention(text, range, handle) {
  const value = String(text || '');
  let tailStart = range.end;
  while (tailStart < value.length && HANDLE_CHAR.test(value[tailStart])) tailStart += 1;
  const insert = '@' + handle + ' ';
  const tail = value.slice(tailStart).replace(/^ /, '');
  const next = value.slice(0, range.start) + insert + tail;
  return { text: next, cursor: range.start + insert.length };
}
