import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { WebDestinationIds } from '@trelliskit/shared';
import { createLibraryFake, makeSeries } from '@trelliskit/shared/testing';
import { AppRoutes } from '../src/App.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { SESSION_HINT_KEY } from '../src/context/AuthContext.jsx';
import { MEMBER_ROUTES, guestRoutesFor, memberRoutesFor } from '../src/views/member/memberRoutes.js';
import { authResponse, createFetch, json, makeMe } from './helpers.jsx';
import { fakeResponse } from './phase4Helpers.jsx';

const ids = (routes) => routes.map((route) => route.id);

describe('training section and web navigation', () => {
  it('declares the training section after the library, which it covers', () => {
    expect(MEMBER_ROUTES.find((route) => route.id === 'training')).toMatchObject({ icon: 'barbell', feature: 'training', live: true, access: 'training', catalog: true, killSwitch: 'training' });
    expect(MEMBER_ROUTES.find((route) => route.id === 'library').coveredBy).toBe('training');
    expect(ids(MEMBER_ROUTES).indexOf('training')).toBe(ids(MEMBER_ROUTES).indexOf('library') + 1);
    // The shared validator and the server know exactly the member sections.
    expect(ids(MEMBER_ROUTES)).toEqual([...WebDestinationIds]);
  });

  it('shows Training instead of the library, and the library when Training is off or switched off', () => {
    const property = { features: {} };
    expect(ids(memberRoutesFor(property))).toContain('training');
    expect(ids(memberRoutesFor(property))).not.toContain('library');
    expect(ids(memberRoutesFor({ features: { training: false } }))).toContain('library');
    const killed = (route) => route.killSwitch === 'training';
    expect(ids(memberRoutesFor(property, killed))).toEqual(expect.arrayContaining(['library']));
    expect(ids(memberRoutesFor(property, killed))).not.toContain('training');
  });

  it('orders sections by web.navigation.primary, applies its labels, and keeps engine order otherwise', () => {
    const engine = ids(memberRoutesFor({ features: {} }));
    expect(engine).toEqual(ids(MEMBER_ROUTES).filter((id) => engine.includes(id)));
    const property = { features: {}, web: { navigation: { primary: ['training', 'messages', 'feed'], labels: { training: 'Train', nowhere: 'X', feed: ' ' } } } };
    const routes = memberRoutesFor(property);
    expect(ids(routes).slice(0, 3)).toEqual(['training', 'messages', 'feed']);
    expect(ids(routes).slice(3)).toEqual(engine.filter((id) => !['training', 'messages', 'feed'].includes(id)));
    expect(routes[0].label).toBe('Train');
    expect(routes[2].label).toBe('Feed');
    expect(MEMBER_ROUTES.find((route) => route.id === 'training').label).toBe('Training');
    // A property that lists the library keeps it beside Training.
    expect(ids(memberRoutesFor({ features: {}, web: { navigation: { primary: ['library', 'training'] } } })).slice(0, 2)).toEqual(['library', 'training']);
    // Guests get the same order.
    const guest = { access: { model: 'Open', anonymousBrowsing: true }, features: {}, web: { navigation: { primary: ['events', 'training'] } } };
    expect(ids(guestRoutesFor(guest)).slice(0, 2)).toEqual(['events', 'training']);
  });

  it('mounts /app/training with the library home', async () => {
    const fake = createLibraryFake({ series: [makeSeries('ser_1', { title: 'Getting started' })] });
    const me = makeMe();
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/auth/refresh') return json(authResponse(me));
      if (call.path === '/api/v1/media/grant') return json({ expiresUtc: '2099-01-01T00:00:00Z' });
      if (call.path === '/api/v1/notifications/unread-count') return json({ unreadCount: 0 });
      return fakeResponse(fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body }));
    });
    window.localStorage.setItem(SESSION_HINT_KEY, '1');
    render(<ThemeProvider><MemoryRouter initialEntries={['/app/training']}><AppRoutes fetchImpl={fetchImpl} restoreOnLoad /></MemoryRouter></ThemeProvider>);
    expect(await screen.findByTestId('series-card-ser_1', {}, { timeout: 10000 })).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: /Training/ }).length).toBeGreaterThan(0);
  }, 20000);
});
