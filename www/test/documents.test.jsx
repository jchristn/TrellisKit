import { cpSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { signupDocuments } from '@trelliskit/shared';
import { loadSite, resolvePropertyDir } from '../scripts/property.mjs';
import { createSiteMeta, legalSource } from '../src/views/marketing/siteMeta.js';
import { signUpBody, validateSignUp } from '../src/views/auth/SignUpPage.jsx';
import { DOCUMENTS_PATH, LEVELS, onboardingRedirect } from '../src/utils/onboarding.js';
import { PurchaseDocumentsNote } from '../src/components/documents/DocumentChecks.jsx';
import DocumentsView from '../src/views/admin/DocumentsView.jsx';
import MemberDetailDrawer from '../src/views/admin/MemberDetailDrawer.jsx';
import { buildAcceptancesQuery } from '../src/components/documents/DocumentsManager.jsx';
import { SESSION_HINT_KEY } from '../src/context/AuthContext.jsx';
import { PLATFORM_TOKEN_KEY } from '../src/utils/delegation.js';
import { PropertyProvider } from '../src/context/PropertyContext.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { AppRoutes } from '../src/App.jsx';
import strings from '../src/strings.js';
import { authResponse, createFetch, fixtureDir, json, loadFixtureSite, makeMe, renderAdmin, renderSite } from './helpers.jsx';

// The neutral sample property declares a `documents` list (the dojo fixture does not).
const SITE = loadSite(resolvePropertyDir({})).site;
const PROPERTY = SITE.property;
const SIGNUP = signupDocuments(PROPERTY);
const SEPARATE = SIGNUP.separate[0];
const TERMS = SIGNUP.withTerms[0];

let currentLocation = null;
function LocationProbe() {
  currentLocation = useLocation();
  return null;
}

function summary(doc, overrides = {}) {
  const { file: _file, content: _content, ...rest } = doc;
  return { description: null, sequence: 1, updatedUtc: '2026-11-12T00:00:00Z', labels: [], tags: {}, ...rest, ...overrides };
}

function page(objects, { token = null, total = null } = {}) {
  return { success: true, maxResults: 25, skip: 0, continuationToken: token, endOfResults: !token, totalRecords: total === null ? objects.length : total, objects };
}

/** A member-session fetch: `/auth/refresh` restores `me`, the live property is `liveProperty`. */
function memberFetch(handler, { me = null, liveProperty = null } = {}) {
  if (me) window.localStorage.setItem(SESSION_HINT_KEY, '1');
  return createFetch((call) => {
    if (call.path === '/api/v1/auth/refresh') return me ? json(authResponse(me)) : json({ error: 'InvalidRefreshToken', message: 'No session.' }, 401);
    if (call.path === '/api/v1/auth/providers') return json({ email: true, apple: { enabled: false }, google: { enabled: false }, facebook: { enabled: false }, stripe: { enabled: false } });
    if (call.path === '/api/v1/property') return liveProperty ? json(liveProperty) : json({ error: 'NotFound', message: 'none' }, 404);
    return handler(call);
  });
}

describe('legal routes from the property documents', () => {
  const meta = createSiteMeta(SITE);

  it('derives one route per document from its slug, title, and description', () => {
    expect(meta.legalRoutes.map((r) => r.path)).toEqual(PROPERTY.documents.map((d) => '/' + d.slug));
    for (const doc of PROPERTY.documents) {
      const route = meta.getRoute('/' + doc.slug);
      expect(route).toMatchObject({ kind: 'legal', documentKey: doc.key, heading: doc.title, linkLabel: doc.title, version: doc.version, sitemap: true, prerender: true });
      expect(route.title).toBe(doc.title + ' | ' + PROPERTY.name);
      expect(route.description).toContain(PROPERTY.name);
      expect(legalSource(SITE, route)).toBe(readFileSync(path.join(resolvePropertyDir({}), doc.file), 'utf8'));
    }
    expect(meta.footerRoutes.map((r) => r.path)).toEqual(PROPERTY.documents.filter((d) => d.footer).map((d) => '/' + d.slug));
    const sitemap = meta.buildSitemap();
    for (const doc of PROPERTY.documents) expect(sitemap).toContain('<loc>' + meta.canonicalUrl('/' + doc.slug) + '</loc>');
    expect(meta.buildHeadTags('/' + SEPARATE.slug)[0]).toEqual({ tag: 'title', text: SEPARATE.title + ' | ' + PROPERTY.name });
  });

  it('keeps footer-hidden documents routable and falls back to the legal folder without a list', () => {
    const hidden = { ...SITE, documents: SITE.documents.map((d) => (d.key === SEPARATE.key ? { ...d, footer: false, description: 'Custom {name} text.' } : d)) };
    const m = createSiteMeta(hidden);
    expect(m.getRoute('/' + SEPARATE.slug)).not.toBeNull();
    expect(m.getRoute('/' + SEPARATE.slug).description).toBe('Custom ' + PROPERTY.name + ' text.');
    expect(m.footerRoutes.map((r) => r.path)).not.toContain('/' + SEPARATE.slug);
    const dojo = createSiteMeta(loadFixtureSite('dojo'));
    expect(dojo.legalRoutes.map((r) => r.documentKey)).toEqual([null, null, null, null, null]);
    expect(dojo.footerRoutes).toHaveLength(5);
  });

  it('reads each document file from the property folder and refuses a missing one', () => {
    expect(SITE.legal).toEqual({});
    expect(SITE.documents.map((d) => d.key)).toEqual(PROPERTY.documents.map((d) => d.key));
    for (const doc of SITE.documents) expect(doc.content.length).toBeGreaterThan(0);
    const tmp = mkdtempSync(path.join(os.tmpdir(), 'tk-docs-'));
    try {
      cpSync(fixtureDir('dojo'), tmp, { recursive: true });
      const property = JSON.parse(readFileSync(path.join(tmp, 'property.json'), 'utf8'));
      const doc = { key: 'rules', title: 'House Rules', file: 'legal/house-rules.md', version: '1.0', kind: 'Other', acceptance: 'InformationOnly', separateCheckbox: false, reacceptOnChange: false, slug: 'house-rules', footer: true };
      writeFileSync(path.join(tmp, 'property.json'), JSON.stringify({ ...property, documents: [doc] }));
      expect(() => loadSite(tmp)).toThrow(/house-rules\.md not found/);
      writeFileSync(path.join(tmp, 'legal', 'house-rules.md'), '# House rules\n\nBe kind.\n');
      const loaded = loadSite(tmp).site;
      expect(loaded.documents).toEqual([{ ...doc, content: '# House rules\n\nBe kind.\n' }]);
      expect(createSiteMeta(loaded).legalRoutes.map((r) => r.path)).toEqual(['/house-rules']);
      writeFileSync(path.join(tmp, 'property.json'), JSON.stringify({ ...property, documents: [{ ...doc, file: '../outside.md' }] }));
      expect(() => loadSite(tmp)).toThrow(/no file inside/);
    } finally {
      rmSync(tmp, { recursive: true, force: true });
    }
  });
});

describe('the public document page', () => {
  it('renders the build-time copy, then the live version with its version and last updated date', async () => {
    const live = summary(SEPARATE, { version: '1.4', sequence: 3, title: SEPARATE.title + ' (revised)', content: '# x\n\nThe live revision of this agreement.', changeSummary: 'Clarified.' });
    const { fetchImpl, calls } = memberFetch((call) => (call.path === '/api/v1/public/documents/' + SEPARATE.key ? json(live) : undefined));
    renderSite('/' + SEPARATE.slug, { site: SITE, fetchImpl });
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(SEPARATE.title);
    expect(screen.getByText('Version ' + SEPARATE.version)).toBeInTheDocument();
    expect(await screen.findByText('The live revision of this agreement.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(SEPARATE.title + ' (revised)');
    expect(screen.getByText('Version 1.4')).toBeInTheDocument();
    expect(screen.getByText(/^Last updated /)).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/v1/public/documents/' + SEPARATE.key && c.method === 'GET')).toBe(true);
  });

  it('keeps the build-time copy when the live read fails and lists footer documents', async () => {
    const { fetchImpl, calls } = memberFetch(() => undefined);
    renderSite('/' + TERMS.slug, { site: SITE, fetchImpl });
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/public/documents/' + TERMS.key)).toBe(true));
    const firstLine = SITE.documents.find((d) => d.key === TERMS.key).content.split('\n').find((l) => l && !l.startsWith('#') && !l.startsWith('>'));
    expect(screen.getByText(firstLine.trim())).toBeInTheDocument();
    const footer = screen.getByRole('navigation', { name: 'Legal' });
    expect(within(footer).getAllByRole('link').map((a) => a.getAttribute('href'))).toEqual(PROPERTY.documents.filter((d) => d.footer).map((d) => '/' + d.slug));
    expect(within(footer).getByRole('link', { name: SEPARATE.title })).toBeInTheDocument();
  });
});

describe('sign-up with required documents', () => {
  const values = { email: 'new@example.com', password: 'correct horse battery', displayName: 'New Member', dateOfBirth: '1990-05-01', acceptTerms: true, code: '', documents: {} };

  it('validates each separate document and builds acceptedDocuments at the live version', () => {
    const errors = validateSignUp(values, new Date(2026, 10, 11), SIGNUP.separate);
    expect(errors).toEqual({ ['documents.' + SEPARATE.key]: 'Accept the ' + SEPARATE.title + ' to create an account.' });
    expect(validateSignUp({ ...values, documents: { [SEPARATE.key]: true } }, new Date(2026, 10, 11), SIGNUP.separate)).toEqual({});
    const live = { ...PROPERTY, documents: PROPERTY.documents.map((d) => (d.key === SEPARATE.key ? summary(d, { version: '9.9' }) : summary(d))) };
    expect(signUpBody({ ...values, documents: { [SEPARATE.key]: true } }, live).acceptedDocuments).toEqual([{ key: SEPARATE.key, version: '9.9' }]);
    expect(signUpBody({ ...values, documents: { [SEPARATE.key]: true } }, PROPERTY).acceptedDocuments).toEqual([{ key: SEPARATE.key, version: SEPARATE.version }]);
    // A property without a documents list sends the Phase 1 body.
    expect(signUpBody(values, loadFixtureSite('dojo').property)).not.toHaveProperty('acceptedDocuments');
  });

  it('links the terms documents, requires the separate checkbox, and maps its server error', async () => {
    const liveProperty = { ...PROPERTY, documents: PROPERTY.documents.map((d) => summary(d, d.key === SEPARATE.key ? { version: '2.0' } : {})) };
    let attempts = 0;
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path === '/api/v1/auth/register') {
        attempts += 1;
        if (attempts === 1) return json({ error: 'ValidationFailed', message: 'Invalid.', fieldErrors: { ['acceptedDocuments.' + SEPARATE.key]: ['The ' + SEPARATE.title + ' has changed; review version 2.1 and accept it.'] } }, 400);
        return json(authResponse(makeMe({ email: 'new@example.com', pendingDocuments: [] }), { isNewAccount: true }), 201);
      }
      return undefined;
    }, { liveProperty });
    renderSite('/sign-up', { site: SITE, fetchImpl, extra: <LocationProbe /> });
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/property')).toBe(true));
    const terms = await screen.findByRole('checkbox', { name: /or older and I agree to the/ });
    const termsLabel = terms.closest('label');
    expect(within(termsLabel).getAllByRole('link').map((a) => a.getAttribute('href'))).toEqual(SIGNUP.withTerms.map((d) => '/' + d.slug));
    const separate = screen.getByRole('checkbox', { name: new RegExp('I have read and accept the ' + SEPARATE.title) });
    expect(within(separate.closest('label')).getByRole('link')).toHaveAttribute('href', '/' + SEPARATE.slug);
    expect(within(separate.closest('label')).getByRole('link')).toHaveAttribute('target', '_blank');

    await userEvent.type(screen.getByLabelText('Email'), 'new@example.com');
    await userEvent.type(screen.getByLabelText('Password'), 'correct horse battery');
    await userEvent.type(screen.getByLabelText('Display name'), 'New Member');
    await userEvent.type(screen.getByLabelText('Date of birth'), '1990-05-01');
    await userEvent.click(terms);
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }));
    expect(screen.getByText('Accept the ' + SEPARATE.title + ' to create an account.')).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/v1/auth/register')).toBe(false);

    await userEvent.click(separate);
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }));
    expect(await screen.findByText('The ' + SEPARATE.title + ' has changed; review version 2.1 and accept it.')).toBeInTheDocument();
    expect(separate).toHaveAttribute('aria-invalid', 'true');
    const first = calls.find((c) => c.path === '/api/v1/auth/register');
    expect(first.body).toEqual({ email: 'new@example.com', password: 'correct horse battery', displayName: 'New Member', dateOfBirth: '1990-05-01', acceptTerms: true, acceptedDocuments: [{ key: SEPARATE.key, version: '2.0' }] });

    await userEvent.click(screen.getByRole('button', { name: 'Create account' }));
    await waitFor(() => expect(currentLocation.pathname).toBe('/app'));
  });
});

