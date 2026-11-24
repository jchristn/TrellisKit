import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  ApiClient, profileFieldsOf, customFields, onboardingFields, editorFields, staffSetFields, cardFields, profilePageFields, filterableFields,
  toFormValue, toFormValues, fromFormValue, buildProfilePatch, validateProfileValue, validateProfileValues, formatRankValue, formatProfileValue,
  historyEntryParts, pickerOptions, rankMaxLevel, rankHasLevel, hasProfileValue, buildProfileFilters, profileFilterParams, parseProfileFilterParams,
  profileFilterChips, removeProfileFilter, profileFilterOperators, missingProfileFields, countryName, isCountryCode, countryOptions,
  unknownTemplateTokens, domainVerificationState, emptyHistoryEntry
} from '../src/index.js';

const dojo = JSON.parse(readFileSync(new URL('./fixtures/dojo.json', import.meta.url), 'utf8'));

const rank = {
  key: 'tier', builtIn: false, label: 'Tier', type: 'Rank', profileOrder: 10, collectAt: 'SignUp', editableBy: 'Member', visibility: 'Public',
  required: true, validation: { required: true }, showOnProfile: true, showOnCard: true, filterable: true,
  options: [
    { key: 'Bronze', label: 'Bronze', color: '#CD7F32', maxLevel: null, freeText: false, retired: false },
    { key: 'Silver', label: 'Silver', color: '#C0C0C0', maxLevel: 1, freeText: false, retired: false },
    { key: 'Old', label: 'Old', color: '#000000', maxLevel: null, freeText: false, retired: true }
  ],
  rank: { levelLabel: 'Stars', maxLevel: 3 }
};
const focus = {
  key: 'focus', label: 'Focus', type: 'MultiSelect', profileOrder: 20, collectAt: 'Profile', editableBy: 'Member', visibility: 'Private', showOnProfile: true, filterable: true,
  validation: { maxEntries: 2 },
  options: [{ key: 'Speed', label: 'Speed' }, { key: 'Form', label: 'Form' }, { key: 'Else', label: 'Something else', freeText: true }]
};
const history = {
  key: 'past', label: 'History', type: 'History', profileOrder: 30, collectAt: 'Profile', editableBy: 'Member', showOnProfile: true, validation: { maxEntries: 2 },
  subFields: [
    { key: 'tier', label: 'Tier', type: 'RankValue', required: true, rankFieldKey: 'tier', withLevel: false, position: 0 },
    { key: 'by', label: 'Awarded by', type: 'Member', required: false, position: 1 },
    { key: 'on', label: 'Date', type: 'Date', required: false, notInFuture: true, position: 2 }
  ]
};
const note = { key: 'staffNote', label: 'Note', type: 'Text', profileOrder: 40, collectAt: 'AdminOnly', editableBy: 'Staff', visibility: 'Private', validation: { maxLength: 10 } };
const odd = { key: 'future', label: 'Future', type: 'Hologram', profileOrder: 50 };
const builtIns = [
  { key: 'displayName', builtIn: true, type: 'Text', profileOrder: 0 },
  { key: 'bio', builtIn: true, type: 'LongText', profileOrder: 5 }
];
const fields = [history, note, odd, rank, focus, ...builtIns];

test('definition helpers order fields, skip unknown types and built-ins, and split by surface', () => {
  assert.deepEqual(customFields(fields).map((f) => f.key), ['tier', 'focus', 'past', 'staffNote']);
  assert.deepEqual(onboardingFields(fields).map((f) => f.key), ['tier']);
  assert.deepEqual(editorFields(fields).map((f) => f.key), ['tier', 'focus', 'past']);
  assert.deepEqual(staffSetFields(fields).map((f) => f.key), ['staffNote']);
  assert.deepEqual(filterableFields(fields).map((f) => f.key), ['tier', 'focus']);
  const profile = { tier: { key: 'Silver', level: 1 }, focus: { keys: ['Speed'], otherText: null }, past: [] };
  assert.deepEqual(cardFields(fields, profile).map((f) => f.key), ['tier']);
  assert.deepEqual(profilePageFields(fields, profile).map((f) => f.key), ['tier', 'focus']);
  assert.deepEqual(missingProfileFields({ profileMissing: ['tier', 'gone'] }, fields).map((m) => m.label), ['Tier', 'gone']);
});

