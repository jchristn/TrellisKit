/**
 * Paging helpers. Lists are EnumerationResults paged by `continuationToken`
 * (docs/ENUMERATION_AND_METADATA.md section 2, which overrides the Phase 2 cursor shape). Tokens
 * are opaque: the client only passes them back. Lists sorted by a changing value can repeat an item across pages, so
 * merging always de-duplicates by `id` (the later copy wins in place).
 */

/**
 * Append a page to a list, de-duplicated by id.
 * @template T
 * @param {T[]} current
 * @param {T[]} incoming
 * @param {(item: T) => string} [keyOf]
 * @returns {T[]}
 */
export function mergePage(current, incoming, keyOf = (item) => item && item.id) {
  const result = Array.isArray(current) ? current.slice() : [];
  const index = new Map();
  result.forEach((item, i) => index.set(keyOf(item), i));
  for (const item of Array.isArray(incoming) ? incoming : []) {
    const key = keyOf(item);
    if (key !== undefined && key !== null && index.has(key)) result[index.get(key)] = item;
    else {
      index.set(key, result.length);
      result.push(item);
    }
  }
  return result;
}

export { readEnumeration as readPage } from './enumeration';