describe('re-acceptance', () => {
  const pending = [summary(SEPARATE, { version: '2.0', sequence: 2 })];

  it('routes to the review step when documents are pending, at the profile level and above', () => {
    const me = makeMe({ pendingDocuments: pending });
    expect(onboardingRedirect(me, LEVELS.member)).toBe(DOCUMENTS_PATH);
    expect(onboardingRedirect(me, LEVELS.profile)).toBe(DOCUMENTS_PATH);
    expect(onboardingRedirect(me, LEVELS.age)).toBeNull();
    expect(onboardingRedirect(makeMe({ pendingDocuments: [] }))).toBeNull();
    expect(onboardingRedirect(makeMe({ ageAttested: false, pendingDocuments: pending }))).toBe('/app/onboarding/age');
  });

  it('blocks the member app until each pending document is accepted, then continues', async () => {
    const me = makeMe({ pendingDocuments: pending });
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path === '/api/v1/me/documents/accept') return json({ ...me, pendingDocuments: [] });
      return undefined;
    }, { me });
    renderSite('/app/feed', { site: SITE, fetchImpl, extra: <LocationProbe /> });
    expect(await screen.findByRole('heading', { name: 'Review and accept' })).toBeInTheDocument();
    expect(currentLocation.pathname).toBe(DOCUMENTS_PATH);
    const item = screen.getByText(SEPARATE.title).closest('li');
    expect(within(item).getByText(/^Version 2\.0/)).toBeInTheDocument();
    expect(within(item).getByRole('link', { name: /Read in full/ })).toHaveAttribute('href', '/' + SEPARATE.slug);
    await userEvent.click(screen.getByRole('button', { name: 'Accept and continue' }));
    expect(screen.getByText('Accept the ' + SEPARATE.title + ' to continue.')).toBeInTheDocument();
    await userEvent.click(within(item).getByRole('checkbox'));
    await userEvent.click(screen.getByRole('button', { name: 'Accept and continue' }));
    await waitFor(() => expect(currentLocation.pathname).toBe('/app'));
    expect(calls.find((c) => c.path === '/api/v1/me/documents/accept').body).toEqual({ documents: [{ key: SEPARATE.key, version: '2.0' }] });
  });

  it('shows the review step after any 403 DocumentAcceptanceRequired', async () => {
    const me = makeMe({ pendingDocuments: [] });
    const gated = { ...me, pendingDocuments: pending };
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path === '/api/v1/auth/me') return json(gated);
      if (call.path.startsWith('/api/v1/feed')) return json({ error: 'DocumentAcceptanceRequired', message: 'Review and accept the updated documents.', documents: [SEPARATE.key] }, 403);
      return undefined;
    }, { me });
    renderSite('/app/feed', { site: SITE, fetchImpl, extra: <LocationProbe /> });
    expect(await screen.findByRole('heading', { name: 'Review and accept' }, { timeout: 4000 })).toBeInTheDocument();
    expect(currentLocation.pathname).toBe(DOCUMENTS_PATH);
    expect(calls.filter((c) => c.path === '/api/v1/auth/me').length).toBeGreaterThanOrEqual(1);
  });

  it('asks for the terms and separate documents with a social account age attestation', async () => {
    const termsPending = SIGNUP.withTerms.map((d) => summary(d));
    const me = makeMe({ ageAttested: false, profileComplete: true, pendingDocuments: [...termsPending, ...pending] });
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path === '/api/v1/me/age-attestation') return json({ ...me, ageAttested: true, pendingDocuments: [] });
      return undefined;
    }, { me });
    renderSite('/app/onboarding/age', { site: SITE, fetchImpl, extra: <LocationProbe /> });
    expect(await screen.findByRole('heading', { name: 'Confirm your age' })).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Date of birth'), '1990-05-01');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(screen.getByText('You must accept the terms to create an account.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('checkbox', { name: /or older and I agree to the/ }));
    await userEvent.click(screen.getByRole('checkbox', { name: new RegExp('I have read and accept the ' + SEPARATE.title) }));
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    await waitFor(() => expect(currentLocation.pathname).toBe('/app'));
    expect(calls.find((c) => c.path === '/api/v1/me/age-attestation').body).toEqual({ dateOfBirth: '1990-05-01', acceptTerms: true, acceptedDocuments: [{ key: SEPARATE.key, version: '2.0' }] });
  });

  it('gates the admin console for a signed-in SystemAdmin with pending documents', async () => {
    const admin = makeMe({ roles: [{ role: 'SystemAdmin', resourceType: 'Global', resourceId: '*' }], pendingDocuments: pending });
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path === '/api/v1/me/documents/accept') return json({ ...admin, pendingDocuments: [] });
      return undefined;
    }, { me: admin });
    renderSite('/admin/home', { site: SITE, fetchImpl });
    expect(await screen.findByRole('heading', { name: 'Review and accept' }, { timeout: 4000 })).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Admin navigation' })).toBeNull();
    await userEvent.click(screen.getByRole('checkbox', { name: new RegExp(SEPARATE.title) }));
    await userEvent.click(screen.getByRole('button', { name: 'Accept and continue' }));
    expect(await screen.findByRole('navigation', { name: 'Admin navigation' }, { timeout: 4000 })).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/v1/me/documents/accept')).toBe(true);
  });
});

