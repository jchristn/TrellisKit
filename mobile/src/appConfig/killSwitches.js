/**
 * Engine feature keys a screen gate checks (docs/LIVE_UPDATES_CONTRACT.md section 13.1), by the access feature or
 * catalog the gate guards. The member directory gate (`members`, and `directory` as an access feature) is the people
 * directory, so only a catalog gate names the venue directory. Mirrors the website's RequireMember mapping.
 */
export const KillableFeatures = Object.freeze(['feed', 'forums', 'search', 'messaging', 'assistants']);

/** The catalogs whose kill switch has the catalog's own key. */
export const KillableCatalogs = Object.freeze(['library', 'events', 'directory']);

/**
 * The engine kill switch key that guards a gate, or null when none does.
 * @param {{ feature?: string|null, catalog?: string|null }} gate
 * @returns {string|null}
 */
export function killSwitchKeyFor({ feature = null, catalog = null } = {}) {
  if (catalog) return KillableCatalogs.includes(catalog) ? catalog : null;
  if (feature && KillableFeatures.includes(feature)) return feature;
  return null;
}
