import { describe, expect, it } from 'vitest';
import { directorySettingsBody, directorySettingsForm } from '../src/utils/directorySettings.js';

describe('directory settings catalog form', () => {
  it('maps config and catalog values and leaves stored secret values untouched on save', () => {
    const form = directorySettingsForm({ config: { venueLabel: 'Place', submissions: true }, settings: [
      { key: 'directory.geocoder.provider', value: 'AwsLocation' },
      { key: 'directory.geocoder.apiKey', value: null, isSecret: true, hasValue: true },
      { key: 'directory.map.provider', value: 'MapLibre' },
      { key: 'directory.map.styleUrl', value: 'https://example.test/map.json' }
    ] });
    expect(form.geocoder.provider).toBe('AwsLocation');
    expect(form.map.styleUrl).toBe('https://example.test/map.json');
    const body = directorySettingsBody(form);
    expect(body.venueLabel).toBe('Place');
    expect(body.values['directory.map.provider']).toBe('MapLibre');
    expect(body.values).not.toHaveProperty('directory.geocoder.apiKey');
    expect(body).not.toHaveProperty('map');
  });
});
