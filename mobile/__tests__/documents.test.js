import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { AppState, Platform } from 'react-native';
import { documents as documentsApi } from '../src/api/endpoints';
import { MobileApiClient } from '../src/api/MobileApiClient';
import { tokenStore } from '../src/api/tokenStore';
import { ForegroundRecheckMs, withRequiredDocuments } from '../src/auth/AuthProvider';
import { canEnterTabs, nextRoute } from '../src/auth/routing';
import {
  acceptedDocumentsBody,
  documentMarkdown,
  documentPath,
  documentsOf,
  isDocumentAcceptanceRequired,
  joinTitles,
  paywallDocuments,
  pendingDocumentsOf,
  purchaseDocuments,
  separateDocuments,
  termsDocuments
} from '../src/documents/documents';
import { validateDocumentAcceptance, validateRegistration } from '../src/forms';
import strings from '../src/strings';
import { authResponse, installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { property } from '../test-utils/property';
import { pickDate } from '../test-utils/dates';

const APP_DIR = path.resolve(__dirname, '../app');
const inactive = { isActive: false, source: null, store: null, expiresUtc: null, autoRenew: false, willConvertToPaid: false };

function doc(key, title, kind, acceptance, extra = {}) {
  return { key, title, kind, acceptance, separateCheckbox: false, reacceptOnChange: false, slug: key, footer: true, description: null, version: '1.0', sequence: 1, updatedUtc: '2026-11-12T00:00:00Z', labels: [], tags: {}, ...extra };
}

/** A test-local document set (neutral titles), served as the live property. */
const DOCS = [
  doc('terms', 'Terms of Service', 'Terms', 'AcceptedWithTerms'),
  doc('privacy', 'Privacy Policy', 'Privacy', 'AcceptedWithTerms'),
  doc('rules', 'House Rules', 'Guidelines', 'RequiredAtSignup'),
  doc('waiver', 'Liability Waiver', 'Waiver', 'RequiredAtSignup', { separateCheckbox: true, reacceptOnChange: true, version: '2.0', sequence: 2 }),
  doc('billing', 'Subscription Terms', 'Subscription', 'ShownAtPurchase'),
  doc('deletion', 'Account Deletion', 'AccountDeletion', 'InformationOnly')
];
const WAIVER = DOCS[3];
const fixture = { ...property, documents: DOCS };
const fullDocument = (summary, content = '# ' + summary.title + '\n\nIntro text.\n\n## Section one\n\nBody **bold**.') => ({ ...summary, content, changeSummary: null });

function makeClient() {
  return new MobileApiClient({ baseUrl: 'http://api.test', appVersion: '1.0.0', tokenStore });
}

describe('document helpers', () => {
  test('group the property documents by how they are accepted', () => {
    expect(documentsOf({ documents: [...DOCS, { key: 'bad key!' }, null] }).map((d) => d.key)).toEqual(DOCS.map((d) => d.key));
    expect(documentsOf({})).toEqual([]);
    expect(termsDocuments(fixture).map((d) => d.key)).toEqual(['terms', 'privacy', 'rules']);
    expect(separateDocuments(fixture).map((d) => d.key)).toEqual(['waiver']);
    expect(purchaseDocuments(fixture).map((d) => d.key)).toEqual(['billing']);
    expect(paywallDocuments(fixture).map((d) => d.key)).toEqual(['terms', 'privacy', 'billing']);
  });

  test('build the acceptedDocuments body with the shown or a newer version', () => {
    expect(acceptedDocumentsBody([WAIVER], { waiver: true })).toEqual([{ key: 'waiver', version: '2.0' }]);
    expect(acceptedDocumentsBody([WAIVER], { waiver: true }, { waiver: '2.1' })).toEqual([{ key: 'waiver', version: '2.1' }]);
    expect(acceptedDocumentsBody([WAIVER], {})).toEqual([]);
  });

  test('pending documents fill missing details from the property', () => {
    const user = makeUser({ pendingDocuments: [{ key: 'waiver' }, { ...DOCS[0], version: '3.0' }] });
    expect(pendingDocumentsOf(user, fixture).map((d) => [d.key, d.title, d.version])).toEqual([
      ['waiver', 'Liability Waiver', '2.0'],
      ['terms', 'Terms of Service', '3.0']
    ]);
    expect(pendingDocumentsOf(makeUser(), fixture)).toEqual([]);
  });

  test('Markdown, titles, paths, and the error code', () => {
    expect(documentMarkdown('# Title\n\nText\n\n# Part\n\n## Sub\n\n#### Deep')).toBe('Text\n\n## Part\n\n## Sub\n\n### Deep');
    expect(joinTitles(['A'])).toBe('A');
    expect(joinTitles(['A', 'B'])).toBe('A and B');
    expect(joinTitles(['A', 'B', 'C'])).toBe('A, B, and C');
    expect(documentPath('a b')).toBe('/documents/a%20b');
    expect(isDocumentAcceptanceRequired({ status: 403, code: 'DocumentAcceptanceRequired' })).toBe(true);
    expect(isDocumentAcceptanceRequired({ status: 403, code: 'Forbidden' })).toBe(false);
  });

  test('a 403 names documents for a Me without pendingDocuments', () => {
    const error = { status: 403, code: 'DocumentAcceptanceRequired', body: { documents: ['waiver'] } };
    const legacy = makeUser();
    expect(withRequiredDocuments(legacy, error).pendingDocuments).toEqual([{ key: 'waiver' }]);
    const current = makeUser({ pendingDocuments: [] });
    expect(withRequiredDocuments(current, error)).toBe(current);
  });
});

describe('document validation', () => {
  const values = { displayName: 'Alex', email: 'alex@example.com', password: 'long enough pw', dob: { month: '1', day: '1', year: '1990' }, acceptTerms: true, acceptedDocuments: { waiver: true } };
  const today = new Date(2026, 10, 11);

  test('every separate document needs its own box, reported under the server field key', () => {
    expect(validateRegistration(values, today, fixture).errors).toEqual({});
    const missing = validateRegistration({ ...values, acceptedDocuments: {} }, today, fixture).errors;
    expect(missing).toEqual({ 'acceptedDocuments.waiver': 'Accept the Liability Waiver to create an account.' });
  });

  test('the terms error names the documents the terms box covers', () => {
    expect(validateRegistration({ ...values, acceptTerms: false }, today, fixture).errors.acceptTerms).toBe('You must accept the Terms of Service, Privacy Policy, and House Rules.');
    expect(validateRegistration({ ...values, acceptTerms: false }, today, { ...property, documents: undefined }).errors.acceptTerms).toBe(strings.register.errors.terms);
  });

  test('the documents step needs every pending document checked', () => {
    expect(validateDocumentAcceptance([WAIVER], {})).toEqual({ 'documents.waiver': 'Accept the Liability Waiver to continue.' });
    expect(validateDocumentAcceptance([WAIVER], { waiver: true })).toEqual({});
  });
});

describe('routing with pending documents', () => {
  const signedIn = (user) => ({ status: 'signedIn', user, notificationsPrompted: true });

  test('the documents step comes after age attestation and before profile setup', () => {
    const pending = [WAIVER];
    expect(nextRoute(signedIn(makeUser({ ageAttested: false, profileComplete: false, pendingDocuments: pending })))).toBe('/age');
    expect(nextRoute(signedIn(makeUser({ profileComplete: false, pendingDocuments: pending })))).toBe('/review-documents');
    expect(nextRoute(signedIn(makeUser({ pendingDocuments: pending })))).toBe('/review-documents');
    expect(nextRoute(signedIn(makeUser({ pendingDocuments: [] })))).toBe('/feed');
  });

  test('the tabs are closed while documents are pending', () => {
    expect(canEnterTabs(signedIn(makeUser({ pendingDocuments: [WAIVER] })))).toBe(false);
    expect(canEnterTabs(signedIn(makeUser({ pendingDocuments: [] })))).toBe(true);
  });
});

describe('API client and endpoints', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('sends X-Client-Platform on every request except a ticket without diagnostics', async () => {
    const server = installFakeServer({ 'GET /public/documents/waiver': fullDocument(WAIVER), 'POST /tickets': { status: 201, body: { id: 'tkt_1' } } });
    const api = makeClient();
    await documentsApi.get(api, 'waiver');
    expect(server.calls[0].headers['X-Client-Platform']).toBe(Platform.OS);
    await api.post('/tickets', { title: 'x' });
    expect(server.calls[1].headers['X-Client-Platform']).toBeUndefined();
    expect((await api.baseHeaders())['X-Client-Platform']).toBe(Platform.OS);
  });

  test('document routes and bodies', async () => {
    const server = installFakeServer({
      'POST /me/documents/accept': makeUser(),
      'POST /me/documents/acceptances/enumerate': { objects: [], continuationToken: null },
      'POST /me/age-attestation': makeUser()
    });
    const api = makeClient();
    await documentsApi.accept(api, { documents: [{ key: 'waiver', version: '2.0', title: 'ignored' }] });
    expect(server.called('POST /me/documents/accept')[0].body).toEqual({ documents: [{ key: 'waiver', version: '2.0' }] });
    await documentsApi.accept(api, { acceptTerms: true, documents: [] });
    expect(server.called('POST /me/documents/accept')[1].body).toEqual({ acceptTerms: true, documents: [] });
    await documentsApi.enumerateAcceptances(api, { continuationToken: 'next' });
    expect(server.called('POST /me/documents/acceptances/enumerate')[0].body).toEqual({ maxResults: 20, continuationToken: 'next' });
    const { account } = require('../src/api/endpoints');
    await account.attestAge(api, '1990-01-01', { acceptTerms: true, acceptedDocuments: [{ key: 'waiver', version: '2.0' }] });
    expect(server.called('POST /me/age-attestation')[0].body).toEqual({ dateOfBirth: '1990-01-01', acceptTerms: true, acceptedDocuments: [{ key: 'waiver', version: '2.0' }] });
  });

  test('403 DocumentAcceptanceRequired runs the access listeners', async () => {
    installFakeServer({ 'POST /feed/enumerate': { status: 403, body: { error: 'DocumentAcceptanceRequired', message: 'Accept.', documents: ['waiver'] } } });
    const api = makeClient();
    const seen = [];
    api.onAccessRequired((error) => seen.push(error.code));
    await expect(api.post('/feed/enumerate', {})).rejects.toMatchObject({ status: 403 });
    expect(seen).toEqual(['DocumentAcceptanceRequired']);
  });
});

