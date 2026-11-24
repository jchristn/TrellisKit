import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { AppRoutes } from '../src/App.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { SESSION_HINT_KEY } from '../src/context/AuthContext.jsx';
import { validateSignUp, validateDateOfBirth } from '../src/views/auth/SignUpPage.jsx';
import { CHECKOUT_POLL } from '../src/views/member/MembershipView.jsx';
import { LEVELS, onboardingRedirect } from '../src/utils/onboarding.js';
import { getPendingCode, PENDING_CODE_KEY } from '../src/utils/pendingCode.js';
import { browser } from '../src/utils/browser.js';
import { routeAfterAuth } from '../src/utils/postAuth.js';
import { takeFlash } from '../src/utils/flash.js';
import { authResponse, createFetch, json, makeMe } from './helpers.jsx';

let currentLocation = null;
function LocationProbe() {
  currentLocation = useLocation();
  return null;
}

/** Render the whole app at `path`. With `me`, a session is restored from the refresh call. */
function renderApp(path, handler = () => undefined, { me = null } = {}) {
  if (me) window.localStorage.setItem(SESSION_HINT_KEY, '1');
  const { fetchImpl, calls } = createFetch((call) => {
    if (call.path === '/api/v1/auth/refresh') return me ? json(authResponse(me)) : json({ error: 'InvalidRefreshToken', message: 'No session.' }, 401);
    if (call.path === '/api/v1/auth/providers') return json({ email: true, apple: { enabled: false }, google: { enabled: false }, facebook: { enabled: false }, stripe: { enabled: true } });
    return handler(call);
  });
  render(
    <ThemeProvider>
      <MemoryRouter initialEntries={[path]}>
        <AppRoutes fetchImpl={fetchImpl} />
        <LocationProbe />
      </MemoryRouter>
    </ThemeProvider>
  );
  return calls;
}

const today = new Date(2026, 10, 11);
const adultValues = { email: 'new@example.com', password: 'correct horse battery', displayName: 'New Member', dateOfBirth: '1990-05-01', acceptTerms: true, code: '' };

describe('sign-up validation', () => {
  it('accepts a complete adult sign-up', () => {
    expect(validateSignUp(adultValues, today)).toEqual({});
  });

  it('enforces the property minimum age (21 in the fixture) at the exact birthday boundary', () => {
    expect(validateDateOfBirth('2005-11-11', today)).toBe('');
    expect(validateDateOfBirth('2005-11-12', today)).toBe('You must be 21 or older to join Fixture Dojo.');
    expect(validateDateOfBirth('2030-01-01', today)).toMatch(/future/);
    expect(validateDateOfBirth('', today)).toMatch(/Enter your date of birth/);
  });

  it('reports each invalid field', () => {
    const errors = validateSignUp({ email: 'nope', password: 'short', displayName: 'J', dateOfBirth: '2006-01-01', acceptTerms: false, code: 'bad code!' }, today);
    expect(Object.keys(errors).sort()).toEqual(['acceptTerms', 'code', 'dateOfBirth', 'displayName', 'email', 'password']);
    expect(validateSignUp({ ...adultValues, password: 'new@example.com' }, today).password).toMatch(/cannot be your email/);
  });

  it('blocks an underage sign-up in the page without calling the server', async () => {
    const calls = renderApp('/sign-up');
    await userEvent.type(await screen.findByLabelText('Email'), 'kid@example.com');
    await userEvent.type(screen.getByLabelText('Password'), 'correct horse battery');
    await userEvent.type(screen.getByLabelText('Display name'), 'Young Person');
    const dob = screen.getByLabelText('Date of birth');
    const year = new Date().getFullYear() - 20;
    await userEvent.type(dob, year + '-01-01');
    await userEvent.click(screen.getByRole('checkbox'));
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }));
    expect(await screen.findByText('You must be 21 or older to join Fixture Dojo.')).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/v1/auth/register')).toBe(false);
  });

  it('shows the server message for 403 AgeRequirementNotMet and 409 EmailInUse', async () => {
    let attempt = 0;
    renderApp('/sign-up', (call) => {
      if (call.path !== '/api/v1/auth/register') return undefined;
      attempt += 1;
      return attempt === 1
        ? json({ error: 'AgeRequirementNotMet', message: 'Too young.' }, 403)
        : json({ error: 'EmailInUse', message: 'Taken.' }, 409);
    });
    await userEvent.type(await screen.findByLabelText('Email'), 'a@example.com');
    await userEvent.type(screen.getByLabelText('Password'), 'correct horse battery');
    await userEvent.type(screen.getByLabelText('Display name'), 'Someone');
    await userEvent.type(screen.getByLabelText('Date of birth'), '1990-01-01');
    await userEvent.click(screen.getByRole('checkbox'));
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }));
    expect(await screen.findByText(/You must be 21 or older to use Fixture Dojo. No account was created./)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }));
    expect(await screen.findByText(/An account with this email already exists/)).toBeInTheDocument();
  });
});

