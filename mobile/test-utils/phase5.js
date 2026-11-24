/**
 * Phase 5 test helpers: serve the shared fakes (`@trelliskit/shared/testing`) behind the app's
 * fake fetch, a signed-in member, and the property with Phase 5 features.
 */
import { combineFakes } from '@trelliskit/shared/testing';
import { act } from '@testing-library/react-native';
import { Dimensions } from 'react-native';
import { installFakeServer, makeUser, signedInAs } from './fakeServer';
import { property } from './property';

/** The test member as the fakes' `me` (UserSummary). */
export const me = { id: 'usr_1', displayName: 'Test Member', handle: 'tester', avatarUrl: null, isStaff: false, isDeleted: false };

/** GET /property for the active property with feature and access overrides. */
export function phase5Property({ features = {}, access } = {}) {
  return { ...property, features: { ...(property.features || {}), messaging: true, support: true, ideas: true, broadcasts: true, ...features }, ...(access ? { access: { ...(property.access || {}), ...access } } : {}) };
}

/**
 * Install the fake server: the signed-in `user`, GET /property, extra `routes`, and the fakes
 * (first answer wins) for everything else, with request headers passed through.
 */
export function serveFakes(fakes, { user = makeUser(), routes = {}, propertyDoc = phase5Property() } = {}) {
  const combined = combineFakes(...fakes);
  return installFakeServer(
    { 'GET /property': propertyDoc, ...signedInAs(user), ...routes },
    { fallback: (call) => combined.handle({ method: call.method, path: call.path, query: call.query, body: call.body, headers: call.headers }) }
  );
}

/** Run `fn` with the window at `width` points (tablet layouts). */
export async function atWidth(width, fn) {
  const original = Dimensions.get('window');
  const screenSize = Dimensions.get('screen');
  Dimensions.set({ window: { ...original, width, height: 820 }, screen: { ...screenSize, width, height: 820 } });
  try {
    await fn();
  } finally {
    await act(async () => Dimensions.set({ window: original, screen: screenSize }));
  }
}