test('profileFieldsOf prefers server definitions and converts a version 1 profile section otherwise', () => {
  assert.deepEqual(profileFieldsOf({ profileFields: fields }).map((f) => f.key), ['displayName', 'bio', 'tier', 'focus', 'past', 'staffNote']);
  assert.deepEqual(profileFieldsOf({ profileFields: [rank] }).map((f) => f.key), ['displayName', 'handle', 'avatar', 'bio', 'tier']);
  const legacy = profileFieldsOf(dojo);
  const keys = legacy.map((f) => f.key);
  assert.deepEqual(keys.filter((k) => !['displayName', 'handle', 'avatar', 'bio'].includes(k)), ['rank', 'affiliation', 'city', 'region', 'country', 'goals', 'rankHistory']);
  const tier = legacy.find((f) => f.key === 'rank');
  assert.equal(tier.label, 'Tier');
  assert.equal(tier.collectAt, 'SignUp');
  assert.equal(formatRankValue(tier, { key: 'Gold', level: 2 }), 'Gold, 2 stars');
  assert.equal(legacy.find((f) => f.key === 'rankHistory').validation.maxEntries, 3);
});

test('rank helpers honor per-option maximum levels, retired options, and the level label', () => {
  assert.equal(rankMaxLevel(rank, 'Bronze'), 3);
  assert.equal(rankMaxLevel(rank, 'Silver'), 1);
  assert.equal(rankHasLevel(rank, 'Bronze'), true);
  assert.equal(rankHasLevel({ ...rank, rank: { levelLabel: '', maxLevel: 3 } }, 'Bronze'), false);
  assert.deepEqual(pickerOptions(rank).map((o) => o.key), ['Bronze', 'Silver']);
  assert.deepEqual(pickerOptions(rank, 'Old').map((o) => o.key), ['Bronze', 'Silver', 'Old']);
  assert.equal(formatRankValue(rank, { key: 'Bronze', level: 0 }), 'Bronze');
  assert.equal(formatRankValue(rank, { key: 'Silver', level: 1 }), 'Silver, 1 star');
  assert.equal(formatRankValue(rank, null), '');
});

test('form values round trip to API values and patches carry only changes', () => {
  assert.deepEqual(toFormValue(rank, null), { key: '', level: 0 });
  assert.equal(fromFormValue(rank, { key: 'Silver', level: 9 }).level, 1);
  assert.equal(fromFormValue(rank, { key: '', level: 0 }), null);
  assert.deepEqual(fromFormValue(focus, { keys: ['Speed'], otherText: 'ignored' }), { keys: ['Speed'], otherText: null });
  assert.deepEqual(fromFormValue(focus, { keys: ['Else'], otherText: ' mine ' }), { keys: ['Else'], otherText: 'mine' });
  assert.equal(fromFormValue({ type: 'Number' }, '12.5'), 12.5);
  assert.equal(fromFormValue({ type: 'Text' }, '   '), null);
  assert.equal(fromFormValue({ type: 'Country' }, 'us'), 'US');
  assert.equal(fromFormValue({ type: 'Boolean' }, false), false);

  const api = [{ id: 'phe_1', values: { tier: { key: 'Bronze' }, by: { name: 'Coach', userId: null }, on: '2020-01-02' } }];
  const form = toFormValue(history, api);
  assert.equal(form[0].id, 'phe_1');
  assert.deepEqual(fromFormValue(history, form, fields), [{ id: 'phe_1', values: { tier: { key: 'Bronze' }, by: { name: 'Coach', userId: null }, on: '2020-01-02' } }]);
  const blank = emptyHistoryEntry(history);
  assert.deepEqual(fromFormValue(history, [blank], fields), [{ id: null, values: {} }]);

  const initial = toFormValues(fields, { tier: { key: 'Bronze', level: 0 }, focus: null, past: api });
  const values = { ...initial, focus: { keys: ['Form'], otherText: '' } };
  assert.deepEqual(buildProfilePatch(editorFields(fields), values, initial, fields), { focus: { keys: ['Form'], otherText: null } });
  assert.deepEqual(buildProfilePatch(editorFields(fields), { ...initial, tier: { key: '', level: 0 } }, initial, fields), { tier: null });
});

