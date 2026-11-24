import { metricsMethods } from '@trelliskit/shared';
import { createAdminApi } from '../src/utils/api.js';
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, renderHook, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { TimeSeries, RankedBars, Funnel, CohortGrid, HeatMap, Histogram, UptimeStrip, Swimlanes } from '../src/components/charts/index.js';
import { csvForSeries, observed, previousChange, segments } from '../src/components/charts/chartData.js';
import { metricDrilldown, metricFilters, metricRange } from '../src/views/admin/metrics/metricViewUtils.js';
import MetricsView, { WorkerTimeline } from '../src/views/admin/metrics/MetricsView.jsx';
import { filtersFromSearchParams, buildRequestHistoryQuery } from '../src/utils/query.js';
import { ADMIN_THREAD_FILTERS, MOD_ACTION_FILTERS, buildAdminThreadsQuery, buildModerationActionsQuery } from '../src/utils/adminQuery.js';
import useTableState from '../src/hooks/useTableState.js';
import { groupedAdminRoutes, findAdminRoute } from '../src/views/admin/adminRoutes.js';

const api = vi.hoisted(() => ({ client: { get: vi.fn(), post: vi.fn() } }));
Object.assign(api, createAdminApi(Object.assign(api.client, metricsMethods)));
const enumeration = (objects, metadata = {}) => ({ objects, totalRecords: objects.length, endOfResults: true, continuationToken: null, ...metadata });
vi.mock('../src/context/AdminAuthContext.jsx', () => ({ useAdminAuth: () => ({ api }) }));
vi.mock('../src/context/PropertyContext.jsx', () => ({ useProperty: () => ({ property: { features: { forums: false } } }) }));
const stamp = (day) => `2026-11-${String(day).padStart(2, '0')}T00:00:00Z`;
const series = [{ key: 'requests', label: 'Requests', points: [{ startUtc: stamp(1), endUtc: stamp(2), value: 10, complete: true }, { startUtc: stamp(2), endUtc: stamp(3), value: null, complete: true }, { startUtc: stamp(3), endUtc: stamp(4), value: 2, suppressed: true }, { startUtc: stamp(4), endUtc: stamp(5), value: 20, complete: false }] }];