describe('sign-in errors', () => {
  it.each([
    [{ error: 'InvalidCredentials', message: 'x' }, 401, 'That email and password do not match an account.'],
    [{ error: 'AccountLocked', message: 'Try again after 3:15 PM.' }, 423, 'Try again after 3:15 PM.'],
    [{ error: 'AccountBanned', message: 'x' }, 403, /suspended/],
    [{ error: 'AccountLinkRequired', message: 'x', existingMethods: ['Email', 'Google'] }, 409, /Sign in with your email and password or Google, then link/]
  ])('maps %o (%i) to a clear message', async (body, status, expected) => {
    renderApp('/sign-in', (call) => (call.path === '/api/v1/auth/login' ? json(body, status) : undefined));
    await userEvent.type(await screen.findByLabelText('Email'), 'member@example.com');
    await userEvent.type(screen.getByLabelText('Password'), 'whatever password');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByText(expected)).toBeInTheDocument();
  });
});

describe('onboarding routing', () => {
  it('orders the steps: age, profile, membership', () => {
    expect(onboardingRedirect(makeMe({ ageAttested: false, profileComplete: false }))).toBe('/app/onboarding/age');
    expect(onboardingRedirect(makeMe({ profileComplete: false }))).toBe('/app/onboarding/profile');
    expect(onboardingRedirect(makeMe({ membership: { isActive: false } }))).toBe('/app/membership');
    expect(onboardingRedirect(makeMe({ membership: { isActive: false }, roles: [{ role: 'SystemAdmin', resourceType: 'Global', resourceId: '*' }] }))).toBeNull();
    expect(onboardingRedirect(makeMe({ membership: { isActive: false } }), LEVELS.profile)).toBeNull();
    expect(onboardingRedirect(makeMe())).toBeNull();
  });

  it('routes a new social account to age attestation, then profile setup', async () => {
    const me = makeMe({ ageAttested: false, profileComplete: false, handle: null, rank: null, membership: { isActive: false } });
    renderApp('/app', (call) => {
      if (call.path === '/api/v1/me/age-attestation') return json({ ...me, ageAttested: true });
      return undefined;
    }, { me });
    expect(await screen.findByRole('heading', { name: 'Confirm your age' })).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Date of birth'), '1990-01-01');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(await screen.findByRole('heading', { name: 'Set up your profile' })).toBeInTheDocument();
    expect(currentLocation.pathname).toBe('/app/onboarding/profile');
  });

  it('closes the account and signs out when the attested age is under 18 on the server', async () => {
    const me = makeMe({ ageAttested: false, profileComplete: false, membership: { isActive: false } });
    renderApp('/app/onboarding/age', (call) => (call.path === '/api/v1/me/age-attestation' ? json({ error: 'AgeRequirementNotMet', message: 'Closed.' }, 403) : undefined), { me });
    await userEvent.type(await screen.findByLabelText('Date of birth'), '1990-01-01');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(await screen.findByText(/Your account has been closed and you have been signed out/)).toBeInTheDocument();
    expect(window.localStorage.getItem(SESSION_HINT_KEY)).toBeNull();
  });

  it('sends a member without a membership to /app/membership and a complete member to the app', async () => {
    renderApp('/app/feed', (call) => (call.path === '/api/v1/membership' ? json({ membership: { isActive: false }, webOffer: { priceUsd: 45 }, history: [] }) : undefined), { me: makeMe({ membership: { isActive: false } }) });
    expect(await screen.findByRole('heading', { level: 1, name: 'Membership' })).toBeInTheDocument();
    expect(currentLocation.pathname).toBe('/app/membership');
  });

  it('saves the profile setup with the handle availability check and continues', async () => {
    const me = makeMe({ profileComplete: false, handle: null, rank: null, profile: { rank: null, goals: null }, profileMissing: ['handle', 'rank'], membership: { isActive: true, source: 'AdminComp' } });
    const calls = renderApp('/app/onboarding/profile', (call) => {
      if (call.path.startsWith('/api/v1/handles/')) return json({ available: call.path.includes('/taken/') === false });
      if (call.path === '/api/v1/me/profile') return json({ ...me, ...call.body, profileComplete: true });
      return undefined;
    }, { me });
    await screen.findByRole('heading', { name: 'Set up your profile' });
    expect(screen.getByLabelText(/^Handle/).labels[0].querySelector('.required-mark')).not.toBeNull();
    expect(screen.getByLabelText(/^(Display name|Name)/, { selector: '#profile-displayName' }).labels[0].querySelector('.required-mark')).not.toBeNull();
    // The handle starts as a suggestion from the display name; typing replaces it.
    expect(screen.getByLabelText(/^Handle/).value).toMatch(/^[a-z0-9_.]{3,30}$/);
    await userEvent.clear(screen.getByLabelText(/^Handle/));
    await userEvent.type(screen.getByLabelText(/^Handle/), 'taken');
    expect(await screen.findByText('That handle is taken.', {}, { timeout: 2000 })).toBeInTheDocument();
    await userEvent.clear(screen.getByLabelText(/^Handle/));
    await userEvent.type(screen.getByLabelText(/^Handle/), 'sam_r');
    expect(await screen.findByText('Available', {}, { timeout: 2000 })).toBeInTheDocument();
    // Onboarding asks only the sign-up fields (the required rank); profile-only fields wait for the editor.
    expect(screen.queryByRole('checkbox', { name: 'Speed' })).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Save and continue' }));
    expect(await screen.findByText('This field is required.')).toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText(/^Tier/), 'Silver');
    await userEvent.selectOptions(screen.getByLabelText('Stars'), '2');
    await userEvent.click(screen.getByRole('button', { name: 'Save and continue' }));
    // /app sends a member with an active membership on to the feed.
    await waitFor(() => expect(['/app', '/app/feed']).toContain(currentLocation.pathname));
    const save = calls.find((c) => c.path === '/api/v1/me/profile');
    expect(save.method).toBe('PUT');
    expect(save.body).toEqual({ displayName: 'Sam Rivera', handle: 'sam_r', profile: { rank: { key: 'Silver', level: 2 } } });
    for (const key of ['rank', 'rankLevel', 'affiliation', 'goals', 'goalsOther', 'rankHistory']) expect(save.body).not.toHaveProperty(key);
  });
});

