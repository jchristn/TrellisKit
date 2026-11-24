import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { ApiClient, documentAcceptanceRequiredKeys, singleFlight } from '@trelliskit/shared';
import { createMemberApi, isAnonymousAuthPath } from '../utils/memberApi.js';
import { isSystemAdmin } from '../utils/onboarding.js';
import { noteApiFailure } from '../utils/appContentErrors.js';
import { setPalettePreference } from '../property/palettePreference.js';
import { version as WEB_VERSION } from '../../package.json';

/** Headers every website request sends (contract section 1). */
export const CLIENT_HEADERS = Object.freeze({ 'X-Client': 'web', 'X-Client-Version': WEB_VERSION });

/**
 * localStorage hint that this browser had a session. The refresh cookie is HttpOnly, so the page
 * cannot see it; the hint avoids a refresh call (and a 401) for every anonymous visitor. The
 * cookie is only ever issued by a sign-in, register, or refresh response that this provider
 * applies (which sets the hint), so without the hint there is no session to restore, on any
 * path, the admin console and member app included.
 */
export const SESSION_HINT_KEY = 'tk.hasSession';

/** 401 codes that describe bad input credentials, not an expired session: never refresh for these. */
const CREDENTIAL_ERRORS = ['InvalidCredentials', 'InvalidIdentityToken'];

const AuthContext = createContext(null);

function readHint() {
  try {
    return window.localStorage.getItem(SESSION_HINT_KEY) === '1';
  } catch {
    return false;
  }
}

function writeHint(on) {
  try {
    if (on) window.localStorage.setItem(SESSION_HINT_KEY, '1');
    else window.localStorage.removeItem(SESSION_HINT_KEY);
  } catch {
    // Ignore storage failures.
  }
}

function defaultShouldRestore() {
  return readHint();
}

/**
 * Member authentication (contract section 1, web client).
 *
 * - The access token lives in memory only (a ref); `Me` lives in state.
 * - The refresh token is the HttpOnly `tk_refresh` cookie; refresh is `POST /auth/refresh` with
 *   credentials included and no body.
 * - Any 401 from a non-auth route triggers one shared, in-flight refresh and a single retry. When
 *   refresh fails, the session ends.
 * - On page load the session is restored silently with a refresh call, only when the session
 *   hint says this browser signed in (never a refresh that is bound to fail with 401).
 * - Any `403 DocumentAcceptanceRequired` reloads `Me`, so `Me.pendingDocuments` routes the member
 *   app (and the admin console) to the blocking "Review and accept" step.
 *
 * @param {{ children: React.ReactNode, fetchImpl?: typeof fetch, restoreOnLoad?: boolean }} props
 */
