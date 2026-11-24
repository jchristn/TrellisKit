/**
 * Wrap an async function so concurrent callers share one in-flight call. When the call settles,
 * the next caller starts a new one. Used for token refresh: many requests can hit 401 at once,
 * but only one refresh request is sent and every caller awaits its result.
 *
 * @template T
 * @param {(...args: any[]) => Promise<T>} fn
 * @returns {(...args: any[]) => Promise<T>}
 */
export function singleFlight(fn) {
  let inFlight = null;
  return (...args) => {
    if (!inFlight) {
      inFlight = Promise.resolve()
        .then(() => fn(...args))
        .finally(() => {
          inFlight = null;
        });
    }
    return inFlight;
  };
}
