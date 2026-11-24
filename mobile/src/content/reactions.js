import { reactionList } from '@trelliskit/shared';

/**
 * Reaction helpers (docs/PHASE2_CONTRACT.md section 6). The set, its order, glyphs, and labels
 * come from the property (`reactions` in property.json, merged from GET /api/v1/property), never
 * from engine constants.
 */

/**
 * The property's reaction set in display order, plus the one-tap like key (the first entry).
 * @param {object} property
 * @returns {{ items: { key: string, emoji: string, label: string }[], likeKey: string|null }}
 */
export function reactionSet(property) {
  const items = reactionList(property).filter((r) => r && typeof r.key === 'string');
  return { items, likeKey: items.length ? items[0].key : null };
}

/** Reaction fields of a payload, normalized. */
export function reactionFields(target) {
  return {
    reactionCounts: (target && target.reactionCounts) || {},
    reactionTotal: (target && target.reactionTotal) || 0,
    myReactions: (target && Array.isArray(target.myReactions) && target.myReactions) || []
  };
}

/**
 * Apply one reaction change locally (the optimistic update): adds or removes `key` from
 * myReactions and adjusts its count and the total. A no-op change returns the same values.
 * @param {{ reactionCounts: object, reactionTotal: number, myReactions: string[] }} fields
 * @param {string} key
 * @param {boolean} on
 */
export function applyReaction(fields, key, on) {
  const current = reactionFields(fields);
  const has = current.myReactions.includes(key);
  if (has === on) return current;
  const counts = { ...current.reactionCounts };
  const delta = on ? 1 : -1;
  const nextCount = Math.max(0, (counts[key] || 0) + delta);
  if (nextCount > 0) counts[key] = nextCount;
  else delete counts[key];
  return {
    reactionCounts: counts,
    reactionTotal: Math.max(0, current.reactionTotal + delta),
    myReactions: on ? current.myReactions.concat(key) : current.myReactions.filter((k) => k !== key)
  };
}

/**
 * Chips to show under a post or comment: keys with a count above zero, in the property's order.
 * Keys no longer in the property are hidden (contract 6.1).
 */
export function visibleReactions(fields, set) {
  const current = reactionFields(fields);
  return set.items
    .filter((r) => (current.reactionCounts[r.key] || 0) > 0)
    .map((r) => ({ ...r, count: current.reactionCounts[r.key], mine: current.myReactions.includes(r.key) }));
}
