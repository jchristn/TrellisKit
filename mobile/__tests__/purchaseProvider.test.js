import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { router as appRouter } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { createPurchaseProcessor, isRetryableError, transactionKey } from '../src/billing/processor';
import { getIap } from '../src/native/iap';
import { disabledProviders, installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { property } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');
const PRODUCT = property.membership.storeProductId;
const inactive = { isActive: false, source: null, store: null, expiresUtc: null, autoRenew: false, willConvertToPaid: false };
const active = { isActive: true, source: 'Subscription', store: 'Apple', expiresUtc: '2027-11-11T00:00:00Z', autoRenew: true, willConvertToPaid: false };
const providers = { ...disabledProviders, appleIap: { enabled: true, productId: PRODUCT }, googlePlay: { enabled: true, productId: PRODUCT } };

function apiError(status, code = null) {
  const error = new Error('HTTP ' + status);
  error.status = status;
  error.code = code;
  return error;
}

/** A fake api whose verify route answers from a queue of results (an Error is thrown). */
function fakeApi(results, log) {
  return {
    post: jest.fn(async (route, body) => {
      log.push('verify:' + (body.signedTransaction || body.purchaseToken));
      const next = results.length > 1 ? results.shift() : results[0];
      if (next instanceof Error) throw next;
      return next;
    })
  };
}

function fakeIap(log, extra = {}) {
  return {
    finishTransaction: jest.fn(async ({ purchase }) => {
      log.push('finish:' + purchase.transactionId);
      return true;
    }),
    ...extra
  };
}

const purchase = (id, extra = {}) => ({ transactionId: id, productId: PRODUCT, purchaseToken: 'jws-' + id, purchaseState: 'purchased', transactionDate: 1, ...extra });

describe('purchase processor', () => {
  test('transaction keys and retryable errors', () => {
    expect(transactionKey({ transactionId: 't1', purchaseToken: 'x' })).toBe('t1');
    expect(transactionKey({ id: 'i1' })).toBe('i1');
    expect(transactionKey({ purchaseToken: 'tok' })).toBe('tok');
    expect(transactionKey(null)).toBeNull();
    for (const status of [0, 408, 429, 500, 503]) expect(isRetryableError(apiError(status))).toBe(true);
    for (const status of [400, 401, 402, 403, 404, 409]) expect(isRetryableError(apiError(status))).toBe(false);
    expect(isRetryableError(new Error('no status'))).toBe(false);
  });

  test('concurrent deliveries of one transaction verify once and finish once', async () => {
    const log = [];
    const api = fakeApi([{ membership: active }], log);
    const iap = fakeIap(log);
    const onVerified = jest.fn(async () => ({ status: 'signedIn' }));
    const processor = createPurchaseProcessor({ api, iap, os: 'ios', productId: PRODUCT, onVerified });
    const p = purchase('t1');
    const [a, b] = await Promise.all([processor.process(p), processor.process({ ...p })]);
    expect(a).toBe(b);
    expect(log).toEqual(['verify:jws-t1', 'finish:t1']);
    const again = await processor.process(p);
    expect(again).toEqual({ ok: true, duplicate: true });
    expect(log).toEqual(['verify:jws-t1', 'finish:t1']);
    expect(onVerified).toHaveBeenCalledTimes(1);
  });

  test('Restore (force) re-posts a transaction that was already finished', async () => {
    const log = [];
    const processor = createPurchaseProcessor({ api: fakeApi([{ membership: active }], log), iap: fakeIap(log), os: 'ios', productId: PRODUCT });
    await processor.process(purchase('t1'));
    await processor.process(purchase('t1'), { force: true });
    expect(log).toEqual(['verify:jws-t1', 'finish:t1', 'verify:jws-t1', 'finish:t1']);
  });

  test('finishTransaction runs only after the server verifies', async () => {
    const log = [];
    const events = [];
    const processor = createPurchaseProcessor({
      api: fakeApi([apiError(400, 'InvalidTransaction')], log),
      iap: fakeIap(log),
      os: 'ios',
      productId: PRODUCT,
      emit: (e) => events.push(e.type)
    });
    const result = await processor.process(purchase('t1'));
    expect(result.ok).toBe(false);
    expect(log).toEqual(['verify:jws-t1']);
    expect(events).toEqual(['verifying', 'failed']);
    expect(processor.isFinished('t1')).toBe(false);
  });

  test('server errors retry with backoff, then verify and finish once', async () => {
    const log = [];
    const events = [];
    const sleep = jest.fn(async () => {});
    const processor = createPurchaseProcessor({
      api: fakeApi([apiError(503), apiError(0), { membership: active }], log),
      iap: fakeIap(log),
      os: 'ios',
      productId: PRODUCT,
      retryDelaysMs: [10, 20, 40],
      sleep,
      emit: (e) => events.push(e.type + (e.attempt ? ':' + e.attempt : ''))
    });
    const result = await processor.process(purchase('t1'));
    expect(result.ok).toBe(true);
    expect(sleep.mock.calls.map((c) => c[0])).toEqual([10, 20]);
    expect(log).toEqual(['verify:jws-t1', 'verify:jws-t1', 'verify:jws-t1', 'finish:t1']);
    expect(events).toEqual(['verifying', 'retrying:1', 'retrying:2', 'activated']);
  });

  test('retries stop after the last delay and leave the transaction unfinished', async () => {
    const log = [];
    const sleep = jest.fn(async () => {});
    const processor = createPurchaseProcessor({ api: fakeApi([apiError(500)], log), iap: fakeIap(log), os: 'ios', productId: PRODUCT, retryDelaysMs: [1, 2], sleep });
    const result = await processor.process(purchase('t1'));
    expect(result.ok).toBe(false);
    expect(sleep).toHaveBeenCalledTimes(2);
    expect(log.filter((l) => l.startsWith('verify'))).toHaveLength(3);
    expect(log.some((l) => l.startsWith('finish'))).toBe(false);
  });

  test('a client error (another account) is final and not retried', async () => {
    const log = [];
    const sleep = jest.fn(async () => {});
    const processor = createPurchaseProcessor({ api: fakeApi([apiError(409, 'TransactionBelongsToAnotherAccount')], log), iap: fakeIap(log), os: 'ios', productId: PRODUCT, sleep });
    await processor.process(purchase('t1'));
    expect(sleep).not.toHaveBeenCalled();
    expect(log).toEqual(['verify:jws-t1']);
  });

  test('verified but not finished: the next delivery finishes without verifying again', async () => {
    const log = [];
    const iap = fakeIap(log);
    iap.finishTransaction.mockRejectedValueOnce(new Error('store busy'));
    const processor = createPurchaseProcessor({ api: fakeApi([{ membership: active }], log), iap, os: 'ios', productId: PRODUCT });
    const first = await processor.process(purchase('t1'));
    expect(first.ok).toBe(true);
    expect(processor.isFinished('t1')).toBe(false);
    await processor.process(purchase('t1'));
    expect(log).toEqual(['verify:jws-t1', 'finish:t1']);
    expect(iap.finishTransaction).toHaveBeenCalledTimes(2);
    expect(processor.isFinished('t1')).toBe(true);
  });

  test('pending purchases (Ask to Buy) are not posted', async () => {
    const log = [];
    const events = [];
    const processor = createPurchaseProcessor({ api: fakeApi([{ membership: active }], log), iap: fakeIap(log), os: 'ios', productId: PRODUCT, emit: (e) => events.push(e.type) });
    const result = await processor.process(purchase('t1', { purchaseState: 'pending' }));
    expect(result).toEqual({ ok: false, pending: true });
    expect(log).toEqual([]);
    expect(events).toEqual(['pending']);
  });

  test('launch sweep: iOS unfinished transactions are verified and finished', async () => {
    const log = [];
    const iap = fakeIap(log, { getPendingTransactionsIOS: jest.fn(async () => [purchase('a'), purchase('b'), purchase('c', { purchaseState: 'pending' }), purchase('d', { productId: 'other' })]) });
    const processor = createPurchaseProcessor({ api: fakeApi([{ membership: active }], log), iap, os: 'ios', productId: PRODUCT });
    await processor.processUnfinished();
    expect(log.sort()).toEqual(['finish:a', 'finish:b', 'verify:jws-a', 'verify:jws-b']);
  });

  test('launch sweep: Android posts only unacknowledged purchases', async () => {
    const log = [];
    const iap = fakeIap(log, {
      getAvailablePurchases: jest.fn(async () => [purchase('acked', { isAcknowledgedAndroid: true }), purchase('new', { isAcknowledgedAndroid: false, purchaseToken: 'play-new' })])
    });
    const api = fakeApi([{ membership: active }], log);
    const processor = createPurchaseProcessor({ api, iap, os: 'android', productId: PRODUCT });
    await processor.processUnfinished();
    expect(api.post).toHaveBeenCalledWith('/membership/google/verify', { purchaseToken: 'play-new', productId: PRODUCT });
    expect(log).toEqual(['verify:play-new', 'finish:new']);
  });

  test('a stopped processor (sign-out) abandons its retries', async () => {
    const log = [];
    let release;
    const sleep = jest.fn(() => new Promise((resolve) => (release = resolve)));
    const processor = createPurchaseProcessor({ api: fakeApi([apiError(503), { membership: active }], log), iap: fakeIap(log), os: 'ios', productId: PRODUCT, retryDelaysMs: [5], sleep });
    const pending = processor.process(purchase('t1'));
    await waitFor(() => expect(sleep).toHaveBeenCalled());
    processor.stop();
    release();
    const result = await pending;
    expect(result.ok).toBe(false);
    expect(log).toEqual(['verify:jws-t1']);
  });
});

function makeIap(order, extra = {}) {
  const listeners = { update: [], error: [] };
  return {
    initConnection: jest.fn(async () => true),
    endConnection: jest.fn(async () => true),
    fetchProducts: jest.fn(async () => [{ id: PRODUCT, displayPrice: '$29.99', displayName: 'Annual membership', type: 'subs' }]),
    purchaseUpdatedListener: jest.fn((fn) => {
      listeners.update.push(fn);
      return { remove: jest.fn(() => (listeners.update = listeners.update.filter((l) => l !== fn))) };
    }),
    purchaseErrorListener: jest.fn((fn) => {
      listeners.error.push(fn);
      return { remove: jest.fn() };
    }),
    requestPurchase: jest.fn(async () => null),
    finishTransaction: jest.fn(async ({ purchase: p }) => {
      order.push('finish:' + p.transactionId);
      return true;
    }),
    restorePurchases: jest.fn(async () => undefined),
    getAvailablePurchases: jest.fn(async () => []),
    listenerCount: () => listeners.update.length,
    emitPurchase: (p) => act(async () => Promise.all(listeners.update.map((fn) => fn(p)))),
    ...extra
  };
}

describe('app-wide purchase listener', () => {
  let order;
  beforeEach(() => {
    resetEnvironment();
    order = [];
  });
  afterEach(() => {
    delete global.fetch;
    getIap.mockReturnValue(null);
  });

  function routes(user, meAfter) {
    return {
      ...signedInAs(user),
      'GET /auth/providers': providers,
      'POST /membership/apple/verify': (call) => {
        order.push('verify:' + call.body.signedTransaction);
        return { membership: active };
      },
      'GET /auth/me': () => meAfter
    };
  }

  test('a renewal delivered while the paywall is closed is verified, finished, and refreshes the member', async () => {
    const iap = makeIap(order);
    getIap.mockReturnValue(iap);
    const server = installFakeServer(routes(makeUser(), makeUser({ membership: { ...active, expiresUtc: '2028-11-11T00:00:00Z' } })));
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
    await waitFor(() => expect(iap.listenerCount()).toBe(1));
    await iap.emitPurchase(purchase('renewal-1'));
    expect(order).toEqual(['verify:jws-renewal-1', 'finish:renewal-1']);
    await waitFor(() => expect(server.called('GET /auth/me')).toHaveLength(1));
    expect(router.getPathname()).toBe('/feed');
  });

  test('an Ask to Buy approval that lands while the member is in Account settings lets them in', async () => {
    const iap = makeIap(order);
    getIap.mockReturnValue(iap);
    installFakeServer(routes(makeUser({ membership: inactive }), makeUser({ membership: active })));
    const router = await renderRouter(APP_DIR, { initialUrl: '/account' });
    expect(await screen.findByTestId('screen-account')).toBeOnTheScreen();
    await waitFor(() => expect(iap.listenerCount()).toBe(1));
    await iap.emitPurchase(purchase('approved-1'));
    expect(order).toEqual(['verify:jws-approved-1', 'finish:approved-1']);
    // Back on the paywall the member is now active and is sent on into the app.
    await act(async () => appRouter.push('/subscribe'));
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
  });

  test('launch-time processing finishes an unfinished transaction from a previous launch', async () => {
    const iap = makeIap(order, { getPendingTransactionsIOS: jest.fn(async () => [purchase('interrupted-1')]) });
    getIap.mockReturnValue(iap);
    installFakeServer(routes(makeUser({ membership: inactive }), makeUser({ membership: active })));
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(order).toEqual(['verify:jws-interrupted-1', 'finish:interrupted-1']));
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
    expect(iap.initConnection).toHaveBeenCalledTimes(1);
  });

  test('the same transaction from the launch sweep and the listener is posted once', async () => {
    let releaseVerify;
    const gate = new Promise((resolve) => (releaseVerify = resolve));
    const iap = makeIap(order, { getPendingTransactionsIOS: jest.fn(async () => [purchase('dup-1')]) });
    getIap.mockReturnValue(iap);
    installFakeServer({
      ...routes(makeUser({ membership: inactive }), makeUser({ membership: active })),
      'POST /membership/apple/verify': async (call) => {
        order.push('verify:' + call.body.signedTransaction);
        await gate;
        return { membership: active };
      }
    });
    await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(order).toEqual(['verify:jws-dup-1']));
    const delivered = iap.emitPurchase(purchase('dup-1'));
    releaseVerify();
    await delivered;
    await waitFor(() => expect(order).toEqual(['verify:jws-dup-1', 'finish:dup-1']));
    expect(iap.finishTransaction).toHaveBeenCalledTimes(1);
  });

  test('signing out closes the store connection and removes the listeners', async () => {
    const iap = makeIap(order);
    getIap.mockReturnValue(iap);
    installFakeServer({ ...routes(makeUser(), makeUser()), 'POST /auth/logout': { status: 204, body: null } });
    const router = await renderRouter(APP_DIR, { initialUrl: '/profile' });
    await waitFor(() => expect(iap.listenerCount()).toBe(1));
    await fireEvent.press(await screen.findByTestId('profile-sign-out'));
    await waitFor(() => expect(router.getPathname()).toBe('/welcome'));
    await waitFor(() => expect(iap.endConnection).toHaveBeenCalled());
    expect(iap.listenerCount()).toBe(0);
  });
});
