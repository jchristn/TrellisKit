import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { ApiClient, isCommunicator, isMessagingModerator, isReleaseManager, isSupportStaff } from '@trelliskit/shared';
import { createAdminApi } from '../utils/api.js';
import { CLIENT_HEADERS, useAuth } from './AuthContext.jsx';
import { isConsoleStaff, staffAreas as staffAreasOf } from '../utils/engagement.js';
import { clearDelegation, readDelegation } from '../utils/delegation.js';

export const ADMIN_TOKEN_KEY = 'tk.adminToken';

const AdminAuthContext = createContext(null);

function readToken() {
  try {
    return window.sessionStorage.getItem(ADMIN_TOKEN_KEY) || null;
  } catch {
    return null;
  }
}

function writeToken(token) {
  try {
    if (token) window.sessionStorage.setItem(ADMIN_TOKEN_KEY, token);
    else window.sessionStorage.removeItem(ADMIN_TOKEN_KEY);
  } catch {
    // Session storage can be unavailable; the token then lives only in memory.
  }
}

/**
 * Admin console authentication (contract section 8), two modes:
 *
 * - `session`: a member signed in with email and password (or any method) who holds the
 *   SystemAdmin role. Requests use the member session from AuthContext (access token in memory,
 *   refresh cookie, refresh on 401). A signed-in non-admin gets status `notAdmin`.
 * - `token`: the Phase 0 break-glass bearer token, validated with GET /settings and kept in
 *   sessionStorage. Any 401 signs the operator out of token mode.
 *
 * Phase 2: members holding `Moderator` or `FeedModerator` may also sign in (session mode). They
 * see only Home and Moderation (`isModeratorOnly`); the backend returns 403 for everything else.
 *
 * Phase 4: holders of DirectoryModerator, EventManager, or LibraryEditor also sign in (session mode)
 * and see the Directory, Events, or Library pages of their area (`staffAreas`); settings and
 * categories, programs, and levels stay SystemAdmin only.
 *
 * - `delegated`: a platform administrator acting in this property through a delegated session
 *   (docs/MULTIPROPERTY_CONTRACT.md section 6.2), started from the platform console and kept in
 *   sessionStorage. Requests send its token with `X-TK-Property`; it acts as SystemAdmin, is
 *   never refreshed, and ends on expiry or on any 401.
 *
 * A delegated session wins over the break-glass token, which wins over a member session.
 *
 * @param {{ children: React.ReactNode, fetchImpl?: typeof fetch }} props
 */
