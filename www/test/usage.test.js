import { describe, expect, it } from 'vitest';
import {
  STORAGE_CLASSES, USAGE_PERIODS, addDays, periodBounds, periodLengthDays, bucketAllowed, bucketSeconds, buildDataTable, buildUsagePropertiesQuery,
  chartData, daysBetween, defaultBucket, describeChart, describeStacked, filenameFromDisposition, formatBucketLabel,
  formatChange, formatCount, formatMeterValue, formatUsageBytes, formatUsdMicros, localDateOf, localMidnightUtc,
  meterValues, parseUsageOverviewState, parseUsageState, periodFromDates, periodStart, serializeUsageState, stackedData, summaryIsEmpty,
  usageQuery, usageRowTotals, validateRange, writeUsageOverviewState, zoneOffsetMs
} from '../src/utils/usage.js';

const NY = 'America/New_York';

function summary(overrides = {}) {
  return {
    propertyId: 'app_x',
    period: 'LastMonth',
    timeZone: NY,
    bucket: 'Day',
    fromUtc: '2026-10-14T04:00:00Z',
    toUtc: '2026-10-16T18:00:00Z',
    buckets: [
      { startUtc: '2026-10-14T04:00:00Z', endUtc: '2026-10-15T04:00:00Z', label: '2026-10-14' },
      { startUtc: '2026-10-15T04:00:00Z', endUtc: '2026-10-16T04:00:00Z', label: '2026-10-15' },
      { startUtc: '2026-10-16T04:00:00Z', endUtc: '2026-10-17T04:00:00Z', label: '2026-10-16' }
    ],
    meters: [
      { meter: 'posts.new', group: 'content', kind: 'Count', unit: 'count', total: 17, previousTotal: 10, change: 0.7, exact: true, values: [3, 9, 5] },
      { meter: 'users.total', group: 'users', kind: 'Gauge', unit: 'count', total: 110, previousTotal: 90, change: 0.2222, exact: true, values: [100, 98, 110] },
      { meter: 'storage.originals', group: 'storage', kind: 'Gauge', unit: 'bytes', total: 300, previousTotal: 0, change: null, exact: true, values: [100, 200, 300] },
      { meter: 'storage.imageVariants', group: 'storage', kind: 'Gauge', unit: 'bytes', total: 30, previousTotal: 0, change: null, exact: true, values: [10, null, 30] },
      { meter: 'storage.videoRenditions', group: 'storage', kind: 'Gauge', unit: 'bytes', total: 0, previousTotal: 0, change: null, exact: true, values: [null, null, null] }
    ],
    ...overrides
  };
}

