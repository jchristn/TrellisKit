import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import strings from '../src/strings.js';
import { groupedAdminRoutes } from '../src/views/admin/adminRoutes.js';
import JobsView from '../src/views/admin/jobs/JobsView.jsx';
import { clock, describeSchedule, jobBody, jobForm, minutesOf, statusTone } from '../src/views/admin/jobs/jobsUtils.js';
import { createFetch, json, renderAdmin } from './helpers.jsx';
import { enumPage } from './engagementHelpers.jsx';

const s = strings.jobsAdmin;

function job(overrides = {}) {
  return {
    id: 'jbd_1', key: 'jobs.prune', name: 'Prune job history', description: 'Deletes old runs.', enabled: true, intervalType: 'EveryNDays', intervalValue: 1,
    timeOfDayMinutesUtc: 660, daysOfWeek: [], maxConcurrency: 1, maxAttempts: 3, budgetUsdPerRun: null, requiresHarness: false, supportsDryRun: true,
    handlerAvailable: true, nextRunUtc: '2026-11-22T11:00:00Z', lastRunUtc: null, latestRun: null, updatedUtc: '2026-11-21T00:00:00Z', ...overrides
  };
}

const overview = { enabled: true, runsHarnesses: false, monthStartUtc: '2026-11-01T00:00:00Z', monthCostUsd: 12.5, monthlyBudgetUsd: 2000, remainingUsd: 1987.5, openRuns: 1, definitions: 2, enabledDefinitions: 1 };

describe('job admin helpers', () => {
  it('describes schedules and converts times', () => {
    expect(clock(660)).toBe('11:00');
    expect(minutesOf('07:30')).toBe(450);
    expect(minutesOf('24:00')).toBeNull();
    expect(describeSchedule(job())).toBe('Daily at 11:00 UTC');
    expect(describeSchedule(job({ intervalType: 'DaysOfWeek', daysOfWeek: ['Monday', 'Thursday'], timeOfDayMinutesUtc: 360 }))).toBe('Monday, Thursday at 06:00 UTC');
    expect(describeSchedule(job({ intervalType: 'EveryNMinutes', intervalValue: 15 }))).toBe('Every 15 minutes');
    expect(statusTone('Dead')).toBe('danger');
    expect(statusTone('Succeeded')).toBe('success');
  });

  it('validates the edit form before sending', () => {
    const form = jobForm(job());
    expect(jobBody(form).body).toEqual({ enabled: true, intervalType: 'EveryNDays', intervalValue: 1, timeOfDayMinutesUtc: 660, maxConcurrency: 1, maxAttempts: 3, budgetUsdPerRun: 0 });
    expect(jobBody({ ...form, intervalType: 'EveryNMinutes', intervalValue: '2' }).error).toBe('intervalValue');
    expect(jobBody({ ...form, intervalType: 'DaysOfWeek', daysOfWeek: [] }).error).toBe('daysOfWeek');
    expect(jobBody({ ...form, intervalType: 'DaysOfWeek', daysOfWeek: ['Thursday', 'Monday'] }).body.daysOfWeek).toEqual(['Monday', 'Thursday']);
    expect(jobBody({ ...form, maxConcurrency: '0' }).error).toBe('maxConcurrency');
    expect(jobBody({ ...form, budgetUsdPerRun: '-1' }).error).toBe('budgetUsdPerRun');
  });
});

