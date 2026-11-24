import { useEffect, useState } from 'react';
import { FORUM_LIMITS, normalizeEnumerationResult } from '@trelliskit/shared';

/**
 * Every forum the caller can see, for the forum index and forum pickers. A property has at most
 * `forums.maxForums` forums, so this follows `continuationToken` until the end, bounded by that
 * limit (PHASE3_CONTRACT.md section 3.1); it never asks for "all" in one request.
 * @param {object} client ApiClient
 * @param {object} [query] EnumerationQuery fields (search, labels, tags, filters)
 * @param {{ enabled?: boolean, admin?: boolean }} [options]
 */
export function useForumList(client, query = null, { enabled = true, admin = false } = {}) {
  const [state, setState] = useState({ forums: [], loading: enabled, error: null });
  const [nonce, setNonce] = useState(0);
  const key = JSON.stringify(query || {});

  useEffect(() => {
    if (!enabled) return undefined;
    const controller = new AbortController();
    let active = true;
    setState((s) => ({ ...s, loading: true, error: null }));
    (async () => {
      const forums = [];
      let token = null;
      const base = JSON.parse(key);
      do {
        const body = { ...base, maxResults: 100, continuationToken: token };
        const page = normalizeEnumerationResult(admin ? await client.adminEnumerateForums(body, controller.signal) : await client.enumerateForums(body, controller.signal));
        forums.push(...page.objects);
        token = page.endOfResults ? null : page.continuationToken;
      } while (token && forums.length < FORUM_LIMITS.maxForums * 2);
      return forums;
    })().then(
      (forums) => { if (active) setState({ forums, loading: false, error: null }); },
      (error) => { if (active) setState((s) => ({ ...s, loading: false, error })); }
    );
    return () => {
      active = false;
      controller.abort();
    };
  }, [client, key, enabled, admin, nonce]);

  return { ...state, reload: () => setNonce((n) => n + 1), setForums: (fn) => setState((s) => ({ ...s, forums: typeof fn === 'function' ? fn(s.forums) : fn })) };
}

export default useForumList;
