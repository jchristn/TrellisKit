/**
 * Error thrown by ApiClient when the server returns a non-2xx response or the request fails.
 */
export class ApiError extends Error {
  /**
   * @param {string} message Human-readable message.
   * @param {object} [options]
   * @param {number} [options.status] HTTP status code (0 when the request never reached the server).
   * @param {string} [options.code] Machine-readable error code from the server body, if any.
   * @param {unknown} [options.body] Parsed response body, if any.
   * @param {number|null} [options.retryAfterSeconds] Value of the `Retry-After` header in seconds (429 responses).
   */
  constructor(message, { status = 0, code = null, body = null, retryAfterSeconds = null } = {}) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.body = body;
    this.retryAfterSeconds = retryAfterSeconds;
  }

  /** `fieldErrors` from the error body (`{}` when absent). */
  get fieldErrors() {
    const body = this.body;
    if (body && typeof body === 'object' && body.fieldErrors && typeof body.fieldErrors === 'object') return body.fieldErrors;
    return {};
  }
}

/**
 * Parse a `Retry-After` header value (whole seconds, or an HTTP date) into seconds.
 * @param {string|null|undefined} value
 * @param {Date} [now]
 * @returns {number|null}
 */
export function parseRetryAfter(value, now = new Date()) {
  if (value === null || value === undefined || value === '') return null;
  const text = String(value).trim();
  if (/^\d+$/.test(text)) return parseInt(text, 10);
  const date = new Date(text);
  if (Number.isNaN(date.getTime())) return null;
  return Math.max(0, Math.ceil((date.getTime() - now.getTime()) / 1000));
}
