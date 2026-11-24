import { reactionList } from './property.js';

/**
 * Reaction helpers (docs/PHASE2_CONTRACT.md section 6). The set comes from the property's
 * `reactions` (or `GET /api/v1/reactions/set`), never from engine constants. Reactions are
 * addressed by key; the first key is the one-tap "like".
 */

/**
 * The reaction set in display order with the one-tap key.
 * @param {object} property
 * @returns {{ items: Array<{ key: string, emoji: string, label: string }>, likeKey: string|null }}
 */
export function reactionSet(property) {
  const items = reactionList(property).filter((r) => r && typeof r.key === 'string');
  return { items, likeKey: items.length ? items[0].key : null };
}

/**
 * Normalize a `GET /reactions/set` response (falls back to the property when it is unusable).
 * @param {object} response
 * @param {object} [property]
 */
export function normalizeReactionSet(response, property) {
  const items = response && Array.isArray(response.items) ? response.items.filter((r) => r && typeof r.key === 'string') : null;
  if (!items || !items.length) return reactionSet(property);
  return { items, likeKey: (response && response.likeKey) || items[0].key };
}

/**
 * Counts to show, in the set's order: only keys in the set with a count above zero.
 * @param {{ items: Array<{ key: string, emoji: string, label: string }> }} set
 * @param {Record<string, number>} counts
 * @param {string[]} [mine]
 * @returns {Array<{ key: string, emoji: string, label: string, count: number, mine: boolean }>}
 */
export function visibleReactions(set, counts, mine = []) {
  const values = counts || {};
  const my = new Set(mine || []);
  return (set && set.items ? set.items : [])
    .filter((r) => Number(values[r.key]) > 0)
    .map((r) => ({ ...r, count: Number(values[r.key]), mine: my.has(r.key) }));
}

/**
 * Optimistically apply adding (`on` true) or removing a key to a reactable's reaction fields.
 * Returns a new `{ reactionCounts, reactionTotal, myReactions }`; unchanged when already in that
 * state (the server routes are idempotent too).
 * @param {{ reactionCounts?: Record<string, number>, reactionTotal?: number, myReactions?: string[] }} state
 * @param {string} key
 * @param {boolean} on
 */
export function applyReaction(state, key, on) {
  const counts = { ...((state && state.reactionCounts) || {}) };
  const mine = ((state && state.myReactions) || []).slice();
  let total = Number((state && state.reactionTotal) || 0);
  const has = mine.includes(key);
  if (on && !has) {
    counts[key] = (Number(counts[key]) || 0) + 1;
    mine.push(key);
    total += 1;
  } else if (!on && has) {
    const next = (Number(counts[key]) || 0) - 1;
    if (next > 0) counts[key] = next;
    else delete counts[key];
    mine.splice(mine.indexOf(key), 1);
    total = Math.max(0, total - 1);
  }
  return { reactionCounts: counts, reactionTotal: total, myReactions: mine };
}

/**
 * Pick the reaction fields from a payload (FeedPost, Comment, or a reaction route response).
 * @param {object} source
 */
export function reactionFields(source) {
  return {
    reactionCounts: (source && source.reactionCounts) || {},
    reactionTotal: Number((source && source.reactionTotal) || 0),
    myReactions: (source && source.myReactions) || []
  };
}

/**
 * Cache the reaction set for the session: `get()` calls `load()` once and shares the result;
 * a failed load is retried on the next call.
 * @param {() => Promise<object>} load
 */
export function createReactionSetCache(load) {
  let pending = null;
  return {
    get() {
      if (!pending) {
        pending = Promise.resolve().then(load).catch((err) => {
          pending = null;
          throw err;
        });
      }
      return pending;
    },
    clear() {
      pending = null;
    }
  };
}
