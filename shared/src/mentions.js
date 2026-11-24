/**
 * Mention helpers (docs/PHASE2_CONTRACT.md section 3.3): the `@handle` pattern, autocomplete
 * triggers for composers, and insertion. Handles are 3 to 30 of `A-Z a-z 0-9 _ .`, not preceded
 * by a letter, digit, `_`, or `.`; a trailing `.` is not part of the handle.
 */

const HANDLE_CHAR = /[A-Za-z0-9_.]/;

/** Minimum and maximum handle length in a mention. */
export const MENTION_MIN_LENGTH = 3;
export const MENTION_MAX_LENGTH = 30;

/** Characters typed after `@` before autocomplete starts (contract section 3.4: one). */
export const MENTION_QUERY_MIN = 1;

/**
 * Every distinct handle mentioned in a text (as written, first occurrence wins, case-insensitive
 * de-duplication). Escaped `\@` is skipped.
 * @param {string} text
 * @returns {string[]}
 */
export function extractMentionHandles(text) {
  const source = String(text || '');
  const seen = new Set();
  const handles = [];
  for (let i = 0; i < source.length; i += 1) {
    if (source[i] === '\\') {
      i += 1;
      continue;
    }
    if (source[i] !== '@' || (i > 0 && HANDLE_CHAR.test(source[i - 1]))) continue;
    let j = i + 1;
    while (j < source.length && HANDLE_CHAR.test(source[j])) j += 1;
    let handle = source.slice(i + 1, j);
    while (handle.endsWith('.')) handle = handle.slice(0, -1);
    if (handle.length >= MENTION_MIN_LENGTH && handle.length <= MENTION_MAX_LENGTH && !seen.has(handle.toLowerCase())) {
      seen.add(handle.toLowerCase());
      handles.push(handle);
    }
    i = j - 1;
  }
  return handles;
}

/**
 * The mention being typed at the caret, for autocomplete: `{ query, start, end }` where `start`
 * is the index of `@` and `end` the caret, or null when the caret is not inside a mention or
 * fewer than `MENTION_QUERY_MIN` characters follow the `@`.
 * @param {string} text
 * @param {number} caret
 * @returns {{ query: string, start: number, end: number }|null}
 */
export function mentionQueryAt(text, caret) {
  const source = String(text || '');
  const end = Math.max(0, Math.min(typeof caret === 'number' ? caret : source.length, source.length));
  let i = end - 1;
  while (i >= 0 && HANDLE_CHAR.test(source[i]) && end - i <= MENTION_MAX_LENGTH) i -= 1;
  if (i < 0 || source[i] !== '@') return null;
  if (i > 0 && (HANDLE_CHAR.test(source[i - 1]) || source[i - 1] === '\\')) return null;
  const query = source.slice(i + 1, end);
  if (query.length < MENTION_QUERY_MIN || query.length > MENTION_MAX_LENGTH) return null;
  return { query, start: i, end };
}

/**
 * Replace the typed mention `range` with `@handle ` and return the new text and caret.
 * @param {string} text
 * @param {{ start: number, end: number }} range
 * @param {string} handle
 * @returns {{ text: string, caret: number }}
 */
export function insertMention(text, range, handle) {
  const source = String(text || '');
  const before = source.slice(0, range.start);
  let after = source.slice(range.end);
  // Swallow the rest of a partially typed handle after the caret.
  let k = 0;
  while (k < after.length && HANDLE_CHAR.test(after[k])) k += 1;
  after = after.slice(k);
  const insert = '@' + handle + (after.startsWith(' ') ? '' : ' ');
  return { text: before + insert + after, caret: before.length + insert.length };
}
