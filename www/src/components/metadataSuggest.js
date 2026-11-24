import { normalizeEnumerationResult } from '@trelliskit/shared';

/**
 * Autocomplete sources for labels and tags (docs/ENUMERATION_AND_METADATA.md section 1), scoped
 * to a target type. Each returns plain strings; a missing route or failure yields no suggestions.
 * @param {object} client ApiClient with `enumerateLabels`, `enumerateTagKeys`, `enumerateTagValues`
 * @param {string} [targetType]
 */
export function metadataSuggesters(client, targetType) {
  const run = async (fn, args, signal, field) => {
    if (!client || typeof client[fn] !== 'function') return [];
    try {
      const result = normalizeEnumerationResult(await client[fn]({ ...args, targetType, maxResults: 10 }, signal));
      return result.objects.map((o) => (o && typeof o === 'object' ? o[field] : o)).filter(Boolean);
    } catch {
      return [];
    }
  };
  return {
    labels: (prefix, signal) => run('enumerateLabels', { prefix }, signal, 'label'),
    tagKeys: (prefix, signal) => run('enumerateTagKeys', { prefix }, signal, 'key'),
    tagValues: (key, prefix, signal) => (key ? run('enumerateTagValues', { key, prefix }, signal, 'value') : Promise.resolve([]))
  };
}