describe('usage period math (display only; contract section 3.2)', () => {
  it('computes the local from-date of every period in UTC', () => {
    const now = new Date('2026-11-12T18:03:11Z'); // a Thursday
    expect(periodFromDates('UTC', now)).toEqual({
      LastDay: '2026-11-11',
      LastWeek: '2026-11-06',
      LastMonth: '2026-10-14',
      Last90Days: '2026-08-15',
      PreviousQuarter: '2026-07-01',
      ThisQuarter: '2026-10-01',
      LastYear: '2025-11-13',
      ThisWeek: '2026-11-09',
      ThisMonth: '2026-11-01',
      ThisYear: '2026-01-01'
    });
    expect(periodStart('LastDay', { timeZone: 'UTC', now }).fromUtc.toISOString()).toBe('2026-11-11T19:00:00.000Z');
  });

  it('uses the local day of the zone, not of UTC', () => {
    // 03:00 UTC on Nov 12 is still Nov 11 in New York.
    expect(localDateOf('2026-11-12T03:00:00Z', NY)).toBe('2026-11-11');
    expect(periodStart('ThisMonth', { timeZone: NY, now: '2026-12-01T03:00:00Z' }).fromDate).toBe('2026-11-01');
  });

  it('places local midnight correctly across the November DST change in New York', () => {
    expect(localMidnightUtc('2026-10-31', NY).toISOString()).toBe('2026-10-31T04:00:00.000Z');
    expect(localMidnightUtc('2026-11-01', NY).toISOString()).toBe('2026-11-01T04:00:00.000Z');
    expect(localMidnightUtc('2026-11-02', NY).toISOString()).toBe('2026-11-02T05:00:00.000Z');
    // Nov 1 is a 25 hour local day.
    expect(localMidnightUtc('2026-11-02', NY) - localMidnightUtc('2026-11-01', NY)).toBe(25 * 3600000);
    const start = periodStart('LastWeek', { timeZone: NY, now: '2026-11-03T15:00:00Z' });
    expect(start.fromDate).toBe('2026-10-28');
    expect(start.fromUtc.toISOString()).toBe('2026-10-28T04:00:00.000Z');
  });

  it('places local midnight correctly across the March DST change in New York', () => {
    expect(localMidnightUtc('2026-03-08', NY).toISOString()).toBe('2026-03-08T05:00:00.000Z');
    expect(localMidnightUtc('2026-03-09', NY).toISOString()).toBe('2026-03-09T04:00:00.000Z');
    expect(localMidnightUtc('2026-03-09', NY) - localMidnightUtc('2026-03-08', NY)).toBe(23 * 3600000);
    expect(zoneOffsetMs('2026-03-08T12:00:00Z', NY)).toBe(-4 * 3600000);
    expect(zoneOffsetMs('2026-03-08T06:00:00Z', NY)).toBe(-5 * 3600000);
    const start = periodStart('ThisWeek', { timeZone: NY, now: '2026-03-10T12:00:00Z' });
    expect(start.fromDate).toBe('2026-03-09');
    expect(start.fromUtc.toISOString()).toBe('2026-03-09T04:00:00.000Z');
  });

  it('starts weeks on Monday by default and on a configured day otherwise', () => {
    const sunday = '2026-11-15T12:00:00Z';
    expect(periodStart('ThisWeek', { timeZone: 'UTC', now: sunday }).fromDate).toBe('2026-11-09');
    expect(periodStart('ThisWeek', { timeZone: 'UTC', now: sunday, weekStartsOn: 'Sunday' }).fromDate).toBe('2026-11-15');
    expect(periodStart('ThisWeek', { timeZone: 'UTC', now: '2026-11-09T00:30:00Z' }).fromDate).toBe('2026-11-09');
  });

  it('ThisQuarter is the current calendar quarter to now', () => {
    expect(periodStart('ThisQuarter', { timeZone: NY, now: '2026-03-31T12:00:00Z' }).fromDate).toBe('2026-01-01');
    expect(periodStart('ThisQuarter', { timeZone: NY, now: '2026-04-01T12:00:00Z' }).fromDate).toBe('2026-04-01');
    const b = periodBounds('ThisQuarter', { timeZone: NY, now: '2026-11-12T18:00:00Z' });
    expect(b.fromUtc.toISOString()).toBe('2026-10-01T04:00:00.000Z');
    expect(b.toUtc.toISOString()).toBe('2026-11-12T18:00:00.000Z');
    // 2026-04-01 02:00 UTC is still March 31 in New York.
    expect(periodStart('ThisQuarter', { timeZone: NY, now: '2026-04-01T02:00:00Z' }).fromDate).toBe('2026-01-01');
  });

  it('Last90Days is the trailing 90 local days including today', () => {
    const last = periodBounds('Last90Days', { timeZone: NY, now: '2026-11-12T18:00:00Z' });
    expect(last.fromDate).toBe('2026-08-15');
    expect(daysBetween(last.fromDate, '2026-11-12') + 1).toBe(90);
    // The period crosses the November DST change: its start is still at EDT midnight.
    expect(last.fromUtc.toISOString()).toBe('2026-08-15T04:00:00.000Z');
    expect(last.toUtc.toISOString()).toBe('2026-11-12T18:00:00.000Z');
  });

  it('PreviousQuarter is the whole previous calendar quarter and does not end at now', () => {
    const b = periodBounds('PreviousQuarter', { timeZone: NY, now: '2026-11-12T18:00:00Z' });
    expect(b.fromDate).toBe('2026-07-01');
    expect(b.toDate).toBe('2026-10-01');
    expect(b.fromUtc.toISOString()).toBe('2026-07-01T04:00:00.000Z');
    expect(b.toUtc.toISOString()).toBe('2026-10-01T04:00:00.000Z');
    // In the first quarter the previous quarter is last year's fourth, across the DST change.
    const q1 = periodBounds('PreviousQuarter', { timeZone: NY, now: '2027-02-10T12:00:00Z' });
    expect(q1.fromDate).toBe('2026-10-01');
    expect(q1.toDate).toBe('2027-01-01');
    expect(q1.fromUtc.toISOString()).toBe('2026-10-01T04:00:00.000Z');
    expect(q1.toUtc.toISOString()).toBe('2027-01-01T05:00:00.000Z');
    expect(periodLengthDays({ period: 'PreviousQuarter' }, { timeZone: 'UTC', now: '2026-11-12T18:00:00Z' })).toBe(92);
    expect(defaultBucket(periodLengthDays({ period: 'PreviousQuarter' }, { timeZone: 'UTC', now: '2026-11-12T18:00:00Z' }))).toBe('Day');
    expect(defaultBucket(93)).toBe('Day');
  });

  it('civil date arithmetic and buckets', () => {
    expect(addDays('2026-02-28', 1)).toBe('2026-03-01');
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28');
    expect(daysBetween('2026-01-01', '2026-12-31')).toBe(364);
    expect(defaultBucket(1)).toBe('Hour');
    expect(defaultBucket(30)).toBe('Day');
    expect(defaultBucket(365)).toBe('Week');
    expect(defaultBucket(1000)).toBe('Month');
    expect(bucketAllowed('Hour', 14)).toBe(true);
    expect(bucketAllowed('Hour', 15)).toBe(false);
    expect(bucketAllowed('Day', 400)).toBe(true);
    expect(bucketAllowed('Day', 401)).toBe(false);
    expect(bucketAllowed('Month', 1830)).toBe(true);
  });

  it('returns null for Range and unknown periods', () => {
    expect(periodStart('Range', { timeZone: 'UTC' })).toBeNull();
    expect(periodStart('Nope', { timeZone: 'UTC' })).toBeNull();
    expect(USAGE_PERIODS).toEqual(['LastDay', 'LastWeek', 'LastMonth', 'Last90Days', 'PreviousQuarter', 'ThisQuarter', 'LastYear', 'ThisWeek', 'ThisMonth', 'ThisYear']);
    expect(periodStart('LastQuarter', { timeZone: 'UTC' })).toBeNull();
  });
});

