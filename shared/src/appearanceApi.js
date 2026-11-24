/**
 * Member appearance settings (docs/TRELLISKIT.md section 2.1), mixed into the website's ApiClient
 * and the mobile app's MobileApiClient (each provides `get` and `put`).
 */
export const appearanceMethods = {
  /**
   * GET /me/appearance: `{ palettePreset, effectivePreset, presetsAvailable }` (signed in; the
   * allowed presets and their colors are `GET /property` `brand.presets`).
   * @param {AbortSignal} [signal]
   */
  getAppearance(signal) { return this.get('/me/appearance', { signal }); },

  /**
   * PUT /me/appearance `{ palettePreset }`: an allowed preset id, or null for the property palette.
   * @param {string|null} palettePreset
   */
  updateAppearance(palettePreset) { return this.put('/me/appearance', { palettePreset: palettePreset || null }); }
};