describe('shared chart contracts', () => {
  it('breaks geometry at missing and suppressed buckets without turning them into zeros', () => {
    expect(segments(series[0].points)).toHaveLength(2);
    expect(observed(series[0].points[2])).toBeNull();
    const { container } = render(<TimeSeries title="Requests" series={series} />);
    expect(screen.getByRole('img', { name: 'Requests: Requests' })).toBeInTheDocument();
    expect(container.querySelectorAll('[data-testid="metric-segment"]')).toHaveLength(2);
    expect(container.querySelectorAll('circle')).toHaveLength(2);
  });
  it('does not invent a zero baseline for stacked layers over a missing observation', () => {
    const { container } = render(<TimeSeries title="Stacked" type="stackedArea" series={[{ key: 'one', points: [{ startUtc: stamp(1), value: null }] }, { key: 'two', points: [{ startUtc: stamp(1), value: 5 }] }]} />);
    expect(container.querySelectorAll('circle')).toHaveLength(0);
    expect(container.querySelectorAll('[data-testid="metric-segment"]')).toHaveLength(0);
  });
  it('distinguishes an observed outage from healthy and unknown uptime', () => {
    const { container } = render(<UptimeStrip title="Availability" series={[{ key: 'provider', points: [{ startUtc: stamp(1), endUtc: stamp(2), value: 100 }, { startUtc: stamp(2), endUtc: stamp(3), value: 0 }, { startUtc: stamp(3), endUtc: stamp(4), value: null }] }]} />);
    const marks = [...container.querySelectorAll('rect')];
    expect(marks).toHaveLength(2);
    expect(marks[1]).toHaveAttribute('stroke-dasharray', '2 2');
    expect(marks[0].getAttribute('fill')).not.toBe(marks[1].getAttribute('fill'));
  });
  it('shares a table with exact observed values, gaps, suppression and partial status', async () => {
    render(<TimeSeries title="Requests" series={series} />);
    await userEvent.click(screen.getByRole('button', { name: 'Show as table' }));
    const table = screen.getByRole('table', { name: 'Requests' });
    expect(within(table).getByText('No data')).toBeInTheDocument();
    expect(within(table).getByText('Fewer than 5')).toBeInTheDocument();
    expect(within(table).getByText('Partial')).toBeInTheDocument();
    expect(within(table).getAllByRole('row')).toHaveLength(5);
    expect(within(table).queryByText('2')).not.toBeInTheDocument();
  });
  it('keeps suppressed values out of CSV and neutralizes spreadsheet formulas', () => {
    const csv = csvForSeries([{ ...series[0], key: '=IMPORTDATA("secret")' }]);
    expect(csv).toContain("'=IMPORTDATA");
    expect(csv).toContain('"","fewer than 5"');
    expect(csv).toContain('"20","partial"');
    expect(csv).not.toContain('"2","fewer than 5"');
  });
  it('supports keyboard drill-down and exposes comparison and annotations', () => {
    const click = vi.fn();
    render(<TimeSeries title="Health" series={series} previous={[{ key: 'requests', points: [{ startUtc: stamp(1), value: 5 }] }]} annotations={[{ id: 'deploy', occurredUtc: stamp(2), label: 'Release published' }]} onPointClick={click} />);
    fireEvent.keyDown(screen.getByRole('button', { name: /^Requests:.*10;/ }), { key: 'Enter' });
    expect(click).toHaveBeenCalledWith(series[0].points[0], expect.objectContaining({ key: 'requests' }));
    expect(screen.getByText(/Previous period/)).toBeInTheDocument();
    expect(screen.getByText(/Release published/)).toBeInTheDocument();
  });
  it('keeps signed values within the plot and displays negative bars', () => {
    const { container } = render(<TimeSeries title="Balance" type="bars" series={[{ key: 'balance', points: [{ startUtc: stamp(1), value: -12 }, { startUtc: stamp(2), value: 8 }] }]} />);
    const bars = [...container.querySelectorAll('rect')];
    expect(bars).toHaveLength(2);
    for (const bar of bars) { expect(Number(bar.getAttribute('height'))).toBeGreaterThan(0); expect(Number(bar.getAttribute('y'))).toBeGreaterThanOrEqual(20); expect(Number(bar.getAttribute('y')) + Number(bar.getAttribute('height'))).toBeLessThanOrEqual(220); }
  });
  it('compares matching series keys when previous-period group order differs', () => {
    render(<TimeSeries title="Groups" series={[{ key: 'A', points: [{ value: 10 }] }, { key: 'B', points: [{ value: 20 }] }]} previous={[{ key: 'B', points: [{ value: 10 }] }, { key: 'A', points: [{ value: 10 }] }]} />);
    expect(screen.getByText(/A: latest 10.*0%.*B: latest 20.*100%/)).toBeInTheDocument();
  });
  it('preserves histogram bins in numeric order instead of ranking their frequencies', () => {
    const { container } = render(<Histogram title="Latency distribution" series={[{ key: 'latency', points: [{ label: '0–10', value: 2 }, { label: '10–20', value: 20 }, { label: '20–30', value: 4 }] }]} />);
    expect([...container.querySelectorAll('svg g > text:first-child')].map((node) => node.textContent)).toEqual(['0–10', '10–20', '20–30']);
  });
  it('does not invent a percentage increase from a zero baseline', () => {
    expect(previousChange(5, 0)).toBeNull();
    expect(previousChange(0, 0)).toBe(0);
    expect(previousChange(15, 10)).toBe(50);
  });
  it.each([RankedBars, Funnel, CohortGrid, HeatMap, Histogram, UptimeStrip, Swimlanes])('gives every specialized chart a table and CSV action (%#)', async (Component) => {
    render(<Component title="Breakdown" series={series} />);
    expect(screen.getByRole('img', { name: 'Breakdown' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Download CSV' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Show as table' }));
    expect(screen.getByRole('table', { name: 'Breakdown' })).toBeInTheDocument();
  });
});

