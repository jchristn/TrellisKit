import { createProgressQueue, createSaveStateBatcher, saveKey } from '@trelliskit/shared';
import { fileStorage } from '../library/storage';

/**
 * Save delivery for the save toggle (REST_API.md, Saves), following the lesson progress pattern
 * (`src/library/progress.js`): a save or unsave that cannot be sent (offline, a network error,
 * `429`, or a `5xx`) is queued on the device with the time it was made and replayed in order on
 * reconnect and when the app returns to the foreground, so the member's last choice wins. Other
 * errors (`403`, `404`, `409 LimitReached`) are the caller's to show; the toggle rolls back.
 * Queued entries older than 7 days are dropped by the shared queue. Saved-state questions from
 * every toggle on a screen share batched `POST /me/saves/state` requests.
 */

let queue = null;
let batchers = new WeakMap();

/** The device's save queue. */
export function saveQueue() {
  if (!queue) queue = createProgressQueue({ storage: fileStorage, key: 'saves-queue' });
  return queue;
}

/** Replace the queue (tests). */
export function setSaveQueue(next) {
  queue = next;
}

function retryable(error) {
  const status = error && typeof error.status === 'number' ? error.status : 0;
  return status === 0 || status === 429 || status >= 500;
}

function deliver(api, targetType, targetId, saved) {
  return saved ? api.saveItem(targetType, targetId, {}) : api.unsaveItem(targetType, targetId);
}

/**
 * The batched saved-state lookups of one API client (one per signed-in session).
 * @param {object} api
 */
export function saveStateBatcher(api) {
  let batcher = batchers.get(api);
  if (!batcher) {
    batcher = createSaveStateBatcher(api);
    batchers.set(api, batcher);
  }
  return batcher;
}

/** Forget every cached saved state (sign-out, account change, tests). */
export function resetSaveState() {
  batchers = new WeakMap();
}

/**
 * Save or unsave one target, queueing the change when it cannot be delivered now.
 * @param {object} api
 * @param {string} targetType
 * @param {string} targetId
 * @param {boolean} saved The new state.
 * @param {{ offline?: boolean, now?: () => number }} [options]
 * @returns {Promise<{ result?: any, queued?: boolean }>} Rejects for errors that a retry would not fix.
 */
export async function sendSave(api, targetType, targetId, saved, { offline = false, now = () => Date.now() } = {}) {
  const body = { targetType, targetId, saved: Boolean(saved), clientUpdatedUtc: new Date(now()).toISOString() };
  if (offline) {
    await saveQueue().enqueue(saveKey(targetType, targetId), body);
    return { queued: true };
  }
  try {
    return { result: await deliver(api, targetType, targetId, saved) };
  } catch (error) {
    if (!retryable(error)) throw error;
    await saveQueue().enqueue(saveKey(targetType, targetId), body);
    return { queued: true };
  }
}

/** Replay queued saves in order (reconnect, foreground). Resolves `{ sent, dropped, remaining }`. */
export function replaySaves(api) {
  return saveQueue().replay((key, body) => deliver(api, body.targetType, body.targetId, body.saved));
}

/** Drop every queued save (sign-out). */
export function clearSaveQueue() {
  return saveQueue().clear();
}
