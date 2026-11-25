import { propertyFeatureEnabled } from './access.js';

/**
 * The reviewed exercise and technique catalog is opt-in: it is on only when the property sets
 * `features.catalog` to `true`. A missing switch counts as off, unlike most `features` switches
 * (`OffByDefaultFeatures` in `access.js`).
 * @param {object} property The property configuration.
 * @returns {boolean}
 */
export function catalogFeatureEnabled(property) {
  return propertyFeatureEnabled(property, 'catalog');
}