describe('sign-up with documents', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  async function fill() {
    await fireEvent.changeText(await screen.findByTestId('register-name'), 'New Member');
    await fireEvent.changeText(screen.getByTestId('register-email'), 'new@example.com');
    await fireEvent.changeText(screen.getByTestId('register-password'), 'correct horse battery');
    await pickDate('register-dob', '01', '15', '1990');
    await fireEvent(screen.getByTestId('register-terms'), 'valueChange', true);
  }

  test('links the terms documents, shows a separate box per document, and blocks sign-up without it', async () => {
    const server = installFakeServer({ 'GET /property': fixture });
    await renderRouter(APP_DIR, { initialUrl: '/register' });
    expect(await screen.findByTestId('register-document-waiver')).toBeOnTheScreen();
    expect(screen.getByTestId('register-terms').props.accessibilityLabel).toBe('Agree to the Terms of Service, Privacy Policy, and House Rules');
    for (const key of ['terms', 'privacy', 'rules']) expect(screen.getByTestId('register-terms-link-' + key)).toBeOnTheScreen();
    expect(screen.queryByTestId('register-terms-link-billing')).toBeNull();
    await fill();
    await fireEvent.press(screen.getByTestId('register-submit'));
    expect(await screen.findByText('Accept the Liability Waiver to create an account.')).toBeOnTheScreen();
    expect(server.called('POST /auth/register')).toHaveLength(0);
  });

  test('sends acceptedDocuments with the live version', async () => {
    const created = makeUser({ id: 'usr_new', profileComplete: false, membership: inactive, pendingDocuments: [] });
    const server = installFakeServer({ 'GET /property': fixture, 'POST /auth/register': { status: 201, body: { ...authResponse(created, 'r-new'), isNewAccount: true } } });
    const router = await renderRouter(APP_DIR, { initialUrl: '/register' });
    await fill();
    await fireEvent(await screen.findByTestId('register-document-waiver'), 'valueChange', true);
    await fireEvent.press(screen.getByTestId('register-submit'));
    await waitFor(() => expect(router.getPathname()).toBe('/profile-setup'));
    expect(server.called('POST /auth/register')[0].body).toMatchObject({ acceptTerms: true, acceptedDocuments: [{ key: 'waiver', version: '2.0' }] });
  });

  test('a stale version error shows on its box, reloads the version, and the next try sends it', async () => {
    let attempts = 0;
    const created = makeUser({ id: 'usr_new', profileComplete: false, membership: inactive, pendingDocuments: [] });
    const server = installFakeServer({
      'GET /property': fixture,
      'GET /public/documents/waiver': fullDocument({ ...WAIVER, version: '2.1' }),
      'POST /auth/register': () => {
        attempts += 1;
        if (attempts === 1) return { status: 400, body: { error: 'ValidationFailed', message: 'Invalid.', fieldErrors: { 'acceptedDocuments.waiver': 'The Liability Waiver has changed; review version 2.1 and accept it.' } } };
        return { status: 201, body: authResponse(created, 'r-new') };
      }
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/register' });
    await fill();
    await fireEvent(await screen.findByTestId('register-document-waiver'), 'valueChange', true);
    await fireEvent.press(screen.getByTestId('register-submit'));
    expect(await screen.findByText('The Liability Waiver has changed; review version 2.1 and accept it.')).toBeOnTheScreen();
    await waitFor(() => expect(server.called('GET /public/documents/waiver')).toHaveLength(1));
    await waitFor(() => expect(screen.getByTestId('register-document-waiver').props.value).toBe(false));
    await fireEvent(screen.getByTestId('register-document-waiver'), 'valueChange', true);
    await fireEvent.press(screen.getByTestId('register-submit'));
    await waitFor(() => expect(router.getPathname()).toBe('/profile-setup'));
    expect(server.called('POST /auth/register')[1].body.acceptedDocuments).toEqual([{ key: 'waiver', version: '2.1' }]);
  });

  test('Read opens the full document with its version and last update', async () => {
    installFakeServer({ 'GET /property': fixture, 'GET /public/documents/waiver': fullDocument(WAIVER) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/register' });
    await fireEvent.press(await screen.findByTestId('register-read-waiver'));
    await waitFor(() => expect(router.getPathname()).toBe('/documents/waiver'));
    expect(await screen.findByTestId('document-title')).toHaveTextContent('Liability Waiver');
    expect(screen.getByTestId('document-version')).toHaveTextContent('Version 2.0');
    expect(screen.getByTestId('document-updated')).toHaveTextContent(/^Last updated /);
    expect(screen.getByText('Section one')).toBeOnTheScreen();
    expect(screen.getByText('Intro text.')).toBeOnTheScreen();
  });
});

describe('document screen', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('a failed load offers Retry', async () => {
    const server = installFakeServer({ 'GET /property': fixture, 'GET /public/documents/terms': { __network: true } });
    await renderRouter(APP_DIR, { initialUrl: '/documents/terms' });
    expect(await screen.findByTestId('document-error')).toBeOnTheScreen();
    server.handlers['GET /public/documents/terms'] = fullDocument(DOCS[0]);
    await fireEvent.press(screen.getByTestId('document-error-retry'));
    expect(await screen.findByTestId('document-title')).toHaveTextContent('Terms of Service');
  });

  test('an unknown document says it is not available', async () => {
    installFakeServer({ 'GET /property': fixture });
    await renderRouter(APP_DIR, { initialUrl: '/documents/missing' });
    expect(await screen.findByTestId('document-not-found')).toBeOnTheScreen();
  });
});

describe('documents step', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => {
    delete global.fetch;
    jest.restoreAllMocks();
  });

  test('a member with pending documents lands on the step, must check each, and continues after accepting', async () => {
    const user = makeUser({ pendingDocuments: [WAIVER] });
    const server = installFakeServer({ ...signedInAs(user), 'GET /property': fixture, 'POST /me/documents/accept': makeUser({ pendingDocuments: [] }) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/review-documents'));
    expect(await screen.findByTestId('review-document-waiver')).toHaveTextContent(/Liability Waiver/);
    expect(screen.getByTestId('review-document-waiver')).toHaveTextContent(/Version 2\.0/);
    await fireEvent.press(screen.getByTestId('review-documents-continue'));
    expect(await screen.findByText('Accept the Liability Waiver to continue.')).toBeOnTheScreen();
    expect(server.called('POST /me/documents/accept')).toHaveLength(0);
    await fireEvent(screen.getByTestId('review-accept-waiver'), 'valueChange', true);
    await fireEvent.press(screen.getByTestId('review-documents-continue'));
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
    expect(server.called('POST /me/documents/accept')[0].body).toEqual({ documents: [{ key: 'waiver', version: '2.0' }] });
  });

  test('Read on the step opens the document', async () => {
    installFakeServer({ ...signedInAs(makeUser({ pendingDocuments: [WAIVER] })), 'GET /public/documents/waiver': fullDocument(WAIVER) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await fireEvent.press(await screen.findByTestId('review-read-waiver'));
    await waitFor(() => expect(router.getPathname()).toBe('/documents/waiver'));
    expect(await screen.findByTestId('document-title')).toHaveTextContent('Liability Waiver');
  });

  test('social sign-up: age attestation, then the documents step, then profile setup', async () => {
    const fresh = { ageAttested: false, profileComplete: false, membership: inactive, pendingDocuments: [WAIVER] };
    installFakeServer({
      ...signedInAs(makeUser(fresh)),
      'POST /me/age-attestation': makeUser({ ...fresh, ageAttested: true }),
      'POST /me/documents/accept': makeUser({ ...fresh, ageAttested: true, pendingDocuments: [] })
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await pickDate('age-dob', '01', '01', '1990');
    await fireEvent.press(screen.getByTestId('age-submit'));
    await waitFor(() => expect(router.getPathname()).toBe('/review-documents'));
    await fireEvent(await screen.findByTestId('review-accept-waiver'), 'valueChange', true);
    await fireEvent.press(screen.getByTestId('review-documents-continue'));
    await waitFor(() => expect(router.getPathname()).toBe('/profile-setup'));
  });

  test('a 403 DocumentAcceptanceRequired from a content route reloads Me and opens the step', async () => {
    const user = makeUser();
    installFakeServer({
      ...signedInAs(user),
      'POST /feed/enumerate': { status: 403, body: { error: 'DocumentAcceptanceRequired', message: 'Accept the new version.', documents: ['waiver'] } },
      'GET /auth/me': makeUser({ pendingDocuments: [WAIVER] })
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/review-documents'));
    expect(await screen.findByTestId('review-document-waiver')).toBeOnTheScreen();
  });

  test('pending documents send the member to the step from screens outside the tabs', async () => {
    installFakeServer({ ...signedInAs(makeUser({ pendingDocuments: [WAIVER] })) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/notifications' });
    await waitFor(() => expect(router.getPathname()).toBe('/review-documents'));
  });

  test('returning to the foreground reloads Me and picks up newly required documents', async () => {
    const listeners = [];
    jest.spyOn(AppState, 'addEventListener').mockImplementation((type, fn) => {
      listeners.push(fn);
      return { remove: jest.fn() };
    });
    const server = installFakeServer({ ...signedInAs(makeUser()), 'GET /auth/me': makeUser({ pendingDocuments: [WAIVER] }) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/' });
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
    const start = Date.now();
    jest.spyOn(Date, 'now').mockReturnValue(start + ForegroundRecheckMs + 1);
    await act(async () => {
      for (const fn of listeners) fn('active');
    });
    await waitFor(() => expect(server.called('GET /auth/me').length).toBeGreaterThan(0));
    await waitFor(() => expect(router.getPathname()).toBe('/review-documents'));
  });
});

describe('Account legal and purchase links', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('Account lists the documents with versions and the member acceptance history', async () => {
    const page1 = { objects: [{ id: 'dac_1', documentKey: 'waiver', documentTitle: 'Liability Waiver', version: '2.0', sequence: 2, method: 'Reacceptance', client: 'Ios', acceptedUtc: '2026-11-12T00:00:00Z', labels: [], tags: {} }], continuationToken: 'next' };
    const page2 = { objects: [{ id: 'dac_0', documentKey: 'terms', documentTitle: 'Terms of Service', version: '1.0', sequence: 1, method: 'Signup', client: 'Web', acceptedUtc: '2026-01-01T00:00:00Z', labels: [], tags: {} }], continuationToken: null };
    const server = installFakeServer({
      ...signedInAs(makeUser()),
      'GET /property': fixture,
      'POST /me/documents/acceptances/enumerate': (call) => (call.body.continuationToken ? page2 : page1),
      'GET /public/documents/deletion': fullDocument(DOCS[5])
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/account' });
    await fireEvent.press(await screen.findByTestId('account-legal'));
    await waitFor(() => expect(router.getPathname()).toBe('/account/legal'));
    for (const d of DOCS) expect(await screen.findByTestId('legal-document-' + d.key)).toHaveTextContent(d.title + ', Version ' + d.version);
    expect(await screen.findByTestId('legal-acceptance-dac_1')).toHaveTextContent(/Liability Waiver, version 2\.0/);
    expect(screen.getByTestId('legal-acceptance-dac_1')).toHaveTextContent(/After a change/);
    await fireEvent.press(screen.getByTestId('legal-history-more'));
    expect(await screen.findByTestId('legal-acceptance-dac_0')).toBeOnTheScreen();
    expect(screen.queryByTestId('legal-history-more')).toBeNull();
    expect(server.called('POST /me/documents/acceptances/enumerate')[0].body.maxResults).toBe(20);
    await fireEvent.press(screen.getByTestId('legal-document-deletion'));
    await waitFor(() => expect(router.getPathname()).toBe('/documents/deletion'));
    expect(await screen.findByTestId('document-title')).toHaveTextContent('Account Deletion');
  });

  test('the membership screen links the purchase documents', async () => {
    const sold = { ...fixture, access: { ...(property.access || {}), model: 'FullyGated', purchasesEnabled: true } };
    installFakeServer({ ...signedInAs(makeUser()), 'GET /auth/me': makeUser(), 'GET /property': sold, 'GET /public/documents/billing': fullDocument(DOCS[4]) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/account/membership' });
    await fireEvent.press(await screen.findByTestId('membership-document-billing'));
    await waitFor(() => expect(router.getPathname()).toBe('/documents/billing'));
    expect(await screen.findByTestId('document-title')).toHaveTextContent('Subscription Terms');
  });
});
