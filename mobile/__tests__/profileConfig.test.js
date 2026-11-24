import { ApiError } from '@trelliskit/shared';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { CardFields, ProfileFieldList } from '../src/components/profile/ProfileDisplay';
import ProfileForm, { formLayout } from '../src/components/profile/ProfileForm';
import { builtInsOf, profileErrorText } from '../src/profile/fields';
import { PropertyProvider } from '../src/property';
import strings from '../src/strings';
import { allFields, builtIns, emptyProfile, level, milestones, plainProperty, propertyWithFields } from '../test-utils/fields';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { pickDate } from '../test-utils/dates';

const APP_DIR = path.resolve(__dirname, '../app');
const e = strings.profileFields.errors;

function formApi(overrides = {}) {
  return {
    url: (p) => p,
    accessToken: null,
    get: jest.fn(async () => ({ available: true })),
    put: jest.fn(async (_path, body) => makeUser({ ...body, profile: emptyProfile(body.profile) })),
    post: jest.fn(async () => ({ objects: [], endOfResults: true })),
    ...overrides
  };
}

async function renderForm({ property = propertyWithFields(), user = makeUser({ profile: emptyProfile() }), api = formApi(), mode = 'editor' } = {}) {
  const onSaved = jest.fn();
  const metrics = { frame: { x: 0, y: 0, width: 390, height: 844 }, insets: { top: 0, left: 0, right: 0, bottom: 0 } };
  await render(
    <SafeAreaProvider initialMetrics={metrics}>
      <PropertyProvider property={property}>
        <ProfileForm api={api} user={user} mode={mode} onUserChange={jest.fn()} onSaved={onSaved} submitLabel="Save" />
      </PropertyProvider>
    </SafeAreaProvider>
  );
  return { onSaved, api };
}

