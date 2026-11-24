import { formatRank, rankOptions } from '@trelliskit/shared';
import strings from '../strings.js';

const en = strings.enums;

/** Select options from a label map, in the map's order. */
export function optionsFrom(labels, keys = Object.keys(labels)) {
  return keys.map((value) => ({ value, label: labels[value] || value }));
}

/** Rank filter options from the property's profile configuration. */
export const rankFilterOptions = (property) => rankOptions(property).map(({ value, label }) => ({ value, label }));

/** Badge tone for a sign-up code status. */
export function codeStatusTone(status) {
  return { Active: 'success', Scheduled: 'info', Expired: 'neutral', Exhausted: 'warning', Inactive: 'neutral' }[status] || 'neutral';
}

/** Badge tone for a subscription status. */
export function subscriptionTone(status) {
  return { Active: 'success', InGrace: 'warning', InBillingRetry: 'warning', Expired: 'neutral', Revoked: 'danger', Refunded: 'danger', Paused: 'info' }[status] || 'neutral';
}

/** Badge tone for a redemption status. */
export function redemptionTone(status) {
  return { Active: 'success', Expired: 'neutral', Revoked: 'danger' }[status] || 'neutral';
}

/** Short role text, for example "Forum moderator (Forum *)". */
export function roleText(role) {
  if (!role) return '';
  const name = en.role[role.role] || role.role;
  if (!role.resourceType || role.resourceType === 'Global') return name;
  return name + ' (' + (en.resourceType[role.resourceType] || role.resourceType) + ' ' + (role.resourceId || '*') + ')';
}

/** Membership summary for tables: "Paid subscription (App Store)". */
export function membershipText(membership) {
  if (!membership || !membership.isActive) return strings.membershipApp.inactive;
  const source = en.membershipSource[membership.source] || membership.source || strings.membershipApp.active;
  return membership.store ? source + ' (' + (en.store[membership.store] || membership.store) + ')' : source;
}

/** Rank text for tables from the property's configuration (see `formatRank` in @trelliskit/shared). */
export function rankText(property, rank, level) {
  return formatRank(property, rank, level);
}
