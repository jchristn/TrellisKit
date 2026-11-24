/**
 * The property the tests run against: the bundled copy from app.config.js for the active
 * property (TK_PROPERTY_DIR, default the neutral sample). Tests derive expected text from it so
 * the suite passes for any property; property-specific behavior is covered with test-local
 * fixtures (see __tests__/fixtures).
 */
import { customFields, formatRankValue, pickerOptions, profileFieldsOf, propertyTokens, rankMaxLevel } from '@trelliskit/shared';
import { getBundledProperty } from '../src/property/property';

export const property = getBundledProperty();

/** Token values ({name}, {minimumAge}, ...) for the active property. */
export const tokens = propertyTokens(property);

/** Profile field definitions of the active property (bundled copy), in profile order. */
export const profileFields = profileFieldsOf(property);

/** The first `Rank` field of the active property, or null. */
export const rankField = profileFields.find((f) => f.type === 'Rank' && !f.builtIn) || null;

/** Rank option keys of that field, in order (empty without a rank field). */
export const rankKeys = rankField ? pickerOptions(rankField).map((o) => o.key) : [];

/** A rank level the active property allows (2, or its maximum when lower). */
export const sampleLevel = rankField && rankKeys.length ? Math.min(2, rankMaxLevel(rankField, rankKeys[1] || rankKeys[0])) : 0;

/** The default test member's rank value, or null. */
export const sampleRank = rankKeys.length ? { key: rankKeys[1] || rankKeys[0], level: sampleLevel } : null;

/** Rank text for the default test member. */
export function expectedRankLine(value = sampleRank) {
  return rankField && value ? formatRankValue(rankField, value) : '';
}

/** A `Me.profile` map: every configurable field key (null when unset), with the sample rank. */
export function defaultProfile(overrides = {}) {
  const profile = {};
  for (const field of customFields(profileFields)) profile[field.key] = null;
  if (rankField && sampleRank) profile[rankField.key] = sampleRank;
  return { ...profile, ...overrides };
}

/**
 * A GET /api/v1/property response for the active property with extra `features` (for example the
 * server capabilities `{ forums: true, search: true }`).
 */
export function propertyWithFeatures(features) {
  return { ...property, features: { ...(property.features || {}), ...features } };
}