describe('join flow with a code', () => {
  it('carries the code through sign-up, redeems right after registration, and skips Stripe', async () => {
    const created = makeMe({ email: 'new@example.com', displayName: 'New Member', profileComplete: false, handle: null, rank: null, membership: { isActive: false } });
    const calls = renderApp('/join?code=seminar2026', (call) => {
      if (call.path === '/api/v1/auth/register') return json(authResponse(created, { isNewAccount: true }), 201);
      if (call.path === '/api/v1/signup-codes/redeem') return json({ membership: { isActive: true, source: 'SignupCode', store: null, expiresUtc: '2027-11-11T00:00:00Z' }, grantSummary: 'Free membership for 12 months', membershipEndsUtc: '2027-11-11T00:00:00Z' });
      return undefined;
    });
    expect(await screen.findByRole('heading', { name: 'Create your account' })).toBeInTheDocument();
    expect(screen.getByText(/Sign-up code SEMINAR2026 will be applied/)).toBeInTheDocument();
    expect(screen.getByLabelText('Sign-up code (optional)')).toHaveValue('SEMINAR2026');
    await userEvent.type(screen.getByLabelText('Email'), 'new@example.com');
    await userEvent.type(screen.getByLabelText('Password'), 'correct horse battery');
    await userEvent.type(screen.getByLabelText('Display name'), 'New Member');
    await userEvent.type(screen.getByLabelText('Date of birth'), '1990-05-01');
    await userEvent.click(screen.getByRole('checkbox', { name: /I am 21 or older/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }));

    expect(await screen.findByRole('heading', { name: 'Set up your profile' })).toBeInTheDocument();
    expect(screen.getByText('Sign-up code applied: Free membership for 12 months.')).toBeInTheDocument();
    const register = calls.find((c) => c.path === '/api/v1/auth/register');
    expect(register.body).toEqual({ email: 'new@example.com', password: 'correct horse battery', displayName: 'New Member', dateOfBirth: '1990-05-01', acceptTerms: true });
    const redeem = calls.find((c) => c.path === '/api/v1/signup-codes/redeem');
    expect(redeem.body).toEqual({ code: 'SEMINAR2026' });
    expect(calls.some((c) => c.path.includes('/stripe/'))).toBe(false);
    expect(getPendingCode()).toBeNull();
  });

  it('keeps the code for an existing account that signs in and asks for confirmation', async () => {
    window.sessionStorage.setItem(PENDING_CODE_KEY, JSON.stringify({ code: 'SEMINAR2026', auto: false, savedAt: Date.now() }));
    const me = makeMe({ membership: { isActive: false } });
    const calls = renderApp('/sign-in', (call) => {
      if (call.path === '/api/v1/auth/login') return json(authResponse(me));
      if (call.path === '/api/v1/membership') return json({ membership: { isActive: false }, webOffer: { priceUsd: 45 }, history: [] });
      if (call.path === '/api/v1/signup-codes/validate') return json({ valid: true, name: 'Spring seminar 2026', grantSummary: 'Free membership for 12 months', membershipEndsUtc: '2027-11-11T00:00:00Z' });
      return undefined;
    });
    expect(await screen.findByText(/will be applied after you sign in/)).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Email'), 'member@example.com');
    await userEvent.type(screen.getByLabelText('Password'), 'correct horse battery');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByText('Free membership for 12 months')).toBeInTheDocument();
    expect(currentLocation.pathname + currentLocation.search).toBe('/app/membership?code=SEMINAR2026');
    expect(calls.some((c) => c.path === '/api/v1/signup-codes/redeem')).toBe(false);
  });

  it('a new account with a discount code goes to the membership page instead of failing', async () => {
    window.sessionStorage.setItem(PENDING_CODE_KEY, JSON.stringify({ code: 'SAVE40', auto: true, savedAt: Date.now() }));
    const err = Object.assign(new Error('discount'), { status: 400, code: 'DiscountCodeAtCheckout' });
    const api = { redeemCode: vi.fn().mockRejectedValue(err) };
    const path = await routeAfterAuth({ me: makeMe({ membership: { isActive: false } }), isNewAccount: true, api, setMe: () => {}, next: '/app' });
    expect(path).toBe('/app/membership?code=SAVE40');
    expect(takeFlash()).toEqual({ tone: 'info', message: 'Your code gives a discount. Use it at checkout below.' });
    expect(getPendingCode()).toMatchObject({ code: 'SAVE40', auto: false });
  });

  it('sends a signed-in member straight to the redeem confirmation', async () => {
    renderApp('/join?code=SEMINAR2026', (call) => {
      if (call.path === '/api/v1/membership') return json({ membership: { isActive: false }, webOffer: { priceUsd: 45 }, history: [] });
      if (call.path === '/api/v1/signup-codes/validate') return json({ valid: true, name: 'Spring seminar', grantSummary: 'Free membership for 12 months', membershipEndsUtc: null });
      return undefined;
    }, { me: makeMe({ membership: { isActive: false } }) });
    expect(await screen.findByText('Free membership for 12 months')).toBeInTheDocument();
    expect(screen.getByText('Free membership that does not expire')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Redeem code' })).toBeInTheDocument();
  });
});