describe('usage URL state', () => {
  it('round trips period, range, and bucket and keeps unrelated params', () => {
    const states = [
      { period: 'LastMonth', from: '', to: '', bucket: '' },
      { period: 'LastWeek', from: '', to: '', bucket: 'Hour' },
      { period: 'Range', from: '2026-01-01', to: '2026-03-31', bucket: 'Week' }
    ];
    for (const state of states) expect(parseUsageState(serializeUsageState(state))).toEqual(state);
    const sp = serializeUsageState(states[2], 'tab=usage&period=LastDay');
    expect(sp.get('tab')).toBe('usage');
    expect(sp.get('period')).toBe('Range');
    expect(serializeUsageState(states[0]).toString()).toBe('');
  });

  it('falls back to the default period and drops invalid values', () => {
    expect(parseUsageState('period=Bogus&bucket=Minute&from=2026-01-01')).toEqual({ period: 'LastMonth', from: '', to: '', bucket: '' });
    expect(parseUsageState('period=ThisYear&from=2026-01-01&to=2026-02-01')).toEqual({ period: 'ThisYear', from: '', to: '', bucket: '' });
    // The retired LastQuarter id falls back to the default period.
    expect(parseUsageState('period=LastQuarter')).toEqual({ period: 'LastMonth', from: '', to: '', bucket: '' });
    expect(parseUsageState('period=PreviousQuarter').period).toBe('PreviousQuarter');
    expect(parseUsageState('period=Last90Days').period).toBe('Last90Days');
  });

  it('builds the API query (from and to only for Range)', () => {
    expect(usageQuery({ period: 'LastWeek', from: 'x', to: 'y', bucket: '' })).toEqual({ period: 'LastWeek' });
    expect(usageQuery({ period: 'Range', from: '2026-01-01', to: '2026-01-31', bucket: 'Day' })).toEqual({ period: 'Range', from: '2026-01-01', to: '2026-01-31', bucket: 'Day' });
  });
});

describe('custom range validation', () => {
  const today = '2026-11-12';
  it('accepts a valid range, including a single day', () => {
    expect(validateRange('2026-10-01', '2026-10-31', today)).toEqual({});
    expect(validateRange('2026-11-12', '2026-11-12', today)).toEqual({});
  });
  it('requires real dates', () => {
    expect(validateRange('', '', today)).toEqual({ from: 'required', to: 'required' });
    expect(validateRange('2026-02-30', '2026-03-01', today)).toEqual({ from: 'invalid' });
  });
  it('rejects from after to, future dates, and more than 1830 days', () => {
    expect(validateRange('2026-10-02', '2026-10-01', today)).toEqual({ from: 'order' });
    expect(validateRange('2026-11-01', '2026-11-13', today)).toEqual({ to: 'future' });
    expect(validateRange('2026-11-13', '2026-11-14', today)).toEqual({ from: 'future', to: 'future' });
    expect(validateRange('2021-11-09', '2026-11-12', today)).toEqual({}); // 1830 days inclusive
    expect(validateRange('2021-11-08', '2026-11-12', today)).toEqual({ to: 'tooLong' });
  });
});

