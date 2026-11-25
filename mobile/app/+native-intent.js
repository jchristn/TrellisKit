import { resolveAppLink } from '../src/linkTargets';
import { getBundledProperty } from '../src/property/property';

/**
 * Incoming system links (expo-router native intent). Universal links and Android app links on the
 * property's website domain arrive as full https URLs; the association files allow only the
 * check-in path (`/app/check-in`), so a scanned check-in QR code opens `/check-in` in the app.
 * Website links resolve through `resolveAppLink` like links tapped inside the app; anything else
 * (the app's own scheme, unknown pages) passes through unchanged.
 * @param {{ path: string, initial: boolean }} options
 * @returns {string}
 */
export function redirectSystemPath({ path }) {
  try {
    if (typeof path !== 'string' || !/^https:\/\//i.test(path)) return path;
    const target = resolveAppLink(path, getBundledProperty());
    return target && target.kind === 'native' ? target.path : path;
  } catch {
    return path;
  }
}
