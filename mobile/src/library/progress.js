import { createProgressQueue } from '@trelliskit/shared';
import { fileStorage } from './storage';

/**
 * Lesson progress delivery (docs/PHASE4_CONTRACT.md section 5.5): reports go to
 * `PUT /lessons/{id}/progress`; a report that cannot be sent (offline, a network error, `429`, or
 * a `5xx`) is queued on the device with its original `clientUpdatedUtc` and replayed on
 * reconnect and when the app returns to the foreground. The server ignores out-of-order resume
 * points and merges the rest monotonically, so replay order is safe. Entries older than 7 days
 * are dropped by the shared queue.
 */

let queue = null;

/** The device's progress queue. */
export function progressQueue() {
  if (!queue) queue = createProgressQueue({ storage: fileStorage, key: 'progress-queue' });
  return queue;
}

/** Replace the queue (tests). */
export function setProgressQueue(next) {
  queue = next;
}

function retryable(error) {
  const status = error && typeof error.status === 'number' ? error.status : 0;
  return status === 0 || status === 429 || status >= 500;
}

/**
 * Send one report, queueing it when it cannot be delivered now.
 * @param {object} api
 * @param {string} lessonId
 * @param {object} body `{ positionSeconds, playedSeconds, clientUpdatedUtc, sessionId }`
 * @param {{ offline?: boolean }} [options] Skip the request while the device is known to be offline.
 * @returns {Promise<{ progress?: object, queued?: boolean }>}
 */
export async function sendProgress(api, lessonId, body, { offline = false } = {}) {
  if (offline) {
    await progressQueue().enqueue(lessonId, body);
    return { queued: true };
  }
  try {
    const progress = await api.putLessonProgress(lessonId, body);
    return { progress };
  } catch (error) {
    if (retryable(error)) {
      await progressQueue().enqueue(lessonId, body);
      return { queued: true };
    }
    return {};
  }
}

/** Replay queued reports (reconnect, foreground). Resolves `{ sent, dropped, remaining }`. */
export function replayProgress(api) {
  return progressQueue().replay((lessonId, body) => api.putLessonProgress(lessonId, body));
}

/** Drop every queued report (sign-out). */
export function clearProgressQueue() {
  return progressQueue().clear();
}
