import { useCallback, useMemo } from 'react';
import { isProfileFilterParam, profileFieldsOf, profileFilterParams } from '@trelliskit/shared';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import { normalizePage } from '../../utils/query.js';

/**
 * The admin's profile field definitions (`POST /admin/profile-fields/enumerate`, AdminOnly and
 * Staff fields included), falling back to the published property's `profileFields` while they
 * load or when the call fails.
 */
export function useAdminProfileFields() {
  const { property } = useProperty();
  const { api } = useAdminAuth();
  const fetcher = useCallback((signal) => api.listProfileFields({ maxResults: 200, ordering: 'ProfileOrderAscending' }, signal), [api]);
  const query = useApiQuery(fetcher);
  const fallback = useMemo(() => profileFieldsOf(property), [property]);
  const loaded = useMemo(() => (query.data ? normalizePage(query.data, 200).items : null), [query.data]);
  return useMemo(() => (loaded && loaded.length ? loaded : fallback), [loaded, fallback]);
}

/** Write a profile filter state into URL params (replacing every `profile.*` param), back to page 1. */
export function writeProfileFilterParams(params, state) {
  const next = new URLSearchParams(params);
  for (const name of Array.from(next.keys())) if (isProfileFilterParam(name)) next.delete(name);
  for (const [name, value] of Object.entries(profileFilterParams(state))) {
    for (const v of Array.isArray(value) ? value : [value]) next.append(name, v);
  }
  next.delete('page');
  return next;
}