describe('Jobs admin page', () => {
  it('is in the System navigation group', () => {
    const ids = groupedAdminRoutes({ features: {} }).flatMap((g) => g.routes.map((r) => r.id));
    expect(ids).toContain('jobs');
  });

  it('shows the budget, lists jobs, runs one now, and retries a dead item', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/jobs/overview') return json(overview);
      if (call.path === '/api/v1/admin/jobs/enumerate' && call.method === 'POST') return json(enumPage([job(), job({ key: 'discovery.organizations', name: 'Discover gyms', enabled: false, requiresHarness: true, supportsDryRun: true, nextRunUtc: null })]));
      if (call.path === '/api/v1/admin/jobs/runs/enumerate') return json(enumPage([{ id: 'jbr_1', key: 'jobs.prune', trigger: 'Manual', status: 'PartiallySucceeded', itemsTotal: 3, itemsSucceeded: 2, itemsDead: 1, costUsd: 0.01, createdUtc: '2026-11-21T10:00:00Z' }]));
      if (call.path === '/api/v1/admin/jobs/items/enumerate') return json(enumPage([{ id: 'jbi_9', runId: 'jbr_1', key: 'jobs.prune', targetKey: 'cell-42', status: 'Dead', attempts: 3, maxAttempts: 3, error: 'Timed out' }]));
      if (call.path === '/api/v1/admin/jobs/jobs.prune/run') return json({ id: 'jbr_2', key: 'jobs.prune', trigger: 'Manual', status: 'Queued' }, 201);
      if (call.path === '/api/v1/admin/jobs/items/jbi_9/retry') return json({ id: 'jbi_9', status: 'Pending' });
      return undefined;
    });
    const user = userEvent.setup();
    renderAdmin(<JobsView />, { fetchImpl });
    expect(await screen.findByText('$2000.00')).toBeInTheDocument();
    expect(screen.getByText('$12.50')).toBeInTheDocument();
    expect(screen.getByText(s.harnessOff)).toBeInTheDocument();
    const pruneRow = screen.getByText('Prune job history').closest('tr');
    expect(within(pruneRow).getByText('Daily at 11:00 UTC')).toBeInTheDocument();
    const discoverRow = screen.getByText('Discover gyms').closest('tr');
    expect(within(discoverRow).getByText(s.harness)).toBeInTheDocument();
    expect(within(discoverRow).getAllByText(s.never).length).toBeGreaterThan(0);

    await user.click(within(pruneRow).getByRole('button', { name: s.runNow }));
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/admin/jobs/jobs.prune/run' && c.body.dryRun === false)).toBe(true));

    const deadRow = screen.getByText('cell-42').closest('tr');
    expect(within(deadRow).getByText('Timed out')).toBeInTheDocument();
    await user.click(within(deadRow).getByRole('button', { name: s.retry }));
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/admin/jobs/items/jbi_9/retry')).toBe(true));
  });

  it('filters runs by status', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/jobs/overview') return json(overview);
      if (call.path === '/api/v1/admin/jobs/enumerate') return json(enumPage([job()]));
      if (call.path.endsWith('/enumerate')) return json(enumPage([]));
      return undefined;
    });
    const user = userEvent.setup();
    renderAdmin(<JobsView />, { fetchImpl });
    await screen.findByText('Prune job history');
    await user.selectOptions(screen.getByLabelText(s.statusFilter), 'Failed');
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/admin/jobs/runs/enumerate' && c.body.filters && c.body.filters.status && c.body.filters.status[0] === 'Failed')).toBe(true));
  });
});


it('requests subsequent job definition pages without fetching or displaying only the first page', async () => {
  const { fetchImpl, calls } = createFetch((call) => {
    if (call.path === '/api/v1/settings/enumerate') return json(enumPage([]));
    if (call.path === '/api/v1/admin/jobs/overview') return json(overview);
    if (call.path === '/api/v1/admin/jobs/enumerate') {
      const skip = call.body.skip || 0;
      return json(enumPage([job({ key: skip ? 'custom.second' : 'jobs.prune', name: skip ? 'Second page job' : 'First page job' })], { totalRecords: 26, skip, endOfResults: skip > 0, continuationToken: skip ? null : 'page-two' }));
    }
    if (call.path.endsWith('/enumerate')) return json(enumPage([]));
  });
  const user = userEvent.setup(); renderAdmin(<JobsView />, { fetchImpl });
  expect(await screen.findByText('First page job')).toBeInTheDocument();
  await user.click(screen.getAllByRole('button', { name: strings.admin.table.next })[0]);
  expect(await screen.findByText('Second page job')).toBeInTheDocument();
  expect(screen.queryByText('First page job')).not.toBeInTheDocument();
  expect(calls.find((call) => call.path === '/api/v1/admin/jobs/enumerate' && call.body.skip === 25).body.maxResults).toBe(25);
});
