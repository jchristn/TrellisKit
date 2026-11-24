import { editorFields, emptyHistoryEntry, formatRankValue, profileFieldsOf, propertyTokens, toFormValues, validateProfileValues } from '@trelliskit/shared';
import { act, render, screen, waitFor } from '@testing-library/react-native';
import fs from 'fs';
import path from 'path';
import os from 'os';
import { AppState, Text } from 'react-native';
import { createFileCache, loadCachedProperty, PropertyCacheFile, saveCachedProperty } from '../src/property/cache';
import { storeConfig } from '../src/billing/purchases';
import { toProfileBody } from '../src/components/profile/ProfileForm';
import { validateRegistration } from '../src/forms';
import { profileErrorTexts } from '../src/profile/fields';
import { fillFromProperty, PropertyProvider, resolveProperty, useProperty, webUrl } from '../src/property';
import strings, { stringTemplates } from '../src/strings';
import { buildColors, neutralDarkColors, neutralLightColors } from '../src/theme';
import { installFakeServer, resetEnvironment } from '../test-utils/fakeServer';
import { property as active } from '../test-utils/property';

const FIXTURES = path.resolve(__dirname, '../test-utils/fixtures');
const plain = JSON.parse(fs.readFileSync(path.join(FIXTURES, 'plain/property.json'), 'utf8'));
const custom = JSON.parse(fs.readFileSync(path.join(FIXTURES, 'custom/property.json'), 'utf8'));

/** Words, domains, prices, and ids that belong to a property, never to engine code. */
const PROPERTY_WORDS = /\bbjj\b|joel|jiu.?jitsu|\bbelts?\b|\bstripes\b|lineage|academy|\bdegrees?\b|https?:\/\/(?!['"]|127\.0\.0\.1|10\.0\.2\.2|apps\.apple\.com|play\.google\.com)|\$\d|membership\.annual/i;

function collect(value, out = []) {
  if (typeof value === 'string') out.push(value);
  else if (typeof value === 'function') collect(value('X', 'Y'), out);
  else if (Array.isArray(value)) value.forEach((v) => collect(v, out));
  else if (value && typeof value === 'object') Object.values(value).forEach((v) => collect(v, out));
  return out;
}

function sourceFiles(dir, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) sourceFiles(full, out);
    else if (/\.(js|jsx)$/.test(entry.name)) out.push(full);
  }
  return out;
}

function loadAppConfig(dir) {
  const previous = process.env.TK_PROPERTY_DIR;
  if (dir === undefined) delete process.env.TK_PROPERTY_DIR;
  else process.env.TK_PROPERTY_DIR = dir;
  try {
    let config;
    jest.isolateModules(() => {
      config = require('../app.config.js')();
    });
    return config;
  } finally {
    if (previous === undefined) delete process.env.TK_PROPERTY_DIR;
    else process.env.TK_PROPERTY_DIR = previous;
  }
}

describe('engine code is property-neutral', () => {
  test('string templates contain engine text only', () => {
    for (const text of collect(stringTemplates)) {
      expect({ text, hit: PROPERTY_WORDS.test(text) }).toEqual({ text, hit: false });
    }
  });

  test('app/ and src/ name no property, sport, person, domain, price, or product id', () => {
    const files = [...sourceFiles(path.resolve(__dirname, '../app')), ...sourceFiles(path.resolve(__dirname, '../src'))];
    expect(files.length).toBeGreaterThan(20);
    for (const file of files) {
      const lines = fs.readFileSync(file, 'utf8').split('\n');
      const hits = lines.filter((line) => PROPERTY_WORDS.test(line));
      expect({ file: path.relative(path.resolve(__dirname, '..'), file), hits }).toEqual({ file: path.relative(path.resolve(__dirname, '..'), file), hits: [] });
    }
  });
});

