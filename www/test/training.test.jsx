import { afterEach, describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import strings from '../src/strings.js';
import { formatClock, librarySegmentsFor, normalizeOverview, parseClock, rankCardOf, saveTargetPath, trainingHubEnabled, visibleSections } from '../src/views/training/trainingModel.js';
import { json, makeMe } from './helpers.jsx';
import { enumPage } from './engagementHelpers.jsx';
import { catalogSite, overviewBody, renderApp, serve, site } from './trainingHelpers.jsx';

const t = strings.training;

afterEach(() => {
  window.localStorage.clear();
});

const tabNames = () => within(screen.getByRole('tablist', { name: t.sectionsLabel })).getAllByRole('tab').map((tab) => tab.textContent);

describe('training model', () => {
  it('keeps only available, known sections and ignores unknown keys', () => {
    const overview = normalizeOverview(overviewBody());
    expect(visibleSections(overview)).toEqual(['Overview', 'Library', 'Progress']);
    expect(visibleSections(normalizeOverview({ sections: [{ key: 'Library', available: 'yes' }, null, { available: true }] }))).toEqual([]);
    expect(normalizeOverview(null)).toMatchObject({ sections: [], rank: null, coach: null, firstRun: false });
    // Completed lessons never show in Continue training.
    expect(normalizeOverview({ continue: enumPage([{ lesson: { id: 'a' }, progress: { completed: true } }]) }).continue.objects).toEqual([]);
  });

  it('decides Library segments from the property, the caller, and the reported segments', () => {
    const me = makeMe();
    expect(librarySegmentsFor({ property: { features: {} }, me })).toEqual(['courses', 'saved']);
    expect(librarySegmentsFor({ property: { features: { catalog: true } }, me })).toEqual(['courses', 'techniques', 'saved']);
    expect(librarySegmentsFor({ property: { features: { catalog: true, saves: false } }, me })).toEqual(['courses', 'techniques']);
    const reported = normalizeOverview({ sections: [{ key: 'Library', available: true, segments: ['Courses', 'Saved'] }] });
    expect(librarySegmentsFor({ property: { features: { catalog: true } }, me, overview: reported })).toEqual(['courses', 'saved']);
    expect(librarySegmentsFor({ property: { features: { catalog: true }, access: { model: 'Open', anonymousBrowsing: true } }, me: null })).toEqual(['courses', 'techniques']);
  });

  it('reads the rank card, save paths, hub switch, and clock times', () => {
    expect(rankCardOf(null)).toBeNull();
    expect(rankCardOf({ rank: { fieldKey: 'level' }, next: null, curriculum: { id: 'cur_1' } })).toMatchObject({ rank: { fieldKey: 'level' }, curriculum: { id: 'cur_1' }, showVerificationLabels: true });
    expect(rankCardOf({ fieldKey: 'level', optionKey: 'first' }).rank.optionKey).toBe('first');
    expect(saveTargetPath('Lesson', 'les 1')).toBe('/app/training/lessons/les%201');
    expect(saveTargetPath('Thread', 'thr_1')).toBe('/app/threads/thr_1');
    expect(saveTargetPath('Unknown', 'x')).toBeNull();
    expect(trainingHubEnabled({ features: {} })).toBe(true);
    expect(trainingHubEnabled({ features: { training: false } })).toBe(false);
    expect(trainingHubEnabled({ features: { library: false } })).toBe(false);
    expect(formatClock(95)).toBe('1:35');
    expect(formatClock(3725)).toBe('1:02:05');
    expect(parseClock('1:35')).toBe(95);
    expect(parseClock('')).toBeNull();
    expect(parseClock('90')).toBe(90);
    expect(Number.isNaN(parseClock('1:75'))).toBe(true);
  });
});

describe('Training hub', () => {
  it('shows only available sections as an arrow-key tablist and the Overview cards in order', async () => {
    const { fetchImpl } = serve();
    renderApp('/app/training', { fetchImpl });
    expect(await screen.findByTestId('training-up-next', {}, { timeout: 10000 })).toBeInTheDocument();
    expect(tabNames()).toEqual(['Overview', 'Library', 'Progress']);
    const overviewTab = screen.getByRole('tab', { name: 'Overview' });
    expect(overviewTab).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tabpanel')).toHaveAttribute('aria-labelledby', overviewTab.id);
    const ids = Array.from(document.querySelectorAll('.training-grid > [data-testid]')).map((node) => node.getAttribute('data-testid'));
    expect(ids).toEqual(['training-up-next', 'training-continue', 'training-recent-saves', 'training-new']);
    expect(screen.getByRole('link', { name: /Open session/ })).toHaveAttribute('href', '/app/events/evt_1');
    expect(screen.getByRole('link', { name: /Hip escape/ })).toHaveAttribute('href', '/app/training/catalog/cat_1');
    expect(screen.getByText(t.savedUnavailable)).toBeInTheDocument();
    expect(screen.queryByTestId('training-first-run')).toBeNull();
    expect(screen.queryByTestId('training-coach')).toBeNull();
    expect(screen.queryByTestId('training-rank')).toBeNull();

    overviewTab.focus();
    await userEvent.keyboard('{ArrowRight}');
    await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent('/app/training/library/courses'));
    expect(screen.getByRole('tab', { name: 'Library' })).toHaveFocus();
    await userEvent.keyboard('{End}');
    await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent('/app/training/progress'));
  }, 20000);

  it('shows the coach card and, with ranks on, the rank and curriculum cards', async () => {
    const rank = { rank: { fieldKey: 'level', fieldLabel: 'Level', optionKey: 'first', optionLabel: 'First', rankLabel: 'First, 1 Marks', verified: true, source: 'Verified' }, next: null, curriculum: { id: 'cur_1', name: 'First curriculum', itemCount: 4, progress: { completed: 1, total: 4 } } };
    const { fetchImpl } = serve({ overview: overviewBody({ rank, coach: { available: true, placement: 'Menu' }, upNext: enumPage([]), recentSaves: enumPage([]), newForYou: enumPage([]) }) });
    renderApp('/app/training', { fetchImpl, siteBundle: site({ features: { ranks: true } }) });
    expect(await screen.findByTestId('training-rank', {}, { timeout: 10000 })).toBeInTheDocument();
    expect(screen.getByText('First, 1 Marks')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'First curriculum' })).toHaveAttribute('href', '/app/training/curricula/cur_1');
    expect(within(screen.getByTestId('training-coach')).getByRole('link', { name: t.coachOpen })).toHaveAttribute('href', '/app/assistants');
    const ids = Array.from(document.querySelectorAll('.training-grid > [data-testid]')).map((node) => node.getAttribute('data-testid'));
    expect(ids).toEqual(['training-continue', 'training-rank', 'training-curriculum', 'training-coach']);
  }, 20000);

  it('shows the first-run card with real featured courses and the directory and events entries', async () => {
    const empty = enumPage([]);
    const { fetchImpl } = serve({ overview: overviewBody({ firstRun: true, upNext: empty, continue: empty, recentSaves: empty, newForYou: empty }) });
    renderApp('/app/training', { fetchImpl });
    const card = await screen.findByTestId('training-first-run', {}, { timeout: 10000 });
    expect(await within(card).findByTestId('series-card-ser_1')).toBeInTheDocument();
    expect(within(card).getByRole('link', { name: t.startCourse })).toHaveAttribute('href', '/app/training/library/courses');
    expect(within(card).getByRole('link', { name: t.findPlace })).toHaveAttribute('href', '/app/directory');
    expect(within(card).getByRole('link', { name: t.findEvent })).toHaveAttribute('href', '/app/events');
  }, 20000);

  it('signed-out visitors see only the public Library, without calling /me/training', async () => {
    const { fetchImpl, calls } = serve({ me: null });
    renderApp('/app/training', { fetchImpl, siteBundle: catalogSite({ access: { model: 'Open', anonymousBrowsing: true } }) });
    expect(await screen.findByTestId('series-card-ser_1', {}, { timeout: 10000 })).toBeInTheDocument();
    expect(screen.getByTestId('where')).toHaveTextContent('/app/training/library/courses');
    expect(screen.queryByRole('tablist', { name: t.sectionsLabel })).toBeNull();
    const segments = within(screen.getByRole('tablist', { name: t.segmentsLabel })).getAllByRole('tab').map((tab) => tab.textContent);
    expect(segments).toEqual(['Courses', 'Techniques']);
    expect(calls.some((call) => call.path === '/api/v1/me/training')).toBe(false);
  }, 20000);

  it('PartiallyGated free accounts without the overview still reach the Library', async () => {
    const free = makeMe({ membership: { isActive: false, source: null }, membershipRequired: false });
    const { fetchImpl } = serve({ me: free, overview: () => json({ error: 'MembershipRequired', message: 'Membership required.' }, 403) });
    renderApp('/app/training', { fetchImpl, siteBundle: site({ access: { model: 'PartiallyGated', anonymousBrowsing: false, gatedFeatures: ['library'] } }) });
    expect((await screen.findAllByTestId('series-card-ser_1', {}, { timeout: 10000 })).length).toBeGreaterThan(0);
    expect(screen.getByTestId('where')).toHaveTextContent('/app/training/library/courses');
    expect(screen.queryByRole('tab', { name: 'Overview' })).toBeNull();
  }, 20000);

  it('the training kill switch leaves only the Library', async () => {
    const { fetchImpl } = serve({ killed: ['training'] });
    renderApp('/app/training', { fetchImpl });
    await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent('/app/training/library/courses'), { timeout: 10000 });
    expect((await screen.findAllByTestId('series-card-ser_1')).length).toBeGreaterThan(0);
    await waitFor(() => expect(screen.queryByRole('tablist', { name: t.sectionsLabel })).toBeNull());
  }, 20000);

  it('redirects a section the server does not report, keeping the query string', async () => {
    const { fetchImpl } = serve();
    renderApp('/app/training/schedule?from=menu', { fetchImpl });
    await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent('/app/training?from=menu'), { timeout: 10000 });
  }, 20000);

  it('shows the Progress section from the library progress routes', async () => {
    const { fetchImpl } = serve({ extra: (call) => (call.path === '/api/v1/me/library/progress/enumerate' ? json(enumPage([], { totalRecords: 3 })) : undefined) });
    renderApp('/app/training/progress', { fetchImpl });
    expect(await screen.findByTestId('progress-completed-count', {}, { timeout: 10000 })).toHaveTextContent('3 lessons completed');
    const followed = screen.getByRole('heading', { name: t.progress.seriesTitle }).closest('section');
    expect(await within(followed).findByRole('link', { name: 'Foundations' })).toHaveAttribute('href', expect.stringMatching(/^\/app\/training\/series\//));
  }, 20000);

  it('shows the Schedule section with upcoming events and, with ranks on, check-ins', async () => {
    const overview = overviewBody({ sections: [{ key: 'Overview', available: true }, { key: 'Library', available: true }, { key: 'Schedule', available: true }] });
    const { fetchImpl, calls } = serve({
      overview,
      extra: (call) => {
        if (call.path === '/api/v1/me/events/enumerate') return json(enumPage([{ id: 'evt_2', slug: 'open-mat', title: 'Open mat', startUtc: '2026-12-01T18:00:00Z', endUtc: '2026-12-01T19:00:00Z', timeZone: 'UTC' }]));
        if (call.path === '/api/v1/me/attendance/enumerate') return json(enumPage([{ id: 'att_1', targetType: 'Event', targetId: 'evt_0', checkedInUtc: '2026-11-01T18:00:00Z' }]));
        return undefined;
      }
    });
    renderApp('/app/training/schedule', { fetchImpl, siteBundle: site({ features: { ranks: true } }) });
    expect(await screen.findByRole('link', { name: /Open mat/ }, { timeout: 10000 })).toHaveAttribute('href', '/app/events/open-mat');
    expect(await screen.findByTestId('schedule-attendance-list')).toBeInTheDocument();
    const call = calls.find((c) => c.path === '/api/v1/me/events/enumerate');
    expect(call.body.filters).toMatchObject({ rsvpStatus: 'Going', upcoming: true });
  }, 20000);
  it('the lesson page inside the hub shows Save and the techniques in the lesson', async () => {
    const { fetchImpl } = serve({ extra: (call) => (call.path === '/api/v1/lessons/les_1/catalog-entries/enumerate' ? json(enumPage([{ id: 'cat_1', slug: 'hip-escape', name: 'Hip escape', startSeconds: 75, position: 0 }])) : undefined) });
    renderApp('/app/training/lessons/les_1', { fetchImpl, siteBundle: catalogSite() });
    expect(await screen.findByRole('heading', { name: t.lessonTechniques.title }, { timeout: 10000 })).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: 'Hip escape' })).toHaveAttribute('href', '/app/training/catalog/hip-escape');
    expect(await screen.findByRole('button', { name: 'Save Opening lesson' })).toHaveAttribute('aria-pressed', 'false');
  }, 20000);
});