test('client validation mirrors the server codes and keys', () => {
  const today = new Date('2026-11-11T12:00:00Z');
  assert.deepEqual(validateProfileValue(rank, { key: '', level: 0 }), { 'profile.tier': { code: 'required', params: {} } });
  assert.deepEqual(validateProfileValue(rank, { key: '', level: 0 }, { required: false }), {});
  assert.equal(validateProfileValue(rank, { key: 'Nope', level: 0 })['profile.tier'].code, 'option');
  assert.deepEqual(validateProfileValue(focus, { keys: ['Speed', 'Form', 'Else'], otherText: '' })['profile.focus'], { code: 'maxEntries', params: { max: 2 } });
  assert.deepEqual(validateProfileValue(note, '01234567890')['profile.staffNote'], { code: 'tooLong', params: { max: 10 } });
  assert.equal(validateProfileValue({ key: 'site', type: 'Url' }, 'ftp://x')['profile.site'].code, 'url');
  assert.equal(validateProfileValue({ key: 'tel', type: 'Phone' }, '555')['profile.tel'].code, 'phone');
  assert.equal(validateProfileValue({ key: 'mail', type: 'Email' }, 'a@b')['profile.mail'].code, 'email');
  assert.equal(validateProfileValue({ key: 'n', type: 'Number', validation: { integerOnly: true } }, '1.5')['profile.n'].code, 'integer');
  assert.equal(validateProfileValue({ key: 'c', type: 'Country' }, 'ZZ')['profile.c'].code, 'country');
  assert.equal(validateProfileValue({ key: 'code', type: 'Text', validation: { pattern: '^[A-Z]+$', patternMessage: 'Capitals.' } }, 'abc')['profile.code'].params.message, 'Capitals.');
  const entries = [
    { rowKey: 'a', id: null, values: { tier: { key: '', level: 0 }, by: { name: '', userId: null }, on: '2030-01-01' } },
    { rowKey: 'b', id: null, values: { tier: { key: 'Bronze', level: 0 }, by: { name: '', userId: null }, on: '' } }
  ];
  const errors = validateProfileValue(history, entries, { today, fields });
  assert.deepEqual(Object.keys(errors).sort(), ['profile.past[0].on', 'profile.past[0].tier']);
  assert.equal(errors['profile.past[0].on'].code, 'dateFuture');
  assert.equal(validateProfileValue(history, [...entries, entries[1]], { fields })['profile.past'].code, 'maxEntries');
  assert.deepEqual(Object.keys(validateProfileValues(onboardingFields(fields), {}, { fields })), ['profile.tier']);
  assert.deepEqual(validateProfileValues(onboardingFields(fields), {}, { fields, skipRequired: new Set(['tier']) }), {});
});

test('formatters render option labels, free text, dates, booleans, countries, and history parts', () => {
  assert.equal(formatProfileValue(focus, { keys: ['Speed', 'Else'], otherText: 'Grip' }), 'Speed, Grip');
  assert.equal(formatProfileValue({ type: 'Boolean' }, false, { no: 'Nope' }), 'Nope');
  assert.equal(formatProfileValue({ type: 'Date' }, '2015-06-01'), 'Jun 1, 2015');
  assert.equal(formatProfileValue({ type: 'Country' }, 'US'), countryName('US'));
  assert.equal(formatProfileValue({ type: 'Url' }, 'https://example.test/a'), 'example.test');
  assert.equal(formatProfileValue({ type: 'SingleSelect', options: [{ key: 'A', label: 'Alpha' }] }, 'A'), 'Alpha');
  assert.equal(hasProfileValue(focus, { keys: [] }), false);
  const parts = historyEntryParts(history, { id: 'phe_1', values: { tier: { key: 'Silver' }, by: { name: 'Coach', userId: 'usr_1' }, on: '2015-06-01' } }, { fields });
  assert.deepEqual(parts.map((p) => [p.key, p.text]), [['tier', 'Silver'], ['by', 'Coach'], ['on', 'Jun 1, 2015']]);
  assert.equal(parts[0].color, '#C0C0C0');
  assert.equal(parts[1].userId, 'usr_1');
});

