/**
 * One-time notices shown on the next member app screen (for example "Sign-up code applied"),
 * surviving the redirects the onboarding guard performs.
 */
const FLASH_KEY = 'tk.flash';

/** Save a notice: `{ tone: 'success'|'info'|'danger', message }`. */
export function setFlash(tone, message) {
  try {
    window.sessionStorage.setItem(FLASH_KEY, JSON.stringify({ tone, message }));
  } catch {
    // Ignore storage failures.
  }
}

/** Read and clear the pending notice. */
export function takeFlash() {
  try {
    const raw = window.sessionStorage.getItem(FLASH_KEY);
    if (!raw) return null;
    window.sessionStorage.removeItem(FLASH_KEY);
    const value = JSON.parse(raw);
    return value && value.message ? value : null;
  } catch {
    return null;
  }
}
