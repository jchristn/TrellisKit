/**
 * Thin wrappers over browser APIs that tests replace with spies (navigation away from the app,
 * file downloads, and crypto).
 */
export const browser = {
  /** Navigate the whole page to an external URL (Stripe Checkout and the billing portal). */
  assign(url) {
    window.location.assign(url);
  },

  /** Offer a Blob or string to the user as a file download. */
  download(data, filename, type = 'application/octet-stream') {
    const blob = data instanceof Blob ? data : new Blob([data], { type });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    link.rel = 'noopener';
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  },

  /** Offer a data: URL (for example a PNG QR code) as a file download. */
  downloadDataUrl(dataUrl, filename) {
    const link = document.createElement('a');
    link.href = dataUrl;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  }
};

/** Random hex string from the platform CSPRNG. */
export function randomHex(byteCount = 32) {
  const bytes = new Uint8Array(byteCount);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}

/** SHA-256 of a string as lower-case hex. */
export async function sha256Hex(text) {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
  return Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, '0')).join('');
}

const scripts = new Map();

/** Load an external script once; later calls share the same promise. */
export function loadScript(src) {
  if (scripts.has(src)) return scripts.get(src);
  const promise = new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = src;
    script.async = true;
    script.defer = true;
    script.onload = () => resolve();
    script.onerror = () => {
      scripts.delete(src);
      script.remove();
      reject(new Error('Unable to load ' + src));
    };
    document.head.appendChild(script);
  });
  scripts.set(src, promise);
  return promise;
}
