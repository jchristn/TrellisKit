/**
 * The reviewed exercise and technique catalog is opt-in: it is on only when the property sets
 * `features.catalog` to `true`. A missing switch counts as off, unlike most `features` switches.
 * @param {object} property The property configuration.
 * @returns {boolean}
 */
export function catalogFeatureEnabled(property) {
  return Boolean(property && property.features && property.features.catalog === true);
}
