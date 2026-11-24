import { useEffect, useState } from 'react';
import { findRemoteAsset } from '@trelliskit/shared';
import { APP_CONTENT_CACHE, useAppContent } from '../context/AppContentContext.jsx';

/**
 * A browser URL for a content asset file, read through the Cache API (`tk-app-content`, keyed by
 * URL; files are content addressed, so a cached entry never goes stale). Falls back to the network
 * URL when the Cache API or object URLs are unavailable or anything fails.
 * @param {string} url
 * @returns {Promise<string>}
 */
export async function cachedAssetUrl(url) {
  if (!url || typeof caches === 'undefined' || typeof URL === 'undefined' || typeof URL.createObjectURL !== 'function') return url;
  try {
    const cache = await caches.open(APP_CONTENT_CACHE);
    let response = await cache.match(url);
    if (!response) {
      response = await fetch(url);
      if (!response.ok) return url;
      await cache.put(url, response.clone());
    }
    return URL.createObjectURL(await response.blob());
  } catch {
    return url;
  }
}

/**
 * The URL of a published remote asset by key (docs/LIVE_UPDATES_CONTRACT.md section 14.2), its
 * dark variant when `dark` is set and one exists, or null when no asset has that key.
 * @param {string} key
 * @param {{ dark?: boolean }} [options]
 * @returns {string|null}
 */
export default function useRemoteAsset(key, { dark = false } = {}) {
  const { state } = useAppContent();
  const asset = key ? findRemoteAsset(state.assets, key) : null;
  const file = asset ? (dark && asset.darkVariant && asset.darkVariant.url ? asset.darkVariant : asset) : null;
  const url = file && file.url ? file.url : null;
  const [resolved, setResolved] = useState({ url: null, src: null });

  useEffect(() => {
    if (!url) return undefined;
    let active = true;
    let objectUrl = null;
    cachedAssetUrl(url).then((src) => {
      const isObject = typeof src === 'string' && src.startsWith('blob:');
      if (!active) {
        if (isObject) URL.revokeObjectURL(src);
        return;
      }
      if (isObject) objectUrl = src;
      setResolved({ url, src });
    });
    return () => {
      active = false;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [url]);

  if (!url) return null;
  return resolved.url === url && resolved.src ? resolved.src : url;
}

/** The remote asset descriptor (alt text, size) of a key, or null. */
export function useRemoteAssetInfo(key) {
  const { state } = useAppContent();
  return key ? findRemoteAsset(state.assets, key) : null;
}
