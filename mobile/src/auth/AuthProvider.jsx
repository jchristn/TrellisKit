import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { AppState } from 'react-native';
import { getApiClient } from '../api/client';
import { auth as authApi, DefaultProviders } from '../api/endpoints';
import { deviceFlags } from '../api/tokenStore';
import { documentKeysOf, isDocumentAcceptanceRequired } from '../documents/documents';
import { resetMapSearchSessions } from '../components/maps/mapSearchSessions';
import { resetSessionFilters } from '../hooks/useListFilters';
import { resetPushRegistrationState, unregisterPushToken } from '../notifications/push';
import { setPalettePreference } from '../theme/palettePreference';

const AuthContext = createContext(null);

/** Reload Me at most this often after access errors (several screens can fail at once). */
export const AccessRecheckMs = 10000;

/** Reload Me on return to the foreground at most this often (picks up newly required documents). */
export const ForegroundRecheckMs = 60000;

/**
 * Me after a `403 DocumentAcceptanceRequired`: the reloaded Me when it reports
 * `pendingDocuments`, otherwise the keys the error named (an older Me without the field).
 */
export function withRequiredDocuments(me, error) {
  if (!me || Array.isArray(me.pendingDocuments)) return me;
  const keys = documentKeysOf(error);
  return keys.length ? { ...me, pendingDocuments: keys.map((key) => ({ key })) } : me;
}

const SIGNED_OUT = Object.freeze({ status: 'signedOut', user: null, notificationsPrompted: false });
const EXPIRED = Object.freeze({ ...SIGNED_OUT, expired: true });

function promptedKey(user) {
  return 'tk.notificationsPrompted.' + String(user.id).replace(/[^A-Za-z0-9._-]/g, '_');
}

/**
 * Holds the signed-in member and the sign-in providers, restores the session at launch from the
 * refresh token in secure storage, and exposes sign-in and sign-out actions.
 *
 * session.status is one of:
 * - `loading`: restoring the session at launch
 * - `offline`: a refresh token is stored but the server could not be reached (retry with restore)
 * - `signedOut` (with `expired: true` when the server revoked the session during use, which
 *   routes to Sign in with a notice; with `browsing: true` after "Browse as a guest")
 * - `signedIn`: session.user is the current Me
 */
