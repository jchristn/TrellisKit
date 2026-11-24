import { buildProfileFilters, findProfileField, profileFilterChips, removeProfileFilter } from '@trelliskit/shared';
import { emptyFilters } from '../content/enumeration';
import strings from '../strings';

/**
 * Member directory filters (docs/MULTIPROPERTY_CONTRACT.md section 9.12): the list filter state
 * (search, ordering, labels, tags) plus `profile`, a `{ <key>: { <operator>: value } }` state
 * for the property's filterable fields.
 */

/** Directory orderings (REST_API.md "Member filters"); the first is the server default. */
export const DirectoryOrderings = Object.freeze(['CreatedDescending', 'CreatedAscending', 'DisplayNameAscending']);

/** Directory page size. */
export const DirectoryPageSize = 25;

/** An empty directory filter state. */
export function emptyDirectoryFilters() {
  return { ...emptyFilters(), profile: {} };
}

/** The `filters` of the EnumerationQuery: `{ profile }` with empty operators dropped, or undefined. */
export function directoryCollectionFilters(filters, fields) {
  const profile = buildProfileFilters((filters && filters.profile) || {}, fields);
  return Object.keys(profile).length ? { profile } : undefined;
}

function chipText(chip, field) {
  const c = strings.directory.chips;
  switch (chip.op) {
    case 'hasValue':
      return c.hasValue(chip.label, chip.value === 'true');
    case 'eq':
      if (field && field.type === 'Boolean') return c.eq(chip.label, chip.value === 'true' ? strings.directory.yes : strings.directory.no);
      return c.eq(chip.label, chip.value);
    case 'minLevel':
      return c.minLevel((field && field.rank && field.rank.levelLabel) || chip.label, chip.value);
    default:
      return (c[chip.op] || c.eq)(chip.label, chip.value);
  }
}

/**
 * Removable chips for the active profile filters, in the `ActiveFilters` extra chip shape:
 * `[{ key, text, remove }]` where `remove` is the filter state without that operator.
 */
export function directoryChips(filters, fields) {
  const active = buildProfileFilters((filters && filters.profile) || {}, fields);
  return profileFilterChips(active, fields).map((chip) => ({
    key: 'profile-' + chip.key + '-' + chip.op,
    text: chipText(chip, findProfileField(fields, chip.key)),
    remove: { ...filters, profile: removeProfileFilter(active, chip.key, chip.op) }
  }));
}
