import { ApiClient } from '@trelliskit/shared';
import { CLIENT_HEADERS } from '../context/AuthContext.jsx';

/**
 * An ApiClient for the platform routes (`/api/v1/platform/*`): same origin, the website client
 * headers, and the platform administrator token (`tkp_...`) as the bearer. It never sends
 * `X-TK-Property` (platform routes resolve to the platform realm by host) and never refreshes:
 * platform sessions end at `expiresUtc` and the console signs in again.
 * @param {() => (string|null)} getToken
 * @param {typeof fetch|null} [fetchImpl]
 */
export function createPlatformClient(getToken, fetchImpl = null) {
  return new ApiClient({ baseUrl: '', fetchImpl, headers: CLIENT_HEADERS, getAccessToken: getToken });
}