describe('purchase screens', () => {
  it('link the ShownAtPurchase documents', () => {
    render(<PurchaseDocumentsNote property={PROPERTY} />);
    const links = screen.getAllByRole('link');
    expect(links.map((a) => a.getAttribute('href'))).toEqual(PROPERTY.documents.filter((d) => d.acceptance === 'ShownAtPurchase').map((d) => '/' + d.slug));
    const { container } = render(<PurchaseDocumentsNote property={{ documents: [] }} />);
    expect(container).toBeEmptyDOMElement();
  });
});

const ADMIN_DOCS = PROPERTY.documents.map((d) => summary(d, { source: 'Seed', versionCount: d.key === SEPARATE.key ? 2 : 1, acceptedCurrentCount: 3, acceptedAnyCount: 5, requiredSequence: 1, syncsFromFolder: true }));
const VERSION = (n, overrides = {}) => ({ id: 'dcv_' + n, documentKey: SEPARATE.key, version: '1.' + n, sequence: n, title: SEPARATE.title, changeSummary: 'Change ' + n, requiresReacceptance: n === 2, source: n === 1 ? 'Seed' : 'Edit', contentLength: 10, createdByUserId: null, createdUtc: '2026-11-1' + n + 'T00:00:00Z', acceptanceCount: n, labels: [], tags: {}, ...overrides });

