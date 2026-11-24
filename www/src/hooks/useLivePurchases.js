import { purchasesEnabled } from '@trelliskit/shared';
import { useProperty } from '../context/PropertyContext.jsx';

/**
 * Whether purchases (and so sign-up code editing) are enabled, from the live access settings.
 * `known` stays false until the runtime property has loaded or failed to load, so admin screens
 * render neither the create controls nor the read-only notice from the build-time copy (which
 * can disagree with the live access model, for example in Open).
 * @returns {{ known: boolean, enabled: boolean }}
 */
export function useLivePurchases() {
  const { property, runtimeSettled, canLoadRuntime } = useProperty();
  const known = !canLoadRuntime || Boolean(runtimeSettled);
  return { known, enabled: known && purchasesEnabled(property) };
}

export default useLivePurchases;