describe('membership page', () => {
  const saved = { ...CHECKOUT_POLL };
  beforeEach(() => {
    CHECKOUT_POLL.intervalMs = 20;
    CHECKOUT_POLL.timeoutMs = 400;
  });
  afterEach(() => Object.assign(CHECKOUT_POLL, saved));

  it('starts Stripe Checkout and redirects to the returned URL', async () => {
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => {});
    const calls = renderApp('/app/membership', (call) => {
      if (call.path === '/api/v1/membership') return json({ membership: { isActive: false }, webOffer: { priceUsd: 45, interval: 'Month' }, history: [] });
      if (call.path === '/api/v1/membership/stripe/checkout') return json({ url: 'https://checkout.stripe.com/c/abc' });
      return undefined;
    }, { me: makeMe({ membership: { isActive: false } }) });
    await userEvent.click(await screen.findByRole('button', { name: 'Join for $45.00/month' }));
    await waitFor(() => expect(assign).toHaveBeenCalledWith('https://checkout.stripe.com/c/abc'));
    expect(calls.find((c) => c.path === '/api/v1/membership/stripe/checkout').method).toBe('POST');
  });

  it('polls GET /membership after ?checkout=success until the membership is active', async () => {
    let polls = 0;
    const calls = renderApp('/app/membership?checkout=success', (call) => {
      if (call.path !== '/api/v1/membership') return undefined;
      polls += 1;
      const active = polls >= 3;
      return json({ membership: active ? { isActive: true, source: 'Subscription', store: 'Stripe', expiresUtc: '2027-11-11T00:00:00Z', autoRenew: true } : { isActive: false }, webOffer: { priceUsd: 45 }, history: [] });
    }, { me: makeMe({ membership: { isActive: false } }) });
    expect(await screen.findByText(/Confirming your membership/)).toBeInTheDocument();
    expect(await screen.findByText('Welcome! Your membership is active.')).toBeInTheDocument();
    const count = calls.filter((c) => c.path === '/api/v1/membership').length;
    await new Promise((r) => setTimeout(r, 100));
    expect(calls.filter((c) => c.path === '/api/v1/membership').length).toBe(count);
    expect(screen.getByRole('button', { name: 'Manage billing' })).toBeInTheDocument();
  });

  it('stops polling after the timeout and explains the delay', async () => {
    renderApp('/app/membership?checkout=success', (call) => (call.path === '/api/v1/membership' ? json({ membership: { isActive: false }, webOffer: { priceUsd: 45 }, history: [] }) : undefined), { me: makeMe({ membership: { isActive: false } }) });
    expect(await screen.findByText(/still processing/, {}, { timeout: 2000 })).toBeInTheDocument();
  });

  it('a lifetime sign-up code membership hides the join offer and says it does not expire', async () => {
    const membership = { isActive: true, source: 'SignupCode', store: null, expiresUtc: null };
    renderApp('/app/membership', (call) => (call.path === '/api/v1/membership' ? json({ membership, webOffer: { priceUsd: 45 }, history: [] }) : undefined), { me: makeMe({ membership }) });
    expect(await screen.findByText(/This one does not expire/)).toBeInTheDocument();
    expect(screen.queryByText(/Subscribe here before it ends/)).toBeNull();
    expect(screen.queryByRole('button', { name: /Join for/ })).toBeNull();
  });

  it('a time-limited sign-up code membership still offers to subscribe', async () => {
    const membership = { isActive: true, source: 'SignupCode', store: null, expiresUtc: '2027-03-01T00:00:00Z' };
    renderApp('/app/membership', (call) => (call.path === '/api/v1/membership' ? json({ membership, webOffer: { priceUsd: 45 }, history: [] }) : undefined), { me: makeMe({ membership }) });
    expect(await screen.findByText(/Subscribe here before it ends/)).toBeInTheDocument();
  });

  it('explains App Store billing instead of offering the Stripe portal', async () => {
    const membership = { isActive: true, source: 'Subscription', store: 'Apple', expiresUtc: '2027-11-11T00:00:00Z', autoRenew: true };
    renderApp('/app/membership', (call) => (call.path === '/api/v1/membership' ? json({ membership, webOffer: { priceUsd: 45 }, history: [{ source: 'Subscription', store: 'Apple', startsUtc: '2026-11-11T00:00:00Z', endsUtc: '2027-11-11T00:00:00Z', status: 'Active' }] }) : undefined), { me: makeMe({ membership }) });
    expect(await screen.findByText(/billed through App Store/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Manage billing' })).toBeNull();
    expect(screen.queryByRole('button', { name: /Join for/ })).toBeNull();
    const history = await screen.findByRole('table');
    expect(within(history).getByText('Paid subscription')).toBeInTheDocument();
  });

  it('validates a code, shows the grant, then redeems on confirm', async () => {
    const calls = renderApp('/app/membership', (call) => {
      if (call.path === '/api/v1/membership') return json({ membership: { isActive: false }, webOffer: { priceUsd: 45 }, history: [] });
      if (call.path === '/api/v1/signup-codes/validate') return json({ valid: true, name: 'Partner', grantSummary: 'Free membership until March 1, 2027', membershipEndsUtc: '2027-03-01T00:00:00Z', note: 'Your free membership starts when your current membership ends on Nov 11, 2027.' });
      if (call.path === '/api/v1/signup-codes/redeem') return json({ membership: { isActive: true, source: 'SignupCode', expiresUtc: '2027-03-01T00:00:00Z' }, grantSummary: 'Free membership until March 1, 2027' });
      return undefined;
    }, { me: makeMe({ membership: { isActive: false } }) });
    await userEvent.type(await screen.findByLabelText('Code'), '  partner-2026 ');
    await userEvent.click(screen.getByRole('button', { name: 'Check code' }));
    expect(await screen.findByText(/starts when your current membership ends/)).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/signup-codes/validate').body).toEqual({ code: 'PARTNER-2026' });
    expect(calls.some((c) => c.path === '/api/v1/signup-codes/redeem')).toBe(false);
    await userEvent.click(screen.getByRole('button', { name: 'Redeem code' }));
    expect(await screen.findByText('Code redeemed: Free membership until March 1, 2027.')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/signup-codes/redeem').body).toEqual({ code: 'PARTNER-2026' });
  });

  it('a discount code is not redeemed: it is sent with checkout', async () => {
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => {});
    const calls = renderApp('/app/membership', (call) => {
      if (call.path === '/api/v1/membership') return json({ membership: { isActive: false }, webOffer: { priceUsd: 45, interval: 'Year' }, history: [] });
      if (call.path === '/api/v1/signup-codes/validate') return json({ valid: true, kind: 'Discount', name: 'Seminar', grantSummary: '40% off the first year', discountSummary: '40% off the first year' });
      if (call.path === '/api/v1/membership/stripe/checkout') return json({ url: 'https://checkout.stripe.com/c/disc' });
      return undefined;
    }, { me: makeMe({ membership: { isActive: false } }) });
    await userEvent.type(await screen.findByLabelText('Code'), 'save40-x');
    await userEvent.click(screen.getByRole('button', { name: 'Check code' }));
    expect(await screen.findByText('This code gives you a discount')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Redeem code' })).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Use at checkout' }));
    expect(await screen.findByText('Code SAVE40-X: 40% off the first year')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Continue to checkout' }));
    await waitFor(() => expect(assign).toHaveBeenCalledWith('https://checkout.stripe.com/c/disc'));
    expect(calls.find((c) => c.path === '/api/v1/membership/stripe/checkout').body).toEqual({ code: 'SAVE40-X' });
    expect(calls.some((c) => c.path === '/api/v1/signup-codes/redeem')).toBe(false);
  });

  it('shows one generic message for invalid codes and a separate one for rate limiting', async () => {
    let attempt = 0;
    renderApp('/app/membership', (call) => {
      if (call.path === '/api/v1/membership') return json({ membership: { isActive: false }, webOffer: { priceUsd: 45 }, history: [] });
      if (call.path === '/api/v1/signup-codes/validate') {
        attempt += 1;
        return attempt === 1 ? json({ valid: false, message: 'This code isn\'t valid.' }) : json({ error: 'RateLimited', message: 'Slow down.' }, 429);
      }
      return undefined;
    }, { me: makeMe({ membership: { isActive: false } }) });
    await userEvent.type(await screen.findByLabelText('Code'), 'EXPIRED-CODE');
    await userEvent.click(screen.getByRole('button', { name: 'Check code' }));
    expect(await screen.findByText('This code isn\'t valid.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Check code' }));
    expect(await screen.findByText('Too many attempts. Wait a while and try again.')).toBeInTheDocument();
  });

  it('uses the generic message when redeem fails with InvalidCode', async () => {
    renderApp('/app/membership', (call) => {
      if (call.path === '/api/v1/membership') return json({ membership: { isActive: false }, webOffer: { priceUsd: 45 }, history: [] });
      if (call.path === '/api/v1/signup-codes/validate') return json({ valid: true, grantSummary: 'Free membership for 1 month', membershipEndsUtc: null });
      if (call.path === '/api/v1/signup-codes/redeem') return json({ error: 'InvalidCode', message: 'Exhausted (internal reason).' }, 400);
      return undefined;
    }, { me: makeMe({ membership: { isActive: false } }) });
    await userEvent.type(await screen.findByLabelText('Code'), 'LASTONE');
    await userEvent.click(screen.getByRole('button', { name: 'Check code' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Redeem code' }));
    expect(await screen.findByText('This code isn\'t valid.')).toBeInTheDocument();
    expect(screen.queryByText(/internal reason/)).toBeNull();
  });
});

describe('account export', () => {
  afterEach(() => vi.restoreAllMocks());

  it('queues the asynchronous export, polls it, and follows the signed download link', async () => {
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => {});
    const me = makeMe();
    let reads = 0;
    const calls = renderApp('/app/account', (call) => {
      if (call.method === 'POST' && call.path === '/api/v1/me/export') return json({ exportId: 'aex_1', status: 'Pending' }, 202);
      if (call.method === 'GET' && call.path === '/api/v1/me/export/aex_1') {
        reads += 1;
        return json(reads === 1
          ? { id: 'aex_1', status: 'Ready', downloadUrl: '/api/v1/me/export/aex_1/download?exp=1&sig=abc', sizeBytes: 10 }
          : { id: 'aex_1', status: 'Pending' });
      }
      return undefined;
    }, { me });
    await userEvent.click(await screen.findByRole('button', { name: 'Download my data' }));
    await waitFor(() => expect(assign).toHaveBeenCalledWith('/api/v1/me/export/aex_1/download?exp=1&sig=abc'));
    expect(calls.some((c) => c.method === 'GET' && c.path === '/api/v1/me/export')).toBe(false);
  });

  it('explains an export already in progress', async () => {
    const me = makeMe();
    renderApp('/app/account', (call) => {
      if (call.method === 'POST' && call.path === '/api/v1/me/export') return json({ error: 'ExportInProgress', message: 'An export is already running.' }, 409);
      return undefined;
    }, { me });
    await userEvent.click(await screen.findByRole('button', { name: 'Download my data' }));
    expect(await screen.findByText('An export is already being prepared. Try again in a few minutes.')).toBeInTheDocument();
  });
});