describe('profile editor driven by profileFields', () => {
  test('built-ins use their definition labels, then every known type in profile order; unknown types are skipped', async () => {
    await renderForm({ user: makeUser({ profile: emptyProfile({ badge: 'Gold star' }) }) });
    expect(screen.getByText('Full name')).toBeOnTheScreen();
    expect(screen.getByText('As other members see it')).toBeOnTheScreen();
    expect(screen.getByText('Username')).toBeOnTheScreen();
    expect(screen.getByText('Picture')).toBeOnTheScreen();
    expect(screen.getByTestId('profile-bio')).toHaveProp('accessibilityLabel', 'About me');
    expect(screen.getByTestId('profile-field-team')).toHaveProp('placeholder', 'Your team');
    expect(screen.getByTestId('profile-field-team-field-help')).toHaveTextContent('Where you meet');
    expect(screen.getByTestId('profile-field-level-field-required')).toHaveTextContent(strings.profileFields.required);
    expect(screen.queryByTestId('profile-field-team-field-required')).toBeNull();
    for (const key of ['level', 'team', 'motto', 'years', 'since', 'mentor', 'site', 'contact', 'phone', 'country', 'style', 'flavor', 'focus', 'milestones']) {
      expect({ key, shown: screen.queryByTestId('profile-field-' + key + '-field') !== null }).toEqual({ key, shown: true });
    }
    expect(screen.queryByText('Hologram')).toBeNull();
    expect(screen.queryByTestId('profile-field-badge-field')).toBeNull();
    expect(within(screen.getByTestId('profile-staff-badge')).getByText(strings.profileFields.setByStaff)).toBeOnTheScreen();
    expect(within(screen.getByTestId('profile-staff-badge')).getByText('Gold star')).toBeOnTheScreen();
    expect(screen.getByTestId('profile-field-years')).toHaveProp('keyboardType', 'number-pad');
    expect(screen.getByTestId('profile-field-phone')).toHaveProp('keyboardType', 'phone-pad');
    expect(screen.getByText(strings.profileFields.phoneHint)).toBeOnTheScreen();
    expect(screen.getByTestId('profile-field-contact')).toHaveProp('keyboardType', 'email-address');
    expect(screen.getByTestId('profile-field-site')).toHaveProp('keyboardType', 'url');
  });

  test('bio disabled (absent from the server definitions) is not collected or sent', async () => {
    const { api } = await renderForm({ property: propertyWithFields(allFields.filter((f) => f.key !== 'bio')), user: makeUser({ profile: emptyProfile({ level: { key: 'Green', level: 0 } }) }) });
    expect(screen.queryByTestId('profile-bio')).toBeNull();
    await fireEvent.press(screen.getByTestId('profile-submit'));
    await waitFor(() => expect(api.put).toHaveBeenCalledTimes(1));
    expect(api.put.mock.calls[0][1]).not.toHaveProperty('bio');
  });

  test('rank picker: colors, retired options hidden, and a level stepper bounded by maxLevel', async () => {
    await renderForm();
    expect(screen.getByTestId('profile-field-level-Gold-swatch')).toHaveStyle({ backgroundColor: '#B8860B' });
    expect(screen.queryByTestId('profile-field-level-Old')).toBeNull();
    expect(screen.queryByTestId('profile-field-level-level')).toBeNull();
    await fireEvent.press(screen.getByTestId('profile-field-level-Gold'));
    expect(screen.getByTestId('profile-field-level-level-value')).toHaveTextContent('0');
    expect(screen.getByTestId('profile-field-level-level-dec')).toBeDisabled();
    await fireEvent.press(screen.getByTestId('profile-field-level-level-inc'));
    await fireEvent.press(screen.getByTestId('profile-field-level-level-inc'));
    expect(screen.getByTestId('profile-field-level-level-value')).toHaveTextContent('2');
    expect(screen.getByTestId('profile-field-level-level-inc')).toBeDisabled();
  });

  test('a retired option stays visible while it is the current value', async () => {
    await renderForm({ user: makeUser({ profile: emptyProfile({ level: { key: 'Old', level: 0 } }) }) });
    expect(screen.getByTestId('profile-field-level-Old')).toBeOnTheScreen();
  });

  test('selects: radios up to five options, a dropdown above, and free text for a freeText option', async () => {
    const { api } = await renderForm({ user: makeUser({ profile: emptyProfile({ level: { key: 'Green', level: 0 } }) }) });
    expect(screen.getByTestId('profile-field-style-Calm')).toHaveProp('accessibilityRole', 'radio');
    expect(screen.queryByTestId('profile-field-style-other')).toBeNull();
    await fireEvent.press(screen.getByTestId('profile-field-style-Other'));
    await fireEvent.changeText(screen.getByTestId('profile-field-style-other'), 'Quiet');

    expect(screen.queryByTestId('profile-field-flavor-One')).toBeNull();
    await fireEvent.press(screen.getByTestId('profile-field-flavor'));
    await fireEvent.press(await screen.findByTestId('profile-field-flavor-option-Six'));
    expect(screen.getByTestId('profile-field-flavor')).toHaveTextContent('Six');

    await fireEvent.press(screen.getByTestId('profile-field-focus-Reading'));
    await fireEvent.press(screen.getByTestId('profile-field-focus-Else'));
    await fireEvent.changeText(screen.getByTestId('profile-field-focus-other'), 'Drawing');

    await fireEvent.press(screen.getByTestId('profile-field-country'));
    await fireEvent.changeText(await screen.findByTestId('profile-field-country-search'), 'Canad');
    expect(screen.queryByTestId('profile-field-country-option-US')).toBeNull();
    await fireEvent.press(await screen.findByTestId('profile-field-country-option-CA'));
    expect(screen.getByTestId('profile-field-country')).toHaveTextContent('Canada', { exact: false });

    await fireEvent(screen.getByTestId('profile-field-mentor'), 'valueChange', true);
    await fireEvent.press(screen.getByTestId('profile-submit'));
    await waitFor(() => expect(api.put).toHaveBeenCalledTimes(1));
    const body = api.put.mock.calls[0][1];
    expect(body.profile).toEqual({
      style: { key: 'Other', otherText: 'Quiet' },
      flavor: { key: 'Six', otherText: null },
      focus: { keys: ['Reading', 'Else'], otherText: 'Drawing' },
      country: 'CA',
      mentor: true
    });
    for (const key of ['rank', 'rankLevel', 'affiliation', 'city', 'region', 'country', 'goals', 'goalsOther', 'rankHistory']) expect(body).not.toHaveProperty(key);
  });

  test('client validation shows mapped messages inline and blocks the save', async () => {
    const { api } = await renderForm();
    await fireEvent.changeText(screen.getByTestId('profile-field-years'), '2.5');
    await fireEvent.changeText(screen.getByTestId('profile-field-site'), 'not a url');
    await fireEvent.changeText(screen.getByTestId('profile-field-contact'), 'nobody');
    await fireEvent.changeText(screen.getByTestId('profile-field-phone'), '555');
    // December 31 of this year is still in the future (the year picker stops at this year).
    await pickDate('profile-field-since', 12, 31, new Date().getFullYear());
    await fireEvent.press(screen.getByTestId('profile-submit'));
    expect(screen.getByTestId('profile-field-level-field-error')).toHaveTextContent(e.required);
    expect(screen.getByTestId('profile-field-years-error')).toHaveTextContent(e.integer);
    expect(screen.getByTestId('profile-field-site-error')).toHaveTextContent(e.url);
    expect(screen.getByTestId('profile-field-contact-error')).toHaveTextContent(e.email);
    expect(screen.getByTestId('profile-field-phone-error')).toHaveTextContent(e.phone);
    expect(screen.getByTestId('profile-field-since-field-error')).toHaveTextContent(e.dateFuture);
    expect(api.put).not.toHaveBeenCalled();
  });

  test('every validation code maps to engine text', () => {
    expect(profileErrorText({ code: 'tooLong', params: { max: 20 } })).toBe('Use 20 characters or fewer.');
    expect(profileErrorText({ code: 'tooShort', params: { min: 2 } })).toBe('Use at least 2 characters.');
    expect(profileErrorText({ code: 'pattern', params: { message: 'Letters only.' } })).toBe('Letters only.');
    expect(profileErrorText({ code: 'pattern', params: {} })).toBe(e.pattern);
    expect(profileErrorText({ code: 'min', params: { min: 3 } })).toBe('Enter 3 or more.');
    expect(profileErrorText({ code: 'max', params: { max: 9 } })).toBe('Enter 9 or less.');
    expect(profileErrorText({ code: 'dateMin', params: { min: '2020-01-01' } })).toContain('2020-01-01');
    expect(profileErrorText({ code: 'dateMax', params: { max: '2020-01-01' } })).toContain('2020-01-01');
    expect(profileErrorText({ code: 'minEntries', params: { min: 1 } })).toBe('Add at least 1.');
    expect(profileErrorText({ code: 'maxEntries', params: { max: 2 } })).toBe('Add up to 2.');
    expect(profileErrorText({ code: 'level', params: { max: 2 } })).toBe('Choose a level from 0 to 2.');
    for (const code of ['required', 'number', 'integer', 'decimals', 'date', 'dateFuture', 'url', 'email', 'phone', 'country', 'option']) expect(profileErrorText({ code })).toBe(e[code]);
    expect(profileErrorText({ code: 'somethingNew' })).toBe(e.invalid);
    expect(profileErrorText('From the server.')).toBe('From the server.');
  });

  test('server fieldErrors keyed profile.<key> and nested History keys show on the field they name', async () => {
    const put = jest.fn(async () => {
      throw new ApiError('Validation failed.', { status: 400, code: 'ValidationFailed', body: { error: 'ValidationFailed', fieldErrors: { 'profile.team': ['That word is not allowed.'], 'profile.milestones[0].when': ['That date is in the future.'], 'profile.focus.otherText': ['Too long.'], handle: ['Taken.'] } } });
    });
    await renderForm({ api: formApi({ put }), user: makeUser({ profile: emptyProfile({ level: { key: 'Green', level: 0 }, focus: { keys: ['Else'], otherText: 'x' }, milestones: [{ id: 'phe_1', values: { step: { key: 'Green' } } }] }) }) });
    await fireEvent.changeText(screen.getByTestId('profile-field-team'), 'Bad');
    await fireEvent.press(screen.getByTestId('profile-submit'));
    expect(await screen.findByTestId('profile-field-team-error')).toHaveTextContent('That word is not allowed.');
    expect(screen.getByTestId('profile-field-milestones-0-when-error')).toHaveTextContent('That date is in the future.');
    expect(screen.getByTestId('profile-field-focus-other-error')).toHaveTextContent('Too long.');
    expect(screen.getByTestId('profile-handle-error')).toHaveTextContent('Taken.');
    expect(screen.queryByTestId('profile-form-message')).toBeNull();
  });

  test('History editor: add up to maxEntries, reorder, remove, link a member by search, and save in order', async () => {
    const post = jest.fn(async () => ({ objects: [{ id: 'usr_7', displayName: 'Sam Lee', handle: 'sam' }], endOfResults: true }));
    const { api } = await renderForm({ api: formApi({ post }), user: makeUser({ profile: emptyProfile({ level: { key: 'Gold', level: 1 } }) }) });
    await fireEvent.press(screen.getByTestId('profile-field-milestones-add'));
    await fireEvent.press(screen.getByTestId('profile-field-milestones-add'));
    expect(screen.queryByTestId('profile-field-milestones-add')).toBeNull();
    expect(screen.getByText(strings.profileFields.historyHint(2))).toBeOnTheScreen();

    await fireEvent.press(screen.getByTestId('profile-field-milestones-0-step-Green'));
    await fireEvent.changeText(screen.getByTestId('profile-field-milestones-0-by'), 'Sa');
    await fireEvent.press(await screen.findByTestId('profile-field-milestones-0-by-suggestion-usr_7', {}, { timeout: 2000 }));
    expect(screen.getByTestId('profile-field-milestones-0-by-linked')).toHaveTextContent(strings.profileFields.linkedTo('Sam Lee'));
    expect(post.mock.calls[0][0]).toBe('/members/enumerate');

    await fireEvent.press(screen.getByTestId('profile-field-milestones-1-step-Gold'));
    await fireEvent.changeText(screen.getByTestId('profile-field-milestones-1-by'), 'Someone Else');
    await fireEvent.press(screen.getByTestId('profile-field-milestones-1-up'));
    expect(screen.getByTestId('profile-field-milestones-0-by')).toHaveProp('value', 'Someone Else');

    await fireEvent.press(screen.getByTestId('profile-submit'));
    await waitFor(() => expect(api.put).toHaveBeenCalledTimes(1));
    expect(api.put.mock.calls[0][1].profile).toEqual({
      milestones: [
        { id: null, values: { step: { key: 'Gold' }, by: { name: 'Someone Else', userId: null } } },
        { id: null, values: { step: { key: 'Green' }, by: { name: 'Sam Lee', userId: 'usr_7' } } }
      ]
    });

    await fireEvent.press(screen.getByTestId('profile-field-milestones-1-remove'));
    expect(screen.queryByTestId('profile-field-milestones-1')).toBeNull();
    expect(screen.getByTestId('profile-field-milestones-add')).toBeOnTheScreen();
  });

  test('History sub-fields are validated with nested keys', async () => {
    const { api } = await renderForm({ user: makeUser({ profile: emptyProfile({ level: { key: 'Gold', level: 1 } }) }) });
    await fireEvent.press(screen.getByTestId('profile-field-milestones-add'));
    await fireEvent.press(screen.getByTestId('profile-submit'));
    expect(screen.getByTestId('profile-field-milestones-0-step-error')).toHaveTextContent(e.required);
    expect(api.put).not.toHaveBeenCalled();
  });
});

