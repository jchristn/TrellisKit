import { useMemo } from 'react';
import strings from '../strings.js';
import TimeSeries from './charts/TimeSeries.jsx';
const DAYS = 90;
function dayKey(date) {
  return date.toISOString().slice(0, 10);
}

/** Fill the last `days` UTC days with counts from `[{ date, count }]`. */
export function fillDays(points, days = DAYS, today = new Date()) {
  const counts = new Map();
  for (const p of points || []) {
    const key = String(p.date || '').slice(0, 10);
    if (key) counts.set(key, (counts.get(key) || 0) + (Number(p.count) || 0));
  }
  const end = Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate());
  const result = [];
  for (let i = days - 1; i >= 0; i--) {
    const date = new Date(end - i * 86400000);
    const key = dayKey(date);
    result.push({ date: key, count: counts.get(key) || 0 });
  }
  return result;
}


/** A legacy count series adapted to the shared accessible SVG chart kit. */
export default function RedemptionsChart({ points, today }) {
  const days = useMemo(() => fillDays(points, DAYS, today || new Date()), [points, today]);
  const total = days.reduce((sum, point) => sum + point.count, 0);
  const d = strings.admin.codes.detail;
  return <TimeSeries title={d.chartAria(total)} series={[{ key: 'redemptions', label: d.chartAria(total), points: days.map((point) => ({ startUtc: point.date + 'T00:00:00Z', value: point.count, complete: true })) }]} type="bars" summary={total ? undefined : d.chartEmpty} />;
}
