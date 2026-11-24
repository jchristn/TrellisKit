import { verifyPurchase } from './purchases';

/**
 * Delays between verify attempts when the server is unreachable or answers 5xx, 408, or 429.
 * Six attempts in total over roughly two minutes; after that the transaction stays with the
 * store and is picked up again on the next launch or the next store delivery.
 */
export const DefaultRetryDelaysMs = Object.freeze([2000, 5000, 15000, 30000, 60000]);

const defaultSleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Stable identity for a store transaction, used to deduplicate deliveries.
 * @param {object} purchase expo-iap Purchase.
 * @returns {string|null}
 */
export function transactionKey(purchase) {
  if (!purchase) return null;
  return purchase.transactionId || purchase.id || purchase.purchaseToken || purchase.purchaseTokenAndroid || purchase.jwsRepresentationIOS || null;
}

/**
 * Whether a verify failure is temporary: the network, a timeout, rate limiting, or a server error.
 * Client errors (400 InvalidTransaction, 409 TransactionBelongsToAnotherAccount, 401) are final.
 * @param {any} error
 */
export function isRetryableError(error) {
  if (!error || typeof error.status !== 'number') return false;
  const s = error.status;
  return s === 0 || s === 408 || s === 429 || s >= 500;
}

/**
 * Processes store transactions for the whole app: posts each one to the verify route once,
 * retries temporary failures with backoff, and calls finishTransaction only after the server
 * answered 200. A transaction that was verified but could not be finished is finished (without
 * verifying again) the next time it is delivered.
 *
 * Events passed to `emit`:
 * - `{ type: 'pending', purchase }` Ask to Buy or a pending payment; nothing is posted.
 * - `{ type: 'verifying', purchase }`
 * - `{ type: 'retrying', purchase, error, attempt, delayMs }`
 * - `{ type: 'activated', purchase, membership, session }`
 * - `{ type: 'failed', purchase, error }` Final failure; the transaction is left unfinished.
 *
 * @param {object} options
 * @param {object} options.api MobileApiClient.
 * @param {object} options.iap expo-iap module (connected).
 * @param {string} options.os 'ios' or 'android'.
 * @param {string|null} options.productId Membership product id.
 * @param {number[]} [options.retryDelaysMs]
 * @param {(ms: number) => Promise<void>} [options.sleep]
 * @param {(event: object) => void} [options.emit]
 * @param {(membership: object|null) => Promise<object|null>} [options.onVerified] Refreshes the session; its result is passed as `session`.
 */
export function createPurchaseProcessor({ api, iap, os, productId, retryDelaysMs = DefaultRetryDelaysMs, sleep = defaultSleep, emit = () => {}, onVerified = async () => null }) {
  const inFlight = new Map();
  const verified = new Map();
  const finished = new Set();
  let stopped = false;

  async function verifyWithRetry(purchase) {
    let attempt = 0;
    for (;;) {
      try {
        const result = await verifyPurchase(api, purchase, { os, productId });
        return { ok: true, membership: (result && result.membership) || null };
      } catch (error) {
        if (stopped) return { ok: false, error, stopped: true };
        if (!isRetryableError(error) || attempt >= retryDelaysMs.length) return { ok: false, error };
        const delayMs = retryDelaysMs[attempt];
        attempt += 1;
        emit({ type: 'retrying', purchase, error, attempt, delayMs });
        await sleep(delayMs);
        if (stopped) return { ok: false, error, stopped: true };
      }
    }
  }

  async function run(purchase, key, force) {
    let membership;
    if (!force && verified.has(key)) {
      membership = verified.get(key);
    } else {
      const outcome = await verifyWithRetry(purchase);
      if (!outcome.ok) {
        if (!outcome.stopped) emit({ type: 'failed', purchase, error: outcome.error });
        return outcome;
      }
      membership = outcome.membership;
      if (key) verified.set(key, membership);
    }
    try {
      await iap.finishTransaction({ purchase, isConsumable: false });
      if (key) {
        verified.delete(key);
        finished.add(key);
      }
    } catch {
      // The server already granted the membership. The store delivers the transaction again
      // (next launch or Transaction.updates), and then it is finished without a second verify.
    }
    let session = null;
    try {
      session = await onVerified(membership);
    } catch {
      session = null;
    }
    if (stopped) return { ok: true, membership, session };
    emit({ type: 'activated', purchase, membership, session });
    return { ok: true, membership, session };
  }

  /**
   * Verify and finish one transaction. Concurrent calls for the same transaction share one run;
   * a transaction finished earlier in this launch is skipped unless `force` (Restore purchases).
   * @param {object} purchase
   * @param {{ force?: boolean }} [options]
   * @returns {Promise<{ ok: boolean, membership?: object, session?: object, error?: any, pending?: boolean, duplicate?: boolean }>}
   */
  function process(purchase, { force = false } = {}) {
    if (stopped || !purchase) return Promise.resolve({ ok: false, skipped: true });
    if (purchase.purchaseState === 'pending') {
      emit({ type: 'pending', purchase });
      return Promise.resolve({ ok: false, pending: true });
    }
    const key = transactionKey(purchase);
    if (key && inFlight.has(key)) return inFlight.get(key);
    if (key && !force && finished.has(key)) return Promise.resolve({ ok: true, duplicate: true });
    emit({ type: 'verifying', purchase });
    const promise = run(purchase, key, force)
      .catch((error) => ({ ok: false, error }))
      .finally(() => {
        if (key) inFlight.delete(key);
      });
    if (key) inFlight.set(key, promise);
    return promise;
  }

  /**
   * Launch-time sweep: transactions the store still holds as unfinished (iOS) or
   * unacknowledged (Android). Covers purchases interrupted by a crash or a closed app, and
   * purchases verified on a previous launch whose finish never ran.
   */
  async function processUnfinished() {
    let list = [];
    try {
      if (os === 'ios' && typeof iap.getPendingTransactionsIOS === 'function') {
        list = await iap.getPendingTransactionsIOS();
      } else if (os === 'android' && typeof iap.getAvailablePurchases === 'function') {
        list = ((await iap.getAvailablePurchases()) || []).filter((p) => p && p.isAcknowledgedAndroid === false);
      }
    } catch {
      return [];
    }
    const mine = (list || []).filter((p) => p && p.purchaseState !== 'pending' && (!productId || !p.productId || p.productId === productId));
    return Promise.all(mine.map((p) => process(p)));
  }

  function stop() {
    stopped = true;
  }

  return { process, processUnfinished, stop, isFinished: (key) => finished.has(key) };
}
