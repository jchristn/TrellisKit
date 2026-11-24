import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { Platform } from 'react-native';
import { useAuth } from '../auth/AuthProvider';
import { getIap as defaultGetIap } from '../native/iap';
import { storeProductIdOf, useProperty } from '../property';
import { createPurchaseProcessor } from './processor';
import { storeConfig } from './purchases';
import { purchasesEnabled } from '@trelliskit/shared';

const PurchaseContext = createContext(null);

const IDLE = Object.freeze({ status: 'idle', iap: null, productId: null, processPurchase: async () => ({ ok: false, skipped: true }), subscribe: () => () => {} });

/**
 * App-wide store connection and purchase listener.
 *
 * While a member is signed in and the store is enabled in GET /auth/providers, this provider
 * holds the expo-iap connection and the purchase update and error listeners, so a transaction
 * is verified and finished even when the paywall is closed: Ask to Buy approvals, renewals and
 * unfinished transactions the store delivers at launch, and purchases interrupted by the app
 * closing. On connect it also sweeps unfinished transactions (see createPurchaseProcessor).
 *
 * After a verified transaction the member is refreshed (GET /auth/me) so gating updates
 * wherever they are in the app.
 *
 * status: `idle` (signed out or providers loading), `connecting`, `ready`, or `unavailable`
 * (no store in this build, the store is disabled on the server, or initConnection failed).
 *
 * @param {object} props
 * @param {() => object|null} [props.getIap] Override for tests.
 * @param {string} [props.os] Override for tests.
 * @param {number[]} [props.retryDelaysMs] Override for tests.
 * @param {(ms: number) => Promise<void>} [props.sleep] Override for tests.
 */
export function PurchaseProvider({ children, getIap = defaultGetIap, os = Platform.OS, retryDelaysMs, sleep }) {
  const { api, session, providers, refreshUser, updateUser } = useAuth();
  const userId = session.status === 'signedIn' && session.user ? session.user.id : null;
  const property = useProperty();
  const config = storeConfig(providers, os, storeProductIdOf(property));
  const iapModule = userId && providers && config.enabled ? getIap() : null;
  // Nothing is sold in the Open access model: never connect to the store.
  const connectKey = userId && providers && config.enabled && purchasesEnabled(property) && iapModule ? userId + '|' + config.productId : null;

  const [connection, setConnection] = useState({ key: null, status: 'connecting', iap: null });
  const listeners = useRef(new Set());
  const processorRef = useRef(null);
  const authRef = useRef({ refreshUser, updateUser, user: session.user });
  useEffect(() => {
    authRef.current = { refreshUser, updateUser, user: session.user };
  });

  const emit = useCallback((event) => {
    for (const listener of Array.from(listeners.current)) {
      try {
        listener(event);
      } catch {
        // A failing listener never blocks the others.
      }
    }
  }, []);

  useEffect(() => {
    if (!connectKey) return undefined;
    const iap = iapModule;
    let active = true;
    let connected = false;
    const subscriptions = [];

    async function connect() {
      try {
        await iap.initConnection();
        connected = true;
      } catch {
        if (active) setConnection({ key: connectKey, status: 'unavailable', iap: null });
        return;
      }
      if (!active) return;
      const processor = createPurchaseProcessor({
        api,
        iap,
        os,
        productId: config.productId,
        retryDelaysMs,
        sleep,
        emit,
        onVerified: async (membership) => {
          const auth = authRef.current;
          try {
            return await auth.refreshUser();
          } catch {
            return auth.user ? auth.updateUser({ ...auth.user, membership: membership || auth.user.membership }) : null;
          }
        }
      });
      processorRef.current = processor;
      subscriptions.push(iap.purchaseUpdatedListener((purchase) => processor.process(purchase)));
      subscriptions.push(iap.purchaseErrorListener((error) => emit({ type: 'error', error })));
      setConnection({ key: connectKey, status: 'ready', iap });
      processor.processUnfinished();
    }

    connect();
    return () => {
      active = false;
      if (processorRef.current) processorRef.current.stop();
      processorRef.current = null;
      for (const sub of subscriptions) {
        try {
          sub.remove();
        } catch {
          // Already removed.
        }
      }
      if (connected) Promise.resolve(iap.endConnection()).catch(() => {});
    };
  }, [connectKey, iapModule, api, os, config.productId, retryDelaysMs, sleep, emit]);

  const subscribe = useCallback((listener) => {
    listeners.current.add(listener);
    return () => listeners.current.delete(listener);
  }, []);

  const processPurchase = useCallback((purchase, options) => {
    const processor = processorRef.current;
    if (!processor) return Promise.resolve({ ok: false, skipped: true });
    return processor.process(purchase, options);
  }, []);

  let status = 'idle';
  if (userId && providers) {
    if (!connectKey) status = 'unavailable';
    else if (connection.key !== connectKey) status = 'connecting';
    else status = connection.status;
  }
  const iap = status === 'ready' ? connection.iap : null;

  const value = useMemo(() => ({ status, iap, productId: config.productId, processPurchase, subscribe }), [status, iap, config.productId, processPurchase, subscribe]);

  return <PurchaseContext.Provider value={value}>{children}</PurchaseContext.Provider>;
}

/** The app-wide purchase context (an idle stub outside PurchaseProvider). */
export function usePurchases() {
  return useContext(PurchaseContext) || IDLE;
}