function adminDocsFetch(extra = () => undefined) {
  return createFetch((call) => {
    if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
    const custom = extra(call);
    if (custom) return custom;
    if (call.path === '/api/v1/admin/documents/enumerate' && call.method === 'POST') return json({ ...page(ADMIN_DOCS), syncsFromFolder: true });
    if (call.path === '/api/v1/admin/documents/' + SEPARATE.key && call.method === 'GET') return json({ ...ADMIN_DOCS.find((d) => d.key === SEPARATE.key), content: '# Agreement\n\nCurrent agreement text.', changeSummary: null });
    if (call.path === '/api/v1/admin/documents/' + SEPARATE.key + '/versions/enumerate') {
      return json(call.body.continuationToken ? page([VERSION(1)]) : page([VERSION(2)], { token: 'next-1', total: 2 }));
    }
    if (call.path === '/api/v1/admin/documents/' + SEPARATE.key + '/versions/dcv_2') return json({ ...VERSION(2), content: 'Version two body.' });
    if (call.path === '/api/v1/admin/documents/acceptances/enumerate') {
      return json(page([{ id: 'dac_1', documentKey: SEPARATE.key, documentTitle: SEPARATE.title, version: '1.0', sequence: 1, userId: 'usr_1', user: { id: 'usr_1', displayName: 'Sam Rivera', handle: 'sam', avatarUrl: null }, method: 'Signup', client: 'Ios', acceptedUtc: '2026-11-12T00:00:00Z', createdUtc: '2026-11-12T00:00:00Z', labels: [], tags: {} }]));
    }
    if (call.path === '/api/v1/labels/enumerate' || call.path === '/api/v1/tags/keys/enumerate') return json(page([]));
    return undefined;
  });
}

