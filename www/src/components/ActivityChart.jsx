import { useMemo } from 'react';
import strings from '../strings.js';
import { RANGES } from '../utils/time.js';
import TimeSeries from './charts/TimeSeries.jsx';

export function normalizeBuckets(buckets, fromUtc, bucketMinutes, bucketCount) {
  const start = new Date(fromUtc).getTime();
  const size = bucketMinutes * 60000;
  const slots = [];
  for (let i = 0; i < bucketCount; i++) {
    slots.push({
      bucketStartUtc: new Date(start + i * size).toISOString(),
      bucketEndUtc: new Date(start + (i + 1) * size).toISOString(),
      successCount: 0,
      failureCount: 0,
      averageDurationMs: 0
    });
  }
  if (!Number.isFinite(start)) return slots;
  for (const b of buckets || []) {
    const t = new Date(b.bucketStartUtc).getTime();
    if (!Number.isFinite(t)) continue;
    const index = Math.floor((t - start) / size + 1e-9);
    if (index < 0 || index >= bucketCount) continue;
    const slot = slots[index];
    slot.successCount += Number(b.successCount) || 0;
    slot.failureCount += Number(b.failureCount) || 0;
    slot.averageDurationMs = Number(b.averageDurationMs) || 0;
    if (b.bucketEndUtc) slot.bucketEndUtc = b.bucketEndUtc;
  }
  return slots;
}

/** Compatibility adapter for the request-history summary, drawn by the shared chart kit. */
export default function ActivityChart({ summary, rangeId, fromUtc, onBucketClick }) {
  const range = RANGES[rangeId] || RANGES.day;
  const buckets = useMemo(() => normalizeBuckets(summary?.buckets, fromUtc, range.bucketMinutes, range.bucketCount), [summary, fromUtc, range.bucketMinutes, range.bucketCount]);
  const c = strings.admin.chart;
  const series = [{ key: 'success', label: c.legendSuccess, field: 'successCount' }, { key: 'failure', label: c.legendFailure, field: 'failureCount' }].map(({ key, label, field }) => ({ key, label, points: buckets.map((b) => ({ startUtc: b.bucketStartUtc, endUtc: b.bucketEndUtc, value: b[field], complete: true, bucket: b })) }));
  const empty = !buckets.some((b) => b.successCount || b.failureCount);
  return <TimeSeries title={c.ariaLabel(c.ranges[range.id])} series={series} type="stackedBars" summary={empty ? c.noData : undefined} onPointClick={onBucketClick ? (point) => onBucketClick(point.bucket) : undefined} />;
}