describe('usage chart data shaping', () => {
  it('maps buckets to Unix seconds and meters to null-safe value arrays', () => {
    const s = summary();
    expect(bucketSeconds(s)).toEqual([Date.parse('2026-10-14T04:00:00Z') / 1000, Date.parse('2026-10-15T04:00:00Z') / 1000, Date.parse('2026-10-16T04:00:00Z') / 1000]);
    expect(chartData(s, ['posts.new'])[1]).toEqual([3, 9, 5]);
    expect(meterValues(s, 'storage.imageVariants')).toEqual([10, null, 30]);
    expect(meterValues(s, 'missing.meter')).toEqual([null, null, null]);
    expect(meterValues({ ...s, meters: [{ meter: 'posts.new', values: [1, 'x'] }] }, 'posts.new')).toEqual([1, null, null]);
    expect(chartData(null, ['posts.new'])).toEqual([[], []]);
  });

  it('gauge series keep their levels', () => {
    expect(chartData(summary(), ['users.total'])[1]).toEqual([100, 98, 110]);
  });

  it('stacks storage classes cumulatively and keeps raw layers', () => {
    const { data, layers } = stackedData(summary(), STORAGE_CLASSES.slice(0, 3));
    expect(data[1]).toEqual([100, 200, 300]);
    expect(data[2]).toEqual([110, 200, 330]);
    expect(data[3]).toEqual([110, 200, 330]);
    expect(layers[1]).toEqual([10, null, 30]);
    const empty = stackedData(summary({ meters: [] }), STORAGE_CLASSES);
    expect(empty.data[5]).toEqual([null, null, null]);
  });
});

describe('usage formatting', () => {
  it('formats bytes, counts, tokens, and dollars', () => {
    expect(formatUsageBytes(512)).toBe('512 B');
    expect(formatUsageBytes(1536)).toBe('1.5 KB');
    expect(formatUsageBytes(5 * 1024 * 1024)).toBe('5.0 MB');
    expect(formatUsageBytes(20 * 1024 ** 3)).toBe('20 GB');
    expect(formatUsageBytes(3 * 1024 ** 4)).toBe('3.0 TB');
    expect(formatUsageBytes(null)).toBe('-');
    expect(formatCount(1234567)).toBe('1,234,567');
    expect(formatCount(undefined)).toBe('-');
    expect(formatMeterValue(4200, 'tokens')).toBe('4,200');
    expect(formatUsdMicros(1250000)).toBe('$1.25');
    expect(formatUsdMicros(42)).toBe('$0.00');
    expect(formatUsdMicros(4200)).toBe('$0.0042');
    expect(formatMeterValue(2000000, 'usdMicros')).toBe('$2.00');
  });

  it('formats the change with a sign and a tone; null reads no previous data', () => {
    expect(formatChange(0.2)).toEqual({ text: '+20.0%', tone: 'up' });
    expect(formatChange(-0.054)).toEqual({ text: '-5.4%', tone: 'down' });
    expect(formatChange(0)).toEqual({ text: '0.0%', tone: 'flat' });
    expect(formatChange(null)).toEqual({ text: 'no previous data', tone: 'flat' });
  });

  it('formats bucket labels without shifting zones', () => {
    expect(formatBucketLabel('2026-11-03', 'Day')).toBe('Nov 3');
    expect(formatBucketLabel('2026-11-02', 'Week')).toBe('Week of Nov 2');
    expect(formatBucketLabel('2026-11', 'Month')).toBe('Nov 2026');
    expect(formatBucketLabel('2026-11-03T15:00', 'Hour')).toBe('Nov 3, 3 PM');
  });

  it('reads the export file name from Content-Disposition', () => {
    expect(filenameFromDisposition('attachment; filename="usage-app_x-2026-10-01-2026-10-31.csv"', 'x')).toBe('usage-app_x-2026-10-01-2026-10-31.csv');
    expect(filenameFromDisposition('', 'fallback.json')).toBe('fallback.json');
  });
});