describe('onboarding with sign-up fields', () => {
  test('shows the built-ins collected at sign-up, then the sign-up fields in profile order', async () => {
    await renderForm({ mode: 'onboarding', user: makeUser({ handle: null, profileComplete: false, profile: emptyProfile() }) });
    expect(screen.getByTestId('profile-display-name')).toBeOnTheScreen();
    expect(screen.getByTestId('profile-handle')).toBeOnTheScreen();
    expect(screen.queryByTestId('choose-photo')).toBeNull();
    expect(screen.queryByTestId('profile-bio')).toBeNull();
    expect(screen.getByTestId('profile-field-level-field')).toBeOnTheScreen();
    expect(screen.getByTestId('profile-field-team-field')).toBeOnTheScreen();
    for (const key of ['motto', 'years', 'country', 'milestones']) expect(screen.queryByTestId('profile-field-' + key + '-field')).toBeNull();
    expect(screen.queryByTestId('profile-metadata')).toBeNull();
    const layout = formLayout(allFields, 'onboarding');
    expect(layout.editable.map((f) => f.key)).toEqual(['level', 'team']);
  });

  test('avatar and bio appear in onboarding when collected at sign-up', async () => {
    const fields = allFields.map((f) => (f.key === 'avatar' || f.key === 'bio' ? { ...f, collectAt: 'SignUp' } : f));
    await renderForm({ mode: 'onboarding', property: propertyWithFields(fields) });
    expect(screen.getByTestId('choose-photo')).toBeOnTheScreen();
    expect(screen.getByTestId('profile-bio')).toBeOnTheScreen();
  });

  test('a required sign-up field must validate before Continue', async () => {
    const { api, onSaved } = await renderForm({ mode: 'onboarding', user: makeUser({ handle: 'newbie', profileComplete: false, profile: emptyProfile() }) });
    await fireEvent.press(screen.getByTestId('profile-submit'));
    expect(screen.getByTestId('profile-field-level-field-error')).toHaveTextContent(e.required);
    expect(api.put).not.toHaveBeenCalled();
    await fireEvent.press(screen.getByTestId('profile-field-level-Green'));
    await fireEvent.changeText(screen.getByTestId('profile-field-team'), 'North');
    await fireEvent.press(screen.getByTestId('profile-submit'));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    expect(api.put.mock.calls[0][1]).toEqual({ displayName: 'Test Member', handle: 'newbie', profile: { level: { key: 'Green', level: 0 }, team: 'North' } });
  });

  test('optional sign-up fields can be skipped; required ones still apply', async () => {
    const { api, onSaved } = await renderForm({ mode: 'onboarding', user: makeUser({ handle: 'newbie', profileComplete: false, profile: emptyProfile() }) });
    await fireEvent.changeText(screen.getByTestId('profile-field-team'), 'x'.repeat(25));
    await fireEvent.press(screen.getByTestId('profile-skip'));
    expect(screen.getByTestId('profile-field-level-field-error')).toHaveTextContent(e.required);
    expect(screen.queryByTestId('profile-field-team-error')).toBeNull();
    await fireEvent.press(screen.getByTestId('profile-field-level-Gold'));
    await fireEvent.press(screen.getByTestId('profile-skip'));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    expect(api.put.mock.calls[0][1].profile).toEqual({ level: { key: 'Gold', level: 0 } });
  });

  test('no Skip button when every sign-up field is required', async () => {
    await renderForm({ mode: 'onboarding', property: propertyWithFields([...builtIns, level]) });
    expect(screen.queryByTestId('profile-skip')).toBeNull();
  });

  test('built-ins fall back to engine labels and defaults for a bundled copy without built-ins', () => {
    const built = builtInsOf([level]);
    expect(built.bio).not.toBeNull();
    expect(built.avatar).not.toBeNull();
    const server = builtInsOf([builtIns[0], builtIns[1], level]);
    expect(server.bio).toBeNull();
    expect(server.avatar).toBeNull();
  });
});

