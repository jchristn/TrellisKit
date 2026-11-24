import strings from '../../strings.js';
import { formatValue, observed, previousChange } from './chartData.js';
import './charts.css';

/** A value and comparison, with an optional decorative sparkline and no hidden source values. */
export default function StatTile({ title, value, previous, unit = '', points = [], href }) {
  const delta = previousChange(value, previous);
  const numbers = points.map(observed); const max = Math.max(1, ...numbers.filter((n) => n !== null));
  const path = numbers.map((n, i) => n === null ? '' : `${i === 0 || numbers[i - 1] === null ? 'M' : 'L'}${i / Math.max(1, numbers.length - 1) * 100},${24 - n / max * 24}`).join(' ');
  return <section className="panel metric-stat"><span>{title}</span><strong>{Number.isFinite(value) ? formatValue(value, unit) : strings.chartKit.missing}</strong>{delta !== null && <p>{strings.chartKit.periodChange(formatValue(delta, 'percent'))}</p>}{path && <svg width="120" height="28" viewBox="0 0 100 28" aria-hidden="true"><path d={path} fill="none" stroke="var(--primary)" strokeWidth="1.5" /></svg>}{href && <a href={href}>{strings.chartKit.details}</a>}</section>;
}
