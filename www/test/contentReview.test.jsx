import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import strings from '../src/strings.js';
import { groupedAdminRoutes } from '../src/views/admin/adminRoutes.js';
import ContentReviewView from '../src/views/admin/ContentReviewView.jsx';
import { actionBody, contentLink, queueFilters, reviewTone, severityTone, snapshotExcerpt } from '../src/views/admin/contentReviewUtils.js';
import { createFetch, json, renderAdmin } from './helpers.jsx';
import { enumPage, user } from './engagementHelpers.jsx';

const s = strings.contentReview;

function change(overrides = {}) {
  return {
    id: 'chg_1', targetType: 'Comment', targetId: 'cmt_1', contextType: 'FeedPost', contextId: 'fpo_1', action: 'Create', actor: user('usr_2', 'Riley Stone'), author: user('usr_2', 'Riley Stone'),
    client: 'Mobile', snapshot: 'body: I will find where you live', mediaRefs: [], reviewStatus: 'Flagged', reviewedUtc: '2026-11-21T10:05:00Z', reviewedBy: 'model:claude-haiku-5-5', reviewerKind: 'Model',
    categories: ['HarassmentAndHate', 'PersonalData'], severity: 'High', reviewerNote: 'Threatens another member.', reviewAttempts: 0, lastError: null, notifiedUtc: '2026-11-21T10:05:01Z',
    moderationActionId: null, moderationTargetType: 'Comment', createdUtc: '2026-11-21T10:00:00Z', updatedUtc: '2026-11-21T10:05:00Z', labels: [], tags: {}, ...overrides
  };
}

const summary = { pending: 3, flagged: 1, flaggedLast24Hours: 1, failing: 0, oldestPendingUtc: '2026-11-21T09:00:00Z', reviewerReady: true, reviewerProblem: null, reviewerMessage: null, lastRunStatus: 'Succeeded', lastRunUtc: '2026-11-21T10:05:00Z', nextRunUtc: '2026-11-21T10:10:00Z' };

describe('content review helpers', () => {
  it('links to the changed content and maps filters and actions', () => {
    expect(contentLink(change())).toBe('/app/feed/fpo_1?comment=cmt_1');
    expect(contentLink(change({ targetType: 'FeedPost', targetId: 'fpo_9', contextType: null }))).toBe('/app/feed/fpo_9');
    expect(contentLink(change({ targetType: 'Thread', targetId: 'thr_1' }))).toBe('/app/threads/thr_1');
    expect(contentLink(change({ targetType: 'User', targetId: 'usr_5' }))).toBe('/admin/members?member=usr_5');
    expect(contentLink(change({ targetType: 'PollOption' }))).toBeNull();
    expect(queueFilters({ status: 'Flagged', category: 'Spam', severity: 'High', targetType: '', client: 'Web', actor: ' usr_2 ' }))
      .toEqual({ status: ['Flagged'], category: ['Spam'], minimumSeverity: 'High', client: ['Web'], actorUserId: 'usr_2' });
    expect(actionBody('Hide', { reason: 'no' }).error).toBe('reason');
    expect(actionBody('Dismiss', { reason: '' }).body).toEqual({ action: 'Dismiss' });
    expect(actionBody('Mute', { reason: 'Repeated spam', until: '2020-01-01T00:00' }).error).toBe('until');
    expect(actionBody('Warn', { reason: 'Be kind', note: 'first time' }).body).toEqual({ action: 'Warn', reason: 'Be kind', note: 'first time' });
    expect(reviewTone('Flagged')).toBe('danger');
    expect(severityTone('Medium')).toBe('warning');
    expect(snapshotExcerpt('title: Hello\nbody: World')).toBe('Hello World');
  });
});

