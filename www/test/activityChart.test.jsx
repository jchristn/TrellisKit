import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import ActivityChart, { normalizeBuckets } from '../src/components/ActivityChart.jsx';
import { rangeToParams } from '../src/utils/time.js';

describe('rangeToParams', () => {
  it('produces the required bucket counts and sizes', () => {
    const now = new Date('2026-11-11T10:07:00Z');
    expect(rangeToParams('hour', now)).toMatchObject({ bucketMinutes: 1, bucketCount: 60, toUtc: '2026-11-11T10:07:00.000Z', fromUtc: '2026-11-11T09:07:00.000Z' });
    expect(rangeToParams('day', now)).toMatchObject({ bucketMinutes: 15, bucketCount: 96, toUtc: '2026-11-11T10:15:00.000Z' });
    expect(rangeToParams('week', now)).toMatchObject({ bucketMinutes: 120, bucketCount: 84 });
    expect(rangeToParams('month', now)).toMatchObject({ bucketMinutes: 360, bucketCount: 120 });
  });
});

describe('normalizeBuckets', () => {
  it('fills missing buckets with zeros and places server buckets by start time', () => {
    const slots = normalizeBuckets([
      { bucketStartUtc: '2026-11-11T10:30:00Z', bucketEndUtc: '2026-11-11T10:45:00Z', successCount: 5, failureCount: 1, averageDurationMs: 3 }
    ], '2026-11-11T10:00:00Z', 15, 4);
    expect(slots).toHaveLength(4);
    expect(slots.map((s) => s.successCount)).toEqual([0, 0, 5, 0]);
    expect(slots[2].failureCount).toBe(1);
    expect(slots[0].bucketStartUtc).toBe('2026-11-11T10:00:00.000Z');
  });
});

describe('ActivityChart', () => {
  it('renders one stacked bar per bucket with heights proportional to counts', () => {
    const fromUtc = '2026-11-11T00:00:00.000Z';
    const buckets = [
      { bucketStartUtc: '2026-11-11T00:00:00Z', bucketEndUtc: '2026-11-11T00:15:00Z', successCount: 8, failureCount: 2, averageDurationMs: 10 },
      { bucketStartUtc: '2026-11-11T00:15:00Z', bucketEndUtc: '2026-11-11T00:30:00Z', successCount: 4, failureCount: 0, averageDurationMs: 5 }
    ];
    const { container } = render(
      <ActivityChart summary={{ totalCount: 14, totalSuccess: 12, totalFailure: 2, averageDurationMs: 8, buckets }} rangeId="day" fromUtc={fromUtc} />
    );
    const bars = container.querySelectorAll('svg rect');
    expect(bars).toHaveLength(192);
    expect(Number(bars[0].getAttribute('height'))).toBeGreaterThan(Number(bars[1].getAttribute('height')));
    expect(Number(bars[96].getAttribute('height'))).toBeGreaterThan(0);
    expect(Number(bars[2].getAttribute('height'))).toBe(0);
    fireEvent.click(screen.getByRole('button', { name: 'Show as table' }));
    expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(193);
    expect(screen.queryByText('No requests in this time range.')).not.toBeInTheDocument();
  });

  it('shows an empty message when every bucket is zero', () => {
    render(<ActivityChart summary={{ totalCount: 0, buckets: [] }} rangeId="hour" fromUtc="2026-11-11T00:00:00.000Z" />);
    expect(screen.getByRole('img')).toBeInTheDocument();
    expect(screen.getByText('No requests in this time range.')).toBeInTheDocument();
  });
});
