/** Map the settings catalog into the directory form without exposing or replacing stored secrets. */
export function directorySettingsForm(view) {
  const fields = new Map((view.settings || []).map((field) => [field.key, field.value]));
  return {
    ...view.config,
    geocoder: { provider: fields.get('directory.geocoder.provider') || 'None' },
    map: Object.fromEntries(['provider', 'styleUrl', 'darkStyleUrl', 'attribution'].map((key) => [key, fields.get('directory.map.' + key) || (key === 'provider' ? 'None' : '')]))
  };
}

/** Directory labels are config; map/geocoder changes use the property's settings values contract. */
export function directorySettingsBody(form) {
  const { geocoder, map, ...config } = form;
  const values = { 'directory.geocoder.provider': geocoder.provider };
  for (const key of ['provider', 'styleUrl', 'darkStyleUrl', 'attribution']) values['directory.map.' + key] = map[key];
  return { ...config, values };
}
