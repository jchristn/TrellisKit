import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as WebBrowser from 'expo-web-browser';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { paywallDocuments } from '../src/documents/documents';
import { buildPurchaseRequest, latestPurchase, verifyAndFinish } from '../src/billing/purchases';
import { getIap } from '../src/native/iap';
import { disabledProviders, installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { property, tokens } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');
const PRODUCT = property.membership.storeProductId;
const inactive = { isActive: false, source: null, store: null, expiresUtc: null, autoRenew: false, willConvertToPaid: false };
const active = { isActive: true, source: 'Subscription', store: 'Apple', expiresUtc: '2027-11-11T00:00:00Z', autoRenew: true, willConvertToPaid: false };
const providers = { ...disabledProviders, appleIap: { enabled: true, productId: PRODUCT }, googlePlay: { enabled: true, productId: PRODUCT } };

function makeIap(order = []) {
  const listeners = { update: [], error: [] };
  return {
    order,
    initConnection: jest.fn(async () => true),
    endConnection: jest.fn(async () => true),
    fetchProducts: jest.fn(async () => [{ id: PRODUCT, displayPrice: '$29.99', displayName: 'Annual membership', platform: 'ios', type: 'subs' }]),
    purchaseUpdatedListener: jest.fn((fn) => {
      listeners.update.push(fn);
      return { remove: jest.fn() };
    }),
    purchaseErrorListener: jest.fn((fn) => {
      listeners.error.push(fn);
      return { remove: jest.fn() };
    }),
    requestPurchase: jest.fn(async () => null),
    finishTransaction: jest.fn(async () => {
      order.push('finish');
      return true;
    }),
    restorePurchases: jest.fn(async () => undefined),
    getAvailablePurchases: jest.fn(async () => []),
    emitPurchase: (purchase) => act(async () => Promise.all(listeners.update.map((fn) => fn(purchase)))),
    emitError: (error) => act(async () => listeners.error.forEach((fn) => fn(error)))
  };
}

function routes(user, order, verify) {
  return {
    ...signedInAs(user),
    'GET /auth/providers': providers,
    'GET /membership': { membership: inactive, webOffer: { priceUsd: 19.95, interval: 'Year', productName: 'Web' }, storeProductId: PRODUCT, storeAccountToken: '7b0c8b0e-0000-4000-8000-000000000001', history: [] },
    'POST /membership/apple/verify': (call) => {
      order.push('verify');
      return verify(call);
    },
    'GET /auth/me': makeUser({ membership: active })
  };
}

async function openPaywall() {
  const router = await renderRouter(APP_DIR, { initialUrl: '/' });
  await waitFor(() => expect(router.getPathname()).toBe('/subscribe'));
  expect(await screen.findByTestId('paywall-subscribe')).toBeOnTheScreen();
  return router;
}

describe('paywall', () => {
  let iap;
  let order;
  beforeEach(() => {
    resetEnvironment();
    order = [];
    iap = makeIap(order);
    getIap.mockReturnValue(iap);
  });
  afterEach(() => {
    delete global.fetch;
    getIap.mockReturnValue(null);
  });

  test('shows the store price, auto-renew terms, Restore purchases, and Terms and Privacy links; never the web offer', async () => {
    installFakeServer(routes(makeUser({ membership: inactive }), order, () => ({ membership: active })));
    await openPaywall();
    expect(screen.getByTestId('paywall-price')).toHaveTextContent('$29.99 per year');
    expect(screen.getByRole('button', { name: 'Subscribe for $29.99 per year' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Restore purchases' })).toBeOnTheScreen();
    expect(screen.getByTestId('paywall-terms')).toHaveTextContent(/renews automatically/);
    expect(screen.queryByText(/19\.95/)).toBeNull();
    const documents = paywallDocuments(property);
    if (documents.length) {
      // Configured documents open in the app (Terms, Privacy, and the ShownAtPurchase ones).
      for (const doc of documents) expect(screen.getByTestId('paywall-document-' + doc.key)).toHaveTextContent(doc.title);
      expect(screen.queryByTestId('paywall-terms-link')).toBeNull();
    } else {
      await fireEvent.press(screen.getByTestId('paywall-terms-link'));
      await fireEvent.press(screen.getByTestId('paywall-privacy-link'));
      expect(WebBrowser.openBrowserAsync).toHaveBeenCalledWith('https://' + property.domains.web + '/terms');
      expect(WebBrowser.openBrowserAsync).toHaveBeenCalledWith('https://' + property.domains.web + '/privacy');
    }
  });

  test('purchase, then verify, then finish, then into the app', async () => {
    const server = installFakeServer(routes(makeUser({ membership: inactive }), order, () => ({ membership: active })));
    const router = await openPaywall();
    await fireEvent.press(screen.getByTestId('paywall-subscribe'));
    expect(iap.requestPurchase).toHaveBeenCalledWith({
      type: 'subs',
      request: {
        apple: { sku: PRODUCT, appAccountToken: '7b0c8b0e-0000-4000-8000-000000000001' },
        google: { skus: [PRODUCT], obfuscatedAccountId: '7b0c8b0e-0000-4000-8000-000000000001' }
      }
    });
    const purchase = { productId: PRODUCT, purchaseToken: 'signed-jws', purchaseState: 'purchased', transactionDate: 1 };
    await iap.emitPurchase(purchase);
    expect(order).toEqual(['verify', 'finish']);
    expect(server.called('POST /membership/apple/verify')[0].body).toEqual({ signedTransaction: 'signed-jws' });
    expect(iap.finishTransaction).toHaveBeenCalledWith({ purchase, isConsumable: false });
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
  });

  test('a failed verification never finishes the transaction', async () => {
    installFakeServer(routes(makeUser({ membership: inactive }), order, () => ({ status: 400, body: { error: 'InvalidTransaction', message: 'Bad' } })));
    const router = await openPaywall();
    await fireEvent.press(screen.getByTestId('paywall-subscribe'));
    await iap.emitPurchase({ productId: PRODUCT, purchaseToken: 'signed-jws', purchaseState: 'purchased' });
    expect(order).toEqual(['verify']);
    expect(iap.finishTransaction).not.toHaveBeenCalled();
    expect(await screen.findByText(/We could not confirm your purchase yet/)).toBeOnTheScreen();
    expect(router.getPathname()).toBe('/subscribe');
  });

  test('a purchase owned by another account explains itself and is not finished', async () => {
    installFakeServer(routes(makeUser({ membership: inactive }), order, () => ({ status: 409, body: { error: 'TransactionBelongsToAnotherAccount', message: 'x' } })));
    await openPaywall();
    await iap.emitPurchase({ productId: PRODUCT, purchaseToken: 'signed-jws', purchaseState: 'purchased' });
    expect(await screen.findByText('This store purchase belongs to a different ' + tokens.name + ' account. Sign in with that account to use it.')).toBeOnTheScreen();
    expect(iap.finishTransaction).not.toHaveBeenCalled();
  });

  test('Restore purchases re-posts the latest transaction, then finishes it', async () => {
    iap.getAvailablePurchases.mockResolvedValue([
      { productId: PRODUCT, purchaseToken: 'old', transactionDate: 100 },
      { productId: PRODUCT, purchaseToken: 'newest', transactionDate: 300 },
      { productId: 'other', purchaseToken: 'nope', transactionDate: 900 }
    ]);
    const server = installFakeServer(routes(makeUser({ membership: inactive }), order, () => ({ membership: active })));
    const router = await openPaywall();
    await fireEvent.press(screen.getByTestId('paywall-restore'));
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
    expect(iap.restorePurchases).toHaveBeenCalled();
    expect(server.called('POST /membership/apple/verify').map((c) => c.body.signedTransaction)).toEqual(['newest']);
    expect(order).toEqual(['verify', 'finish']);
  });

  test('Restore with nothing to restore says so', async () => {
    installFakeServer(routes(makeUser({ membership: inactive }), order, () => ({ membership: active })));
    await openPaywall();
    await fireEvent.press(screen.getByTestId('paywall-restore'));
    expect(await screen.findByText('No membership purchase was found for this store account.')).toBeOnTheScreen();
    expect(iap.finishTransaction).not.toHaveBeenCalled();
  });

  test('cancelled and pending purchases are handled without verifying', async () => {
    const server = installFakeServer(routes(makeUser({ membership: inactive }), order, () => ({ membership: active })));
    await openPaywall();
    await fireEvent.press(screen.getByTestId('paywall-subscribe'));
    await iap.emitError({ code: 'user-cancelled', message: 'Cancelled' });
    expect(await screen.findByText('Purchase cancelled.')).toBeOnTheScreen();

    await iap.emitPurchase({ productId: PRODUCT, purchaseToken: 't', purchaseState: 'pending' });
    expect(await screen.findByText(/pending approval/)).toBeOnTheScreen();
    expect(server.called('POST /membership/apple/verify')).toHaveLength(0);
    expect(iap.finishTransaction).not.toHaveBeenCalled();
  });

  test('without a store (Expo Go) the paywall says purchases are not available', async () => {
    getIap.mockReturnValue(null);
    installFakeServer(routes(makeUser({ membership: inactive }), order, () => ({ membership: active })));
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/subscribe'));
    expect(await screen.findByText('Purchases are not available in this build')).toBeOnTheScreen();
    expect(screen.queryByTestId('paywall-subscribe')).toBeNull();
    const first = paywallDocuments(property)[0];
    expect(screen.getByTestId(first ? 'paywall-document-' + first.key : 'paywall-terms-link')).toBeOnTheScreen();
    expect(screen.getByTestId('paywall-sign-out')).toBeOnTheScreen();
  });

  test('a store connection failure (simulator without StoreKit) is also unavailable', async () => {
    iap.initConnection.mockRejectedValue(new Error('E_IAP_NOT_AVAILABLE'));
    installFakeServer(routes(makeUser({ membership: inactive }), order, () => ({ membership: active })));
    await renderRouter(APP_DIR, { initialUrl: '/' });
    expect(await screen.findByText('Purchases are not available in this build')).toBeOnTheScreen();
  });
});

describe('membership ending mid-session', () => {
  beforeEach(() => {
    resetEnvironment();
    getIap.mockReturnValue(null);
  });
  afterEach(() => {
    delete global.fetch;
  });

  test('a 402 from a member route reloads Me and sends the member to the paywall', async () => {
    const server = installFakeServer({
      ...signedInAs(makeUser({ membership: active })),
      'POST /feed/enumerate': { status: 402, body: { error: 'MembershipRequired', message: 'An active membership is required.' } },
      'GET /auth/me': makeUser({ membership: inactive }),
      'GET /membership': { membership: inactive, storeProductId: PRODUCT, history: { objects: [] } }
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await waitFor(() => expect(router.getPathname()).toBe('/subscribe'));
    expect(server.called('GET /auth/me')).toHaveLength(1);
  });
});

describe('purchase helpers', () => {
  test('Android requests include the base plan offer token', () => {
    const product = {
      id: PRODUCT,
      platform: 'android',
      subscriptionOffers: [
        { id: 'intro', basePlanIdAndroid: 'annual', offerTokenAndroid: 'intro-token' },
        { id: 'annual', basePlanIdAndroid: 'annual', offerTokenAndroid: 'base-token' }
      ]
    };
    expect(buildPurchaseRequest({ productId: PRODUCT, product, accountToken: 'acct' }).request.google).toEqual({
      skus: [PRODUCT],
      obfuscatedAccountId: 'acct',
      subscriptionOffers: [{ sku: PRODUCT, offerToken: 'base-token' }]
    });
  });

  test('Android verification posts the purchase token and product id, then finishes', async () => {
    const calls = [];
    const api = { post: jest.fn(async (p, body) => (calls.push(['verify', p, body]), { membership: active })) };
    const iap = { finishTransaction: jest.fn(async () => calls.push(['finish'])) };
    const purchase = { productId: PRODUCT, purchaseToken: 'play-token' };
    await verifyAndFinish({ api, iap, purchase, os: 'android', productId: PRODUCT });
    expect(calls).toEqual([['verify', '/membership/google/verify', { purchaseToken: 'play-token', productId: PRODUCT }], ['finish']]);
  });

  test('latestPurchase picks the newest matching transaction', () => {
    expect(latestPurchase([], PRODUCT)).toBeNull();
    expect(latestPurchase([{ productId: PRODUCT, transactionDate: 1 }, { productId: PRODUCT, transactionDate: 5 }], PRODUCT).transactionDate).toBe(5);
  });
});
