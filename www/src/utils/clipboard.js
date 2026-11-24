/**
 * Copy text to the clipboard on any origin: https, http, localhost, or a LAN address.
 *
 * The async Clipboard API exists only in secure contexts (https and localhost) and can still
 * reject (permissions, document not focused, embedded frames). When it is missing or rejects, a
 * hidden textarea is selected and copied with document.execCommand('copy'), which works over plain
 * http because it runs inside the click that called it.
 *
 * @param {string} text
 * @returns {Promise<void>} resolves when copied; rejects when neither method worked
 */
export async function copyText(text) {
  const value = String(text ?? '');
  if (typeof window !== 'undefined' && window.isSecureContext && navigator.clipboard && navigator.clipboard.writeText) {
    try {
      await navigator.clipboard.writeText(value);
      return;
    } catch {
      // Fall through to the textarea copy.
    }
  }
  if (!legacyCopy(value)) throw new Error('Copy to clipboard is not available.');
}

/** Copy with a hidden, selected textarea. Returns whether the browser reported success. */
function legacyCopy(value) {
  if (typeof document === 'undefined' || !document.body) return false;
  const active = document.activeElement;
  const selection = document.getSelection ? document.getSelection() : null;
  const previousRange = selection && selection.rangeCount > 0 ? selection.getRangeAt(0) : null;
  const area = document.createElement('textarea');
  area.value = value;
  area.setAttribute('readonly', '');
  area.setAttribute('aria-hidden', 'true');
  // Off screen but rendered (iOS will not select display:none or zero-size elements); 16px avoids iOS zoom.
  area.style.cssText = 'position:fixed;top:0;left:-9999px;width:1px;height:1px;opacity:0;font-size:16px;';
  document.body.appendChild(area);
  let ok = false;
  try {
    area.focus({ preventScroll: true });
    area.select();
    area.setSelectionRange(0, value.length);
    ok = typeof document.execCommand === 'function' && document.execCommand('copy');
  } catch {
    ok = false;
  } finally {
    document.body.removeChild(area);
    if (selection && previousRange) {
      selection.removeAllRanges();
      selection.addRange(previousRange);
    }
    if (active && typeof active.focus === 'function') active.focus({ preventScroll: true });
  }
  return Boolean(ok);
}