describe('usage data table and text alternative', () => {
  it('builds one row per bucket with formatted values', () => {
    const table = buildDataTable(summary(), ['posts.new']);
    expect(table.columns).toEqual(['Bucket', 'Posts created']);
    expect(table.rows.map((r) => [r.label, ...r.cells])).toEqual([['Oct 14', '3'], ['Oct 15', '9'], ['Oct 16', '5']]);
    const stacked = buildDataTable(summary(), ['storage.originals', 'storage.imageVariants'], { total: true });
    expect(stacked.columns).toEqual(['Bucket', 'Originals', 'Image variants', 'Total']);
    expect(stacked.rows[1].cells).toEqual(['200 B', '-', '200 B']);
  });

  it('describes a count chart with its total and peak', () => {
    expect(describeChart(summary(), 'posts.new')).toBe('Posts created per day from Oct 14 to Oct 16: total 17, peak 9 on Oct 15.');
  });

  it('describes a gauge chart with latest, low, and high', () => {
    expect(describeChart(summary(), 'users.total')).toBe('Total members per day from Oct 14 to Oct 16: latest 110, low 98 on Oct 15, high 110 on Oct 16.');
  });

  it('describes the stacked storage chart and empty charts', () => {
    expect(describeStacked(summary(), STORAGE_CLASSES.slice(0, 3))).toBe('Storage by class per day from Oct 14 to Oct 16: latest total 330 B (Originals 300 B, Image variants 30 B).');
    expect(describeChart(summary(), 'storage.videoRenditions')).toBe('Video renditions: no data for this period.');
    expect(describeChart(summary({ buckets: [] }), 'posts.new')).toBe('Posts created: no data for this period.');
  });

  it('detects an empty summary', () => {
    expect(summaryIsEmpty(summary())).toBe(false);
    expect(summaryIsEmpty(summary({ buckets: [] }))).toBe(true);
    expect(summaryIsEmpty(summary({ meters: [{ meter: 'posts.new', total: 0, values: [0, 0, 0] }] }))).toBe(true);
    expect(summaryIsEmpty(null)).toBe(true);
  });
});

describe('platform usage comparison helpers', () => {
  it('builds the enumerate body with period filters and skip paging', () => {
    expect(buildUsagePropertiesQuery({ period: 'LastWeek' }, { search: ' acme ', status: 'Enabled', ordering: 'PostsDescending' }, { pageNumber: 3, pageSize: 25 })).toEqual({
      maxResults: 25, skip: 50, ordering: 'PostsDescending', search: 'acme', filters: { period: 'LastWeek', status: 'Enabled' }
    });
    expect(buildUsagePropertiesQuery({ period: 'Range', from: '2026-01-01', to: '2026-01-31' }, {}, {}).filters).toEqual({ period: 'Range', from: '2026-01-01', to: '2026-01-31' });
  });

  it('sends label and tag filters and keeps the table state in the URL', () => {
    expect(buildUsagePropertiesQuery({ period: 'LastMonth' }, { labels: ['vip', ''], labelMatch: 'Any', tags: { tier: 'gold' } }, {})).toEqual({
      maxResults: 50, skip: 0, ordering: 'NameAscending', labels: ['vip'], labelMatch: 'Any', tags: { tier: 'gold' }, filters: { period: 'LastMonth' }
    });
    const state = parseUsageOverviewState(new URLSearchParams('q=x&status=Enabled&order=PostsDescending&page=3&size=25'));
    expect(state).toEqual({ search: 'x', status: 'Enabled', ordering: 'PostsDescending', pageNumber: 3, pageSize: 25 });
    expect(parseUsageOverviewState(new URLSearchParams('order=Bogus&size=7&page=-2'))).toEqual({ search: '', status: '', ordering: 'NameAscending', pageNumber: 1, pageSize: 50 });
    expect(writeUsageOverviewState(new URLSearchParams('period=LastWeek&page=3'), { status: 'Disabled' }).toString()).toBe('period=LastWeek&status=Disabled');
    expect(writeUsageOverviewState(new URLSearchParams('period=LastWeek'), { pageNumber: 2 }).toString()).toBe('period=LastWeek&page=2');
    expect(writeUsageOverviewState(new URLSearchParams('order=PostsDescending&size=25'), { ordering: 'NameAscending', pageSize: 50 }).toString()).toBe('');
  });

  it('sums a page of rows', () => {
    const totals = usageRowTotals([{ usersTotal: 3, storageBytes: 100, aiCostMicros: 5 }, { usersTotal: 4, storageBytes: null }]);
    expect(totals.usersTotal).toBe(7);
    expect(totals.storageBytes).toBe(100);
    expect(totals.aiCostMicros).toBe(5);
    expect(totals.posts).toBe(0);
  });
});
