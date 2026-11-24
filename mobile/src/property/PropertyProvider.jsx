import { createContext, useContext, useEffect, useRef, useState } from 'react';
import { AppState } from 'react-native';
import { getApiClient } from '../api/client';
import { fileCache } from './cache';
import { getBundledProperty, resolveProperty } from './property';

const PropertyContext = createContext(null);

function revalidate(api, etag) {
  if (api && typeof api.propertyIfChanged === 'function') return api.propertyIfChanged(etag);
  return api.get('/property').then((property) => ({ notModified: false, property, etag: null }));
}

/**
 * Provides the property configuration.
 *
 * Starts with the bundled copy (so the first launch works offline), then the last runtime
 * property cached on the device, then revalidates GET /api/v1/property with its `ETag`
 * (`If-None-Match`) at launch and whenever the app returns to the foreground. A `304` keeps the
 * current copy without re-rendering; a `200` replaces it (merged over the bundled copy) and is
 * cached with its new `ETag`. A failed fetch keeps whatever copy is showing.
 * @param {object} props
 * @param {object} [props.api] MobileApiClient (defaults to the shared client).
 * @param {object} [props.property] Fixed property (tests); skips the cache and the fetch.
 * @param {{ load: () => Promise<{ property: object, etag: string|null }|null>, save: (property: object, etag: string|null) => Promise<any> }} [props.cache]
 */
export function PropertyProvider({ children, api: apiProp, property: fixed, cache = fileCache }) {
  const [property, setProperty] = useState(() => fixed || getBundledProperty());
  const etagRef = useRef(null);
  const runtimeRef = useRef(null);

  useEffect(() => {
    if (fixed) return undefined;
    let active = true;
    let inFlight = null;
    const api = apiProp || getApiClient();
    const apply = (runtime) => {
      setProperty((current) => {
        const next = resolveProperty(getBundledProperty(), runtime);
        return JSON.stringify(next) === JSON.stringify(current) ? current : next;
      });
    };

    const refresh = () => {
      if (inFlight) return inFlight;
      inFlight = revalidate(api, etagRef.current)
        .then((result) => {
          if (!active || !result || result.notModified || !result.property || typeof result.property !== 'object') return;
          runtimeRef.current = result.property;
          etagRef.current = result.etag || null;
          apply(result.property);
          Promise.resolve(cache && cache.save ? cache.save(result.property, etagRef.current) : null).catch(() => {});
        })
        .catch(() => {
          // Offline or not available yet: keep the copy that is showing.
        })
        .finally(() => {
          inFlight = null;
        });
      return inFlight;
    };

    Promise.resolve(cache && cache.load ? cache.load() : null)
      .catch(() => null)
      .then((cached) => {
        if (!active) return;
        if (cached && cached.property && !runtimeRef.current) {
          runtimeRef.current = cached.property;
          etagRef.current = cached.etag || null;
          apply(cached.property);
        }
        return refresh();
      });

    const sub = AppState.addEventListener('change', (state) => {
      if (state === 'active') refresh();
    });
    return () => {
      active = false;
      if (sub && typeof sub.remove === 'function') sub.remove();
    };
  }, [apiProp, fixed, cache]);

  return <PropertyContext.Provider value={property}>{children}</PropertyContext.Provider>;
}

/**
 * The current property configuration (the bundled copy outside a PropertyProvider).
 * @returns {object}
 */
export function useProperty() {
  return useContext(PropertyContext) || getBundledProperty();
}
