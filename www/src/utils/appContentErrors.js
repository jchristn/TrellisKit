import { featureDisabledOf } from '@trelliskit/shared';

/**
 * Bridge between the member ApiClient's central error handling (AuthContext) and the app content
 * store (AppContentContext): a `503 FeatureDisabled` from any API route marks that feature's kill
 * switch on at once (docs/LIVE_UPDATES_CONTRACT.md section 11.3). Kept in its own module so the
 * two contexts do not import each other.
 */

let activeStore = null;

/** Register the store that receives FeatureDisabled answers (null to clear). Returns the unregister function. */
export function setFeatureDisabledStore(store) {
  activeStore = store || null;
  return () => {
    if (activeStore === store) activeStore = null;
  };
}

/**
 * Note a failed API call: when it is a `503 FeatureDisabled`, mark the feature killed in the store.
 * Never throws.
 * @param {any} error
 * @returns {boolean} Whether the error was a FeatureDisabled answer.
 */
export function noteApiFailure(error) {
  const disabled = featureDisabledOf(error);
  if (!disabled) return false;
  try {
    if (activeStore && disabled.feature) activeStore.noteFeatureDisabled(disabled.feature, disabled.message);
  } catch {
    // The store never blocks the caller's error handling.
  }
  return true;
}
