/**
 * Job engine admin methods (docs/INTEGRATION.md section 31): definitions with schedules and budgets, run now and dry
 * run, run history, cancel, items, the dead-letter view, and retry. Mixed into ApiClient.prototype.
 * (c) 2026 Lattice AI, LLC. Licensed under the MIT License; see LICENSE.md.
 */

import { ENUMERATION_MAX_RESULTS_ADMIN, toEnumerationBody } from './enumeration.js';

const enc = encodeURIComponent;
const ADMIN = { maxLimit: ENUMERATION_MAX_RESULTS_ADMIN };

function body(query) {
  return toEnumerationBody(query || {}, ADMIN);
}

/** Job run statuses. */
export const JobRunStatuses = Object.freeze(['Queued', 'Running', 'Succeeded', 'PartiallySucceeded', 'Failed', 'Cancelled', 'Skipped']);

/** Job item statuses (Dead is the dead-letter view). */
export const JobItemStatuses = Object.freeze(['Pending', 'Running', 'Succeeded', 'Failed', 'Dead', 'Skipped']);

/** Job schedule types. */
export const JobIntervalTypes = Object.freeze(['EveryNMinutes', 'EveryNHours', 'EveryNDays', 'DaysOfWeek', 'Manual']);

/** Bounds of each schedule type's interval value. */
export const JobIntervalBounds = Object.freeze({ EveryNMinutes: [5, 1440], EveryNHours: [1, 168], EveryNDays: [1, 90] });

export const jobsMethods = {
  /** POST /admin/jobs/enumerate: paged job definitions and their latest runs. */
  adminListJobs(query = {}, signal) { return this.post('/admin/jobs/enumerate', body(query), { signal }); },
  /** GET /admin/jobs/overview: the month's cost against the cap and open runs. */
  adminJobOverview(signal) { return this.get('/admin/jobs/overview', { signal }); },
  /** GET /admin/jobs/{key}. */
  adminGetJob(key, signal) { return this.get('/admin/jobs/' + enc(key), { signal }); },
  /** PUT /admin/jobs/{key}: schedule, concurrency, attempts, and run budget (budgetUsdPerRun 0 removes the cap). */
  adminUpdateJob(key, changes) { return this.put('/admin/jobs/' + enc(key), changes || {}); },
  /** POST /admin/jobs/{key}/run with `{ dryRun, parameters? }`; `parameters` is a JSON object encoded as a string. */
  adminRunJob(key, dryRun = false, parameters) { return this.post('/admin/jobs/' + enc(key) + '/run', { dryRun: !!dryRun, ...(parameters == null ? {} : { parameters }) }); },
  /** POST /admin/jobs/runs/enumerate (filters key, status, trigger). */
  adminEnumerateJobRuns(query = {}, signal) { return this.post('/admin/jobs/runs/enumerate', body(query), { signal }); },
  /** GET /admin/jobs/runs/{id} with live counts. */
  adminGetJobRun(id, signal) { return this.get('/admin/jobs/runs/' + enc(id), { signal }); },
  /** POST /admin/jobs/runs/{id}/cancel. */
  adminCancelJobRun(id) { return this.post('/admin/jobs/runs/' + enc(id) + '/cancel', {}); },
  /** POST /admin/jobs/runs/{id}/items/enumerate (filters status, key). */
  adminEnumerateJobRunItems(id, query = {}, signal) { return this.post('/admin/jobs/runs/' + enc(id) + '/items/enumerate', body(query), { signal }); },
  /** POST /admin/jobs/items/enumerate (filters runId, key, status; status Dead is the dead-letter view). */
  adminEnumerateJobItems(query = {}, signal) { return this.post('/admin/jobs/items/enumerate', body(query), { signal }); },
  /** POST /admin/jobs/items/{id}/retry. */
  adminRetryJobItem(id) { return this.post('/admin/jobs/items/' + enc(id) + '/retry', {}); }
};
