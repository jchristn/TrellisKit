import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import RequestHistoryView from '../src/views/admin/RequestHistoryView.jsx';
import { buildRequestHistoryQuery, buildSummaryQuery, describeFilters } from '../src/utils/query.js';
import { createFetch, json, renderAdmin } from './helpers.jsx';

function item(i) {
  return {
    id: 'req_' + i,
    userId: null,
    principalName: null,
    method: i % 2 ? 'POST' : 'GET',
    path: '/api/v1/thing/' + i,
    url: '/api/v1/thing/' + i,
    statusCode: i % 5 === 0 ? 500 : 200,
    durationMs: 1.5,
    sourceIp: '127.0.0.1',
    requestBodyBytes: 0,
    responseBodyBytes: 10,
    createdUtc: '2026-11-11T10:00:00Z',
    completedUtc: '2026-11-11T10:00:00Z'
  };
}

function backend() {
  return createFetch((call) => {
    if (call.path === '/api/v1/request-history/summary/enumerate') {
      return json({ totalCount: 60, totalSuccess: 50, totalFailure: 10, averageDurationMs: 2, success: true, objects: [], maxResults: 100, totalRecords: 0, recordsRemaining: 0, endOfResults: true, continuationToken: null });
    }
    if (call.path === '/api/v1/request-history' && call.method === 'GET') {
      // EnumerationResult (docs/ENUMERATION_AND_METADATA.md): maxResults and skip in, objects out.
      const maxResults = Number(call.query.maxResults);
      const skip = Number(call.query.skip || 0);
      const pageNumber = skip / maxResults + 1;
      return json({ success: true, maxResults, skip, continuationToken: null, endOfResults: skip + maxResults >= 60, totalRecords: 60, recordsRemaining: Math.max(0, 60 - skip - maxResults), objects: [1, 2, 3].map((n) => item((pageNumber - 1) * 10 + n)) });
    }
    if (call.path === '/api/v1/request-history' && call.method === 'DELETE') return json({ deletedCount: 7 });
    if (call.path.startsWith('/api/v1/request-history/') && call.method === 'GET') {
      return json({ ...item(1), requestHeaders: { Accept: 'application/json' }, requestBody: null, requestBodyTruncated: false, responseHeaders: { 'Content-Type': 'application/json' }, responseBody: '{"ok":true}', responseBodyTruncated: false });
    }
    return undefined;
  });
}

const listCalls = (calls) => calls.filter((c) => c.path === '/api/v1/request-history' && c.method === 'GET');

describe('request history query builders', () => {
  it('drops empty filters and adds paging', () => {
    expect(buildRequestHistoryQuery({ method: 'GET', statusCode: '', pathContains: ' /x ', fromUtc: '', toUtc: '' }, { pageNumber: 3, pageSize: 50 }))
      .toEqual({ maxResults: 50, skip: 100, method: 'GET', pathContains: '/x' });
    expect(buildRequestHistoryQuery({ fromUtc: 'a' }, { pageNumber: 1, pageSize: 25, meta: { labels: ['x'], labelMatch: 'Any', tags: { k: '*' } } }))
      .toEqual({ maxResults: 25, createdAfterUtc: 'a', labels: ['x'], labelMatch: 'Any', 'tag.k': '*' });
  });

  it('summary query replaces from and to with the chart window', () => {
    expect(buildSummaryQuery({ method: 'POST', fromUtc: 'a', toUtc: 'b' }, { fromUtc: 'c', toUtc: 'd', bucketMinutes: 15 }))
      .toEqual({ method: 'POST', fromUtc: 'c', toUtc: 'd', bucketMinutes: 15 });
  });

  it('describes filters in plain English', () => {
    expect(describeFilters({ method: 'GET', statusCode: '500' })).toBe('method=GET, status=500');
    expect(describeFilters({})).toBe('no filters');
  });
});

describe('RequestHistoryView', () => {
  it('requests page 1 with the default page size, then pages forward', async () => {
    const { fetchImpl, calls } = backend();
    renderAdmin(<RequestHistoryView />, { fetchImpl });
    expect(await screen.findByText('/api/v1/thing/1')).toBeInTheDocument();
    expect(listCalls(calls)[0].search).toBe('?maxResults=25');

    const user = userEvent.setup();
    await user.click(screen.getAllByRole('button', { name: 'Next page' })[0]);
    await screen.findByText('/api/v1/thing/11');
    expect(listCalls(calls).at(-1).search).toBe('?maxResults=25&skip=25');

    await user.selectOptions(screen.getByLabelText('Rows per page'), '50');
    await waitFor(() => expect(listCalls(calls).at(-1).search).toBe('?maxResults=50'));
    expect(screen.getByText('Page 1 of 2')).toBeInTheDocument();
  });

  it('applies filters to the list and summary queries and resets to page 1', async () => {
    const { fetchImpl, calls } = backend();
    renderAdmin(<RequestHistoryView />, { fetchImpl, path: '/admin/test?page=3' });
    await screen.findByText('/api/v1/thing/21');
    expect(listCalls(calls)[0].query.skip).toBe('50');

    const user = userEvent.setup();
    await user.selectOptions(screen.getByLabelText('Method'), 'POST');
    await user.type(screen.getByLabelText('Status code'), '500');
    await user.type(screen.getByLabelText('Path contains'), '/thing');
    await user.click(screen.getByRole('button', { name: 'Apply filters' }));

    await waitFor(() => expect(listCalls(calls).at(-1).search).toBe('?maxResults=25&method=POST&statusCode=500&pathContains=%2Fthing'));
    const summary = calls.filter((c) => c.path === '/api/v1/request-history/summary/enumerate').at(-1);
    expect(summary.body.filters).toMatchObject({ method: 'POST', statusCode: '500', pathContains: '/thing', bucketMinutes: 15 });
  });

  it('rejects an invalid status code without querying', async () => {
    const { fetchImpl, calls } = backend();
    renderAdmin(<RequestHistoryView />, { fetchImpl });
    await screen.findByText('/api/v1/thing/1');
    const before = listCalls(calls).length;
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Status code'), '99x');
    await user.click(screen.getByRole('button', { name: 'Apply filters' }));
    expect(await screen.findByText('Status code must be a number from 100 to 599.')).toBeInTheDocument();
    expect(listCalls(calls).length).toBe(before);
  });

  it('bulk deletes by the active filter after confirmation', async () => {
    const { fetchImpl, calls } = backend();
    renderAdmin(<RequestHistoryView />, { fetchImpl, path: '/admin/test?method=GET&statusCode=500' });
    await screen.findByText('/api/v1/thing/1');
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Delete matching' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Delete all 60 requests matching method=GET, status=500? This cannot be undone.')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE')).toBe(true));
    const del = calls.find((c) => c.method === 'DELETE');
    expect(del.url).toBe('/api/v1/request-history?method=GET&statusCode=500');
    expect(await screen.findByText('Deleted 7 requests.')).toBeInTheDocument();
  });

  it('opens the request inspector with headers and bodies on row click', async () => {
    const { fetchImpl, calls } = backend();
    renderAdmin(<RequestHistoryView />, { fetchImpl });
    const user = userEvent.setup();
    await user.click(await screen.findByText('/api/v1/thing/1'));
    const dialog = await screen.findByRole('dialog');
    expect(await within(dialog).findByText('Request headers')).toBeInTheDocument();
    expect(within(dialog).getByText('Response body')).toBeInTheDocument();
    expect(within(dialog).getByText('Accept')).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/v1/request-history/req_1')).toBe(true);
  });
});