export function AuthProvider({ children, api: apiProp }) {
  const [api] = useState(() => apiProp || getApiClient());
  const [session, setSession] = useState({ status: 'loading', user: null, notificationsPrompted: false });
  const [providers, setProviders] = useState(null);
  const [providersError, setProvidersError] = useState(false);
  const [notice, setNotice] = useState(null);
  const statusRef = useRef('loading');
  useEffect(() => {
    statusRef.current = session.status;
  }, [session.status]);

  const applyUser = useCallback(async (user) => {
    const notificationsPrompted = await deviceFlags.get(promptedKey(user));
    const next = { status: 'signedIn', user, notificationsPrompted };
    setSession(next);
    return next;
  }, []);

  // The member's palette preset is stored on the account (GET /me/appearance), so it follows them
  // across devices; it is read once per signed-in member and applied by useTheme.
  const signedInId = session.status === 'signedIn' && session.user ? session.user.id : null;
  useEffect(() => {
    if (!signedInId || !api || typeof api.getAppearance !== 'function') return undefined;
    let active = true;
    Promise.resolve()
      .then(() => api.getAppearance())
      .then(
        (view) => { if (active && view && typeof view === 'object') setPalettePreference(view.palettePreset || null); },
        () => {}
      );
    return () => {
      active = false;
    };
  }, [api, signedInId]);

  const runRestore = useCallback(async () => {
    if (!(await api.hasStoredSession())) {
      setSession(SIGNED_OUT);
      return SIGNED_OUT;
    }
    try {
      const result = await api.refresh();
      return await applyUser(result.user);
    } catch (error) {
      if (error && error.status === 0) {
        const offline = { status: 'offline', user: null, notificationsPrompted: false };
        setSession(offline);
        return offline;
      }
      await api.clearSession();
      setSession(SIGNED_OUT);
      return SIGNED_OUT;
    }
  }, [api, applyUser]);

  /** Retry a launch restore (for example after an offline start). */
  const restore = useCallback(() => {
    setSession((s) => ({ ...s, status: 'loading' }));
    return runRestore();
  }, [runRestore]);

  const loadProviders = useCallback(async () => {
    try {
      const result = await authApi.providers(api);
      setProviders({ ...DefaultProviders, ...(result || {}) });
      setProvidersError(false);
    } catch {
      // Email sign-in still works; the sign-in screen offers Retry for the other options.
      setProviders((current) => current || DefaultProviders);
      setProvidersError(true);
    }
  }, [api]);

  useEffect(() => {
    const offExpired = api.onSessionExpired((error) => {
      resetPushRegistrationState();
      setSession(EXPIRED);
      setNotice(error && error.code === 'AccountBanned' ? 'accountBanned' : 'sessionExpired');
    });
    const offSession = api.onSession((result) => {
      if (result && result.user) setSession((s) => (s.status === 'signedIn' ? { ...s, user: result.user } : s));
    });
    // A 402 or 403 AgeAttestationRequired mid-session means Me is stale (the membership ended or
    // the age step is due): reload it so the route gates send the member to the right step.
    // A 403 DocumentAcceptanceRequired means a required document changed: Me then lists it in
    // pendingDocuments and the gates send the member to the documents step. It has its own
    // throttle so an earlier 402 does not delay it.
    const applyMe = (me) => {
      if (me && me.id) setSession((s) => (s.status === 'signedIn' && s.user && s.user.id === me.id ? { ...s, user: me } : s));
    };
    let lastAccessCheck = 0;
    let lastDocumentCheck = 0;
    const offAccess = api.onAccessRequired((error) => {
      const now = Date.now();
      const documents = isDocumentAcceptanceRequired(error);
      if (now - (documents ? lastDocumentCheck : lastAccessCheck) < AccessRecheckMs) return;
      if (documents) lastDocumentCheck = now;
      else lastAccessCheck = now;
      authApi
        .me(api)
        .then((me) => applyMe(documents ? withRequiredDocuments(me, error) : me))
        .catch(() => {
          if (!documents) return;
          const keys = documentKeysOf(error);
          if (keys.length) setSession((s) => (s.status === 'signedIn' && s.user ? { ...s, user: { ...s.user, pendingDocuments: keys.map((key) => ({ key })) } } : s));
        });
    });
    // Back in the foreground: reload Me (throttled) so documents that became required while the
    // app was in the background send the member to the documents step.
    let lastForegroundCheck = Date.now();
    const appState = AppState.addEventListener('change', (state) => {
      if (state !== 'active' || statusRef.current !== 'signedIn') return;
      const now = Date.now();
      if (now - lastForegroundCheck < ForegroundRecheckMs) return;
      lastForegroundCheck = now;
      authApi
        .me(api)
        .then(applyMe)
        .catch(() => {});
    });
    // Both run asynchronously and only set state after their first await.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    runRestore();
    loadProviders();
    return () => {
      offExpired();
      offSession();
      offAccess();
      if (appState && typeof appState.remove === 'function') appState.remove();
    };
  }, [api, runRestore, loadProviders]);

  /** Store an AuthResponse from any sign-in route and return the new session. */
  const completeSignIn = useCallback(
    async (result) => {
      await api.setSession(result);
      setNotice(null);
      return applyUser(result.user);
    },
    [api, applyUser]
  );

  /** Replace the cached Me (every profile and account route returns Me). */
  const updateUser = useCallback(
    (user) => {
      const next = { status: 'signedIn', user, notificationsPrompted: session.notificationsPrompted };
      setSession(next);
      return next;
    },
    [session.notificationsPrompted]
  );

  const refreshUser = useCallback(async () => {
    const me = await authApi.me(api);
    return updateUser(me);
  }, [api, updateUser]);

  const markNotificationsPrompted = useCallback(async () => {
    if (session.user) await deviceFlags.set(promptedKey(session.user));
    const next = { ...session, notificationsPrompted: true };
    setSession(next);
    return next;
  }, [session]);

  /** Revoke the session on the server (best effort) and clear it locally. */
  const signOut = useCallback(
    async ({ notice: nextNotice = null, revoke = true } = {}) => {
      if (revoke) {
        await unregisterPushToken(api).catch(() => false);
        await authApi.logout(api);
      }
      resetPushRegistrationState();
      resetSessionFilters();
      resetMapSearchSessions();
      // The palette preset belongs to the account: the next person on this device sees the property palette.
      setPalettePreference(null);
      await api.clearSession();
      setSession(SIGNED_OUT);
      setNotice(nextNotice);
    },
    [api]
  );

  /** Browse signed out (anonymous browsing): the tabs open with only the sections open to guests. */
  const browseAsGuest = useCallback(() => {
    setSession((s) => (s.status === 'signedOut' ? { ...SIGNED_OUT, browsing: true } : s));
  }, []);

  const value = useMemo(
    () => ({
      api,
      session,
      user: session.user,
      providers,
      providersError,
      notice,
      clearNotice: () => setNotice(null),
      restore,
      reloadProviders: loadProviders,
      completeSignIn,
      updateUser,
      refreshUser,
      markNotificationsPrompted,
      signOut,
      browseAsGuest
    }),
    [api, session, providers, providersError, notice, restore, loadProviders, completeSignIn, updateUser, refreshUser, markNotificationsPrompted, signOut, browseAsGuest]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

/** Access the auth context. */
export function useAuth() {
  const value = useContext(AuthContext);
  if (!value) throw new Error('useAuth must be used inside AuthProvider.');
  return value;
}

/** The auth context, or null outside AuthProvider (components that also render standalone). */
export function useOptionalAuth() {
  return useContext(AuthContext);
}
