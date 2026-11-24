import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import { LabelMatch } from '@trelliskit/shared';

/** An empty metadata filter. */
export const EMPTY_META = Object.freeze({ labels: [], labelMatch: LabelMatch.All, tags: {}, search: '', ordering: '' });

const TAG = 'tag.';

/**
 * Read label, tag, search, and ordering filters from URL parameters (`labels=a&labels=b`,
 * `labelMatch=Any`, `tag.level=beginner`, `search=`, `ordering=`), optionally namespaced by
 * `prefix` (docs/ENUMERATION_AND_METADATA.md section 3: shareable state).
 * @param {URLSearchParams} params
 * @param {string} [prefix]
 */
export function readMeta(params, prefix = '') {
  const labels = params.getAll(prefix + 'labels').map((x) => x.trim()).filter(Boolean);
  const tags = {};
  for (const [key, value] of params.entries()) {
    if (key.startsWith(prefix + TAG)) tags[key.slice((prefix + TAG).length)] = value;
  }
  return {
    labels,
    labelMatch: params.get(prefix + 'labelMatch') === LabelMatch.Any ? LabelMatch.Any : LabelMatch.All,
    tags,
    search: params.get(prefix + 'search') || '',
    ordering: params.get(prefix + 'ordering') || ''
  };
}

/**
 * Write metadata filters into a copy of `params` (replacing the previous ones). Defaults are
 * omitted so URLs stay short. `extra` keys (for example `page: ''`) are set or removed too.
 * @param {URLSearchParams} params
 * @param {object} meta
 * @param {{ prefix?: string, defaultOrdering?: string, extra?: Record<string,string> }} [options]
 */
export function writeMeta(params, meta, { prefix = '', defaultOrdering = '', extra = {} } = {}) {
  const next = new URLSearchParams(params);
  next.delete(prefix + 'labels');
  next.delete(prefix + 'labelMatch');
  next.delete(prefix + 'search');
  next.delete(prefix + 'ordering');
  for (const key of Array.from(next.keys())) if (key.startsWith(prefix + TAG)) next.delete(key);
  const m = meta || EMPTY_META;
  for (const label of m.labels || []) next.append(prefix + 'labels', label);
  if ((m.labels || []).length && m.labelMatch === LabelMatch.Any) next.set(prefix + 'labelMatch', LabelMatch.Any);
  for (const [key, value] of Object.entries(m.tags || {})) next.set(prefix + TAG + key, value === null || value === undefined ? '*' : value);
  if (m.search) next.set(prefix + 'search', m.search);
  if (m.ordering && m.ordering !== defaultOrdering) next.set(prefix + 'ordering', m.ordering);
  for (const [key, value] of Object.entries(extra)) {
    if (value === '' || value === null || value === undefined) next.delete(prefix + key);
    else next.set(prefix + key, String(value));
  }
  return next;
}

/** Number of active metadata filters (labels, tags, and a search). */
export function activeMetaCount(meta) {
  if (!meta) return 0;
  return (meta.labels || []).length + Object.keys(meta.tags || {}).length + (meta.search ? 1 : 0);
}

/**
 * URL-backed metadata filters for a list. Returns `[meta, setMeta, metaKey]`; `metaKey` is a stable
 * string for memo dependencies.
 * @param {{ prefix?: string, defaultOrdering?: string }} [options]
 */
export function useEnumerationUrlState({ prefix = '', defaultOrdering = '' } = {}) {
  const [searchParams, setSearchParams] = useSearchParams();
  const key = JSON.stringify(readMeta(searchParams, prefix));
  const meta = useMemo(() => {
    const parsed = JSON.parse(key);
    return { ...parsed, ordering: parsed.ordering || defaultOrdering };
  }, [key, defaultOrdering]);
  const setMeta = useCallback((next, extra) => {
    setSearchParams((current) => writeMeta(current, next, { prefix, defaultOrdering, extra }), { replace: false });
  }, [setSearchParams, prefix, defaultOrdering]);
  return [meta, setMeta, key];
}

export default useEnumerationUrlState;