describe('token substitution', () => {
  test('{name}, {minimumAge}, {owner}, {interval}, and {productName} are filled from the property', () => {
    const s = fillFromProperty(stringTemplates, plain);
    expect(s.appName).toBe('Plain Reading Circle');
    expect(s.welcome.membershipNote).toBe('Members only. 13 and older.');
    expect(s.register.errors.underage).toBe('You must be 13 or older to join Plain Reading Circle.');
    expect(s.paywall.benefits[0]).toBe('Posts and videos from Pat Fixture');
    expect(s.paywall.pricePerYear('$4.99')).toBe('$4.99 per month');
    expect(s.paywall.productFallbackName).toBe('Plain Circle Pass');
    expect(s.feed.summary).toBe('Posts from Pat Fixture and the circle.');
  });

  test('no placeholder is left unfilled for a complete property', () => {
    for (const text of collect(fillFromProperty(stringTemplates, custom))) {
      expect({ text, open: /\{[A-Za-z]+\}/.test(text) }).toEqual({ text, open: false });
    }
  });

  test('the default strings are the templates filled from the bundled property', () => {
    expect(strings.appName).toBe(active.name);
    expect(strings.errors.ageRequirementNotMet).toBe(fillFromProperty(stringTemplates, active).errors.ageRequirementNotMet);
    expect(strings.appName).not.toBe(stringTemplates.appName);
  });

  test('unknown tokens are left as they are', () => {
    expect(fillFromProperty({ a: 'Hello {nobody} at {name}' }, plain).a).toBe('Hello {nobody} at Plain Reading Circle');
  });
});

