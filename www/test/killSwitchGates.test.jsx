import { afterEach, describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SESSION_HINT_KEY } from '../src/context/AuthContext.jsx';
import { killSwitchKeyFor } from '../src/views/member/RequireMember.jsx';
import strings from '../src/strings.js';
import { authResponse, createFetch, json, makeMe, renderSite } from './helpers.jsx';
import { enumPage } from './engagementHelpers.jsx';

const off = strings.member.featureOff;

afterEach(() => {
  window.localStorage.clear();
});

/** A signed-in member whose manifest kills `killed` (a feature key to message map), switchable later. */
function memberFetch(state) {
  window.localStorage.setItem(SESSION_HINT_KEY, '1');
  return createFetch((call) => {
    if (call.path === '/api/v1/auth/refresh') return json(authResponse(makeMe()));
    if (call.path === '/api/v1/media/grant') return json({ mode: 'Local', expiresUtc: '2099-01-01T00:00:00Z', cookies: [] });
    if (call.path === '/api/v1/notifications/unread-count') return json({ unreadCount: 0 });
    if (call.path === '/api/v1/app/manifest') {
      const killSwitches = {};
      for (const [key, message] of Object.entries(state.killed)) killSwitches[key] = { killed: true, message };
      state.version += 1;
      return json({ contentVersion: state.version, flags: {}, killSwitches, strings: { upserts: {}, deletes: [] }, banners: { objects: [], deletes: [] }, assets: { upserts: [], deletes: [] } });
    }
    if (call.path === '/api/v1/events/enumerate' || call.path === '/api/v1/events') return json(enumPage([]));
    return undefined;
  });
}

describe('kill switch gates in the member app (LIVE_UPDATES_CONTRACT.md section 13.2)', () => {
  it('maps route guards to engine feature keys', () => {
    expect(killSwitchKeyFor({ catalog: 'events' })).toBe('events');
    expect(killSwitchKeyFor({ browse: 'feed' })).toBe('feed');
    expect(killSwitchKeyFor({ feature: 'messaging' })).toBe('messaging');
    // The people directory uses the access key `directory`, which is not the venue directory switch.
    expect(killSwitchKeyFor({ feature: 'directory' })).toBeNull();
    expect(killSwitchKeyFor({ browse: 'any' })).toBeNull();
  });

  it('hides a killed section, shows the operator message in its place, and recovers on Try again', async () => {
    const state = { version: 0, killed: { events: 'Events are paused for maintenance.' } };
    const { fetchImpl, calls } = memberFetch(state);
    renderSite('/app/events', { fetchImpl });
    expect(await screen.findByText('Events are paused for maintenance.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: off.title })).toBeInTheDocument();
    const nav = screen.getByRole('navigation', { name: strings.member.navLabel });
    expect(within(nav).queryByRole('link', { name: strings.member.nav.events })).toBeNull();
    expect(within(nav).getByRole('link', { name: strings.member.nav.feed })).toBeInTheDocument();
    const eventCalls = () => calls.filter((c) => c.path.startsWith('/api/v1/events')).length;
    const before = eventCalls();

    state.killed = {};
    await userEvent.click(screen.getByRole('button', { name: off.retry }));
    await waitFor(() => expect(screen.queryByText('Events are paused for maintenance.')).toBeNull());
    await waitFor(() => expect(eventCalls()).toBeGreaterThan(before));
    expect(within(screen.getByRole('navigation', { name: strings.member.navLabel })).getByRole('link', { name: strings.member.nav.events })).toBeInTheDocument();
  });
});
