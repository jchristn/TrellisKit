import { useCallback } from 'react';
import { metadata } from '../../api/community';
import { useOptionalAuth } from '../../auth/AuthProvider';

/**
 * Autocomplete sources for labels, tag keys, and tag values of one object type (bounded
 * enumerations, docs/ENUMERATION_AND_METADATA.md section 1).
 */
export function useMetadataSuggestions(targetType, apiOverride) {
  const auth = useOptionalAuth();
  const api = apiOverride || (auth && auth.api);
  const labels = useCallback((prefix) => (api && api.post ? metadata.labels(api, { targetType, prefix }) : Promise.resolve(null)), [api, targetType]);
  const tagKeys = useCallback((prefix) => (api && api.post ? metadata.tagKeys(api, { targetType, prefix }) : Promise.resolve(null)), [api, targetType]);
  const tagValues = useCallback((key, prefix) => (api && api.post ? metadata.tagValues(api, { targetType, key, prefix }) : Promise.resolve(null)), [api, targetType]);
  return { labels, tagKeys, tagValues };
}
