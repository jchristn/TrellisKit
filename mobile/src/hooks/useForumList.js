import { FORUM_LIMITS } from '@trelliskit/shared';
import { useCallback, useEffect, useRef, useState } from 'react';
import { readEnumeration } from '../content/enumeration';

/**
 * Every forum the member can see. A property has at most `forums.maxForums` forums, so this
 * follows `continuationToken` (pages of 100) to the end, bounded by that limit
 * (docs/PHASE3_CONTRACT.md section 3.1). It never asks for "all" in one request.
 * @param {object} api MobileApiClient (with the shared forum methods).
 * @param {object} [query] EnumerationQuery fields (search, labels, tags).
 */
export function useForumList(api, query = null) {
  const [state, setState] = useState({ forums: [], status: 'loading', error: null });
  const key = JSON.stringify(query || {});
  const generation = useRef(0);

  const load = useCallback(async () => {
    const gen = (generation.current += 1);
    setState((s) => ({ ...s, status: s.forums.length ? 'ready' : 'loading', error: null }));
    try {
      const forums = [];
      let token = null;
      do {
        const page = readEnumeration(await api.enumerateForums({ ...JSON.parse(key), maxResults: 100, continuationToken: token }));
        forums.push(...page.items);
        token = page.nextCursor;
      } while (token && forums.length < FORUM_LIMITS.maxForums * 2);
      if (gen === generation.current) setState({ forums, status: 'ready', error: null });
    } catch (error) {
      if (gen === generation.current) setState((s) => ({ ...s, status: s.forums.length ? 'ready' : 'error', error }));
    }
  }, [api, key]);

  useEffect(() => {
    load();
  }, [load]);

  const replace = useCallback((forum) => setState((s) => ({ ...s, forums: s.forums.map((f) => (f.id === forum.id ? { ...f, ...forum } : f)) })), []);
  return { ...state, reload: load, replace };
}