describe('the admin Documents page', () => {
  it('lists documents, previews content, pages versions, and publishes a new version', async () => {
    const { fetchImpl, calls } = adminDocsFetch((call) => {
      if (call.path === '/api/v1/admin/documents/' + SEPARATE.key + '/versions' && call.method === 'POST') return json({ ...ADMIN_DOCS.find((d) => d.key === SEPARATE.key), version: call.body.version, sequence: 3 }, 201);
      return undefined;
    });
    renderAdmin(<DocumentsView />, { fetchImpl, path: '/admin/documents', routePath: '/admin/documents', site: SITE });
    expect(await screen.findByRole('heading', { name: 'Documents' })).toBeInTheDocument();
    expect(await screen.findByText(/syncs documents from the property folder/)).toBeInTheDocument();
    const table = screen.getByRole('table', { name: 'Documents' });
    const row = within(table).getByText(SEPARATE.title).closest('tr');
    expect(within(row).getByText('Required at sign-up')).toBeInTheDocument();
    expect(within(row).getAllByText('3')).toHaveLength(1);
    expect(within(row).getByText('5')).toBeInTheDocument();

    await userEvent.click(row);
    expect(await screen.findByText('Current agreement text.')).toBeInTheDocument();
    expect(await screen.findByText('Change 2')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Load more' }));
    expect(await screen.findByText('Change 1')).toBeInTheDocument();
    expect(calls.filter((c) => c.path.endsWith('/versions/enumerate')).map((c) => c.body)).toEqual([{ maxResults: 25 }, { maxResults: 25, continuationToken: 'next-1' }]);
    expect(screen.queryByRole('button', { name: 'Load more' })).toBeNull();

    await userEvent.click(screen.getAllByRole('button', { name: 'View' })[0]);
    expect(await screen.findByText('Version two body.')).toBeInTheDocument();
    await userEvent.click(screen.getAllByRole('button', { name: /close/i }).at(-1));

    await userEvent.click(screen.getByRole('button', { name: 'Publish new version' }));
    expect(screen.getByLabelText('Content (Markdown)')).toHaveValue('# Agreement\n\nCurrent agreement text.');
    // Every field is labelled, including the optional title (its label once pointed at the form heading).
    expect(screen.getByLabelText('Title (optional)').tagName).toBe('INPUT');
    const reaccept = screen.getByRole('checkbox', { name: /Members must accept this version/ });
    expect(reaccept).toBeChecked();
    await userEvent.click(screen.getByRole('button', { name: 'Publish' }));
    expect(screen.getAllByText('Required.')).toHaveLength(2);
    await userEvent.type(screen.getByLabelText('Version'), '1.3');
    await userEvent.type(screen.getByLabelText('Change summary'), 'Clarified the risks section.');
    await userEvent.click(reaccept);
    await userEvent.click(screen.getByRole('button', { name: 'Publish' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/admin/documents/' + SEPARATE.key + '/versions')).toBe(true));
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/admin/documents/' + SEPARATE.key + '/versions').body).toEqual({
      version: '1.3', content: '# Agreement\n\nCurrent agreement text.', changeSummary: 'Clarified the risks section.', requireReacceptance: false
    });
  });

  it('shows the version-exists error and hides re-acceptance for non-gating documents', async () => {
    const info = ADMIN_DOCS.find((d) => d.acceptance === 'InformationOnly');
    const { fetchImpl } = adminDocsFetch((call) => {
      if (call.path === '/api/v1/admin/documents/' + info.key && call.method === 'GET') return json({ ...info, content: 'Info body.' });
      if (call.path === '/api/v1/admin/documents/' + info.key + '/versions/enumerate') return json(page([]));
      if (call.path === '/api/v1/admin/documents/' + info.key + '/versions') return json({ error: 'VersionExists', message: 'Exists.' }, 409);
      return undefined;
    });
    renderAdmin(<DocumentsView />, { fetchImpl, path: '/admin/documents', routePath: '/admin/documents', site: SITE });
    await userEvent.click((await screen.findByText(info.title)).closest('tr'));
    await userEvent.click(await screen.findByRole('button', { name: 'Publish new version' }));
    expect(screen.queryByRole('checkbox', { name: /Members must accept this version/ })).toBeNull();
    await userEvent.type(screen.getByLabelText('Version'), '1.0');
    await userEvent.type(screen.getByLabelText('Change summary'), 'Same label.');
    await userEvent.click(screen.getByRole('button', { name: 'Publish' }));
    expect(await screen.findByText('That version already exists. Choose another label.')).toBeInTheDocument();
  });

  it('filters acceptances by document, method, client, search, and labels', async () => {
    expect(buildAcceptancesQuery({ search: 'sam', documentKey: 'terms', method: 'Signup', client: 'Web', version: '1.0' }, { pageNumber: 2, pageSize: 25, sort: '-createdUtc', meta: { labels: ['vip'], labelMatch: 'Any', tags: { cohort: '*' } } })).toEqual({
      maxResults: 25, skip: 25, ordering: 'CreatedDescending', search: 'sam', createdAfterUtc: undefined, createdBeforeUtc: undefined, labels: ['vip'], labelMatch: 'Any', tags: { cohort: '*' },
      filters: { documentKey: 'terms', method: 'Signup', client: 'Web', version: '1.0' }
    });
    const { fetchImpl, calls } = adminDocsFetch();
    renderAdmin(<DocumentsView />, { fetchImpl, path: '/admin/documents', routePath: '/admin/documents', site: SITE });
    await userEvent.click(await screen.findByRole('tab', { name: 'Acceptances' }));
    const table = await screen.findByRole('table', { name: 'Acceptances' });
    expect(await within(table).findByText('Sam Rivera')).toBeInTheDocument();
    expect(within(table).getByText('iOS')).toBeInTheDocument();
    expect(within(table).getByText('Sign-up')).toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText('Document'), SEPARATE.key);
    await userEvent.selectOptions(screen.getByLabelText('Method'), 'Reacceptance');
    await userEvent.selectOptions(screen.getByLabelText('Client'), 'Android');
    await userEvent.type(screen.getByLabelText('Member name or email'), 'sam');
    await userEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
    await waitFor(() => {
      const last = calls.filter((c) => c.path === '/api/v1/admin/documents/acceptances/enumerate').at(-1);
      expect(last.body).toMatchObject({ search: 'sam', filters: { documentKey: SEPARATE.key, method: 'Reacceptance', client: 'Android' } });
    });
    // Label and tag filters sit in the same panel (docs/ENUMERATION_AND_METADATA.md section 3).
    await userEvent.click(screen.getByRole('button', { name: strings.metadata.filters }));
    expect(screen.getByRole('group', { name: strings.metadata.filters })).toBeInTheDocument();
    expect(screen.getByRole('radiogroup', { name: strings.metadata.labelMatch })).toBeInTheDocument();
  });
});

describe('the member drawer Documents section', () => {
  it('lists the member acceptance history with Load more', async () => {
    const member = { id: 'usr_2', displayName: 'Alex', email: 'a@example.test', flags: {}, roles: [], labels: [], tags: {} };
    const row = (n) => ({ id: 'dac_' + n, documentKey: SEPARATE.key, documentTitle: SEPARATE.title, version: '1.' + n, sequence: n, userId: 'usr_2', user: null, method: n === 1 ? 'Signup' : 'Reacceptance', client: 'Web', acceptedUtc: '2026-11-1' + n + 'T00:00:00Z', createdUtc: '2026-11-12T00:00:00Z', labels: [], tags: {} });
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
      if (call.path === '/api/v1/admin/members/usr_2') return json(member);
      if (call.path === '/api/v1/admin/moderators') return json({ user: { id: 'usr_2' }, roles: [] });
      if (call.path === '/api/v1/admin/documents/acceptances/enumerate') return json(call.body.continuationToken ? page([row(1)]) : page([row(2)], { token: 't1' }));
      return undefined;
    });
    renderAdmin(<MemberDetailDrawer id="usr_2" onClose={() => {}} onChanged={() => {}} />, { fetchImpl, site: SITE });
    const section = (await screen.findByRole('heading', { name: 'Documents' })).closest('section');
    await userEvent.click(within(section).getByRole('button', { name: 'Show acceptance history' }));
    expect(await within(section).findByText('1.2')).toBeInTheDocument();
    expect(within(section).getByText('Re-acceptance')).toBeInTheDocument();
    await userEvent.click(within(section).getByRole('button', { name: 'Load more' }));
    expect(await within(section).findByText('1.1')).toBeInTheDocument();
    const bodies = calls.filter((c) => c.path === '/api/v1/admin/documents/acceptances/enumerate').map((c) => c.body);
    expect(bodies).toEqual([{ maxResults: 25, filters: { userId: 'usr_2' } }, { maxResults: 25, continuationToken: 't1', filters: { userId: 'usr_2' } }]);
    expect(within(section).queryByRole('button', { name: 'Load more' })).toBeNull();
  });

  it('is hidden for a property without documents', async () => {
    const member = { id: 'usr_2', displayName: 'Alex', email: 'a@example.test', flags: {}, roles: [], labels: [], tags: {} };
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
      if (call.path === '/api/v1/admin/members/usr_2') return json(member);
      return undefined;
    });
    renderAdmin(<MemberDetailDrawer id="usr_2" onClose={() => {}} onChanged={() => {}} />, { fetchImpl, site: loadFixtureSite('dojo') });
    expect(await screen.findByRole('heading', { name: 'Posting' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Documents' })).toBeNull();
  });
});