export function AdminAuthProvider({ children, fetchImpl = null }) {
  const auth = useAuth();
  const initialToken = useRef(readToken());
  const tokenRef = useRef(initialToken.current);
  const [tokenStatus, setTokenStatus] = useState(initialToken.current ? 'checking' : 'none');
  const [delegation, setDelegation] = useState(() => readDelegation());
  const [delegationEnded, setDelegationEnded] = useState(false);
  const delegationRef = useRef(delegation);

  const delegatedClient = useMemo(() => new ApiClient({
    baseUrl: '',
    fetchImpl,
    getAccessToken: () => (delegationRef.current ? delegationRef.current.accessToken : null),
    headers: () => (delegationRef.current ? { ...CLIENT_HEADERS, 'X-TK-Property': delegationRef.current.propertyId } : { ...CLIENT_HEADERS })
  }), [fetchImpl]);

  const endDelegation = useCallback(({ expired = false } = {}) => {
    clearDelegation();
    delegationRef.current = null;
    setDelegation(null);
    if (expired) setDelegationEnded(true);
  }, []);
  const delegatedApi = useMemo(() => createAdminApi(delegatedClient, { onUnauthorized: () => endDelegation({ expired: true }) }), [delegatedClient, endDelegation]);

  const tokenClient = useMemo(
    () => new ApiClient({ baseUrl: '', getAccessToken: () => tokenRef.current, fetchImpl, headers: CLIENT_HEADERS }),
    [fetchImpl]
  );

  const clearToken = useCallback(() => {
    tokenRef.current = null;
    writeToken(null);
    setTokenStatus('none');
  }, []);

  const tokenApi = useMemo(() => createAdminApi(tokenClient, { onUnauthorized: clearToken }), [tokenClient, clearToken]);
  const sessionApi = useMemo(() => createAdminApi(auth.client), [auth.client]);

  useEffect(() => {
    if (!initialToken.current) return undefined;
    let active = true;
    tokenClient.getServerSettings({ maxResults: 1 }).then(
      () => { if (active) setTokenStatus('valid'); },
      (err) => {
        if (!active) return;
        if (err && (err.status === 401 || err.status === 403)) clearToken();
        else setTokenStatus('valid');
      }
    );
    return () => { active = false; };
  }, [tokenClient, clearToken]);

  const loginWithToken = useCallback(async (token) => {
    const value = String(token || '').trim();
    tokenRef.current = value;
    try {
      await tokenClient.getServerSettings({ maxResults: 1 });
    } catch (err) {
      tokenRef.current = null;
      throw err;
    }
    writeToken(value);
    setTokenStatus('valid');
  }, [tokenClient]);

  const { signIn, signOut } = auth;
  const loginWithPassword = useCallback(async (email, password) => {
    const response = await signIn(email, password);
    return response.user;
  }, [signIn]);

  const logout = useCallback(async () => {
    if (delegationRef.current) {
      endDelegation();
      return;
    }
    if (tokenRef.current) {
      clearToken();
      return;
    }
    await signOut();
  }, [clearToken, signOut, endDelegation]);

  let status;
  let mode = null;
  if (delegation) {
    status = 'signedIn';
    mode = 'delegated';
  } else if (tokenStatus === 'checking') status = 'checking';
  else if (tokenStatus === 'valid') {
    status = 'signedIn';
    mode = 'token';
  } else if (auth.status === 'restoring') status = 'checking';
  else if (auth.status === 'signedIn') {
    // Phase 4 staff roles and Phase 5 staff roles (SupportAgent, Communicator, Messaging moderators) sign in too and see only their sections.
    status = auth.isAdmin || isConsoleStaff(auth.me) || isSupportStaff(auth.me) || isCommunicator(auth.me) || isMessagingModerator(auth.me) || isReleaseManager(auth.me) ? 'signedIn' : 'notAdmin';
    mode = 'session';
  } else status = 'signedOut';
  const isSystemAdmin = mode === 'token' || mode === 'delegated' || (mode === 'session' && auth.isAdmin);
  const isModeratorOnly = status === 'signedIn' && !isSystemAdmin;
  const staffAreas = useMemo(() => (isSystemAdmin ? ['directory', 'events', 'library', 'ranks'] : mode === 'session' ? staffAreasOf(auth.me) : []), [isSystemAdmin, mode, auth.me]);
  const sessionMe = mode === 'session' ? auth.me : null;
  // What a non-admin staff member may open (a SystemAdmin, token, or delegated session may open everything).
  const staff = useMemo(() => ({
    moderator: isSystemAdmin || isConsoleStaff(sessionMe) || isMessagingModerator(sessionMe),
    supportStaff: isSystemAdmin || isSupportStaff(sessionMe),
    communicator: isSystemAdmin || isCommunicator(sessionMe),
    messagingModerator: isSystemAdmin || isMessagingModerator(sessionMe),
    releaseManager: isSystemAdmin || isReleaseManager(sessionMe)
  }), [isSystemAdmin, sessionMe]);

  const api = mode === 'delegated' ? delegatedApi : mode === 'session' ? sessionApi : tokenApi;
  const client = mode === 'delegated' ? delegatedClient : mode === 'session' ? auth.client : tokenClient;
  const principal = mode === 'session' && auth.me ? (auth.me.displayName || auth.me.email) : null;

  const value = useMemo(() => ({
    status,
    mode,
    principal,
    me: mode === 'session' ? auth.me : null,
    isAuthenticated: status === 'signedIn',
    isLoading: status === 'checking',
    isNotAdmin: status === 'notAdmin',
    isSystemAdmin,
    isModeratorOnly,
    staffAreas,
    staff,
    client,
    fetchImpl,
    delegation,
    delegationEnded,
    endDelegation,
    api,
    login: loginWithToken,
    loginWithToken,
    loginWithPassword,
    logout
  }), [status, mode, principal, auth.me, isSystemAdmin, isModeratorOnly, staffAreas, staff, client, fetchImpl, delegation, delegationEnded, endDelegation, api, loginWithToken, loginWithPassword, logout]);

  return <AdminAuthContext.Provider value={value}>{children}</AdminAuthContext.Provider>;
}

/** Access admin authentication state and the admin API. */
export function useAdminAuth() {
  const context = useContext(AdminAuthContext);
  if (!context) throw new Error('useAdminAuth must be used within an AdminAuthProvider');
  return context;
}