describe('Content review admin page', () => {
  it('is in the Community navigation group for administrators only', () => {
    const ids = groupedAdminRoutes({ features: {} }).flatMap((g) => g.routes.map((r) => r.id));
    expect(ids).toContain('content-review');
    const moderatorIds = groupedAdminRoutes({ features: {} }, { moderatorOnly: true }).flatMap((g) => g.routes.map((r) => r.id));
    expect(moderatorIds).not.toContain('content-review');
  });

  it('says why rows wait when the reviewer is not ready', async () => {
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/admin/content-review/summary') return json({ ...summary, reviewerReady: false, reviewerProblem: 'NoExecutors', reviewerMessage: 'The Moderation task role has no executors.' });
      if (call.path === '/api/v1/admin/content-review/enumerate') return json(enumPage([change({ reviewStatus: 'Pending', categories: [], severity: 'None' })], { totalRecords: 1 }));
      return undefined;
    });
    renderAdmin(<ContentReviewView />, { fetchImpl });
    expect(await screen.findByText('The Moderation task role has no executors.')).toBeInTheDocument();
    expect(screen.getByText(s.notReady)).toBeInTheDocument();
  });

  it('filters the queue, opens a flagged row, and hides it through the action route', async () => {
    let row = change();
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/content-review/summary') return json(summary);
      if (call.path === '/api/v1/admin/content-review/enumerate') return json(enumPage([row], { totalRecords: 1 }));
      if (call.path === '/api/v1/admin/content-review/chg_1' && call.method === 'GET') return json(row);
      if (call.path === '/api/v1/admin/content-review/chg_1/action') {
        row = change({ reviewStatus: 'Actioned', moderationActionId: 'mda_1' });
        return json({ change: row, action: 'Hide', moderationActionId: 'mda_1' });
      }
      return undefined;
    });
    const ui = userEvent.setup();
    renderAdmin(<ContentReviewView />, { fetchImpl });
    const cell = await screen.findByText('I will find where you live');
    const tableRow = cell.closest('tr');
    expect(within(tableRow).getByText(s.statuses.Flagged)).toBeInTheDocument();
    expect(within(tableRow).getByText('Harassment and hate, Personal data')).toBeInTheDocument();
    expect(screen.getByText(s.total(1))).toBeInTheDocument();

    await ui.selectOptions(screen.getByLabelText(s.filters.status), 'Flagged');
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/admin/content-review/enumerate' && c.body.filters && c.body.filters.status && c.body.filters.status[0] === 'Flagged')).toBe(true));
    await ui.selectOptions(screen.getByLabelText(s.filters.category), 'HarassmentAndHate');
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/admin/content-review/enumerate' && c.body.filters && c.body.filters.category && c.body.filters.category[0] === 'HarassmentAndHate')).toBe(true));

    await ui.click(await screen.findByText('I will find where you live'));
    expect(await screen.findByText('Threatens another member.', { exact: false })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: s.open })).toHaveAttribute('href', '/app/feed/fpo_1?comment=cmt_1');
    await ui.click(screen.getByRole('button', { name: s.actions.Hide }));
    await ui.click(await screen.findByRole('button', { name: s.confirm }));
    expect(await screen.findByText(s.reasonProblem)).toBeInTheDocument();
    await ui.type(screen.getByLabelText(s.reason), 'Threat against a member');
    await ui.click(screen.getByRole('button', { name: s.confirm }));
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/admin/content-review/chg_1/action' && c.body.action === 'Hide' && c.body.reason === 'Threat against a member')).toBe(true));
  });

  it('offers only Dismiss, Warn, Mute, and Ban when the content has no hide and remove', async () => {
    const row = change({ id: 'chg_2', targetType: 'Message', moderationTargetType: null, contextType: 'Conversation', contextId: 'cnv_1' });
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/admin/content-review/summary') return json(summary);
      if (call.path === '/api/v1/admin/content-review/enumerate') return json(enumPage([row]));
      if (call.path === '/api/v1/admin/content-review/chg_2') return json(row);
      return undefined;
    });
    renderAdmin(<ContentReviewView />, { fetchImpl, path: '/admin/test?id=chg_2' });
    expect(await screen.findByRole('button', { name: s.actions.Warn })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: s.actions.Ban })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: s.actions.Hide })).toBeNull();
    expect(screen.queryByRole('button', { name: s.actions.Remove })).toBeNull();
  });
});
