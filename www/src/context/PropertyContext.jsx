import { createContext, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { fillTemplate, mergeProperty, withPalettePreset } from '@trelliskit/shared';
import { BUILD_SITE } from '../property/buildSite.js';
import { setCurrentProperty } from '../property/current.js';
import { usePalettePreference } from '../property/palettePreference.js';
import { applyRuntimePalette } from '../property/runtimeTheme.js';

function makeValue(site, runtime, setRuntime, runtimeFailed = false, setRuntimeFailed = () => {}) {
  const property = runtime ? mergeProperty(site.property, runtime) : site.property;
  const merged = property === site.property ? site : { ...site, property };
  return {
    site: merged,
    property,
    runtimeLoaded: Boolean(runtime),
    // True once the runtime property loaded or failed to load (the build-time copy then stands).
    runtimeSettled: Boolean(runtime) || runtimeFailed,
    setRuntimeProperty: setRuntime,
    setRuntimeFailed,
    fill: (template, extra) => fillTemplate(template, property, extra)
  };
}

// Without a provider the runtime property can never load (`canLoadRuntime` false): the build-time copy is final.
const DEFAULT_VALUE = { ...makeValue(BUILD_SITE, null, () => {}), canLoadRuntime: false };
const PropertyContext = createContext(DEFAULT_VALUE);

/**
 * Provides the active property. `site` defaults to the build-time bundle (tests pass fixtures).
 * The member app, the admin console, and the marketing layout call `useRuntimeProperty` to
 * replace `property` with the server's `GET /api/v1/property` response. Marketing pages are
 * prerendered from the build-time copy and switch to the live one after hydration; the live brand
 * palette is applied to the page's CSS custom properties then (property/runtimeTheme.js), replaced by
 * the signed-in member's palette preset while the property allows it (property/palettePreference.js).
 *
 * @param {{ site?: object, children: React.ReactNode }} props
 */
export function PropertyProvider({ site = BUILD_SITE, children }) {
  const [runtime, setRuntime] = useState(null);
  const [runtimeFailed, setRuntimeFailed] = useState(false);
  const value = useMemo(() => ({ ...makeValue(site, runtime, setRuntime, runtimeFailed, setRuntimeFailed), canLoadRuntime: true }), [site, runtime, runtimeFailed]);
  // Non-React helpers (error messages, validators) read the same property.
  setCurrentProperty(value.property);
  // The live brand palette replaces the build-time theme colors as soon as GET /property loads (no rebuild).
  const runtimeBrand = runtime ? value.property.brand : null;
  const palettePreset = usePalettePreference();
  useEffect(() => {
    if (runtimeBrand) applyRuntimePalette(withPalettePreset({ brand: runtimeBrand }, palettePreset));
  }, [runtimeBrand, palettePreset]);
  return <PropertyContext.Provider value={value}>{children}</PropertyContext.Provider>;
}

/** The active property: `{ site, property, fill, runtimeLoaded }`. Works without a provider. */
export function useProperty() {
  return useContext(PropertyContext);
}

/** A tab refetches the property document on focus at most this often (milliseconds). */
export const PROPERTY_FOCUS_REFETCH_MS = 60 * 1000;

// One clock per tab (shared by every hook instance), so two mounted layouts never double the requests.
const tabFetch = { last: null };

/** Test hook: forget the last property request of this tab. */
export function resetPropertyRefetchClock() {
  tabFetch.last = null;
}

/**
 * Whether a tab that regained focus should refetch `GET /api/v1/property`: at most once per
 * `minIntervalMs` since the last request (load or focus).
 * @param {number|null} lastFetchMs Time of the last request, or null.
 * @param {number} nowMs
 * @param {number} [minIntervalMs]
 */
export function shouldRefetchProperty(lastFetchMs, nowMs, minIntervalMs = PROPERTY_FOCUS_REFETCH_MS) {
  if (lastFetchMs === null || lastFetchMs === undefined) return true;
  return nowMs - lastFetchMs >= minIntervalMs;
}

/**
 * Load `GET /api/v1/property` and make it the active property. Failures keep the build-time
 * copy (for example when the server is unreachable or predates the route). The request runs on
 * load and again when the tab regains focus or becomes visible (at most once a minute per tab), so
 * an access-model, palette, or switch change shows in open tabs without a manual reload.
 * @param {{ property: (signal?: AbortSignal) => Promise<object> }} client a shared ApiClient
 * @param {{ now?: () => number, minIntervalMs?: number }} [options]
 */
export function useRuntimeProperty(client, { now = Date.now, minIntervalMs = PROPERTY_FOCUS_REFETCH_MS } = {}) {
  const { runtimeLoaded, runtimeSettled, setRuntimeProperty, setRuntimeFailed } = useContext(PropertyContext);
  const lastBody = useRef(null);
  useEffect(() => {
    if (runtimeSettled || !client || typeof client.property !== 'function') return undefined;
    const controller = new AbortController();
    tabFetch.last = now();
    client.property(controller.signal).then(
      (body) => {
        if (controller.signal.aborted) return;
        if (body && typeof body === 'object') {
          lastBody.current = JSON.stringify(body);
          setRuntimeProperty(body);
        } else setRuntimeFailed(true);
      },
      () => { if (!controller.signal.aborted) setRuntimeFailed(true); }
    );
    return () => controller.abort();
  }, [client, runtimeLoaded, runtimeSettled, setRuntimeProperty, setRuntimeFailed, now]);

  useEffect(() => {
    if (!client || typeof client.property !== 'function' || typeof window === 'undefined') return undefined;
    let controller = null;
    const refetch = () => {
      if (typeof document !== 'undefined' && document.visibilityState === 'hidden') return;
      if (!shouldRefetchProperty(tabFetch.last, now(), minIntervalMs)) return;
      tabFetch.last = now();
      if (controller) controller.abort();
      const current = new AbortController();
      controller = current;
      client.property(current.signal).then(
        (body) => {
          if (current.signal.aborted || !body || typeof body !== 'object') return;
          const text = JSON.stringify(body);
          // An unchanged document (for example a 304 revalidation) does not re-render the app.
          if (text === lastBody.current) return;
          lastBody.current = text;
          setRuntimeProperty(body);
        },
        () => {}
      );
    };
    window.addEventListener('focus', refetch);
    document.addEventListener('visibilitychange', refetch);
    return () => {
      window.removeEventListener('focus', refetch);
      document.removeEventListener('visibilitychange', refetch);
      if (controller) controller.abort();
    };
  }, [client, setRuntimeProperty, now, minIntervalMs]);
}
