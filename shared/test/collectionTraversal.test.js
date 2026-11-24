import { test } from 'node:test';
import assert from 'node:assert/strict';
import { collectEnumeration, metricSeriesFromEnumeration, legacyMetricSeriesFromEnumeration, usageSeriesFromEnumeration } from '../src/index.js';

test('bootstrap collection follows continuation, preserves filters and metadata, and does not request all', async () => {
  const queries = [];
  const signal = new AbortController().signal;
  const result = await collectEnumeration(async (query, receivedSignal) => {
    assert.equal(receivedSignal, signal); queries.push(query);
    return query.continuationToken ? { objects: ['later'], endOfResults: true } : { objects: ['first'], continuationToken: 'cursor', endOfResults: false, totalRecords: 2, propertyId: 'app_example' };
  }, { maxResults: 1, filters: { kind: 'Example' } }, { signal });
  assert.deepEqual(result.objects, ['first', 'later']);
  assert.equal(result.propertyId, 'app_example');
  assert.equal(queries[1].continuationToken, 'cursor');
  assert.deepEqual(queries[1].filters, { kind: 'Example' });
  assert.ok(queries.every((q) => q.maxResults === 1));
});

test('bootstrap rejects missing/repeated continuations, excessive pages, and aborts without partial results', async () => {
  await assert.rejects(collectEnumeration(async () => ({ objects: [1], endOfResults: false })), /continuation token/);
  await assert.rejects(collectEnumeration(async () => ({ objects: [1], endOfResults: false, continuationToken: 'same' })), /continuation token/);
  await assert.rejects(collectEnumeration(async () => ({ objects: [1], endOfResults: false, continuationToken: 'next' }), {}, { maxPages: 1 }), /page limit/);
  const controller = new AbortController(); controller.abort();
  await assert.rejects(collectEnumeration(() => assert.fail('aborted request must not run'), {}, { signal: controller.signal }), { name: 'AbortError' });
});

test('metric observation pages reconstruct current and prior series without losing nulls or metadata', () => {
  const result = metricSeriesFromEnumeration({ metric: 'example', unit: '%', timeZone: 'UTC', completeThroughUtc: '2026-01-01', objects: [
    { period: 'Current', seriesKey: 'a', value: null, suppressed: true, complete: false, samples: 2 },
    { period: 'Previous', seriesKey: 'a', value: 3, complete: true },
    { period: 'Current', seriesKey: 'b', value: 4, complete: true }
  ] }, [{ kind: 'Deploy' }]);
  assert.equal(result.series.length, 2); assert.equal(result.previous[0].points[0].value, 3);
  assert.equal(result.series[0].points[0].value, null); assert.equal(result.series[0].points[0].suppressed, true);
  assert.equal(result.unit, '%'); assert.equal(result.annotations[0].kind, 'Deploy');
  assert.deepEqual(metricSeriesFromEnumeration({ unit: 'count', objects: [] }).series, []);
});


test('daily and usage pages retain scalar context and align all meters across sparse buckets', () => {
  const daily = legacyMetricSeriesFromEnumeration({ activeMembers: 8, objects: [{ series: 'postsByDay', date: '2026-01-02', value: 3 }, { series: 'postsByDay', date: '2026-01-01', value: 2 }] });
  assert.equal(daily.activeMembers, 8); assert.deepEqual(daily.postsByDay, [{ date: '2026-01-01', count: 2 }, { date: '2026-01-02', count: 3 }]);
  const usage = usageSeriesFromEnumeration({ timeZone: 'Pacific/Honolulu', objects: [
    { meter: 'api.requests', startUtc: '2026-01-02', endUtc: '2026-01-03', total: 5, previousTotal: 4, unit: 'count', value: 3 },
    { meter: 'api.requests', startUtc: '2026-01-01', endUtc: '2026-01-02', total: 5, previousTotal: 4, unit: 'count', value: 2 },
    { meter: 'storage.bytes', startUtc: '2026-01-02', endUtc: '2026-01-03', total: 10, unit: 'bytes', value: 10 }
  ] });
  assert.equal(usage.timeZone, 'Pacific/Honolulu'); assert.equal(usage.meters[0].meter, 'api.requests');
  assert.deepEqual(usage.meters[0].values, [2, 3]); assert.equal(usage.meters[0].previousTotal, 4);
  assert.deepEqual(usage.meters[1].values, [null, 10]);
});
