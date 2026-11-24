/**
 * Reject with a TimeoutError when a promise does not settle in time. Used around store calls
 * (fetchProducts, getAvailablePurchases), which have no timeout of their own.
 * @template T
 * @param {Promise<T>} promise
 * @param {number} ms
 * @returns {Promise<T>}
 */
export function withTimeout(promise, ms) {
  let timer;
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => {
      const error = new Error('The operation timed out.');
      error.name = 'TimeoutError';
      error.code = 'Timeout';
      error.status = 0;
      reject(error);
    }, ms);
  });
  return Promise.race([Promise.resolve(promise), timeout]).finally(() => clearTimeout(timer));
}
