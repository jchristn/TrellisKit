import { File, Paths } from 'expo-file-system';
import { fetch as nativeFetch } from 'expo/fetch';
import { resolveApiUrl } from './config';
import { getBundledProperty } from './property/property';
import * as Sharing from 'expo-sharing';
import { safeWebUrl } from './linkTargets';

/** Download first, then present the native file sheet. Successful files remain in app cache for the recipient to read; the OS may reclaim them. */
export async function shareDownloadedFile(url, { name = 'download', headers = {}, mimeType } = {}) {
  if (!safeWebUrl(url)) throw new Error('Invalid download URL');
  if (!(await Sharing.isAvailableAsync())) throw new Error('File sharing is unavailable');
  const credentialed = Object.keys(headers).length > 0;
  if (credentialed) {
    const property = getBundledProperty();
    const origins = [resolveApiUrl(), ...['api', 'media'].map((key) => property.domains?.[key]).filter(Boolean).map((host) => 'https://' + host)].map((value) => safeWebUrl(value)?.origin);
    if (!origins.includes(safeWebUrl(url).origin)) throw new Error('Untrusted download origin');
  }
  const safeName = String(name).replace(/[^A-Za-z0-9._-]/g, '_').slice(0, 100) || 'download';
  const destination = new File(Paths.cache, Date.now() + '-' + safeName);
  let file;
  let shared = false;
  try {
    if (credentialed) {
      // Expo's native fetch honors redirect:error on iOS and Android, unlike the legacy RN fetch.
      const response = await nativeFetch(url, { headers, credentials: 'omit', redirect: 'error' });
      if (!response.ok || !response.body) throw new Error('Download failed');
      const reader = response.body.getReader();
      file = destination;
      file.create();
      const handle = file.open();
      let bytes = 0;
      try {
        for (;;) {
          const chunk = await reader.read();
          if (chunk.done) break;
          bytes += chunk.value.byteLength;
          if (bytes > 100 * 1024 * 1024) throw new Error('Download too large');
          await handle.writeBytes(chunk.value);
        }
      } finally { await reader.cancel(); handle.close(); }
    } else {
      file = await File.downloadFileAsync(url, destination, { idempotent: true });
    }
    await Sharing.shareAsync(file.uri, mimeType ? { mimeType } : {});
    shared = true;
  } finally {
    if (!shared) {
      if (file?.exists) file.delete();
      else if (destination.exists) destination.delete();
    }
  }
}
