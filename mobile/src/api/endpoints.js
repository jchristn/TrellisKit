/**
 * Typed wrappers for the Phase 1 routes (docs/PHASE1_CONTRACT.md). Each takes the
 * MobileApiClient so screens and tests can pass their own instance.
 *
 * The mobile app never calls the sign-up code routes (contract section 6).
 */

/** Values used when GET /auth/providers cannot be loaded: email only. */
export const DefaultProviders = Object.freeze({
  email: true,
  apple: { enabled: false, webClientId: null },
  google: { enabled: false, webClientId: null, iosClientId: null, androidClientId: null },
  facebook: { enabled: false, appId: null },
  stripe: { enabled: false },
  appleIap: { enabled: false, productId: null },
  googlePlay: { enabled: false, productId: null }
});

export const auth = {
  providers: (api) => api.get('/auth/providers'),
  login: (api, { email, password }) => api.post('/auth/login', { email, password }),
  register: (api, body) => api.post('/auth/register', body),
  social: (api, provider, body) => api.post('/auth/' + provider, body),
  forgotPassword: (api, email) => api.post('/auth/password/forgot', { email }),
  me: (api) => api.get('/auth/me'),
  resendVerification: (api) => api.post('/auth/email/resend'),
  /** Always resolves; logout is best effort. */
  async logout(api) {
    const refreshToken = await api.tokenStore.getRefreshToken();
    if (!refreshToken) return;
    try {
      await api.post('/auth/logout', { refreshToken });
    } catch {
      // The server always answers 204; a network failure still signs out locally.
    }
  }
};

export const account = {
  /**
   * POST /me/age-attestation. `extras` may carry `acceptTerms` and `acceptedDocuments`
   * (`[{ key, version }]`), recorded after a successful attestation.
   */
  attestAge: (api, dateOfBirth, extras) => api.post('/me/age-attestation', { dateOfBirth, ...(extras || {}) }),
  updateProfile: (api, fields) => api.put('/me/profile', fields),
  handleAvailability: (api, handle) => api.get('/handles/' + encodeURIComponent(handle) + '/availability'),
  uploadAvatar: (api, bytes, contentType) => api.request('PUT', '/me/avatar', { rawBody: bytes, contentType }),
  deleteAvatar: (api) => api.delete('/me/avatar'),
  setPassword: (api, { currentPassword, newPassword }) => api.put('/me/password', currentPassword ? { currentPassword, newPassword } : { newPassword }),
  linkIdentity: (api, provider, body) => api.post('/me/identities/' + provider, body),
  unlinkIdentity: (api, provider) => api.delete('/me/identities/' + provider),
  /** POST /me/export: queue an asynchronous export; `202 { exportId, status }`. */
  requestExport: (api) => api.post('/me/export'),
  /** GET /me/export/{id}: `AccountExport` with `downloadUrl` once `Ready`. */
  getExport: (api, id) => api.get('/me/export/' + encodeURIComponent(id)),
  deleteAccount: (api) => api.request('DELETE', '/me', { body: { confirm: 'DELETE' } })
};

/** Export polling: every 3 seconds, for up to 2 minutes per attempt. */
export const ExportPolling = Object.freeze({ intervalMs: 3000, timeoutMs: 120000 });

/** Export statuses that end polling. */
const EXPORT_DONE = ['Ready', 'Failed', 'Expired'];

/**
 * Run an account export (REST_API.md "Account export (asynchronous)"): queue it with
 * POST /me/export (or keep polling `exportId` when one from this session is still pending), then
 * poll GET /me/export/{id} until it is Ready, Failed, or Expired, or the time runs out.
 * @param {object} api MobileApiClient.
 * @param {object} [options]
 * @param {string|null} [options.exportId] A pending export from earlier in the session.
 * @param {(exportId: string) => void} [options.onQueued] Receives the export id once known.
 * @param {number} [options.intervalMs]
 * @param {number} [options.timeoutMs]
 * @param {(ms: number) => Promise<void>} [options.sleep]
 * @param {() => number} [options.now]
 * @returns {Promise<object>} The last AccountExport read (status `Pending` or `Running` on timeout).
 * @throws {ApiError}
 */
export async function runAccountExport(api, { exportId = null, onQueued, intervalMs = ExportPolling.intervalMs, timeoutMs = ExportPolling.timeoutMs, sleep = (ms) => new Promise((r) => setTimeout(r, ms)), now = () => Date.now() } = {}) {
  let id = exportId;
  if (!id) {
    const queued = await account.requestExport(api);
    id = queued && queued.exportId;
    if (!id) throw new Error('The export could not be started.');
  }
  if (onQueued) onQueued(id);
  const deadline = now() + timeoutMs;
  for (;;) {
    const current = await account.getExport(api, id);
    if (current && EXPORT_DONE.includes(current.status)) return current;
    if (now() + intervalMs > deadline) return current || { id, status: 'Pending' };
    await sleep(intervalMs);
  }
}

/** Page size of the member's own acceptance history (the server enforces its own maximum). */
export const AcceptancesPageSize = 20;

/**
 * Required documents (documents contract). The register body carries `acceptTerms` and
 * `acceptedDocuments` (`[{ key, version }]`) through `auth.register`.
 */
export const documents = {
  /** GET /public/documents/{key}: the current `Document` (anonymous). 404 for an unknown key. */
  get: (api, key, signal) => api.get('/public/documents/' + encodeURIComponent(key), signal ? { signal } : undefined),
  /**
   * POST /me/documents/accept with `{ acceptTerms?, documents: [{ key, version }] }`; returns the
   * updated Me.
   */
  accept: (api, { acceptTerms, documents: list }) => api.post('/me/documents/accept', { ...(acceptTerms === undefined ? {} : { acceptTerms: Boolean(acceptTerms) }), documents: (list || []).map((d) => ({ key: d.key, version: d.version })) }),
  /**
   * POST /me/documents/acceptances/enumerate: `EnumerationResult<DocumentAcceptance>` of the
   * caller's own acceptances (bounded; continue with `continuationToken`).
   */
  enumerateAcceptances: (api, query = {}, signal) => api.post('/me/documents/acceptances/enumerate', { maxResults: AcceptancesPageSize, ...query }, signal ? { signal } : undefined)
};

export const membership = {
  get: (api) => api.get('/membership'),
  verifyApple: (api, signedTransaction) => api.post('/membership/apple/verify', { signedTransaction }),
  verifyGoogle: (api, { purchaseToken, productId }) => api.post('/membership/google/verify', { purchaseToken, productId })
};
