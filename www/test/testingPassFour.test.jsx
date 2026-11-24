import { describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { createFetch, json, renderSite } from './helpers.jsx';
import { BUILD_SITE } from '../src/property/buildSite.js';
import { PropertyProvider, resetPropertyRefetchClock, shouldRefetchProperty, useRuntimeProperty, useProperty } from '../src/context/PropertyContext.jsx';
import DocumentsReviewPanel from '../src/components/documents/DocumentsReviewPanel.jsx';
import { canCheckIn } from '../src/views/admin/events/EventEditorView.jsx';
import { formatDateTime, formatPreciseDateTime, formatTime } from '../src/utils/format.js';
import strings from '../src/strings.js';

const live = { ...BUILD_SITE.property, features: { ...(BUILD_SITE.property.features || {}), publicSamples: true, library: true, events: false, directory: false } };

const SAMPLE = {
  id: 'les_1',
  slug: 'first-steps',
  title: 'First steps',
  summary: 'Where to begin.',
  description: 'Watch the **basics** first.',
  kind: 'Video',
  durationSeconds: 300,
  series: { id: 'ser_1', slug: 'basics', title: 'Basics' },
  levels: [],
  audience: 'Public',
  video: { id: 'med_1', kind: 'Video', status: 'Ready', video: { hlsUrl: '/api/v1/media/files/public/med_1/master.m3u8', mp4Url: null, posterUrl: null } },
  markers: [{ id: 'lmk_1', seconds: 10, label: 'Grips' }],
  captions: [],
  commentCount: 2,
  reactionTotal: 0,
  labels: [],
  tags: {}
};

describe('public sample lessons for signed-out visitors', () => {
  it('links a sample from the home page catalog to its public page instead of the sign-up page', async () => {
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/property') return json(live);
      if (call.path === '/api/v1/public/lessons/enumerate') return json({ objects: [SAMPLE], endOfResults: true });
      return undefined;
    });
    renderSite('/', { fetchImpl });
    const lessons = await screen.findByRole('region', { name: 'Sample lessons' });
    expect(within(lessons).getByRole('link', { name: 'Watch the sample: First steps' })).toHaveAttribute('href', '/lessons/first-steps');
  });

  it('plays the lesson on the marketing site with a join prompt and no account', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/property') return json(live);
      if (call.path === '/api/v1/public/lessons/first-steps') return json(SAMPLE);
      return undefined;
    });
    renderSite('/lessons/first-steps', { fetchImpl });
    expect(await screen.findByRole('heading', { level: 1, name: 'First steps' })).toBeInTheDocument();
    expect(screen.getByText('From Basics')).toBeInTheDocument();
    expect(screen.getByText('basics')).toBeInTheDocument();
    expect(document.querySelector('video')).not.toBeNull();
    expect(screen.getByRole('link', { name: 'Join to watch every lesson' })).toHaveAttribute('href', '/sign-up');
    expect(within(screen.getByRole('main')).getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/sign-in?next=' + encodeURIComponent('/app/lessons/first-steps'));
    expect(calls.some((c) => c.path.includes('/progress'))).toBe(false);
    expect(calls.some((c) => c.path === '/api/v1/media/grant')).toBe(false);
  });

  it('shows not found for a lesson that is not a public sample', async () => {
    const { fetchImpl } = createFetch((call) => (call.path === '/api/v1/public/lessons/les_x' ? json({ error: 'NotFound' }, 404) : undefined));
    renderSite('/lessons/les_x', { fetchImpl });
    expect(await screen.findByText('Lesson not found')).toBeInTheDocument();
  });
});

describe('timestamps without seconds', () => {
  it('formats member and admin times to the minute and keeps seconds for logs', () => {
    const value = '2026-11-13T15:04:05Z';
    expect(formatDateTime(value)).not.toMatch(/:\d\d:\d\d/);
    expect(formatTime(value)).not.toMatch(/:\d\d:\d\d/);
    expect(formatPreciseDateTime(value)).toMatch(/:\d\d:\d\d/);
  });
});

describe('RSVP check-in', () => {
  it('offers Check in only for Going RSVPs that are not checked in', () => {
    expect(canCheckIn({ status: 'Going', checkedInUtc: null })).toBe(true);
    expect(canCheckIn({ status: 'Waitlisted', checkedInUtc: null })).toBe(false);
    expect(canCheckIn({ status: 'Interested', checkedInUtc: null })).toBe(false);
    expect(canCheckIn({ status: 'Going', checkedInUtc: '2026-11-13T10:00:00Z' })).toBe(false);
  });
});

describe('membership page labels', () => {
  it('does not repeat the Status heading as the field label', () => {
    expect(strings.membershipApp.stateLabel).not.toBe(strings.membershipApp.statusTitle);
  });
});

describe('required documents wording', () => {
  const doc = (extra) => ({ key: 'terms', title: 'Terms of Service', kind: 'Terms', acceptance: 'RequiredAtSignup', version: '2.0', sequence: 2, updatedUtc: '2026-11-10T12:00:00Z', ...extra });
  const renderPanel = (pending) => render(
    <PropertyProvider>
      <MemoryRouter>
        <DocumentsReviewPanel me={{ id: 'usr_1', pendingDocuments: pending }} client={{}} onAccepted={() => {}} />
      </MemoryRouter>
    </PropertyProvider>
  );

  it('asks to "please review and accept" a document the member never accepted, without "updated"', () => {
    renderPanel([doc({ previouslyAccepted: false })]);
    expect(screen.getByText(/please review and accept/)).toBeInTheDocument();
    expect(screen.queryByText(strings.documents.review.updatedBody)).toBeNull();
    expect(screen.queryByText(/updated/)).toBeNull();
  });

  it('says "updated" only when a newer version replaces one the member accepted', () => {
    renderPanel([doc({ previouslyAccepted: true })]);
    expect(screen.getByText(strings.documents.review.updatedBody)).toBeInTheDocument();
    expect(screen.getByText(/updated Nov/)).toBeInTheDocument();
  });
});

describe('the property document in open tabs', () => {
  it('refetches at most once a minute', () => {
    expect(shouldRefetchProperty(null, 1000)).toBe(true);
    expect(shouldRefetchProperty(1000, 30000)).toBe(false);
    expect(shouldRefetchProperty(1000, 61000)).toBe(true);
  });

  it('refetches GET /property when the tab regains focus and applies a changed access model', async () => {
    resetPropertyRefetchClock();
    let clock = 1000;
    const now = () => clock;
    let model = 'Open';
    const property = vi.fn(async () => ({ ...live, access: { ...(live.access || {}), model } }));
    const client = { property };
    function Probe() {
      useRuntimeProperty(client, { now });
      const { property: current } = useProperty();
      return <span data-testid="model">{current.access ? current.access.model : ''}</span>;
    }
    render(<PropertyProvider><Probe /></PropertyProvider>);
    await waitFor(() => expect(screen.getByTestId('model')).toHaveTextContent('Open'));
    expect(property).toHaveBeenCalledTimes(1);
    model = 'FullyGated';
    clock += 10 * 1000;
    act(() => { window.dispatchEvent(new Event('focus')); });
    expect(property).toHaveBeenCalledTimes(1);
    clock += 60 * 1000;
    act(() => { window.dispatchEvent(new Event('focus')); });
    expect(property).toHaveBeenCalledTimes(2);
    await waitFor(() => expect(screen.getByTestId('model')).toHaveTextContent('FullyGated'));
  });
});
