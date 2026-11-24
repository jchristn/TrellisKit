import { useEffect, useState } from 'react';
import { firstLinkUrl } from '@trelliskit/shared';

const DEBOUNCE_MS = 600;

/**
 * Preview card for the first URL in a draft (contract section 5.9), fetched by the server with
 * `POST /link-previews` while the member types. Unavailable previews simply show nothing.
 * @param {object} client
 * @param {string} body
 * @param {boolean} enabled
 * @param {object|null} [initial] the saved preview when editing
 */
export function useLinkPreview(client, body, enabled, initial = null) {
  const url = enabled ? firstLinkUrl(body || '') : null;
  const [state, setState] = useState({ url: initial ? initial.url : null, preview: initial });

  useEffect(() => {
    if (!url || !client || typeof client.getLinkPreview !== 'function') return undefined;
    if (state.url === url) return undefined;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      client.getLinkPreview(url, controller.signal).then(
        (preview) => { if (!controller.signal.aborted) setState({ url, preview: preview && preview.url ? preview : null }); },
        () => { if (!controller.signal.aborted) setState({ url, preview: null }); }
      );
    }, DEBOUNCE_MS);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [client, url, state.url]);

  return url && state.url === url ? state.preview : null;
}

export default useLinkPreview;
