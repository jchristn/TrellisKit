/**
 * Assistant embed origins (docs/AI_CONTRACT.md section 13.3): https://host[:port] by default;
 * plain http only for the local development hosts localhost and 127.0.0.1, on any port. Mirrors
 * the backend's EmbedOrigins; the server's validation is authoritative.
 */

const ORIGIN = /^(https?):\/\/([a-z0-9.-]+)(:([0-9]{1,5}))?$/;
const LOCAL_HOSTS = Object.freeze(['localhost', '127.0.0.1']);

function parse(value) {
  if (typeof value !== 'string') return null;
  const origin = value.trim().replace(/\/+$/, '').toLowerCase();
  const m = ORIGIN.exec(origin);
  if (!m) return null;
  const host = m[2];
  if (host.startsWith('.') || host.endsWith('.') || host.startsWith('-') || host.includes('..')) return null;
  if (m[4] !== undefined) {
    const port = Number(m[4]);
    if (port < 1 || port > 65535) return null;
  }
  return { origin, scheme: m[1], host };
}

/**
 * Normalize an embed origin (trimmed, lowercase, no trailing slash).
 * @param {string} value
 * @returns {string|null} null when the origin is not allowed
 */
export function normalizeEmbedOrigin(value) {
  const p = parse(value);
  if (!p) return null;
  if (p.scheme === 'http' && !LOCAL_HOSTS.includes(p.host)) return null;
  return p.origin;
}

/** Whether an origin is a local development origin (localhost or 127.0.0.1, any scheme and port). */
export function isLocalDevOrigin(value) {
  const p = parse(value);
  return Boolean(p && LOCAL_HOSTS.includes(p.host));
}

/**
 * Validate a list of embed origins as entered.
 * @param {string[]} values
 * @returns {{ origins: string[], problems: Record<number, string> }} normalized distinct origins and the bad entries by index
 */
export function validateEmbedOrigins(values) {
  const origins = [];
  const problems = {};
  (Array.isArray(values) ? values : []).forEach((value, i) => {
    const normalized = normalizeEmbedOrigin(value);
    if (!normalized) problems[i] = value;
    else if (!origins.includes(normalized)) origins.push(normalized);
  });
  return { origins, problems };
}
