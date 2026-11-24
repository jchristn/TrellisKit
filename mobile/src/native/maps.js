import { MapProvider } from '@trelliskit/shared';

/**
 * The native map modules, loaded lazily so a build (or a test) without them falls back to the
 * list view instead of crashing (docs/PHASE4_CONTRACT.md section 3.6): `@maplibre/maplibre-react-native`
 * for `MapLibre` and `react-native-maps` (Apple Maps on iOS, Google Maps on Android) for
 * `PlatformNative`. A module that is missing or not linked returns null.
 */

const cache = new Map();

function load(provider) {
  if (provider === MapProvider.MapLibre) return require('@maplibre/maplibre-react-native');
  if (provider === MapProvider.PlatformNative) return require('react-native-maps');
  return null;
}

/**
 * The map module for a provider, or null when the provider is `None` or the native module is
 * unavailable in this build.
 * @param {string} provider a MapProvider value
 */
export function mapModule(provider) {
  if (cache.has(provider)) return cache.get(provider);
  let mod = null;
  try {
    mod = load(provider);
  } catch {
    mod = null;
  }
  if (provider === MapProvider.MapLibre && !(mod && mod.Map && mod.Marker)) mod = null;
  if (provider === MapProvider.PlatformNative && !(mod && (mod.default || mod.MapView) && mod.Marker)) mod = null;
  cache.set(provider, mod);
  return mod;
}

/** Forget loaded modules (tests). */
export function resetMapModules() {
  cache.clear();
}

/** An approximate zoom level for a longitude span (react-native-maps regions have no zoom). */
export function zoomForLongitudeDelta(delta) {
  const d = Math.max(0.0001, Math.min(360, Math.abs(Number(delta) || 360)));
  return Math.max(0, Math.min(22, Math.round(Math.log2(360 / d))));
}

/** The longitude span for a zoom level (inverse of zoomForLongitudeDelta). */
export function longitudeDeltaForZoom(zoom) {
  const z = Math.max(0, Math.min(22, Number(zoom) || 0));
  return Math.min(360, 360 / 2 ** z);
}

/** A `{ south, west, north, east }` box from a react-native-maps region. */
export function regionToBbox(region) {
  if (!region) return null;
  const halfLat = region.latitudeDelta / 2;
  const halfLng = region.longitudeDelta / 2;
  return { south: Math.max(-90, region.latitude - halfLat), west: region.longitude - halfLng, north: Math.min(90, region.latitude + halfLat), east: region.longitude + halfLng };
}
