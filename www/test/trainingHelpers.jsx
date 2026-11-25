import { render } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { createLibraryFake, makeLesson, makeProgress, makeSeries } from '@trelliskit/shared/testing';
import { SESSION_HINT_KEY } from '../src/context/AuthContext.jsx';
import { AppRoutes } from '../src/App.jsx';
import { PropertyProvider } from '../src/context/PropertyContext.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { authResponse, createFetch, json, makeMe } from './helpers.jsx';
import { enumPage } from './engagementHelpers.jsx';
import { fakeResponse, siteWith } from './phase4Helpers.jsx';

/** Library content shared by the Training hub tests. */
export const series = makeSeries('ser_1', { title: 'Foundations', lessonCount: 2, isFollowed: true });
export const lesson = makeLesson('les_1', series, { title: 'Opening lesson', position: 1 });
export const fresh = makeLesson('les_2', series, { title: 'Newest lesson', position: 2 });
export const technique = { id: 'cat_1', slug: 'hip-escape', name: 'Hip escape', kind: 'Technique', difficulty: 'Beginner', status: 'Published', summary: 'Move the hips away.' };

/** The build-time fixture (`test/fixtures/dojo`) with extra property sections. */
export const site = (extra = {}) => siteWith({ features: { library: true, events: true, directory: true, messaging: true, ...(extra.features || {}) }, ...Object.fromEntries(Object.entries(extra).filter(([key]) => key !== 'features')) });

/** The dojo fixture with the opt-in technique catalog switched on. */
export const catalogSite = (extra = {}) => site({ ...extra, features: { catalog: true, ...(extra.features || {}) } });

/** A `GET /me/training` body; `extra` replaces any field. */
export function overviewBody(extra = {}) {
  return {
    sections: [
      { key: 'Overview', available: true },
      { key: 'Library', available: true, segments: ['Courses', 'Techniques', 'Saved'] },
      { key: 'Schedule', available: false, reason: 'NoData' },
      { key: 'Progress', available: true },
      { key: 'Rewards', available: true }
    ],
    upNext: enumPage([{ kind: 'Event', id: 'evt_1', title: 'Open session', startUtc: '2026-12-05T18:00:00Z', endUtc: '2026-12-05T19:00:00Z', timeZone: 'America/Los_Angeles', locationSummary: 'Main room' }]),
    continue: enumPage([{ lesson, progress: makeProgress('les_1', { percent: 25 }) }]),
    recentSaves: enumPage([
      { id: 'fav_1', targetType: 'CatalogEntry', targetId: 'cat_1', note: null, collectionIds: [], target: { title: 'Hip escape', imageUrl: null, href: '/catalog/hip-escape', available: true }, labels: [], tags: {} },
      { id: 'fav_2', targetType: 'Lesson', targetId: 'les_gone', note: null, collectionIds: [], target: { title: null, imageUrl: null, href: null, available: false }, labels: [], tags: {} }
    ]),
    newForYou: enumPage([fresh]),
    rank: null,
    coach: null,
    firstRun: false,
    labels: [],
    tags: {},
    ...extra
  };
}

/**
 * A restored member session (or a guest with `me: null`) served by the library fake, a small
 * catalog, `overview` for `GET /me/training` (a function may return a Response), and `extra`.
 */
export function serve({ me = makeMe(), overview = overviewBody(), extra = null, killed = null } = {}) {
  if (me) window.localStorage.setItem(SESSION_HINT_KEY, '1');
  const fake = createLibraryFake({ series: [series], lessons: [lesson, fresh], progress: [makeProgress('les_1', { positionSeconds: 30, maxPositionSeconds: 30, watchedSeconds: 30, percent: 25, lastWatchedUtc: '2026-11-20T00:00:00Z' })] });
  return createFetch((call) => {
    if (call.path === '/api/v1/auth/refresh') return me ? json(authResponse(me)) : json({ error: 'Unauthorized' }, 401);
    if (call.path === '/api/v1/media/grant') return json({ mode: 'Local', expiresUtc: '2099-01-01T00:00:00Z', cookies: [] });
    if (call.path === '/api/v1/notifications/unread-count') return json({ unreadCount: 0 });
    if (call.path === '/api/v1/app/manifest' && killed) {
      const killSwitches = {};
      for (const key of killed) killSwitches[key] = { killed: true, message: null };
      return json({ contentVersion: 1, flags: {}, killSwitches, strings: { upserts: {}, deletes: [] }, banners: { objects: [], deletes: [] }, assets: { upserts: [], deletes: [] } });
    }
    if (extra) {
      const answer = extra(call);
      if (answer) return answer;
    }
    if (call.path === '/api/v1/me/training') return typeof overview === 'function' ? overview(call) : json(overview);
    if (call.path === '/api/v1/catalog/config') return json({ programs: [], muscles: [], equipment: [] });
    if (/\/catalog\/entries\/enumerate$/.test(call.path)) return json(enumPage([technique]));
    if (call.path === '/api/v1/me/saves/state') return json({ items: (call.body.targets || []).map((target) => ({ ...target, saved: false, favoriteId: null })) });
    const result = fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body });
    return result ? fakeResponse(result) : undefined;
  });
}

function Where() {
  const location = useLocation();
  return <span data-testid="where">{location.pathname + location.search}</span>;
}

/** Render the whole app at `path` with a probe (`data-testid="where"`) showing the current address. */
export function renderApp(path, { fetchImpl, siteBundle = site() } = {}) {
  return render(
    <PropertyProvider site={siteBundle}>
      <ThemeProvider>
        <MemoryRouter initialEntries={[path]}>
          <AppRoutes fetchImpl={fetchImpl} />
          <Routes><Route path="*" element={<Where />} /></Routes>
        </MemoryRouter>
      </ThemeProvider>
    </PropertyProvider>
  );
}
