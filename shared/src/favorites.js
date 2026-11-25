/**
 * Saves helpers shared by the website and the mobile app (REST_API.md, Saves). Nothing here names a
 * property. `createSaveStateBatcher` collects the saved-state questions of every save toggle on a
 * screen and asks the server in batches of at most 100 targets (the server bound), so a list of
 * cards costs one request instead of one per card.
 */

/** Target types a member can save. */
export const FavoriteTargetType = Object.freeze({
  Lesson: 'Lesson',
  Series: 'Series',
  CatalogEntry: 'CatalogEntry',
  Venue: 'Venue',
  Instructor: 'Instructor',
  Event: 'Event',
  Post: 'Post',
  Thread: 'Thread'
});

/** Every target type, in the server's order. */
export const FavoriteTargetTypes = Object.freeze(Object.values(FavoriteTargetType));

/** Most targets in one `POST /me/saves/state` request. */
export const SAVE_STATE_MAX_TARGETS = 100;

/** Longest note (characters). */
export const SAVE_NOTE_MAX_LENGTH = 500;

/** Longest collection name (characters). */
export const SAVE_COLLECTION_NAME_MAX_LENGTH = 60;

/** Most collections one save request may name. */
export const SAVE_MAX_COLLECTIONS_PER_REQUEST = 20;

/** The map key of a target. */
export function saveKey(targetType, targetId) {
  return String(targetType) + ':' + String(targetId);
}

/** Whether a value names a saveable target type (exact casing). */
export function isFavoriteTargetType(value) {
  return FavoriteTargetTypes.includes(value);
}

/**
 * Whether a saved item's target can be opened now. Unavailable targets carry no title or link,
 * so lists show a neutral "no longer available" row instead.
 */
export function saveAvailable(save) {
  return Boolean(save && save.target && save.target.available);
}

/**
 * Batched saved-state lookups. `get(targetType, targetId)` returns a promise of
 * `{ saved, favoriteId }`; lookups made in the same tick (or within `delayMs`) share requests of
 * at most 100 targets. Results are cached until `set` or `invalidate` changes them, so an
 * optimistic toggle can update the cache before the server answers.
 *
 * @param {{ saveState: (targets: Array<{targetType: string, targetId: string}>) => Promise<{ items: Array<{targetType: string, targetId: string, saved: boolean, favoriteId: string|null}> }> }} api
 * @param {{ delayMs?: number, maxBatch?: number }} [options]
 */
export function createSaveStateBatcher(api, options = {}) {
  if (!api || typeof api.saveState !== 'function') throw new TypeError('createSaveStateBatcher needs an API client with saveState.');
  const delayMs = Math.max(0, Number(options.delayMs ?? 0));
  const maxBatch = Math.min(SAVE_STATE_MAX_TARGETS, Math.max(1, Number(options.maxBatch ?? SAVE_STATE_MAX_TARGETS)));
  const cache = new Map();
  const pending = new Map();
  let timer = null;

  async function send(entries) {
    try {
      const result = await api.saveState(entries.map((e) => ({ targetType: e.targetType, targetId: e.targetId })));
      const found = new Map();
      for (const item of (result && result.items) || []) found.set(saveKey(item.targetType, item.targetId), item);
      for (const entry of entries) {
        const item = found.get(entry.key);
        const state = { saved: Boolean(item && item.saved), favoriteId: (item && item.favoriteId) || null };
        cache.set(entry.key, state);
        for (const waiter of entry.waiters) waiter.resolve(state);
      }
    } catch (error) {
      for (const entry of entries) for (const waiter of entry.waiters) waiter.reject(error);
    }
  }

  function flush() {
    timer = null;
    const entries = [...pending.values()];
    pending.clear();
    const sends = [];
    for (let i = 0; i < entries.length; i += maxBatch) sends.push(send(entries.slice(i, i + maxBatch)));
    return Promise.all(sends);
  }

  function schedule() {
    if (timer !== null) return;
    timer = delayMs > 0 ? setTimeout(flush, delayMs) : setTimeout(flush, 0);
  }

  return {
    /** The saved state of a target, batched with other lookups. */
    get(targetType, targetId) {
      const key = saveKey(targetType, targetId);
      if (cache.has(key)) return Promise.resolve(cache.get(key));
      return new Promise((resolve, reject) => {
        let entry = pending.get(key);
        if (!entry) {
          entry = { key, targetType, targetId, waiters: [] };
          pending.set(key, entry);
        }
        entry.waiters.push({ resolve, reject });
        schedule();
      });
    },
    /** The cached state of a target, or undefined when it was never fetched. */
    peek(targetType, targetId) { return cache.get(saveKey(targetType, targetId)); },
    /** Record a known state (after a save or unsave, or optimistically before one). */
    set(targetType, targetId, state) {
      cache.set(saveKey(targetType, targetId), { saved: Boolean(state && state.saved), favoriteId: (state && state.favoriteId) || null });
    },
    /** Forget one target, or every target when called without arguments. */
    invalidate(targetType, targetId) {
      if (targetType === undefined) cache.clear();
      else cache.delete(saveKey(targetType, targetId));
    },
    /** Send every queued lookup now. */
    flush() {
      if (timer !== null) clearTimeout(timer);
      return flush();
    }
  };
}