test('countries come from the shared list', () => {
  assert.equal(isCountryCode('us'), true);
  assert.equal(isCountryCode('ZZ'), false);
  assert.ok(countryOptions().length > 240);
  assert.equal(countryName(''), '');
});

test('profile filters normalize, round trip through URL params, and describe chips', () => {
  assert.deepEqual(profileFilterOperators('Rank'), ['in', 'minRank', 'maxRank', 'minLevel', 'hasValue']);
  assert.deepEqual(profileFilterOperators('Hologram'), []);
  const state = { tier: { minRank: 'Silver', minLevel: '1', in: [] }, focus: { any: ['Speed', ''] }, empty: { eq: '' }, n: { gte: '3' } };
  const filters = buildProfileFilters(state, [...fields, { key: 'n', type: 'Number' }]);
  assert.deepEqual(filters, { tier: { minRank: 'Silver', minLevel: 1 }, focus: { any: ['Speed'] }, n: { gte: 3 } });
  const params = profileFilterParams(filters);
  assert.deepEqual(params, { 'profile.tier.minRank': 'Silver', 'profile.tier.minLevel': '1', 'profile.focus.any': ['Speed'], 'profile.n.gte': '3' });
  const search = new URLSearchParams('profile.focus.any=Speed&profile.focus.any=Form&profile.tier.minRank=Silver&labels=x');
  assert.deepEqual(parseProfileFilterParams(search), { focus: { any: ['Speed', 'Form'] }, tier: { minRank: 'Silver' } });
  const chips = profileFilterChips({ focus: { any: ['Speed', 'Else'] }, tier: { minRank: 'Silver' } }, fields);
  assert.deepEqual(chips.map((c) => [c.label, c.value]), [['Focus', 'Speed, Something else'], ['Tier', 'Silver']]);
  assert.deepEqual(removeProfileFilter({ tier: { minRank: 'Silver', minLevel: 1 } }, 'tier', 'minRank'), { tier: { minLevel: 1 } });
  assert.deepEqual(removeProfileFilter({ tier: { minRank: 'Silver' } }, 'tier', 'minRank'), {});
});

test('email and domain helpers', () => {
  assert.deepEqual(unknownTemplateTokens('Hi {name}, {bogus} and {bogus} {name}', ['name']), ['bogus']);
  assert.equal(domainVerificationState({ verifiedUtc: '2026-01-01T00:00:00Z' }), 'Verified');
  assert.equal(domainVerificationState({ verifiedUtc: null, verification: { recordName: 'x' } }), 'Pending');
  assert.equal(domainVerificationState({}), 'Unverified');
});

function recording(respond) {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url, method: init.method, body: init.body ? JSON.parse(init.body) : undefined, headers: init.headers });
    return respond ? respond(url, init) : new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  return { client: new ApiClient({ fetchImpl }), calls };
}

