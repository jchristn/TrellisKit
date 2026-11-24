import { useEffect, useMemo, useState } from 'react';
import { ApiClient, createVisitorTokenStore } from '@trelliskit/shared';
import { version as WEB_VERSION } from '../../../package.json';

function storage() {
  try {
    return typeof window !== 'undefined' ? window.localStorage : null;
  } catch {
    return null;
  }
}

/**
 * An API client for signed-out visitors of Public assistants (AI contract section 13.4): no bearer
 * token, `X-TK-Visitor-Token` from localStorage (memory when unavailable), and a visitor session
 * created on first use. `surface` is `web` for /assistants/:id and `widget` inside the embed frame.
 * `enabled: false` skips the visitor session (a signed-in member uses the member client instead).
 * @param {{ fetchImpl?: typeof fetch, surface?: 'web'|'widget', enabled?: boolean }} [options]
 * @returns {{ client: ApiClient|null, error: Error|null }}
 */
export default function useVisitorClient({ fetchImpl = null, surface = 'web', enabled = true } = {}) {
  const store = useMemo(() => createVisitorTokenStore(storage(), 'tk_visitor_token'), []);
  const client = useMemo(() => {
    const c = new ApiClient({ baseUrl: '', fetchImpl, headers: { 'X-Client': surface, 'X-Client-Version': WEB_VERSION } });
    c.visitorToken = store.get();
    // A stored token can stop resolving (key rotation, another property, a removed visitor): the
    // server answers 401. Start a new visitor session once and retry the request.
    c.onUnauthorized = async ({ path }) => {
      if (path === '/assistant-visitors') return false;
      store.set(null);
      c.visitorToken = null;
      try {
        const session = await c.createVisitorSession();
        if (!session || !session.visitorToken) return false;
        store.set(session.visitorToken);
        c.visitorToken = session.visitorToken;
        return true;
      } catch {
        return false;
      }
    };
    return c;
  }, [fetchImpl, store, surface]);
  const [ready, setReady] = useState(Boolean(client.visitorToken));
  const [error, setError] = useState(null);
  useEffect(() => {
    if (!enabled || client.visitorToken) return undefined;
    let active = true;
    client.createVisitorSession()
      .then((session) => {
        if (!active || !session) return;
        store.set(session.visitorToken);
        client.visitorToken = session.visitorToken;
        setReady(true);
      })
      .catch((err) => { if (active) setError(err); });
    return () => { active = false; };
  }, [client, store, enabled]);
  return { client: ready ? client : null, error };
}
