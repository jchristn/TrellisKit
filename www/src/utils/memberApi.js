import { normalizeSignupCode } from '@trelliskit/shared';

/** Paths under /auth that are anonymous: a 401 there never triggers a refresh. */
const ANONYMOUS_AUTH_PATHS = [
  '/auth/register', '/auth/login', '/auth/apple', '/auth/google', '/auth/facebook', '/auth/refresh', '/auth/logout',
  '/auth/password/forgot', '/auth/password/reset', '/auth/email/verify', '/auth/providers'
];

/** Whether a path is an anonymous authentication route (no refresh on 401). */
export function isAnonymousAuthPath(path) {
  return ANONYMOUS_AUTH_PATHS.includes(String(path || '').split('?')[0]);
}

const NO_RETRY = { retryOnUnauthorized: false };

/**
 * Member and authentication API methods (Phase 1 contract sections 2, 3, 5, and 6). Views call
 * these named methods instead of building URLs.
 *
 * @param {import('@trelliskit/shared').ApiClient} client
 */
export function createMemberApi(client) {
  return {
    client,

    /** GET /auth/providers (anonymous). */
    getProviders: (signal) => client.get('/auth/providers', { signal, ...NO_RETRY }),
    /** POST /auth/register. */
    register: (body) => client.post('/auth/register', body, NO_RETRY),
    /** POST /auth/login. */
    login: (email, password) => client.post('/auth/login', { email, password }, NO_RETRY),
    /** POST /auth/{provider} for apple, google, or facebook. */
    socialLogin: (provider, body) => client.post('/auth/' + provider, body, NO_RETRY),
    /** POST /auth/refresh (web: cookie only, no body). */
    refresh: () => client.post('/auth/refresh', undefined, NO_RETRY),
    /** POST /auth/logout (web: cookie only). */
    logout: () => client.post('/auth/logout', undefined, NO_RETRY),
    /** POST /auth/password/forgot. */
    forgotPassword: (email) => client.post('/auth/password/forgot', { email }, NO_RETRY),
    /** POST /auth/password/reset. */
    resetPassword: (token, newPassword) => client.post('/auth/password/reset', { token, newPassword }, NO_RETRY),
    /** POST /auth/email/verify. */
    verifyEmail: (token) => client.post('/auth/email/verify', { token }, NO_RETRY),
    /** POST /auth/email/resend (signed in). */
    resendVerification: () => client.post('/auth/email/resend'),
    /** GET /auth/me. */
    getMe: (signal) => client.get('/auth/me', { signal }),

    /** POST /me/age-attestation, optionally with `{ acceptTerms, acceptedDocuments }` (recorded after the attestation). */
    attestAge: (dateOfBirth, documents = null) => client.post('/me/age-attestation', { dateOfBirth, ...(documents || {}) }),
    /** PUT /me/profile. */
    updateProfile: (body) => client.put('/me/profile', body),
    /** GET /handles/{handle}/availability. */
    checkHandle: (handle, signal) => client.get('/handles/' + encodeURIComponent(handle) + '/availability', { signal }),
    /** PUT /me/avatar with the raw image body. */
    uploadAvatar: (file) => client.request('PUT', '/me/avatar', { rawBody: file, contentType: file.type }),
    /** DELETE /me/avatar. */
    deleteAvatar: () => client.delete('/me/avatar'),
    /** PUT /me/password. */
    setPassword: (body) => client.put('/me/password', body),
    /** POST /me/identities/{provider}. */
    linkIdentity: (provider, body) => client.post('/me/identities/' + provider, body),
    /** DELETE /me/identities/{provider}. */
    unlinkIdentity: (provider) => client.delete('/me/identities/' + provider),
    /** POST /me/export: queue an asynchronous account export; `202 { exportId, status }`. */
    requestExport: () => client.post('/me/export'),
    /** GET /me/export/{id}: the export job, with a signed `downloadUrl` once Ready. */
    getExport: (id) => client.get('/me/export/' + encodeURIComponent(id)),
    /** DELETE /me with the typed confirmation. */
    deleteAccount: () => client.request('DELETE', '/me', { body: { confirm: 'DELETE' } }),

    /** GET /membership. */
    getMembership: (signal) => client.get('/membership', { signal }),
    /** POST /membership/stripe/checkout, optionally with a discount sign-up code (`{ code }`). */
    startCheckout: (code) => client.post('/membership/stripe/checkout', code ? { code } : undefined),
    /** POST /membership/stripe/portal. */
    openBillingPortal: () => client.post('/membership/stripe/portal'),

    /** POST /signup-codes/validate (code trimmed and upper-cased). */
    validateCode: (code) => client.post('/signup-codes/validate', { code: normalizeSignupCode(code) }),
    /** POST /signup-codes/redeem (code trimmed and upper-cased). */
    redeemCode: (code) => client.post('/signup-codes/redeem', { code: normalizeSignupCode(code) })
  };
}