describe('profile routing and display', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('an existing account whose profile became incomplete is sent to "Complete your profile" with the missing labels', async () => {
    const user = makeUser({ profileComplete: false, profileMissing: ['level'], profile: emptyProfile() });
    const server = installFakeServer({
      ...signedInAs(user),
      'GET /property': propertyWithFields(),
      'GET /health': () => new Promise(() => {}),
      'PUT /me/profile': (call) => makeUser({ profileComplete: true, profile: emptyProfile(call.body.profile) })
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await waitFor(() => expect(router.getPathname()).toBe('/profile-setup'));
    expect(await screen.findByText(strings.profileSetup.completeHeading)).toBeOnTheScreen();
    await waitFor(() => expect(screen.getByTestId('profile-setup-missing')).toHaveTextContent(strings.profileSetup.missing('Level')));
    await fireEvent.press(await screen.findByTestId('profile-field-level-Gold'));
    // The "Still needed" note follows the form as fields are filled, before saving.
    await waitFor(() => expect(screen.queryByText(strings.profileSetup.missing('Level'))).toBeNull());
    await fireEvent.press(screen.getByTestId('profile-submit'));
    await waitFor(() => expect(router.getPathname()).toBe('/feed'));
    expect(server.called('PUT /me/profile')[0].body.profile).toEqual({ level: { key: 'Gold', level: 0 } });
  });

  test('the Profile tab shows card fields, bio, and fields from Me.profile honoring showOnProfile', async () => {
    const profile = emptyProfile({ level: { key: 'Gold', level: 1 }, team: 'North', secret: 'hidden', country: 'CA', focus: { keys: ['Reading'], otherText: null }, mentor: false });
    installFakeServer({ ...signedInAs(makeUser({ bio: 'Hi there', profile })), 'GET /property': propertyWithFields(), 'GET /health': () => new Promise(() => {}) });
    await renderRouter(APP_DIR, { initialUrl: '/profile' });
    expect(await screen.findByTestId('profile-card-level-text')).toHaveTextContent('Gold, 1 pip');
    expect(screen.getByTestId('profile-card-level-bar')).toHaveStyle({ backgroundColor: '#B8860B' });
    expect(screen.getByTestId('profile-card-team')).toHaveTextContent('North');
    expect(screen.getByTestId('profile-bio-text')).toHaveTextContent('Hi there');
    expect(screen.getByTestId('profile-fields-country-value')).toHaveTextContent('Canada');
    expect(screen.getByTestId('profile-fields-focus-value')).toHaveTextContent('Reading');
    expect(screen.getByTestId('profile-fields-mentor-value')).toHaveTextContent(strings.profileFields.no);
    expect(screen.queryByTestId('profile-fields-secret')).toBeNull();
    expect(screen.queryByTestId('profile-fields-motto')).toBeNull();
  });

  test('a public profile renders user.profile: History in stored order with swatches, linked members, and dates', async () => {
    const profile = {
      level: { key: 'Gold', level: 2 },
      milestones: [
        { id: 'phe_2', values: { step: { key: 'Gold' }, by: { name: 'Sam Lee', userId: 'usr_7' }, when: '2024-03-05' } },
        { id: 'phe_1', values: { step: { key: 'Green' }, by: { name: 'Pat', userId: null } } }
      ]
    };
    installFakeServer({
      ...signedInAs(makeUser()),
      'GET /property': propertyWithFields(),
      'GET /users/usr_2': { id: 'usr_2', displayName: 'Alex Chen', handle: 'alexchen', avatarUrl: null, bio: null, profile, createdUtc: '2026-01-01T00:00:00Z', isBlockedByMe: false },
      'GET /users/usr_7': { id: 'usr_7', displayName: 'Sam Lee', handle: 'sam', avatarUrl: null, bio: null, profile: {}, createdUtc: '2026-01-01T00:00:00Z', isBlockedByMe: false }
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/members/usr_2' });
    expect(await screen.findByTestId('member-card-level-text')).toHaveTextContent('Gold, 2 pips');
    expect(screen.queryByTestId('member-bio')).toBeNull();
    const list = 'member-fields-milestones-value';
    expect(screen.getByTestId(list + '-0-lead')).toHaveTextContent('Gold');
    expect(screen.getByTestId(list + '-0-swatch')).toHaveStyle({ backgroundColor: '#B8860B' });
    expect(screen.getByTestId(list + '-0-when')).toHaveTextContent('Mar 5, 2024');
    expect(screen.getByTestId(list + '-1-lead')).toHaveTextContent('Green');
    expect(screen.getByTestId(list + '-1-by')).toHaveTextContent('Granted by Pat');
    await fireEvent.press(screen.getByTestId(list + '-0-by-link'));
    await waitFor(() => expect(router.getPathname()).toBe('/members/usr_7'));
  });

  test('a property with no configurable fields shows only the built-ins', async () => {
    installFakeServer({ ...signedInAs(makeUser({ profile: {} })), 'GET /property': plainProperty, 'GET /health': () => new Promise(() => {}) });
    await renderRouter(APP_DIR, { initialUrl: '/profile' });
    expect(await screen.findByTestId('profile-name')).toHaveTextContent('Test Member');
    await waitFor(() => expect(screen.queryByTestId('profile-card')).toBeNull());
    expect(screen.queryByTestId('profile-fields')).toBeNull();
  });
});

describe('card fields', () => {
  async function renderCard(profile, fields = allFields) {
    await render(
      <PropertyProvider property={propertyWithFields(fields)}>
        <CardFields profile={profile} testID="card" />
        <ProfileFieldList profile={profile} testID="list" />
      </PropertyProvider>
    );
  }

  test('show showOnCard fields with a value in profile order (Rank as a color bar and short label), at most three', async () => {
    const fields = allFields.map((f) => (['country', 'years'].includes(f.key) ? { ...f, showOnCard: true } : f));
    await renderCard({ level: { key: 'Green', level: 0 }, team: 'North', years: 4, country: 'CA' }, fields);
    expect(screen.getByTestId('card-level-text')).toHaveTextContent('Green');
    expect(screen.getByTestId('card-level-bar')).toHaveStyle({ backgroundColor: '#2E7D32' });
    expect(screen.getByTestId('card-team')).toHaveTextContent('North');
    expect(screen.getByTestId('card-years')).toHaveTextContent('4');
    expect(screen.queryByTestId('card-country')).toBeNull();
  });

  test('nothing renders without card values; unknown keys are ignored', async () => {
    await renderCard({ unknownKey: 'x' });
    expect(screen.queryByTestId('card')).toBeNull();
    expect(screen.queryByTestId('list')).toBeNull();
  });

  test('links: Url shows its host', async () => {
    await renderCard({ site: 'https://example.org/page' });
    expect(screen.getByTestId('list-site-value')).toHaveTextContent('example.org');
    await act(async () => {});
  });

  test('History field rank lookups use the referenced rank field', () => {
    expect(milestones.subFields[0].rankFieldKey).toBe(level.key);
  });
});