describe('property-driven configuration', () => {
  test('app.config.js defaults to the neutral sample property', () => {
    const config = loadAppConfig(undefined);
    expect(config.slug).toBe('example');
    expect(config.icon).toMatch(/samples\/example\/mobile\/icon\.png$/);
  });

  test('app.config.js takes identity, colors, and images from TK_PROPERTY_DIR', () => {
    const config = loadAppConfig(path.join(FIXTURES, 'custom'));
    expect(config).toMatchObject({ name: 'Custom Climbing Club', slug: 'custom', scheme: 'customclub' });
    expect(config.ios.bundleIdentifier).toBe('test.custom.app');
    expect(config.android.package).toBe('test.custom.app');
    expect(config.android.adaptiveIcon.foregroundImage).toMatch(/fixtures\/custom\/mobile\/android-icon-foreground\.png$/);
    const splash = config.plugins.find((p) => Array.isArray(p) && p[0] === 'expo-splash-screen')[1];
    expect(splash.image).toMatch(/custom\/mobile\/splash-icon\.png$/);
    expect(splash.dark).toMatchObject({ backgroundColor: '#0F140F' });
    expect(splash.dark.image).toMatch(/custom\/mobile\/splash-icon-dark\.png$/);
    expect(config.extra.eas.projectId).toBe('00000000-0000-4000-8000-000000000000');
    expect(config.extra.property.name).toBe('Custom Climbing Club');
    expect(config.extra.property.web).toBeUndefined();
  });

  test('a relative TK_PROPERTY_DIR resolves from the engine root', () => {
    const relative = path.relative(path.resolve(__dirname, '../..'), path.join(FIXTURES, 'plain'));
    expect(loadAppConfig(relative).name).toBe('Plain Reading Circle');
  });

  test('a missing property folder fails with a message naming it', () => {
    expect(() => loadAppConfig('/nowhere/at/all')).toThrow(/Property not found: \/nowhere\/at\/all/);
  });

  test('theme brand colors come from the property palette', () => {
    const light = buildColors(neutralLightColors, plain.brand.colors.light);
    const dark = buildColors(neutralDarkColors, plain.brand.colors.dark);
    expect(light).toMatchObject({ primary: '#2E6B30', onPrimary: '#FFFFFF', accent: '#1B1F1B', accentSoft: '#E7F1E7' });
    expect(dark).toMatchObject({ primary: '#8FD18F', onPrimary: '#0F140F' });
    expect(light.background).toBe(neutralLightColors.background);
  });

  test('legal links use domains.web and the store product id comes from the property', () => {
    expect(webUrl(plain, '/terms')).toBe('https://plain.test/terms');
    expect(storeConfig({ appleIap: { enabled: true, productId: 'server.id' } }, 'ios', plain.membership.storeProductId)).toEqual({ enabled: true, productId: 'plain.pass.monthly' });
    expect(storeConfig({ appleIap: { enabled: true, productId: 'server.id' } }, 'ios', null).productId).toBe('server.id');
  });

  test('the minimum age comes from the property', () => {
    const today = new Date(2026, 10, 11);
    const base = { displayName: 'Alex', email: 'a@example.com', password: 'long enough pw', acceptTerms: true };
    const fourteen = { ...base, dob: { month: '1', day: '1', year: '2012' } };
    expect(validateRegistration(fourteen, today, plain).errors).toEqual({});
    expect(validateRegistration(fourteen, today, custom).errors.dateOfBirth).toBeTruthy();
  });

  test('rank text uses only configured labels and levels', () => {
    const rank = profileFieldsOf(custom).find((f) => f.type === 'Rank');
    expect(formatRankValue(rank, { key: 'Gold', level: 2 })).toBe('Gold, 2 pips');
    expect(formatRankValue(rank, { key: 'Gold', level: 1 })).toBe('Gold, 1 pip');
    expect(formatRankValue(rank, { key: 'Green', level: 0 })).toBe('Green');
    expect(profileFieldsOf(plain).find((f) => f.type === 'Rank')).toBeUndefined();
  });

  test('a property without ranks needs no rank and sends only the built-ins', () => {
    const fields = profileFieldsOf(plain);
    const editable = editorFields(fields);
    const initial = toFormValues(fields, {});
    expect(validateProfileValues(editable, initial, { fields })).toEqual({});
    const body = toProfileBody({ displayName: 'Alex', handle: 'alex', bio: '', profile: initial }, editable, initial, fields);
    expect(body).toEqual({ displayName: 'Alex', handle: 'alex', bio: null });
  });

  test('a property with ranks requires one and sends values in profile, never the legacy fields', () => {
    const fields = profileFieldsOf(custom);
    const editable = editorFields(fields);
    const initial = toFormValues(fields, {});
    const history = fields.find((f) => f.type === 'History');
    const values = { ...initial, rankHistory: [emptyHistoryEntry(history)] };
    expect(profileErrorTexts(validateProfileValues(editable, values, { fields }))).toEqual({ 'profile.rank': 'This field is required.', 'profile.rankHistory[0].rank': 'This field is required.' });
    const entry = { rowKey: 'r1', id: 'rh_1', values: { rank: { key: 'Green', level: 0 }, awardedBy: { name: 'Sam', userId: null }, affiliation: '', dateAwarded: '2021-01-01' } };
    const filled = { ...initial, rank: { key: 'Gold', level: 5 }, affiliation: 'Crag Team', goals: { keys: ['Else'], otherText: 'Hiking' }, rankHistory: [entry] };
    const body = toProfileBody({ displayName: 'Alex', handle: 'alex', bio: '', profile: filled }, editable, initial, fields);
    expect(body.profile).toEqual({
      rank: { key: 'Gold', level: 2 },
      affiliation: 'Crag Team',
      goals: { keys: ['Else'], otherText: 'Hiking' },
      rankHistory: [{ id: 'rh_1', values: { rank: { key: 'Green' }, awardedBy: { name: 'Sam', userId: null }, dateAwarded: '2021-01-01' } }]
    });
    for (const key of ['rank', 'rankLevel', 'affiliation', 'goals', 'goalsOther', 'rankHistory', 'city', 'region', 'country']) expect(body).not.toHaveProperty(key);
  });
});