describe('the platform console Documents tab', () => {
  it('reads one property through the platform document routes', async () => {
    const PID = 'app_test_Documents';
    window.sessionStorage.setItem(PLATFORM_TOKEN_KEY, 'tkp_1');
    const record = { id: PID, slug: 'second', name: 'Second Example', status: 'Enabled', domains: [], apps: [], memberCount: 1, createdUtc: '2026-11-11T20:00:00Z', updatedUtc: '2026-11-11T20:00:00Z', labels: [], tags: {} };
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/health') return json({ status: 'Healthy', version: '1.0.0', startedUtc: '2026-11-11T00:00:00Z' });
      if (call.path === '/api/v1/platform/auth/me') return json({ id: 'pad_1', email: 'ops@example.test', displayName: 'Ops', labels: [], tags: {} });
      if (call.path === '/api/v1/platform/properties/' + PID) return json(record);
      if (call.path === '/api/v1/platform/properties/' + PID + '/documents/enumerate') return json({ ...page(ADMIN_DOCS), syncsFromFolder: false });
      if (call.path === '/api/v1/platform/properties/' + PID + '/documents/acceptances/enumerate') return json(page([]));
      return undefined;
    });
    render(
      <PropertyProvider site={loadFixtureSite('dojo')}>
        <ThemeProvider>
          <MemoryRouter initialEntries={['/platform/properties/' + PID + '?tab=documents']}>
            <AppRoutes fetchImpl={fetchImpl} restoreOnLoad={false} />
          </MemoryRouter>
        </ThemeProvider>
      </PropertyProvider>
    );
    expect(await screen.findByText(SEPARATE.title, {}, { timeout: 4000 })).toBeInTheDocument();
    expect(document.getElementById('tab-documents')).toHaveAttribute('aria-selected', 'true');
    expect(screen.queryByText(/syncs documents from the property folder/)).toBeNull();
    await userEvent.click(screen.getByRole('tab', { name: 'Acceptances' }));
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/platform/properties/' + PID + '/documents/acceptances/enumerate')).toBe(true));
    expect(await screen.findByText('No acceptances yet.')).toBeInTheDocument();
  });
});
