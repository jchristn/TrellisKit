import { router } from 'expo-router';
import { Linking } from 'react-native';
import { getBundledProperty, webUrl } from './property/property';
import { resolveAppLink } from './linkTargets';

const property = getBundledProperty();
export const TermsUrl = webUrl(property, '/terms');
export const PrivacyUrl = webUrl(property, '/privacy');
export const AppleSubscriptionsUrl = 'https://apps.apple.com/account/subscriptions';
export const AndroidPackage = property.mobile?.androidPackage || '';

export function googleSubscriptionsUrl(productId) {
  const base = 'https://play.google.com/store/account/subscriptions';
  return productId ? base + '?sku=' + encodeURIComponent(productId) + '&package=' + AndroidPackage : base;
}

/** App routes stay native. External pages use an embedded view, never a browser activity. */
export async function openAppLink(url, options = {}) {
  const target = resolveAppLink(url, options.property || property);
  if (!target) return false;
  const navigator = options.router || router;
  if (target.kind === 'native') navigator.push(target.path);
  else navigator.push({ pathname: '/external-content', params: target.kind === 'web' ? { url: target.url } : { unavailable: '1' } });
  return true;
}

/** Compatibility for integrations; this function no longer opens any browser. */
export const openInAppBrowser = openAppLink;

/** Explicit operating-system integrations only. Arbitrary web URLs remain inside the app. */
export function openExternal(url) {
  if (typeof url !== 'string') return Promise.resolve(false);
  const store = /^https:\/\/(?:apps\.apple\.com\/|play\.google\.com\/store\/)/i.test(url);
  const system = /^(?:tel:|mailto:|webcal:|calshow:|content:|geo:|maps:|itms-apps:|market:)/i.test(url);
  return store || system ? Linking.openURL(url) : openAppLink(url);
}
