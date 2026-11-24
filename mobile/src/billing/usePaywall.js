import { useCallback, useEffect, useRef, useState } from 'react';
import { membership as membershipApi } from '../api/endpoints';
import { describeError } from '../auth/errors';
import strings from '../strings';
import { withTimeout } from '../utils/timeout';
import { buildPurchaseRequest, isCancelledError, isPendingError, latestPurchase } from './purchases';

/** How long the paywall waits for the server and the store before offering Retry. */
export const PaywallLoadTimeoutMs = 20000;

function failureMessage(error) {
  return error && error.code === 'TransactionBelongsToAnotherAccount' ? describeError(error) : strings.paywall.verifyFailed;
}

/**
 * Paywall state machine (contract section 5) on top of the app-wide PurchaseProvider.
 *
 * The provider owns the store connection and the purchase listeners; this hook loads the product
 * and the account token, starts purchases and restores, and turns provider events into
 * messages. Navigation after a verified purchase is not done here: the session refresh makes
 * the member active and the paywall screen redirects.
 *
 * phase: loading, unavailable, ready, purchasing, verifying, pending.
 * reason (when unavailable): unavailable (no store), offline, timeout, productMissing.
 *
 * @param {object} options
 * @param {object} options.api MobileApiClient.
 * @param {ReturnType<import('./PurchaseProvider').usePurchases>} options.purchases
 */
export function usePaywall({ api, purchases }) {
  const { status: storeStatus, iap, productId: configProductId, processPurchase, subscribe } = purchases;
  const [attempt, setAttempt] = useState(0);
  const [loaded, setLoaded] = useState({ key: null, phase: 'loading', product: null, reason: null, message: null });
  const [activity, setActivity] = useState({ phase: null, message: null, tone: 'info' });
  const ctx = useRef({ productId: null, accountToken: null, product: null });
  const loadKey = storeStatus === 'ready' ? String(attempt) : null;

  const handleError = useCallback((error) => {
    if (isCancelledError(error)) setActivity({ phase: null, message: strings.paywall.cancelled, tone: 'info' });
    else if (isPendingError(error)) setActivity({ phase: 'pending', message: strings.paywall.pending, tone: 'info' });
    else setActivity({ phase: null, message: strings.paywall.purchaseFailed, tone: 'error' });
  }, []);

  useEffect(
    () =>
      subscribe((event) => {
        switch (event.type) {
          case 'verifying':
            setActivity({ phase: 'verifying', message: null, tone: 'info' });
            break;
          case 'retrying':
            setActivity({ phase: 'verifying', message: strings.paywall.retrying, tone: 'info' });
            break;
          case 'pending':
            setActivity({ phase: 'pending', message: strings.paywall.pending, tone: 'info' });
            break;
          case 'activated':
            setActivity({ phase: null, message: null, tone: 'info' });
            break;
          case 'failed':
            setActivity({ phase: null, message: failureMessage(event.error), tone: 'error' });
            break;
          case 'error':
            handleError(event.error);
            break;
          default:
            break;
        }
      }),
    [subscribe, handleError]
  );

  useEffect(() => {
    if (!loadKey) return undefined;
    let active = true;
    const store = ctx.current;
    async function load() {
      try {
        const info = await withTimeout(membershipApi.get(api), PaywallLoadTimeoutMs);
        store.accountToken = (info && info.storeAccountToken) || null;
        store.productId = configProductId || (info && info.storeProductId) || null;
        if (!store.productId) throw new Error('No product id.');
        const products = await withTimeout(iap.fetchProducts({ skus: [store.productId], type: 'subs' }), PaywallLoadTimeoutMs);
        const product = (products || []).find((p) => p && p.id === store.productId) || (products || [])[0] || null;
        if (!active) return;
        store.product = product;
        setLoaded(product ? { key: loadKey, phase: 'ready', product, reason: null, message: null } : { key: loadKey, phase: 'unavailable', product: null, reason: 'productMissing', message: null });
      } catch (error) {
        if (!active) return;
        const reason = error && error.code === 'Timeout' ? 'timeout' : error && error.status === 0 ? 'offline' : 'productMissing';
        setLoaded({ key: loadKey, phase: 'unavailable', product: null, reason, message: reason === 'productMissing' ? null : describeError(error) });
      }
    }
    load();
    return () => {
      active = false;
    };
  }, [loadKey, api, iap, configProductId]);

  const buy = useCallback(async () => {
    const { productId, product, accountToken } = ctx.current;
    if (!iap || !productId) return;
    setActivity({ phase: 'purchasing', message: null, tone: 'info' });
    try {
      await iap.requestPurchase(buildPurchaseRequest({ productId, product, accountToken }));
    } catch (error) {
      handleError(error);
    }
  }, [iap, handleError]);

  const restore = useCallback(async () => {
    const { productId } = ctx.current;
    if (!iap) return;
    setActivity({ phase: 'verifying', message: null, tone: 'info' });
    try {
      try {
        await withTimeout(iap.restorePurchases(), PaywallLoadTimeoutMs);
      } catch {
        // Some stores reject a sync when offline; the available purchases below still work.
      }
      const available = await withTimeout(iap.getAvailablePurchases(), PaywallLoadTimeoutMs);
      const purchase = latestPurchase(available, productId);
      if (!purchase) {
        setActivity({ phase: null, message: strings.paywall.nothingToRestore, tone: 'info' });
        return;
      }
      const result = await processPurchase(purchase, { force: true });
      if (result && result.skipped) setActivity({ phase: null, message: strings.paywall.verifyFailed, tone: 'error' });
    } catch (error) {
      setActivity({ phase: null, message: failureMessage(error), tone: 'error' });
    }
  }, [iap, processPurchase]);

  /** Load the product again after an offline, timeout, or store failure. */
  const reload = useCallback(() => setAttempt((n) => n + 1), []);

  let phase;
  let reason = null;
  let message = activity.message;
  let tone = activity.tone;
  let product = null;
  if (storeStatus === 'unavailable') {
    phase = 'unavailable';
    reason = 'unavailable';
  } else if (storeStatus !== 'ready' || loaded.key !== loadKey) {
    phase = 'loading';
  } else if (loaded.phase === 'unavailable') {
    phase = 'unavailable';
    reason = loaded.reason;
    message = loaded.message;
    tone = 'error';
  } else {
    product = loaded.product;
    phase = activity.phase || 'ready';
  }

  return { phase, reason, product, message, tone, buy, restore, reload };
}
