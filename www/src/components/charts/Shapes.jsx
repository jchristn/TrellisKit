import ChartFrame from './ChartFrame.jsx';
import { boundedSeries, formatValue, lastObserved, observed, timeLabel } from './chartData.js';
import strings from '../../strings.js';

const colors = (i) => `var(--chart-series-${i % 6 + 1})`;
function action(onPointClick, point, item, canPointClick) {
  return onPointClick && observed(point) !== null && canPointClick(point, item) ? { tabIndex: 0, role: 'button', onClick: () => onPointClick(point, item), onKeyDown: (event) => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onPointClick(point, item); } } } : {};
}
function label(point, item, unit) { return `${item.label || item.key}: ${point.suppressed ? strings.chartKit.suppressed : observed(point) === null ? strings.chartKit.missing : formatValue(point.value, unit)}`; }

/** Ranked aggregate bars. Rows may include a secondary value and a drill-down URL. */
export function RankedBars({ rows, series = [], title, unit, onPointClick, canPointClick = () => true, preserveOrder = false, ...rest }) {
  const data = rows ? rows.slice(0, 20).map((r) => ({ key: r.key || r.label, label: r.label, points: [{ ...r, label: r.label }] })) : boundedSeries(series).slice(0, 20);
  const ranked = data.map((item) => ({ item, point: lastObserved(item.points) || item.points[item.points.length - 1] })).filter((r) => r.point);
  if (!preserveOrder) ranked.sort((a, b) => (observed(b.point) ?? -Infinity) - (observed(a.point) ?? -Infinity));
  const max = Math.max(1, ...ranked.map((r) => observed(r.point) || 0));
  return <ChartFrame title={title} series={data} unit={unit} onPointClick={onPointClick} canPointClick={canPointClick} {...rest}><svg className="metric-svg" role="img" aria-label={title} viewBox={`0 0 800 ${Math.max(80, ranked.length * 34)}`}>
    {ranked.map(({ item, point }, i) => <g key={item.key} {...action(onPointClick, point, item, canPointClick)} aria-label={label(point, item, unit)}><text x="0" y={i * 34 + 22} className="metric-axis">{(item.label || item.key).slice(0, 25)}</text><rect x="180" y={i * 34 + 5} height="22" width={Math.max(0, observed(point) || 0) / max * 450} fill={colors(i)} /><text x="640" y={i * 34 + 22} className="metric-axis">{point.suppressed ? strings.chartKit.suppressed : formatValue(observed(point), unit)}{point.secondary !== undefined ? ' / ' + formatValue(point.secondary) : ''}</text><title>{label(point, item, unit)}</title></g>)}
  </svg>{ranked.some((r) => r.point.href) && <ul>{ranked.filter((r) => /^\/admin\//.test(r.point.href || '')).map(({ item, point }) => <li key={item.key}><a href={point.href}>{item.label || item.key}</a></li>)}</ul>}</ChartFrame>;
}

/** Ordered funnel steps, rendered as centered bars rather than implying unmeasured transitions. */
export function Funnel({ title, series = [], unit, onPointClick, canPointClick = () => true, ...rest }) {
  const data = boundedSeries(series);
  const steps = data.length === 1 ? data[0].points.map((p) => ({ item: data[0], point: p })) : data.map((s) => ({ item: s, point: lastObserved(s.points) || s.points[0] })).filter((r) => r.point);
  const max = Math.max(1, ...steps.map((s) => observed(s.point) || 0));
  return <ChartFrame title={title} series={data} unit={unit} onPointClick={onPointClick} canPointClick={canPointClick} {...rest}><svg className="metric-svg" viewBox={`0 0 800 ${Math.max(80, steps.length * 45)}`} role="img" aria-label={title}>{steps.map(({ point, item }, i) => {
    const width = (observed(point) || 0) / max * 500;
    return <g key={i} {...action(onPointClick, point, item, canPointClick)} aria-label={label(point, item, unit)}><rect x={(800 - width) / 2} y={i * 45} width={width} height="30" fill={colors(i)} opacity=".35" /><text x="400" y={i * 45 + 20} textAnchor="middle" className="metric-axis">{point.label || item.label || item.key}: {point.suppressed ? strings.chartKit.suppressed : observed(point) === null ? strings.chartKit.missing : formatValue(point.value, unit)}</text></g>;
  })}</svg></ChartFrame>;
}

/** Numeric grid shared by cohorts and time-of-week heat maps; labels make intensity optional. */
function Grid({ title, series = [], unit, onPointClick, canPointClick = () => true, ...rest }) {
  const data = boundedSeries(series); const max = Math.max(1, ...data.flatMap((s) => s.points.map((p) => observed(p) || 0)));
  const columns = Math.max(1, ...data.map((s) => s.points.length)); const cell = Math.min(60, 600 / columns);
  return <ChartFrame title={title} series={data} unit={unit} onPointClick={onPointClick} canPointClick={canPointClick} {...rest}><svg className="metric-svg" viewBox={`0 0 800 ${Math.max(80, data.length * 38 + 20)}`} role="img" aria-label={title}>{data.map((s, row) => <g key={s.key}><text x="0" y={row * 38 + 25} className="metric-axis">{(s.label || s.key).slice(0, 25)}</text>{s.points.map((p, col) => <g key={col} {...action(onPointClick, p, s, canPointClick)} aria-label={label(p, s, unit)}><rect x={180 + col * cell} y={row * 38 + 8} width={cell - 2} height="28" fill={colors(row)} opacity={observed(p) === null ? .08 : .15 + .75 * Math.abs(p.value) / max} /><text x={180 + col * cell + cell / 2} y={row * 38 + 27} textAnchor="middle" className="metric-axis">{p.suppressed ? '<5' : observed(p) === null ? '·' : formatValue(p.value, unit)}</text><title>{label(p, s, unit)} · {p.label || p.startUtc}</title></g>)}</g>)}</svg></ChartFrame>;
}
export function CohortGrid(props) { return <Grid {...props} />; }
export function HeatMap(props) { return <Grid {...props} />; }
/** Bins retain their declared numeric order instead of being ranked by frequency. */
export function Histogram({ series = [], rows, ...props }) {
  const bins = rows || (series.length === 1 ? series[0].points.map((point, i) => ({ ...point, key: String(i), label: point.label || point.startUtc || String(i + 1) })) : undefined);
  return <RankedBars {...props} rows={bins} series={series} preserveOrder />;
}

/** Time intervals retain exact durations and have keyboard drill-down and a data table. */
export function Swimlanes({ title, series = [], unit, timeZone = 'UTC', onPointClick, canPointClick = () => true, ...rest }) {
  const data = boundedSeries(series); const dates = data.flatMap((s) => s.points.flatMap((p) => [Date.parse(p.startUtc), Date.parse(p.endUtc || p.startUtc)])).filter(Number.isFinite);
  const min = Math.min(...dates); const max = Math.max(min + 1, ...dates); const x = (stamp) => 180 + (Date.parse(stamp) - min) / (max - min) * 580;
  return <ChartFrame title={title} series={data} unit={unit} timeZone={timeZone} onPointClick={onPointClick} canPointClick={canPointClick} {...rest}><svg className="metric-svg" viewBox={`0 0 800 ${Math.max(80, data.length * 38 + 20)}`} role="img" aria-label={title}>{data.map((s, row) => <g key={s.key}><text className="metric-axis" x="0" y={row * 38 + 25}>{(s.label || s.key).slice(0, 25)}</text>{s.points.map((p, col) => observed(p) === null || !Number.isFinite(Date.parse(p.startUtc)) ? null : <g key={col} {...action(onPointClick, p, s, canPointClick)} aria-label={label(p, s, unit)}><rect x={x(p.startUtc)} y={row * 38 + 8} width={Math.max(3, x(p.endUtc || p.startUtc) - x(p.startUtc))} height="22" fill={p.outcome === 'Failed' ? 'var(--danger, #b42332)' : p.outcome === 'Degraded' || p.outcome === 'Cancelled' ? 'var(--warning, #9a6700)' : colors(row)} stroke={p.outcome === 'Failed' ? 'currentColor' : 'none'} strokeDasharray={p.outcome === 'Failed' ? '2 2' : undefined} /><title>{label(p, s, unit)} · {p.outcome || ''} · {timeLabel(p.startUtc, timeZone)} – {timeLabel(p.endUtc || p.startUtc, timeZone)} · {strings.chartKit.duration}: {formatValue(Math.max(0, Date.parse(p.endUtc || p.startUtc) - Date.parse(p.startUtc)) / 1000)} {strings.chartKit.seconds}{p.intervalSeconds ? ' · ' + strings.chartKit.interval + ': ' + formatValue(p.intervalSeconds) : ''}</title></g>)}</g>)}</svg></ChartFrame>;
}
export function UptimeStrip({ series = [], ...props }) {
  const data = boundedSeries(series).map((item) => ({ ...item, points: item.points.map((point) => ({ ...point, outcome: point.outcome || (observed(point) === null ? undefined : point.value <= 0 ? 'Failed' : point.value < 100 ? 'Degraded' : 'Ok') })) }));
  return <Swimlanes {...props} series={data} />;
}