export function AuthProvider({ children, fetchImpl = null, restoreOnLoad }) {
  const tokenRef = useRef(null);
  const refreshRef = useRef(null);
  const restoreStarted = useRef(false);
  const providersRef = useRef(null);
  const documentGateRef = useRef(null);
  const [shouldRestore] = useState(() => (typeof restoreOnLoad === 'boolean' ? restoreOnLoad : defaultShouldRestore()));
  const [state, setState] = useState(() => ({ status: shouldRestore ? 'restoring' : 'signedOut', me: null }));

  const client = useMemo(() => {
    const created = new ApiClient({
      baseUrl: '',
      fetchImpl,
      headers: CLIENT_HEADERS,
      getAccessToken: () => tokenRef.current,
      onUnauthorized: async ({ path, error }) => {
        if (isAnonymousAuthPath(path) || !tokenRef.current || !refreshRef.current) return false;
        if (error && CREDENTIAL_ERRORS.includes(error.code)) return false;
        return refreshRef.current();
      }
    });
    // The document gate: a 403 DocumentAcceptanceRequired from any route refreshes `Me`.
    created.request = async (method, path, options) => {
      try {
        return await ApiClient.prototype.request.call(created, method, path, options);
      } catch (err) {
        if (tokenRef.current && documentGateRef.current && documentAcceptanceRequiredKeys(err)) documentGateRef.current();
        // A 503 FeatureDisabled marks that feature's kill switch on in the app content store at once.
        noteApiFailure(err);
        throw err;
      }
    };
    return created;
  }, [fetchImpl]);

  const api = useMemo(() => createMemberApi(client), [client]);

  const endSession = useCallback(() => {
    tokenRef.current = null;
    writeHint(false);
    // The palette preset belongs to the account: the next visitor on this browser sees the property palette.
    setPalettePreference(null);
    setState({ status: 'signedOut', me: null });
  }, []);

  const applyAuth = useCallback((response) => {
    if (!response || !response.accessToken) throw new Error('The server did not return an access token.');
    tokenRef.current = response.accessToken;
    writeHint(true);
    setState({ status: 'signedIn', me: response.user || null });
    return response;
  }, []);

  const refresh = useMemo(() => singleFlight(async () => {
    try {
      applyAuth(await api.refresh());
      return true;
    } catch (err) {
      const status = err && err.status;
      if (status && status >= 400 && status < 500) {
        endSession();
      } else {
        // Network or server failure: keep the hint so the next load tries again.
        tokenRef.current = null;
        setState({ status: 'signedOut', me: null });
      }
      return false;
    }
  }), [api, applyAuth, endSession]);

  useEffect(() => {
    refreshRef.current = refresh;
  }, [refresh]);

  useEffect(() => {
    if (restoreStarted.current) return;
    restoreStarted.current = true;
    if (shouldRestore) refresh();
  }, [refresh, shouldRestore]);

  const signIn = useCallback(async (email, password) => applyAuth(await api.login(email, password)), [api, applyAuth]);
  const register = useCallback(async (body) => applyAuth(await api.register(body)), [api, applyAuth]);
  const socialSignIn = useCallback(async (provider, body) => applyAuth(await api.socialLogin(provider, body)), [api, applyAuth]);

  const signOut = useCallback(async () => {
    try {
      await api.logout();
    } catch {
      // Logout always succeeds from the member's point of view.
    }
    endSession();
  }, [api, endSession]);

  const setMe = useCallback((me) => {
    setState((prev) => (prev.status === 'signedIn' ? { ...prev, me: typeof me === 'function' ? me(prev.me) : me } : prev));
  }, []);

  const reloadMe = useCallback(async () => {
    const me = await api.getMe();
    setMe(me);
    return me;
  }, [api, setMe]);

  useEffect(() => {
    documentGateRef.current = singleFlight(async () => {
      try {
        setMe(await api.getMe());
      } catch {
        // The next request or page load tries again.
      }
    });
  }, [api, setMe]);

  // The member's palette preset is stored on the account (GET /me/appearance), so it follows them
  // to every browser; it is read once per signed-in session and applied by PropertyProvider.
  const signedInId = state.status === 'signedIn' && state.me ? state.me.id : null;
  useEffect(() => {
    if (!signedInId) return undefined;
    const controller = new AbortController();
    // Best effort and side-effect free: a 401 here never starts a refresh or ends the session.
    client.get('/me/appearance', { signal: controller.signal, retryOnUnauthorized: false }).then(
      (view) => { if (!controller.signal.aborted && view && typeof view === 'object') setPalettePreference(view.palettePreset || null); },
      () => {}
    );
    return () => controller.abort();
  }, [client, signedInId]);

  const getProviders = useCallback(() => {
    if (!providersRef.current) {
      providersRef.current = api.getProviders().catch((err) => {
        providersRef.current = null;
        throw err;
      });
    }
    return providersRef.current;
  }, [api]);

  const value = useMemo(() => ({
    status: state.status,
    me: state.me,
    isRestoring: state.status === 'restoring',
    isSignedIn: state.status === 'signedIn',
    isAdmin: isSystemAdmin(state.me),
    client,
    api,
    signIn,
    register,
    socialSignIn,
    signOut,
    endSession,
    refresh,
    setMe,
    reloadMe,
    getProviders
  }), [state, client, api, signIn, register, socialSignIn, signOut, endSession, refresh, setMe, reloadMe, getProviders]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

/** Access member authentication state and the member API. */
export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within an AuthProvider');
  return context;
}
