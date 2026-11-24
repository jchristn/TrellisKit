import { fireEvent, renderHook, screen, waitFor } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import fs from 'fs';
import path from 'path';
import { AccessibilityInfo, Dimensions, StyleSheet } from 'react-native';
import { useStackOptions } from '../src/hooks/useStackOptions';
import { getIap } from '../src/native/iap';
import { layout } from '../src/theme/tokens';
import { disabledProviders, installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { property, tokens } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');
const PRODUCT = property.membership.storeProductId;
const inactive = { isActive: false, source: null, store: null, expiresUtc: null, autoRenew: false, willConvertToPaid: false };
const INTERACTIVE = ['button', 'link', 'radio', 'checkbox', 'tab'];

function hostNodes(node, out = []) {
  if (!node || typeof node === 'string') return out;
  if (Array.isArray(node)) {
    node.forEach((n) => hostNodes(n, out));
    return out;
  }
  out.push(node);
  hostNodes(node.children, out);
  return out;
}

/** The host element that directly contains the element with this testID. */
function parentOf(node, testID, parent = null) {
  if (!node || typeof node === 'string') return null;
  if (Array.isArray(node)) {
    for (const n of node) {
      const found = parentOf(n, testID, parent);
      if (found) return found;
    }
    return null;
  }
  if (node.props && node.props.testID === testID) return parent;
  return parentOf(node.children, testID, node);
}

function textOf(node) {
  if (!node) return '';
  if (typeof node === 'string') return node;
  if (Array.isArray(node)) return node.map(textOf).join('');
  return textOf(node.children);
}

/** Every interactive element has a name and at least a 44 point target (size plus hitSlop). */
function expectAccessibleControls(where) {
  const controls = hostNodes(screen.toJSON()).filter((n) => n.props && INTERACTIVE.includes(n.props.accessibilityRole));
  expect(controls.length).toBeGreaterThan(0);
  for (const node of controls) {
    const name = node.props.accessibilityLabel || textOf(node);
    expect({ where, name: Boolean(name && name.trim()) }).toEqual({ where, name: true });
    const style = StyleSheet.flatten(node.props.style) || {};
    const slop = typeof node.props.hitSlop === 'number' ? node.props.hitSlop * 2 : node.props.hitSlop ? (node.props.hitSlop.top || 0) + (node.props.hitSlop.bottom || 0) : 0;
    const height = Math.max(style.minHeight || 0, style.height || 0) + slop;
    expect({ where, name, ok: height >= layout.minTouchTarget }).toEqual({ where, name, ok: true });
  }
}

describe('accessibility', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => {
    delete global.fetch;
    getIap.mockReturnValue(null);
    jest.restoreAllMocks();
  });

  test.each([
    ['/welcome', null],
    ['/sign-in', null],
    ['/email-sign-in', null],
    ['/register', null],
    ['/forgot-password', null],
    ['/age', makeUser({ ageAttested: false, profileComplete: false, membership: inactive })],
    ['/profile-setup', makeUser({ profileComplete: false, membership: inactive })],
    ['/allow-notifications', makeUser()],
    ['/account', makeUser()],
    ['/account/sign-in-methods', makeUser()],
    ['/account/password', makeUser()],
    ['/account/membership', makeUser()],
    ['/account/delete', makeUser()],
    ['/profile', makeUser()]
  ])('%s: controls are labeled and at least 44 points tall', async (url, user) => {
    installFakeServer(user ? { ...signedInAs(user, { notificationsPrompted: url !== '/allow-notifications' }), 'GET /auth/me': user } : {});
    const router = await renderRouter(APP_DIR, { initialUrl: url });
    await waitFor(() => expect(router.getPathname()).toBe(url));
    await waitFor(() => expect(screen.queryByLabelText(/Loading|Signing you in/)).toBeNull());
    expectAccessibleControls(url);
  });

  test('the paywall price card and benefits read as one element each', async () => {
    const iap = {
      initConnection: jest.fn(async () => true),
      endConnection: jest.fn(async () => true),
      fetchProducts: jest.fn(async () => [{ id: PRODUCT, displayPrice: '$29.99', displayName: 'Annual membership' }]),
      purchaseUpdatedListener: jest.fn(() => ({ remove: jest.fn() })),
      purchaseErrorListener: jest.fn(() => ({ remove: jest.fn() })),
      getAvailablePurchases: jest.fn(async () => [])
    };
    getIap.mockReturnValue(iap);
    installFakeServer({
      ...signedInAs(makeUser({ membership: inactive })),
      'GET /auth/providers': { ...disabledProviders, appleIap: { enabled: true, productId: PRODUCT } },
      'GET /membership': { membership: inactive, storeProductId: PRODUCT, storeAccountToken: 'acct', history: [] }
    });
    await renderRouter(APP_DIR, { initialUrl: '/' });
    expect(await screen.findByLabelText('Annual membership, $29.99 per year')).toBeOnTheScreen();
    expect(screen.getByLabelText('Membership includes: Posts and videos from ' + tokens.owner, { exact: false })).toBeOnTheScreen();
    expectAccessibleControls('/subscribe');
  });

  test('date of birth fields name the field they belong to and widen with the text size', async () => {
    const original = Dimensions.get('window');
    Dimensions.set({ window: { ...original, fontScale: 3 }, screen: { ...Dimensions.get('screen'), fontScale: 3 } });
    try {
      installFakeServer({});
      await renderRouter(APP_DIR, { initialUrl: '/register' });
      const month = await screen.findByTestId('register-dob-month');
      expect(month.props.accessibilityLabel).toBe('Date of birth, Month, Month');
      // The month picker's basis is 88 x 1.6 at text scale 3.
      expect(StyleSheet.flatten(screen.getByTestId('register-dob-month-wrap').props.style).flexBasis).toBe(422);
    } finally {
      Dimensions.set({ window: original, screen: Dimensions.get('screen') });
    }
  });

  test('field errors are part of the field announcement', async () => {
    installFakeServer({});
    await renderRouter(APP_DIR, { initialUrl: '/register' });
    await fireEvent.press(await screen.findByTestId('register-submit'));
    expect(screen.getByTestId('register-email').props.accessibilityHint).toBe('Enter a valid email address.');
  });

  test('Reduce Motion swaps the slide transition for a cross-fade', async () => {
    jest.spyOn(AccessibilityInfo, 'isReduceMotionEnabled').mockResolvedValue(true);
    const { result } = await renderHook(() => useStackOptions());
    await waitFor(() => expect(result.current.animation).toBe('fade'));
  });

  test('without Reduce Motion the platform transition is kept', async () => {
    jest.spyOn(AccessibilityInfo, 'isReduceMotionEnabled').mockResolvedValue(false);
    const { result } = await renderHook(() => useStackOptions());
    await waitFor(() => expect(AccessibilityInfo.isReduceMotionEnabled).toHaveBeenCalled());
    expect(result.current.animation).toBeUndefined();
    // Back buttons never fall back to a raw route name as their label.
    expect(result.current.headerBackButtonDisplayMode).toBe('minimal');
  });

  test('no source disables or caps Dynamic Type or truncates text', () => {
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
      expect({ file, hit: /allowFontScaling=\{false\}|maxFontSizeMultiplier|numberOfLines|adjustsFontSizeToFit/.test(source) }).toEqual({ file, hit: false });
    }
  });
});
