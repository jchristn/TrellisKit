import { normalizeSignupCode } from '@trelliskit/shared';

/**
 * A sign-up code carried from /join?code= through registration, social sign-in, age
 * attestation, and email verification. Kept in sessionStorage for the current tab and mirrored
 * to localStorage for 24 hours so it survives opening the verification email in a new tab.
 */
export const PENDING_CODE_KEY = 'tk.pendingSignupCode';
const MAX_AGE_MS = 24 * 60 * 60 * 1000;

function read(storage) {
  try {
    const raw = storage.getItem(PENDING_CODE_KEY);
    if (!raw) return null;
    const value = JSON.parse(raw);
    if (!value || typeof value.code !== 'string' || !value.code) return null;
    if (typeof value.savedAt === 'number' && Date.now() - value.savedAt > MAX_AGE_MS) return null;
    return { code: value.code, auto: Boolean(value.auto), savedAt: value.savedAt || Date.now() };
  } catch {
    return null;
  }
}

function write(storage, value) {
  try {
    if (value) storage.setItem(PENDING_CODE_KEY, JSON.stringify(value));
    else storage.removeItem(PENDING_CODE_KEY);
  } catch {
    // Storage can be unavailable (private mode); the code then lives only in the URL.
  }
}

/** The pending code, if any: `{ code, auto }`. `auto` means redeem without asking again. */
export function getPendingCode() {
  return read(window.sessionStorage) || read(window.localStorage);
}

/** Save a pending code (normalized). An empty code clears it. */
export function setPendingCode(code, { auto = false } = {}) {
  const normalized = normalizeSignupCode(code);
  if (!normalized) {
    clearPendingCode();
    return null;
  }
  const value = { code: normalized, auto, savedAt: Date.now() };
  write(window.sessionStorage, value);
  write(window.localStorage, value);
  return value;
}

/** Mark the pending code for automatic redemption (new accounts). */
export function markPendingCodeAuto() {
  const current = getPendingCode();
  if (current) setPendingCode(current.code, { auto: true });
}

/** Forget the pending code. */
export function clearPendingCode() {
  write(window.sessionStorage, null);
  write(window.localStorage, null);
}
