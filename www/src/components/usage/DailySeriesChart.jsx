import { useMemo } from 'react';
import strings from '../../strings.js';
import { formatNumber } from '../../utils/format.js';
import TimeSeries from '../charts/TimeSeries.jsx';
import '../../usage.css';

const s = strings.seriesChart;

function listOf(points) {
  if (Array.isArray(points)) return points;
  if (points && Array.isArray(points.objects)) return points.objects;
  return [];
}

function dayKey(date) {
  return String(date || '').slice(0, 10);
}

/**
 * Align daily `{ date, count }` (or `{ date, value }`, or the event metrics' `{ day, count }`) series on one sorted date axis. Missing days count as zero; the
 * x values are UTC midnights in seconds (uPlot's time scale).
 * @param {Array<{ key: string, label: string, points: Array<{ date: string, count: number }> }>} series
 * @returns {{ dates: string[], xs: number[], columns: number[][] }}
 */
export function alignDailySeries(series) {
  const dates = new Set();
  const maps = series.map((item) => {
    const map = new Map();
    for (const point of listOf(item.points)) {
      const key = dayKey(point && (point.date || point.day));
      if (!/^\d{4}-\d{2}-\d{2}$/.test(key)) continue;
      dates.add(key);
      map.set(key, (map.get(key) || 0) + (Number(point.count !== undefined ? point.count : point.value) || 0));
    }
    return map;
  });
  const sorted = Array.from(dates).sort();
  const xs = sorted.map((d) => Date.parse(d + 'T00:00:00Z') / 1000);
  const columns = maps.map((map) => sorted.map((d) => map.get(d) || 0));
  return { dates: sorted, xs, columns };
}

/** The text alternative: each series' total and its peak day. */
export function describeDailySeries(title, series, aligned) {
  const parts = series.map((item, i) => {
    const values = aligned.columns[i] || [];
    const total = values.reduce((sum, v) => sum + v, 0);
    let peak = 0;
    let peakDate = '';
    values.forEach((v, j) => { if (v > peak) { peak = v; peakDate = aligned.dates[j]; } });
    return s.seriesPart(item.label, formatNumber(total), formatNumber(peak), peakDate);
  });
  return s.summary(title, parts);
}

/**
 * A daily activity chart using the shared SVG chart kit, an accessible table, summary and CSV export.
 *
 * @param {{ title: string, series: Array<{ key: string, label: string, points: any }>, type?: 'bars'|'line'|'area', unit?: string, defaultOpen?: boolean }} props
 */
function DailySeriesChart({ title, series, type = 'bars', unit = 'count', defaultOpen = false }) {
  const aligned = useMemo(() => alignDailySeries(series), [series]);
  const text = describeDailySeries(title, series, aligned);
  const data = series.map((item, i) => ({ ...item, points: aligned.dates.map((date, j) => ({ startUtc: date + 'T00:00:00Z', endUtc: new Date(Date.parse(date + 'T00:00:00Z') + 86400000).toISOString(), value: aligned.columns[i][j], complete: true })) }));
  const table = <table className="data-table"><caption>{s.dataCaption(title)}</caption><thead><tr><th scope="col">{s.dateColumn}</th>{series.map((item) => <th key={item.key} scope="col">{item.label}</th>)}</tr></thead><tbody>{aligned.dates.map((date, j) => <tr key={date}><th scope="row">{date}</th>{aligned.columns.map((col, i) => <td key={series[i].key}>{formatNumber(col[j])}</td>)}</tr>)}</tbody></table>;
  if (!aligned.dates.length) return <figure className="panel daily-series-chart"><h3>{title}</h3><p className="muted small">{s.noData}</p></figure>;
  return <TimeSeries showLegend={false} title={title} series={data} type={type} unit={unit} summary={text} ariaLabel={text} defaultOpen={defaultOpen} showTableLabel={s.showData} hideTableLabel={s.hideData} tableContent={table} />;
}

export default DailySeriesChart;
