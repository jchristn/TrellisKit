import { Linking } from 'react-native';
import * as WebBrowser from 'expo-web-browser';
import { getBundledProperty, webUrl } from './property/property';

const property = getBundledProperty();

/** Terms of Use page on the property's website (domains.web), shown on the paywall and at registration. */
export const TermsUrl = webUrl(property, '/terms');

/** Privacy Policy page on the property's website (domains.web). */
export const PrivacyUrl = webUrl(property, '/privacy');

/** App Store subscription management. */
export const AppleSubscriptionsUrl = 'https://apps.apple.com/account/subscriptions';

/** Android package name (the property's mobile.androidPackage), used for the Google Play subscription link. */
export const AndroidPackage = (property.mobile && property.mobile.androidPackage) || '';

/**
 * Google Play subscription management for one product.
 * @param {string|null} productId
 */
export function googleSubscriptionsUrl(productId) {
  const base = 'https://play.google.com/store/account/subscriptions';
  return productId ? base + '?sku=' + encodeURIComponent(productId) + '&package=' + AndroidPackage : base;
}

/** Open a page in the in-app browser (SFSafariViewController or Custom Tabs). */
export async function openInAppBrowser(url) {
  try {
    await WebBrowser.openBrowserAsync(url);
  } catch {
    await Linking.openURL(url);
  }
}

/** Open a store URL outside the app. */
export function openExternal(url) {
  return Linking.openURL(url);
}
