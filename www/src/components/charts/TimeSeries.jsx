import ChartFrame from './ChartFrame.jsx';
import strings from '../../strings.js';
const copy = strings.chartKit;
import { boundedSeries, formatValue, observed, segments, timeLabel } from './chartData.js';

const W = 800; const H = 260; const L = 60; const R = 740; const T = 20; const B = 220;
const color = (i) => `var(--chart-series-${i % 6 + 1})`;

/** Hand-drawn SVG series with gaps, comparison, independent right axis and annotations. */
export default function TimeSeries({ title, series = [], previous = [], type = 'line', annotations = [], timeZone = 'UTC', unit = '', threshold, onPointClick, canPointClick = () => true, ariaLabel, showLegend = true, ...rest }) {
  const data = boundedSeries(series);
  const old = boundedSeries(previous);
  const stamps = [...new Set(data.flatMap((s) => s.points.map((p) => p.startUtc || p.label)))].sort();
  const count = Math.max(1, stamps.length);
  const times = stamps.map((s) => Date.parse(s));
  const dated = times.every(Number.isFinite);
  const minTime = dated ? times[0] : 0;
  const maxTime = dated ? times[times.length - 1] : 0;
  const stacked = type === 'stackedArea' || type === 'stackedBars' || type === 'stacked';
  const bars = type.toLowerCase().includes('bar') || type === 'stacked';
  // A cumulative band has no known baseline when an earlier band is missing.
  const plotData = stacked ? data.map((item, index) => ({ ...item, points: item.points.map((point) => {
    const stamp = point.startUtc || point.label;
    const missingBase = data.slice(0, index).filter((prior) => (prior.axis || 'left') === (item.axis || 'left')).some((prior) => observed(prior.points.find((p) => (p.startUtc || p.label) === stamp)) === null);
    return missingBase ? { ...point, value: null } : point;
  }) })) : data;
  const extentFor = (axis) => {
    const values = [...data, ...old].filter((s) => (s.axis || 'left') === axis).flatMap((s) => s.points.map(observed).filter((v) => v !== null));
    if (stacked) for (const stamp of stamps) {
      const at = data.filter((s) => (s.axis || 'left') === axis).map((s) => observed(s.points.find((p) => (p.startUtc || p.label) === stamp)) || 0);
      values.push(at.reduce((sum, v) => sum + Math.max(0, v), 0), at.reduce((sum, v) => sum + Math.min(0, v), 0));
    }
    if (Number.isFinite(threshold)) values.push(threshold);
    const min = Math.min(0, ...values); const max = Math.max(0, ...values);
    return { min, max: min === max ? min + 1 : max };
  };
  const extents = { left: extentFor('left'), right: extentFor('right') };
  const inset = bars ? (R - L) / count / 2 : 0;
  const x = (stamp, i = stamps.indexOf(stamp)) => dated && maxTime > minTime ? L + inset + ((Date.parse(stamp) - minTime) / (maxTime - minTime)) * (R - L - inset * 2) : L + (i + .5) / count * (R - L);
  const y = (value, axis = 'left') => B - (value - extents[axis].min) / (extents[axis].max - extents[axis].min) * (B - T);
  const axisValue = (fraction, axis = 'left') => extents[axis].min + (extents[axis].max - extents[axis].min) * fraction;
  const base = (stamp, index, value) => stacked ? data.slice(0, index).filter((s) => (s.axis || 'left') === (data[index].axis || 'left')).reduce((sum, s) => {
    const prior = observed(s.points.find((p) => (p.startUtc || p.label) === stamp)) || 0;
    return sum + (value >= 0 ? Math.max(0, prior) : Math.min(0, prior));
  }, 0) : 0;
  const tickLabel = (stamp) => {
    if (!dated) return stamp;
    try { return new Intl.DateTimeFormat(undefined, { timeZone, ...(maxTime - minTime < 86400000 * 2 ? { hour: 'numeric', minute: '2-digit' } : { month: 'short', day: 'numeric' }) }).format(new Date(stamp)); }
    catch { return timeLabel(stamp, timeZone); }
  };
  const activate = (point, item) => onPointClick?.(point, item);
  const svgTitle = title + ': ' + data.map((item) => item.label || item.key).join(', ');
  return <ChartFrame title={title} series={data} previous={old} timeZone={timeZone} unit={unit} onPointClick={onPointClick} canPointClick={canPointClick} {...rest}>
    <svg className="metric-svg" viewBox={`0 0 ${W} ${H}`} role="img" aria-label={ariaLabel || svgTitle}>
      {[0, .5, 1].map((fraction) => <g key={fraction}><line className="metric-grid" x1={L} x2={R} y1={y(axisValue(fraction))} y2={y(axisValue(fraction))} /><text className="metric-axis" x={L - 6} y={y(axisValue(fraction)) + 4} textAnchor="end">{formatValue(axisValue(fraction), unit)}</text></g>)}
      {data.some((s) => s.axis === 'right') && [0, .5, 1].map((fraction) => <text key={fraction} className="metric-axis" x={R + 6} y={y(axisValue(fraction, 'right'), 'right') + 4}>{formatValue(axisValue(fraction, 'right'))}</text>)}
      {Number.isFinite(threshold) && <line className="metric-threshold" x1={L} x2={R} y1={y(threshold)} y2={y(threshold)}><title>{formatValue(threshold, unit)}</title></line>}
      {old.map((item) => segments(item.points.map((point, j) => ({ ...point, displayStamp: stamps[j] }))).map((group, k) => <path key={item.key + k} fill="none" stroke={color(Math.max(0, data.findIndex((current) => current.key === item.key)))} strokeDasharray="5 5" opacity="0.4" d={group.filter((p) => p.displayStamp).map((p, j) => `${j ? 'L' : 'M'}${x(p.displayStamp)},${y(p.value, item.axis)}`).join(' ')} />))}
      {plotData.map((item, i) => <g key={item.key}>
        {!bars && segments(item.points).map((group, k) => {
          const path = group.map((p, j) => `${j ? 'L' : 'M'}${x(p.startUtc || p.label)},${y(p.value + base(p.startUtc || p.label, i, p.value), item.axis)}`).join(' ');
          const area = stacked || type === 'area';
          const bottom = [...group].reverse().map((p) => `L${x(p.startUtc || p.label)},${y(base(p.startUtc || p.label, i, p.value), item.axis)}`).join(' ');
          return <path data-testid="metric-segment" key={k} d={area ? path + bottom + 'Z' : path} fill={area ? color(i) : 'none'} fillOpacity="0.18" stroke={color(i)} strokeWidth="2" strokeDasharray={i % 2 ? '7 3' : undefined} />;
        })}
        {item.points.map((p, j) => {
          const value = observed(p); if (value === null) return null;
          const stamp = p.startUtc || p.label; const bottom = base(stamp, i, value); const px = x(stamp); const py = y(value + bottom, item.axis);
          const label = `${item.label || item.key}: ${timeLabel(stamp, timeZone)}; ${formatValue(value, unit)}${p.complete === false ? '; ' + copy.partial : ''}; ${copy.local}: ${new Date(stamp).toLocaleString()}`;
          const clickable = onPointClick && canPointClick(p, item);
          const props = { tabIndex: clickable ? 0 : undefined, role: clickable ? 'button' : undefined, 'aria-label': label, onClick: clickable ? () => activate(p, item) : undefined, onKeyDown: (e) => { if (clickable && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); activate(p, item); } }, fill: color(i), opacity: p.complete === false ? .5 : 1 };
          const barWidth = Math.max(1, (R - L) / count * .7 / (stacked ? 1 : data.length));
          return bars ? <rect key={j} {...props} x={px - barWidth * (stacked ? .5 : data.length / 2) + (stacked ? 0 : i * barWidth)} y={Math.min(py, y(bottom, item.axis))} width={barWidth} height={Math.abs(y(bottom, item.axis) - py)}><title>{label}</title></rect>
            : <circle key={j} {...props} cx={px} cy={py} r={i % 2 ? 3.5 : 2.5} stroke="var(--surface, #fff)" strokeWidth="1"><title>{label}</title></circle>;
        })}
      </g>)}
      {stamps.filter((_, i) => i % Math.max(1, Math.ceil(count / 5)) === 0).map((stamp) => <text className="metric-axis" key={stamp} x={x(stamp)} y={H - 12} textAnchor="middle">{tickLabel(stamp)}</text>)}
      {annotations.slice(0, 200).filter((a) => dated && Date.parse(a.occurredUtc) >= minTime && Date.parse(a.occurredUtc) <= maxTime).map((a, i) => <g key={a.id || i}><line className="metric-annotation" x1={x(a.occurredUtc)} x2={x(a.occurredUtc)} y1={T} y2={B} /><text x={x(a.occurredUtc)} y={T - 4} textAnchor="middle">◆<title>{a.label} · {timeLabel(a.occurredUtc, timeZone)}</title></text></g>)}
    </svg>
    {showLegend && <ul className="metric-legend">{data.map((s, i) => <li key={s.key}><span style={{ color: color(i) }} aria-hidden="true">{i % 2 ? '◆' : '●'}</span> {s.label || s.key}</li>)}{old.length > 0 && <li>┄ {copy.previous}</li>}</ul>}
  </ChartFrame>;
}
