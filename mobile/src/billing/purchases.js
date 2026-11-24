import { membership as membershipApi } from '../api/endpoints';

/**
 * The store config for this platform: enabled from the GET /auth/providers block, and the
 * product id from the property (membership.storeProductId), falling back to the providers block.
 * @param {object} providers
 * @param {string} os
 * @param {string|null} [propertyProductId] The property's membership.storeProductId.
 * @returns {{ enabled: boolean, productId: string|null }}
 */
export function storeConfig(providers, os, propertyProductId = null) {
  const block = providers && (os === 'ios' ? providers.appleIap : providers.googlePlay);
  return { enabled: Boolean(block && block.enabled), productId: propertyProductId || (block && block.productId) || null };
}

/**
 * Pick the Android offer token for a subscription product (Play Billing requires one).
 * Prefers the plain base plan (no offer id) over promotional offers.
 * @param {object} product ProductSubscriptionAndroid from expo-iap.
 * @returns {string|null}
 */
export function androidOfferToken(product) {
  if (!product) return null;
  const offers = Array.isArray(product.subscriptionOffers) ? product.subscriptionOffers.filter((o) => o && o.offerTokenAndroid) : [];
  const base = offers.find((o) => !o.id || o.id === o.basePlanIdAndroid) || offers[0];
  if (base) return base.offerTokenAndroid;
  const legacy = Array.isArray(product.subscriptionOfferDetailsAndroid) ? product.subscriptionOfferDetailsAndroid.find((o) => o && o.offerToken) : null;
  return legacy ? legacy.offerToken : null;
}

/**
 * Build the expo-iap requestPurchase argument for the annual subscription. The account token
 * (`storeAccountToken` from GET /membership) ties store notifications to the member.
 * @param {{ productId: string, product: object, accountToken: string|null }} options
 */
export function buildPurchaseRequest({ productId, product, accountToken }) {
  const apple = { sku: productId };
  if (accountToken) apple.appAccountToken = accountToken;
  const google = { skus: [productId] };
  if (accountToken) google.obfuscatedAccountId = accountToken;
  const offerToken = androidOfferToken(product);
  if (offerToken) google.subscriptionOffers = [{ sku: productId, offerToken }];
  return { type: 'subs', request: { apple, google } };
}

/**
 * Post a store transaction to the matching verify route.
 * @param {object} api MobileApiClient.
 * @param {object} purchase expo-iap Purchase.
 * @param {{ os: string, productId: string }} options
 * @returns {Promise<{ membership: object }>}
 * @throws {ApiError|Error} When the transaction has no token or the server rejects it.
 */
export async function verifyPurchase(api, purchase, { os, productId }) {
  if (os === 'ios') {
    const signedTransaction = purchase.purchaseToken || purchase.jwsRepresentationIOS || null;
    if (!signedTransaction) throw new Error('The store did not return a signed transaction.');
    return membershipApi.verifyApple(api, signedTransaction);
  }
  const purchaseToken = purchase.purchaseToken || purchase.purchaseTokenAndroid || null;
  if (!purchaseToken) throw new Error('The store did not return a purchase token.');
  return membershipApi.verifyGoogle(api, { purchaseToken, productId: purchase.productId || productId });
}

/**
 * Verify with the server, then finish the transaction. finishTransaction runs only after the
 * verify route answers 200, so a failed verification leaves the transaction with the store to
 * be retried (Restore purchases, or the next launch on iOS).
 * @returns {Promise<object>} The membership returned by the server.
 */
export async function verifyAndFinish({ api, iap, purchase, os, productId }) {
  const result = await verifyPurchase(api, purchase, { os, productId });
  await iap.finishTransaction({ purchase, isConsumable: false });
  return result && result.membership;
}

/**
 * The most recent purchase of a product, for Restore purchases.
 * @param {object[]} purchases From getAvailablePurchases.
 * @param {string} productId
 */
export function latestPurchase(purchases, productId) {
  const matching = (purchases || []).filter((p) => p && (!productId || p.productId === productId || (Array.isArray(p.ids) && p.ids.includes(productId))));
  if (!matching.length) return null;
  return matching.reduce((latest, p) => (Number(p.transactionDate || 0) > Number(latest.transactionDate || 0) ? p : latest));
}

/** expo-iap error codes that mean the member backed out. */
export function isCancelledError(error) {
  return Boolean(error && (error.code === 'user-cancelled' || error.code === 'E_USER_CANCELLED'));
}

/** expo-iap error codes that mean the purchase awaits approval (Ask to Buy, pending payment). */
export function isPendingError(error) {
  return Boolean(error && (error.code === 'deferred-payment' || error.code === 'pending'));
}