test('profile, directory, email, settings, and domain methods map to their routes', async () => {
  const { client, calls } = recording();
  await client.listProfileFields();
  await client.createProfileField({ key: 'x' });
  await client.updateProfileField('pfd_1', { label: 'Y' });
  await client.deleteProfileField('pfd_1', { deleteValues: true, confirm: 'x' });
  await client.deleteProfileField('pfd_2');
  await client.reorderProfileFields(['pfd_1']);
  await client.previewProfileFields({ target: 'Card', viewer: 'Member' });
  await client.getProfileFieldStats('pfd_1');
  await client.updateMemberProfile('usr_1', { x: null });
  await client.enumerateMemberDirectory({ search: 'pat', filters: { profile: { tier: { in: ['Gold'] } } } });
  await client.getPropertySettings();
  await client.updatePropertySettings({ values: { 'email.provider': 'Mailgun' }, clear: ['email.mailgunApiKey'] });
  await client.testIntegration('email');
  await client.getPropertyDomains();
  await client.verifyPropertyDomain('pdm_1');
  await client.listEmailTemplates();
  await client.updateEmailTemplate('welcome', { subject: 'Hi' });
  await client.resetEmailTemplate('welcome');
  await client.previewEmailTemplate('welcome', { subject: 'Hi' });
  await client.sendTestEmail({ template: 'welcome' });
  await client.updateEmailStaffEvent('staff_new_member', { enabled: false });
  await client.listEmailMessages({ filters: { status: 'Failed' } });
  await client.addEmailSuppression({ email: 'a@example.test' });
  await client.removeEmailSuppression('sup_1');
  await client.listPlatformRequestHistory({ filters: { propertyId: 'platform' } });
  await client.listPropertyMetrics({});
  await client.enumeratePlatformLabels({ prefix: 'be' });
  await client.enumeratePlatformTagValues({ key: 'tier', prefix: 'g' });
  const pairs = calls.map((c) => c.method + ' ' + c.url.replace('/api/v1', ''));
  assert.deepEqual(pairs, [
    'POST /admin/profile-fields/enumerate', 'POST /admin/profile-fields', 'PUT /admin/profile-fields/pfd_1',
    'DELETE /admin/profile-fields/pfd_1?deleteValues=true', 'DELETE /admin/profile-fields/pfd_2', 'POST /admin/profile-fields/reorder',
    'POST /admin/profile-fields/preview', 'POST /admin/profile-fields/pfd_1/stats/enumerate', 'PUT /admin/members/usr_1/profile',
    'POST /members/directory/enumerate', 'POST /admin/property/settings/enumerate', 'PUT /admin/property/settings', 'POST /admin/property/integrations/email/test',
    'POST /admin/property/domains/enumerate', 'POST /admin/property/domains/pdm_1/verify', 'POST /admin/email/templates/enumerate', 'PUT /admin/email/templates/welcome',
    'DELETE /admin/email/templates/welcome', 'POST /admin/email/templates/welcome/preview', 'POST /admin/email/test',
    'PUT /admin/email/staff-events/staff_new_member', 'POST /admin/email/messages/enumerate', 'POST /admin/email/suppressions',
    'DELETE /admin/email/suppressions/sup_1', 'POST /platform/request-history/enumerate', 'POST /platform/metrics/properties/enumerate',
    'POST /platform/labels/enumerate', 'POST /platform/tags/values/enumerate'
  ]);
  assert.deepEqual(calls[3].body, { confirm: 'x' });
  assert.equal(calls[0].body.ordering, 'ProfileOrderAscending');
  assert.deepEqual(calls[9].body.filters, { profile: { tier: { in: ['Gold'] } } });
  assert.deepEqual(calls[11].body, { values: { 'email.provider': 'Mailgun' }, clear: ['email.mailgunApiKey'] });
  assert.deepEqual(calls[24].body.filters, { propertyId: 'platform' });
  assert.equal(calls[26].body.prefix, 'be');
  assert.equal(calls[27].body.key, 'tier');
});

test('propertyIfChanged sends If-None-Match and treats 304 as not modified', async () => {
  let status = 304;
  const { client, calls } = recording(() => (status === 304
    ? new Response(null, { status: 304, headers: { etag: '"v1"' } })
    : new Response(JSON.stringify({ id: 'app_x' }), { status: 200, headers: { 'content-type': 'application/json', etag: '"v2"' } })));
  const same = await client.propertyIfChanged('"v1"');
  assert.deepEqual(same, { notModified: true, etag: '"v1"' });
  assert.equal(calls[0].headers['If-None-Match'], '"v1"');
  status = 200;
  const fresh = await client.propertyIfChanged(null);
  assert.deepEqual(fresh, { notModified: false, property: { id: 'app_x' }, etag: '"v2"' });
  assert.equal(calls[1].headers['If-None-Match'], undefined);
});