describe('metric view URL and declarations', () => {
  it('uses bounded custom ranges and accepts only declared filters', () => {
    const params = new URLSearchParams('range=custom&from=2026-11-01T00:00:00Z&to=2026-11-03T00:00:00Z&compare=false&filter.route=%2Fhealth&filter.secret=x');
    expect(metricRange(params)).toMatchObject({ bucket: 'Hour', compare: false });
    expect(metricFilters(params, ['route'])).toEqual({ route: '/health' });
    expect(metricRange(new URLSearchParams('range=custom&from=x&to=y'))).toBeNull();
    expect(metricRange(new URLSearchParams('range=custom&from=2020-01-01&to=2026-01-01'))).toBeNull();
    expect(metricDrilldown({ drilldown: 'https://elsewhere.test' }, series[0].points[0])).toBeNull();
    expect(metricDrilldown({ key: 'requests.count', drilldown: '/admin/requests' }, series[0].points[0])).toContain('fromUtc=2026-11-01');
  });
  it('maps request dimensions and bucket boundaries through the real request-history URL parser', () => {
    const definition = { key: 'requests.count', drilldown: '/admin/requests' };
    const href = metricDrilldown(definition, series[0].points[0], { dimension: 'route', key: '/api/v1/things/{id}' }, { statusClass: '5xx', errorCode: 'Unavailable', app: 'web', platform: 'browser', version: '1.2' });
    const query = buildRequestHistoryQuery(filtersFromSearchParams(new URL(href, 'https://example.test').searchParams));
    expect(query).toMatchObject({ createdAfterUtc: stamp(1), createdBeforeUtc: stamp(2), routeTemplate: '/api/v1/things/{id}', statusClass: '5xx', errorCode: 'Unavailable', clientApp: 'web', clientPlatform: 'browser', clientVersion: '1.2' });
    expect(query).not.toHaveProperty('fromUtc');
    expect(query).not.toHaveProperty('route');
    expect(metricDrilldown(definition, series[0].points[2], { dimension: 'route', key: '/secret' })).toBeNull();
    expect(metricDrilldown(definition, series[0].points[0], { dimension: 'route', key: 'Other' })).toBeNull();
    expect(metricDrilldown(definition, series[0].points[0], { dimension: 'weekday', key: 'Monday' })).toBeNull();
    expect(metricDrilldown({ key: 'members.retention', drilldown: '/admin/members' }, series[0].points[0])).toBeNull();
  });
  it('uses actual thread and prefixed moderation table state for supported source lists', () => {
    const threadHref = metricDrilldown({ key: 'forums.posts', drilldown: '/admin/forums' }, series[0].points[0], { dimension: 'forum', key: 'frm_example' });
    const thread = renderHook(() => useTableState({ storageKey: 'test.metric.threads', filterKeys: ADMIN_THREAD_FILTERS }), { wrapper: ({ children }) => <MemoryRouter initialEntries={[threadHref]}>{children}</MemoryRouter> });
    expect(buildAdminThreadsQuery(thread.result.current.filters)).toMatchObject({ createdAfterUtc: stamp(1), createdBeforeUtc: stamp(2), filters: { forumIds: ['frm_example'] } });
    thread.unmount();
    const actionHref = metricDrilldown({ key: 'moderation.actions', drilldown: '/admin/moderation' }, series[0].points[0], { dimension: 'action', key: 'Hide' });
    expect(actionHref).toContain('tab=actions');
    const action = renderHook(() => useTableState({ storageKey: 'test.metric.actions', filterKeys: MOD_ACTION_FILTERS, prefix: 'a.' }), { wrapper: ({ children }) => <MemoryRouter initialEntries={[actionHref]}>{children}</MemoryRouter> });
    expect(buildModerationActionsQuery(action.result.current.filters)).toMatchObject({ createdAfterUtc: stamp(1), createdBeforeUtc: stamp(2), action: 'Hide' });
  });
  it.each([TimeSeries, RankedBars, Funnel, CohortGrid, HeatMap, UptimeStrip, Swimlanes])('never exposes suppressed points as drill-down controls (%#)', (Component) => {
    const { container } = render(<Component title="Suppressed" series={[{ key: 'private', points: [series[0].points[2]] }]} onPointClick={vi.fn()} />);
    expect(container.querySelectorAll('svg [role="button"]')).toHaveLength(0);
  });
  it('adds declared pages to the admin sidebar and keeps them hidden from non-admin staff', () => {
    const metricPages = [{ id: 'extension', title: 'Extension activity' }, { id: 'bad/path', title: 'Invalid' }];
    const routes = groupedAdminRoutes({}, { metricPages }).flatMap((g) => g.routes);
    expect(routes.find((r) => r.id === 'metrics/extension')?.label).toBe('Extension activity');
    expect(routes.some((r) => r.id.includes('bad/path'))).toBe(false);
    expect(findAdminRoute('metrics/extension', metricPages)?.label).toBe('Extension activity');
    expect(groupedAdminRoutes({}, { metricPages, moderatorOnly: true }).flatMap((g) => g.routes).some((r) => r.id.startsWith('metrics/'))).toBe(false);
  });
  it('renders exact worker passes within the requested window using standard enumeration', async () => {
    api.client.post.mockResolvedValue(enumeration([{ id: 'pass', worker: 'SnapshotWorker', startedUtc: stamp(1), endedUtc: stamp(2), outcome: 'Failed', itemsProcessed: 0, intervalSeconds: 60, error: 'TimeoutException' }]));
    render(<WorkerTimeline api={api} range={{ from: '2026-08-01T00:00:00Z', to: stamp(3) }} />);
    await screen.findByText('SnapshotWorker · Since last success');
    await userEvent.click(screen.getByRole('button', { name: 'Show as table' }));
    expect(screen.getByRole('table', { name: 'Workers' })).toHaveTextContent('TimeoutException');
    expect(api.client.post).toHaveBeenCalledWith('/admin/metrics/workers/enumerate', expect.objectContaining({ maxResults: 25, createdAfterUtc: '2026-08-01T00:00:00Z', createdBeforeUtc: stamp(3) }), expect.objectContaining({ signal: expect.any(AbortSignal) }));
  });
  it('renders a property-declared page, skips disabled features and queries the registry contract', async () => {
    api.client.post.mockImplementation(async (path, body) => {
      if (path === '/admin/metrics/pages/enumerate') return enumeration([{ id: 'extension', title: 'Extension activity', question: 'Are resources used?', charts: [{ id: 'extra', title: 'Resource activity', metric: 'extension.activity', form: 'Bars' }, { id: 'off', metric: 'forums.unanswered', feature: 'forums' }] }]);
      if (path === '/admin/metrics/enumerate') return enumeration([{ key: 'extension.activity', title: 'Activity', unit: 'count', groupings: ['kind'], buckets: ['Day'] }]);
      if (path === '/admin/metrics/annotations/enumerate') return enumeration([{ id: 'deploy', kind: 'Deploy', label: 'Release published', occurredUtc: stamp(1) }, { id: 'broadcast', kind: 'Broadcast', label: 'Broadcast sent', occurredUtc: stamp(1) }]);
      if (path === '/admin/metrics/query') {
        const rows = series[0].points.map((point, index) => ({ ...point, id: 'point' + index, period: 'Current', seriesKey: 'Requests' }));
        return enumeration(body.continuationToken ? rows.slice(2) : rows.slice(0, 2), { metric: 'extension.activity', unit: 'count', timeZone: 'UTC', totalRecords: 4, endOfResults: Boolean(body.continuationToken), continuationToken: body.continuationToken ? null : 'next-points' });
      }
      throw new Error('Unexpected request: ' + path);
    });
    render(<MemoryRouter initialEntries={['/admin/metrics/extension?range=custom&from=2026-11-01T00%3A00%3A00Z&to=2026-11-05T00%3A00%3A00Z&filter.kind=example&annotationKind=Deploy']}><Routes><Route path="/admin/metrics/:pageId" element={<MetricsView />} /></Routes></MemoryRouter>);
    expect(await screen.findByRole('heading', { name: 'Extension activity' })).toBeInTheDocument();
    expect(await screen.findByRole('img', { name: 'Resource activity: Requests' })).toBeInTheDocument();
    expect(api.client.post).toHaveBeenCalledWith('/admin/metrics/query', expect.objectContaining({ query: expect.objectContaining({ metric: 'extension.activity', bucket: 'Day', filters: { kind: 'example' }, compare: true }) }), expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(screen.getByText(/Release published/)).toBeInTheDocument();
    expect(screen.queryByText(/Broadcast sent/)).not.toBeInTheDocument();
    expect(api.client.post.mock.calls.some((call) => call[1]?.query?.metric === 'forums.unanswered')).toBe(false);
    expect(api.client.post.mock.calls.some((call) => call[0] === '/admin/metrics/query' && call[1].continuationToken === 'next-points')).toBe(true);
  });
});
