import { screen, waitFor } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import fs from 'fs';
import path from 'path';
import { getIap } from '../src/native/iap';
import strings from '../src/strings';
import { disabledProviders, installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { property } from '../test-utils/property';

/**
 * App Store Guideline 3.1.1: the app must never show, accept, or mention sign-up codes (or any
 * other code that unlocks membership), web pricing, or a way to pay on the website.
 */
const escape = (text) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const FORBIDDEN = [/redeem/i, /promo ?code/i, /promotional/i, /coupon/i, /voucher/i, /\bcodes?\b/i, /sign-?up code/i, /website/i, new RegExp(escape(property.domains.web), 'i'), /\bstripe\b/i];

const APP_DIR = path.resolve(__dirname, '../app');
const PRODUCT = property.membership.storeProductId;
const inactive = { isActive: false, source: null, store: null, expiresUtc: null, autoRenew: false };
const allProviders = {
  ...disabledProviders,
  apple: { enabled: true },
  google: { enabled: true, webClientId: 'w', iosClientId: 'i' },
  facebook: { enabled: true, appId: '1' },
  stripe: { enabled: true },
  appleIap: { enabled: true, productId: PRODUCT },
  googlePlay: { enabled: true, productId: PRODUCT }
};

function collectStrings(value, out = []) {
  if (typeof value === 'string') out.push(value);
  else if (typeof value === 'function') {
    const sample = value('X', 1, 'y');
    if (typeof sample === 'string') out.push(sample);
  } else if (value && typeof value === 'object') Object.values(value).forEach((v) => collectStrings(v, out));
  return out;
}

function renderedText() {
  const out = [];
  const walk = (node) => {
    if (!node) return;
    if (typeof node === 'string') out.push(node);
    else if (Array.isArray(node)) node.forEach(walk);
    else {
      if (node.props) {
        for (const key of ['accessibilityLabel', 'accessibilityHint', 'placeholder', 'title']) {
          if (typeof node.props[key] === 'string') out.push(node.props[key]);
        }
      }
      walk(node.children);
    }
  };
  walk(screen.toJSON());
  return out.join('\n');
}

function expectClean(text, where) {
  for (const pattern of FORBIDDEN) {
    expect({ where, pattern: String(pattern), match: (text.match(pattern) || [null])[0] }).toEqual({ where, pattern: String(pattern), match: null });
  }
}

describe('no sign-up codes, web pricing, or web purchase links', () => {
  test('the strings module never mentions them', () => {
    expectClean(collectStrings(strings).join('\n'), 'strings.js');
  });

  test('no source file calls the sign-up code routes', () => {
    const files = [];
    const walk = (dir) => {
      for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) walk(full);
        else if (/\.(js|jsx)$/.test(entry.name)) files.push(full);
      }
    };
    walk(path.resolve(__dirname, '../app'));
    walk(path.resolve(__dirname, '../src'));
    for (const file of files) {
      const source = fs.readFileSync(file, 'utf8');
      expect({ file, hit: /signup-codes|stripe\/checkout|webOffer\.|priceUsd/.test(source.replace(/^\s*(\*|\/\/).*$/gm, '')) }).toEqual({ file, hit: false });
    }
  });

  describe('rendered screens', () => {
    beforeEach(() => {
      resetEnvironment();
      getIap.mockReturnValue({
        initConnection: jest.fn(async () => true),
        endConnection: jest.fn(async () => true),
        fetchProducts: jest.fn(async () => [{ id: PRODUCT, displayPrice: '$29.99', displayName: 'Annual membership' }]),
        purchaseUpdatedListener: () => ({ remove() {} }),
        purchaseErrorListener: () => ({ remove() {} })
      });
    });
    afterEach(() => {
      delete global.fetch;
      getIap.mockReturnValue(null);
    });

    const signedOut = ['/welcome', '/sign-in', '/email-sign-in', '/register', '/forgot-password'];
    test.each(signedOut)('%s', async (url) => {
      installFakeServer({ 'GET /auth/providers': allProviders });
      const router = await renderRouter(APP_DIR, { initialUrl: url });
      await waitFor(() => expect(router.getPathname()).toBe(url));
      await screen.findAllByRole('button');
      expectClean(renderedText(), url);
    });

    const code = { isActive: true, source: 'SignupCode', store: null, expiresUtc: '2027-03-01T00:00:00Z', autoRenew: false };
    test.each([
      ['/age', makeUser({ ageAttested: false, profileComplete: false })],
      ['/profile-setup', makeUser({ profileComplete: false, membership: inactive })],
      ['/subscribe', makeUser({ membership: inactive })],
      ['/allow-notifications', makeUser()],
      ['/feed', makeUser({ membership: code })],
      ['/profile', makeUser({ membership: code })],
      ['/account', makeUser({ membership: code })],
      ['/account/membership', makeUser({ membership: code })],
      ['/account/membership', makeUser({ membership: inactive })],
      ['/account/sign-in-methods', makeUser({ membership: code })],
      ['/account/password', makeUser({ membership: code })],
      ['/account/edit-profile', makeUser({ membership: code })],
      ['/account/delete', makeUser({ membership: code })]
    ])('%s', async (url, user) => {
      installFakeServer({
        ...signedInAs(user, { notificationsPrompted: url !== '/allow-notifications' }),
        'GET /auth/providers': allProviders,
        'GET /auth/me': user,
        'GET /membership': { membership: user.membership, webOffer: { priceUsd: 29.99, interval: 'Year', productName: 'Web' }, storeProductId: PRODUCT, storeAccountToken: 'tok', history: [] }
      });
      const router = await renderRouter(APP_DIR, { initialUrl: url });
      await waitFor(() => expect(router.getPathname()).toBe(url));
      if (url.startsWith('/account/membership')) await screen.findByTestId('membership-status');
      else await screen.findAllByRole('button');
      if (url === '/subscribe') await screen.findByTestId('paywall-subscribe');
      expectClean(renderedText(), url);
    });
  });
});
