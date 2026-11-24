import { UploadError } from '@trelliskit/shared';

/**
 * `put` for the shared upload helper using XMLHttpRequest, so the browser reports upload
 * progress. Sends exactly the given headers (no Authorization) and reads the `ETag` response
 * header (storage exposes it through CORS).
 * @param {{ url: string, method?: string, headers?: Record<string,string>, body: Blob, signal?: AbortSignal, onProgress?: (bytes: number) => void }} request
 * @returns {Promise<{ etag: string|null }>}
 */
export function xhrPut({ url, method = 'PUT', headers = {}, body, signal, onProgress }) {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open(method, url, true);
    for (const [name, value] of Object.entries(headers || {})) xhr.setRequestHeader(name, value);
    if (onProgress && xhr.upload) xhr.upload.onprogress = (e) => { if (e.lengthComputable) onProgress(e.loaded); };
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) resolve({ etag: xhr.getResponseHeader('ETag') });
      else reject(new UploadError('Storage rejected the upload (status ' + xhr.status + ').', { status: xhr.status }));
    };
    xhr.onerror = () => reject(new UploadError('The upload could not reach storage.', { status: 0 }));
    xhr.ontimeout = xhr.onerror;
    xhr.onabort = () => {
      const err = new Error('The upload was cancelled.');
      err.name = 'AbortError';
      reject(err);
    };
    if (signal) {
      if (signal.aborted) {
        xhr.abort();
        return;
      }
      signal.addEventListener('abort', () => xhr.abort(), { once: true });
    }
    xhr.send(body);
  });
}

export default xhrPut;
