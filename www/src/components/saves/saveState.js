import { createSaveStateBatcher } from '@trelliskit/shared';

/**
 * One saved-state batcher per API client and member, shared by every Save toggle on the page, so
 * a screen of cards costs one `POST /me/saves/state` per 100 targets. A different member (sign out
 * and in again on the same client) gets a fresh cache.
 */
const batchers = new WeakMap();

/** Whether a client has the saves routes. */
export function hasSavesApi(client) {
  return Boolean(client && typeof client.saveState === 'function' && typeof client.saveItem === 'function' && typeof client.unsaveItem === 'function');
}

/**
 * The shared batcher for `client` and the member `userId`, or null when the client has no saves routes.
 * @param {object} client
 * @param {string|null} userId
 */
export function saveStateFor(client, userId) {
  if (!hasSavesApi(client)) return null;
  const hit = batchers.get(client);
  if (hit && hit.userId === userId) return hit.batcher;
  const batcher = createSaveStateBatcher(client);
  batchers.set(client, { userId, batcher });
  return batcher;
}

/** Forget every cached saved state of `client` (tests, or after bulk changes such as Saved removals). */
export function resetSaveState(client) {
  if (client) batchers.delete(client);
}
