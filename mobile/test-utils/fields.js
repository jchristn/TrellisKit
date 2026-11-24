/**
 * Test-local profile field definitions in the GET /api/v1/property `profileFields` shape (with the
 * four built-ins), covering every field type, a staff-set field, a retired option, and an
 * unknown type that clients must skip. Neutral names only.
 */
import fs from 'fs';
import path from 'path';

const FIXTURES = path.resolve(__dirname, 'fixtures');
export const customProperty = JSON.parse(fs.readFileSync(path.join(FIXTURES, 'custom/property.json'), 'utf8'));
export const plainProperty = JSON.parse(fs.readFileSync(path.join(FIXTURES, 'plain/property.json'), 'utf8'));

const validation = (extra = {}) => ({
  required: false, minLength: null, maxLength: null, minValue: null, maxValue: null, integerOnly: false, pattern: null, patternMessage: null,
  minEntries: null, maxEntries: null, minDate: null, maxDate: null, notInFuture: false, ...extra
});

function def(key, type, order, extra = {}) {
  const required = Boolean(extra.validation && extra.validation.required);
  return {
    key, builtIn: false, label: key[0].toUpperCase() + key.slice(1), helpText: '', placeholder: '', type, options: [], rank: null, subFields: [],
    required, collectAt: 'Profile', editableBy: 'Member', visibility: 'Public', showOnProfile: true, profileOrder: order, showOnCard: false, searchable: false, filterable: false,
    ...extra, validation: validation(extra.validation)
  };
}

const opt = (key, label, extra = {}) => ({ id: 'pfo_' + key, key, label, color: null, maxLevel: null, freeText: false, retired: false, ...extra });

export const builtIns = [
  def('displayName', 'Text', 0, { builtIn: true, label: 'Full name', helpText: 'As other members see it', collectAt: 'SignUp', showOnCard: true, validation: { required: true, maxLength: 50 } }),
  def('handle', 'Text', 1, { builtIn: true, label: 'Username', collectAt: 'SignUp', showOnCard: true, validation: { required: true, maxLength: 30 } }),
  def('avatar', 'Image', 2, { builtIn: true, label: 'Picture', showOnCard: true }),
  def('bio', 'LongText', 5, { builtIn: true, label: 'About me', validation: { maxLength: 500 } })
];

export const level = def('level', 'Rank', 10, {
  label: 'Level',
  options: [opt('Green', 'Green', { color: '#2E7D32' }), opt('Gold', 'Gold', { color: '#B8860B' }), opt('Old', 'Old', { color: '#999999', retired: true })],
  rank: { levelLabel: 'Pips', maxLevel: 2 },
  collectAt: 'SignUp', showOnCard: true, filterable: true,
  validation: { required: true }
});
export const team = def('team', 'Text', 20, { label: 'Team', helpText: 'Where you meet', placeholder: 'Your team', collectAt: 'SignUp', filterable: true, showOnCard: true, validation: { maxLength: 20 } });
export const motto = def('motto', 'LongText', 25, { label: 'Motto' });
export const years = def('years', 'Number', 30, { label: 'Years', filterable: true, validation: { integerOnly: true, minValue: 0, maxValue: 80 } });
export const since = def('since', 'Date', 35, { label: 'Since', filterable: true, validation: { notInFuture: true } });
export const mentor = def('mentor', 'Boolean', 40, { label: 'Mentor', filterable: true });
export const site = def('site', 'Url', 45, { label: 'Site' });
export const contact = def('contact', 'Email', 46, { label: 'Contact' });
export const phone = def('phone', 'Phone', 47, { label: 'Phone' });
export const country = def('country', 'Country', 50, { label: 'Country', filterable: true });
export const style = def('style', 'SingleSelect', 55, { label: 'Style', filterable: true, options: [opt('Calm', 'Calm'), opt('Loud', 'Loud'), opt('Other', 'Other', { freeText: true })] });
export const flavor = def('flavor', 'SingleSelect', 56, { label: 'Flavor', options: ['One', 'Two', 'Three', 'Four', 'Five', 'Six', 'Seven'].map((k) => opt(k, k)) });
export const focus = def('focus', 'MultiSelect', 60, { label: 'Focus', filterable: true, options: [opt('Reading', 'Reading'), opt('Writing', 'Writing'), opt('Else', 'Something else', { freeText: true })] });
export const badge = def('badge', 'Text', 65, { label: 'Badge', collectAt: 'AdminOnly', editableBy: 'Staff', visibility: 'Members' });
export const secret = def('secret', 'Text', 66, { label: 'Secret', showOnProfile: false });
export const milestones = def('milestones', 'History', 70, {
  label: 'Milestones',
  validation: { maxEntries: 2 },
  subFields: [
    { id: 'pfs_1', key: 'step', label: 'Level', type: 'RankValue', required: true, rankFieldKey: 'level', withLevel: false, options: [], position: 0 },
    { id: 'pfs_2', key: 'by', label: 'Granted by', type: 'Member', required: false, options: [], position: 1 },
    { id: 'pfs_3', key: 'when', label: 'Date', type: 'Date', required: false, notInFuture: true, options: [], position: 2 }
  ]
});
export const hologram = def('hologram', 'Hologram', 80, { label: 'Hologram' });

export const allFields = [...builtIns, level, team, motto, years, since, mentor, site, contact, phone, country, style, flavor, focus, badge, secret, milestones, hologram];

/** The custom fixture property with these definitions. */
export function propertyWithFields(fields = allFields, extra = {}) {
  return { ...customProperty, profileFields: fields, ...extra };
}

/** A complete `Me.profile` for these definitions (every key, null when unset). */
export function emptyProfile(overrides = {}) {
  const profile = {};
  for (const f of allFields) if (!f.builtIn && f.type !== 'Hologram') profile[f.key] = null;
  return { ...profile, ...overrides };
}
