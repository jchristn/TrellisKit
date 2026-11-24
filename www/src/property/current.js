import { fillTemplate } from '@trelliskit/shared';
import { BUILD_SITE } from './buildSite.js';

let current = BUILD_SITE.property;

/** The property in effect (the runtime copy once loaded, else the build-time copy). */
export function currentProperty() {
  return current;
}

/** Set by PropertyProvider whenever its property changes. */
export function setCurrentProperty(property) {
  if (property) current = property;
}

/**
 * Fill `{name}`, `{price}`, `{minimumAge}`, and the other property tokens in an engine string
 * (see `propertyTokens` in @trelliskit/shared). For non-React helpers; components use
 * `useProperty().fill`.
 * @param {string} template
 * @param {Record<string, string|number>} [extra]
 * @param {object} [property]
 */
export function fill(template, extra = {}, property = current) {
  return fillTemplate(template, property, extra);
}
