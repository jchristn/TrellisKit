import { pagedQuery } from '../../utils/adminQuery.js';

/**
 * Query helpers of the live content admin pages (strings, banners, assets): the EnumerationQuery
 * for `POST /admin/app/{collection}/enumerate`, with yes or no filters sent as booleans.
 */

/** A pagedQuery transform that sends the listed filters as booleans (`'true'` becomes true). */
export function booleanFilters(keys) {
  return (rest) => {
    const out = { ...rest };
    for (const key of keys) if (Object.prototype.hasOwnProperty.call(out, key)) out[key] = out[key] === 'true';
    return out;
  };
}

/**
 * EnumerationQuery from table filters and paging.
 * @param {object} filters
 * @param {readonly string[]} keys Accepted filter keys (with `search`).
 * @param {object} paging `{ pageNumber, pageSize, meta }`
 * @param {{ ordering: string, booleans?: string[] }} options
 */
export function contentQuery(filters, keys, paging, { ordering, booleans = [] }) {
  return { ...pagedQuery(filters, keys, { ...paging, sort: '' }, booleanFilters(booleans)), ordering };
}

/** The draft state badges of a content object: state plus draft changes. */
export function contentStateTone(state) {
  if (state === 'Published') return 'success';
  if (state === 'Archived') return 'neutral';
  return 'info';
}
