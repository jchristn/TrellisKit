import * as SecureStore from 'expo-secure-store';
import { VISITOR_TOKEN_HEADER } from '@trelliskit/shared';
import { identityHeaders } from '../api/identity';
import { MobileApiClient } from '../api/MobileApiClient';
import { getAppVersion, resolveApiUrl } from '../config';

/**
 * Visitor sessions for Public assistants signed out (docs/AI_CONTRACT.md sections 13.1 and 13.4,
 * ACCESS_MODELS_CONTRACT.md section 8.8), the same flow as the website:
 *
 * - The token is `X-TK-Visitor-Token`: taken from the response that starts a conversation (the
 *   server creates a visitor when none is sent) or from `POST /assistant-visitors` before a call
 *   that needs one (listing assistants and conversations), and kept in secure storage.
 * - Every assistant call from a guest sends it; no bearer token is ever sent.
 * - A stored token that stops resolving (key rotation, the visitor was removed) answers `401`: the
 *   client starts a new visitor session once and retries the request (streams retry through
 *   `onUnauthorized`).
 *
 * Signed-in members never use this client; their conversations are separate, and a visitor who
 * signs up does not carry conversations over.
 */

/** Secure-store key of the visitor token. */
export const VisitorTokenKey = 'tk.ai.visitorToken';

const OPTIONS = { keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK };
const SESSION_PATH = '/assistant-visitors';
/** Starting a conversation creates the visitor when no token is sent. */
const START_PATH = /^\/assistants\/[^/]+\/conversations$/;

const emptyTokenStore = Object.freeze({ getRefreshToken: async () => null, setRefreshToken: async () => {}, clear: async () => {} });

function tokenFrom(response) {
  try {
    const value = response && response.headers && typeof response.headers.get === 'function' ? response.headers.get(VISITOR_TOKEN_HEADER) : null;
    return typeof value === 'string' && value.trim() ? value.trim() : null;
  } catch {
    return null;
  }
}

function streamingFetch() {
  try {
    const mod = require('expo/fetch');
    return mod && typeof mod.fetch === 'function' ? (url, init) => mod.fetch(url, init) : null;
  } catch {
    return null;
  }
}

/**
 * Create a visitor client: a MobileApiClient without a session that carries the visitor token.
 * @param {object} [options]
 * @param {string} [options.baseUrl]
 * @param {string} [options.appVersion]
 * @param {typeof fetch} [options.fetchImpl]
 * @param {typeof fetch|null} [options.streamFetchImpl] Defaults to expo/fetch when available.
 * @param {{ getItemAsync: Function, setItemAsync: Function, deleteItemAsync: Function }} [options.store]
 * @returns {MobileApiClient}
 */
export function createVisitorClient({
  baseUrl = resolveApiUrl(),
  appVersion = getAppVersion(),
  fetchImpl = (...args) => globalThis.fetch(...args),
  streamFetchImpl = streamingFetch(),
  store = SecureStore
} = {}) {
  let client = null;
  const capture = (impl) => async (url, init) => {
    const response = await impl(url, init);
    const token = tokenFrom(response);
    if (token && client && token !== client.visitorToken) client.setVisitorToken(token);
    return response;
  };
  client = new MobileApiClient({ baseUrl, appVersion, tokenStore: emptyTokenStore, fetchImpl: capture(fetchImpl), timeoutMs: 15000, identityHeaders: () => identityHeaders() });
  client.isVisitor = true;
  client.visitorToken = null;
  if (streamFetchImpl) client.streamFetchImpl = capture(streamFetchImpl);

  client.visitorReady = (async () => {
    try {
      const stored = await store.getItemAsync(VisitorTokenKey, OPTIONS);
      if (typeof stored === 'string' && stored && !client.visitorToken) client.visitorToken = stored;
    } catch {
      // Unreadable storage: a new session is started when needed.
    }
  })();

  /** Keep a token (memory now, secure storage in the background). */
  client.setVisitorToken = (token) => {
    client.visitorToken = token || null;
    const write = token ? store.setItemAsync(VisitorTokenKey, token, OPTIONS) : store.deleteItemAsync(VisitorTokenKey, OPTIONS);
    return Promise.resolve(write).catch(() => {});
  };

  let renewing = null;
  /** Start a new visitor session (one in flight for every caller). Resolves whether it worked. */
  client.renewVisitorSession = () => {
    if (!renewing) {
      renewing = (async () => {
        client.visitorToken = null;
        try {
          const session = await client.createVisitorSession();
          if (!session || !session.visitorToken) return false;
          await client.setVisitorToken(session.visitorToken);
          return true;
        } catch {
          return false;
        }
      })().finally(() => {
        renewing = null;
      });
    }
    return renewing;
  };

  const baseRequest = client.request.bind(client);
  const withToken = (options) => {
    const headers = { ...((options && options.headers) || {}) };
    delete headers[VISITOR_TOKEN_HEADER];
    if (client.visitorToken) headers[VISITOR_TOKEN_HEADER] = client.visitorToken;
    return { ...(options || {}), headers, withAuth: false };
  };
  client.request = async (method, path, options = {}) => {
    const clean = String(path).split('?')[0];
    if (clean === SESSION_PATH) return baseRequest(method, path, { ...options, withAuth: false });
    await client.visitorReady;
    if (!client.visitorToken && !(method === 'POST' && START_PATH.test(clean))) await client.renewVisitorSession();
    try {
      return await baseRequest(method, path, withToken(options));
    } catch (error) {
      if (!error || error.status !== 401) throw error;
      if (!(await client.renewVisitorSession())) throw error;
      return baseRequest(method, path, withToken(options));
    }
  };

  // Streams: no bearer token; the stored token is read before the first request.
  client.baseHeaders = async () => {
    await client.visitorReady;
    return { ...client.identityHeaders(), 'X-Client': 'mobile', 'X-Client-Version': appVersion };
  };
  client.onUnauthorized = ({ path } = {}) => (path === SESSION_PATH ? Promise.resolve(false) : client.renewVisitorSession());
  client.hasStoredSession = async () => false;
  return client;
}

let shared = null;

/** The app's visitor client (created on first use). */
export function getVisitorClient() {
  if (!shared) shared = createVisitorClient();
  return shared;
}

/** Drop the cached visitor client (tests). */
export function resetVisitorClient() {
  shared = null;
}
