import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { ApiError } from '@trelliskit/shared';
import { createPlatformClient } from '../utils/platformClient.js';
import { readPlatformToken, writePlatformToken } from '../utils/delegation.js';

const PlatformAuthContext = createContext(null);

/**
 * Platform console authentication (REST_API.md "Platform (MultiProperty)").
 *
 * On mount the console asks the backend whether this host serves platform routes
 * (`GET /platform/auth/me`): `404` means the deployment is SingleProperty or this is not a
 * platform host, so the console shows nothing but that explanation. Platform administrators sign
 * in with `POST /platform/auth/login`; the `tkp_...` token is kept in sessionStorage (this tab
 * only) and is never refreshed. Any 401 from a platform route signs the console out.
 *
 * `status`: `checking`, `unavailable`, `error` (server unreachable), `signedOut`, `signedIn`.
 *
 * @param {{ children: React.ReactNode, fetchImpl?: typeof fetch }} props
 */
export function PlatformAuthProvider({ children, fetchImpl = null }) {
  const tokenRef = useRef(readPlatformToken());
  const [state, setState] = useState({ status: 'checking', admin: null, code: null, error: null });
  const [nonce, setNonce] = useState(0);

  const signOutLocal = useCallback(() => {
    tokenRef.current = null;
    writePlatformToken(null);
    setState((prev) => ({ ...prev, status: prev.status === 'unavailable' ? prev.status : 'signedOut', admin: null }));
  }, []);

  const client = useMemo(() => createPlatformClient(() => tokenRef.current, fetchImpl), [fetchImpl]);

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    client.platformAvailability(controller.signal).then(
      (result) => {
        if (!active) return;
        if (!result.available) setState({ status: 'unavailable', admin: null, code: result.code, error: null });
        else if (result.signedIn && tokenRef.current) setState({ status: 'signedIn', admin: result.admin, code: null, error: null });
        else {
          if (tokenRef.current) writePlatformToken(null);
          tokenRef.current = null;
          setState({ status: 'signedOut', admin: null, code: null, error: null });
        }
      },
      (error) => { if (active) setState({ status: 'error', admin: null, code: null, error }); }
    );
    return () => {
      active = false;
      controller.abort();
    };
  }, [client, nonce]);

  const login = useCallback(async (email, password) => {
    const response = await client.platformLogin(email, password);
    if (!response || !response.accessToken) throw new ApiError('The server did not return a platform token.', { status: 500 });
    tokenRef.current = response.accessToken;
    writePlatformToken(response.accessToken);
    setState({ status: 'signedIn', admin: response.admin || null, code: null, error: null });
    return response;
  }, [client]);

  const acceptInvite = useCallback((token, password) => client.platformAcceptInvite(token, password), [client]);

  const logout = useCallback(async () => {
    try {
      if (tokenRef.current) await client.platformLogout();
    } catch {
      // Signing out always succeeds locally.
    }
    signOutLocal();
  }, [client, signOutLocal]);

  /** Run a platform call; a 401 ends the platform session. */
  const call = useCallback(async (fn) => {
    try {
      return await fn(client);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) signOutLocal();
      throw err;
    }
  }, [client, signOutLocal]);

  const retry = useCallback(() => {
    setState((prev) => ({ ...prev, status: 'checking' }));
    setNonce((n) => n + 1);
  }, []);

  const value = useMemo(() => ({
    ...state,
    isAuthenticated: state.status === 'signedIn',
    client,
    call,
    login,
    acceptInvite,
    logout,
    retry
  }), [state, client, call, login, acceptInvite, logout, retry]);

  return <PlatformAuthContext.Provider value={value}>{children}</PlatformAuthContext.Provider>;
}

/** Platform console authentication state and the platform client. */
export function usePlatformAuth() {
  const context = useContext(PlatformAuthContext);
  if (!context) throw new Error('usePlatformAuth must be used within a PlatformAuthProvider');
  return context;
}
