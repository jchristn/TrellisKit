import { useEffect, useRef, useState } from 'react';
import { linkPreviews } from '../api/community';
import { firstUrl } from '../content/markdown';

/** Wait after the last keystroke before asking the server for a preview. */
export const LinkPreviewWaitMs = 600;

/**
 * The link preview for the first URL in a composer's text (docs/PHASE2_CONTRACT.md section 5.9).
 * The server fetches the page (`POST /link-previews`), never the device. Returns null while there
 * is no URL, while `enabled` is false, and when the server has no preview.
 * @param {object} api
 * @param {string} text
 * @param {boolean} enabled
 * @returns {{ url: string|null, preview: object|null }}
 */
export function useLinkPreview(api, text, enabled) {
  const url = firstUrl(text || '');
  const [preview, setPreview] = useState(null);
  const request = useRef(0);
  useEffect(() => {
    if (!url || !enabled) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setPreview(null);
      return undefined;
    }
    if (preview && (preview.url === url || preview.canonicalUrl === url)) return undefined;
    const id = (request.current += 1);
    const timer = setTimeout(async () => {
      try {
        const result = await linkPreviews.get(api, url);
        if (id === request.current) setPreview(result && result.url ? result : null);
      } catch {
        if (id === request.current) setPreview(null);
      }
    }, LinkPreviewWaitMs);
    return () => clearTimeout(timer);
  }, [api, url, enabled, preview]);
  return { url, preview: url && enabled ? preview : null };
}