describe('PropertyProvider', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  function Probe() {
    const property = useProperty();
    return <Text testID="probe">{property.name + '|' + String(property.profile.rank.enabled)}</Text>;
  }

  test('starts from the bundled copy, then uses GET /api/v1/property', async () => {
    const server = installFakeServer({ 'GET /property': { ...plain, web: undefined } });
    await render(
      <PropertyProvider>
        <Probe />
      </PropertyProvider>
    );
    await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('Plain Reading Circle|false'));
    expect(server.called('GET /property')).toHaveLength(1);
  });

  test('keeps the bundled copy when the fetch fails (first launch offline)', async () => {
    const server = installFakeServer({ 'GET /property': { __network: true } });
    await render(
      <PropertyProvider>
        <Probe />
      </PropertyProvider>
    );
    await waitFor(() => expect(server.called('GET /property')).toHaveLength(1));
    expect(screen.getByTestId('probe')).toHaveTextContent(active.name + '|' + String(active.profile.rank.enabled));
  });

  describe('ETag revalidation', () => {
    let listeners;
    let renders;
    function CountingProbe() {
      const property = useProperty();
      renders += 1;
      return <Text testID="probe">{property.name}</Text>;
    }
    beforeEach(() => {
      listeners = [];
      renders = 0;
      jest.spyOn(AppState, 'addEventListener').mockImplementation((type, fn) => {
        if (type === 'change') listeners.push(fn);
        return { remove: jest.fn() };
      });
    });
    afterEach(() => jest.restoreAllMocks());
    const foreground = () => act(async () => listeners.forEach((fn) => fn('active')));

    test('starts from the cached runtime copy before the network answers, and a 304 keeps it without re-rendering', async () => {
      const cache = { load: jest.fn(async () => ({ property: { ...plain, web: undefined }, etag: '"v1"' })), save: jest.fn(async () => true) };
      let answer;
      const server = installFakeServer({ 'GET /property': () => new Promise((resolve) => (answer = resolve)) });
      await render(
        <PropertyProvider cache={cache}>
          <CountingProbe />
        </PropertyProvider>
      );
      await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('Plain Reading Circle'));
      await waitFor(() => expect(server.called('GET /property')).toHaveLength(1));
      const call = server.called('GET /property')[0];
      expect(call.headers).toMatchObject({ 'If-None-Match': '"v1"', 'X-TK-Property': active.id, 'X-TK-App': active.mobile.bundleId, 'X-Client': 'mobile' });
      const before = renders;
      await act(async () => answer({ status: 304, headers: { ETag: '"v1"' } }));
      expect(renders).toBe(before);
      expect(screen.getByTestId('probe')).toHaveTextContent('Plain Reading Circle');
      expect(cache.save).not.toHaveBeenCalled();
    });

    test('a 200 replaces the copy and is cached with its ETag; returning to the foreground revalidates with it', async () => {
      const cache = { load: jest.fn(async () => null), save: jest.fn(async () => true) };
      const responses = [{ status: 200, body: { ...plain, web: undefined }, headers: { ETag: '"v2"' } }, { status: 200, body: { ...custom, web: undefined }, headers: { ETag: '"v3"' } }, { status: 304, headers: { ETag: '"v3"' } }];
      const server = installFakeServer({ 'GET /property': () => responses.shift() });
      await render(
        <PropertyProvider cache={cache}>
          <CountingProbe />
        </PropertyProvider>
      );
      await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('Plain Reading Circle'));
      expect(server.called('GET /property')[0].headers['If-None-Match']).toBeUndefined();
      await waitFor(() => expect(cache.save).toHaveBeenCalledWith(expect.objectContaining({ name: 'Plain Reading Circle' }), '"v2"'));

      await foreground();
      await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('Custom Climbing Club'));
      expect(server.called('GET /property')[1].headers['If-None-Match']).toBe('"v2"');
      await waitFor(() => expect(cache.save).toHaveBeenLastCalledWith(expect.objectContaining({ name: 'Custom Climbing Club' }), '"v3"'));

      await foreground();
      await waitFor(() => expect(server.called('GET /property')).toHaveLength(3));
      expect(server.called('GET /property')[2].headers['If-None-Match']).toBe('"v3"');
      expect(screen.getByTestId('probe')).toHaveTextContent('Custom Climbing Club');
      expect(cache.save).toHaveBeenCalledTimes(2);
    });

    test('a cache that cannot be read or written is harmless', async () => {
      const cache = { load: jest.fn(async () => { throw new Error('unreadable'); }), save: jest.fn(async () => { throw new Error('disk full'); }) };
      installFakeServer({ 'GET /property': { status: 200, body: { ...plain, web: undefined }, headers: { ETag: '"v9"' } } });
      await render(
        <PropertyProvider cache={cache}>
          <CountingProbe />
        </PropertyProvider>
      );
      await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('Plain Reading Circle'));
      await waitFor(() => expect(cache.save).toHaveBeenCalled());
    });

    test('a fixed property skips the cache and the fetch', async () => {
      const cache = { load: jest.fn(), save: jest.fn() };
      const server = installFakeServer({ 'GET /property': plain });
      await render(
        <PropertyProvider property={custom} cache={cache}>
          <CountingProbe />
        </PropertyProvider>
      );
      expect(screen.getByTestId('probe')).toHaveTextContent('Custom Climbing Club');
      expect(cache.load).not.toHaveBeenCalled();
      expect(server.called('GET /property')).toHaveLength(0);
    });

    test('the file cache round-trips the property and ETag, and ignores corrupt files', async () => {
      const files = new Map();
      class FakeFile {
        constructor(uri) {
          this.uri = uri;
        }
        get exists() {
          return files.has(this.uri);
        }
        create() {
          files.set(this.uri, '');
        }
        async text() {
          return files.get(this.uri);
        }
        async write(text) {
          files.set(this.uri, text);
        }
      }
      const cache = createFileCache(() => new FakeFile('file:///document/' + PropertyCacheFile));
      await expect(cache.load()).resolves.toBeNull();
      await expect(cache.save({ name: 'Plain Reading Circle' }, '"v4"')).resolves.toBe(true);
      await expect(cache.load()).resolves.toEqual({ property: { name: 'Plain Reading Circle' }, etag: '"v4"' });
      files.set('file:///document/' + PropertyCacheFile, '{not json');
      await expect(cache.load()).resolves.toBeNull();
      const broken = createFileCache(() => {
        throw new Error('no file system');
      });
      await expect(broken.load()).resolves.toBeNull();
      await expect(broken.save({}, null)).resolves.toBe(false);
    });

    test('the default file cache never throws when the file system is unavailable', async () => {
      await expect(loadCachedProperty()).resolves.toBeNull();
      await expect(saveCachedProperty(plain, '"v1"')).resolves.toBe(false);
    });
  });

  test('ignores an invalid runtime property', () => {
    expect(resolveProperty(active, { schemaVersion: 99 })).toBe(active);
    expect(propertyTokens(resolveProperty(active, plain)).name).toBe('Plain Reading Circle');
  });
});


describe('mobile navigation build declarations', () => {
  test('the build loader retains valid navigation and rejects unknown destinations before bundling', () => {
    const { readProperty, publicProperty } = require('../scripts/property');
    const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'navigation-property-'));
    try {
      const document = { ...plain, mobile: { ...plain.mobile, navigation: { drawerItems: ['members', 'feed'], shortcuts: ['members', 'messages'] } } };
      const file = path.join(temporary, 'property.json');
      fs.writeFileSync(file, JSON.stringify(document));
      expect(publicProperty(readProperty(temporary)).mobile.navigation).toEqual(document.mobile.navigation);
      document.mobile.navigation.shortcuts = ['members', '/admin'];
      fs.writeFileSync(file, JSON.stringify(document));
      expect(() => readProperty(temporary)).toThrow(/mobile.navigation.shortcuts\[1\]/);
      document.mobile.navigation.shortcuts = [];
      fs.writeFileSync(file, JSON.stringify(document));
      expect(readProperty(temporary).mobile.navigation.shortcuts).toEqual([]);
    } finally {
      fs.rmSync(temporary, { recursive: true, force: true });
    }
  });
});
