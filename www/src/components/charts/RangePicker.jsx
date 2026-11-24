import strings from '../../strings.js';

export const METRIC_RANGES = { hour: 3600000, day: 86400000, week: 7 * 86400000, month: 30 * 86400000, quarter: 90 * 86400000 };
/** UTC range controls; URL ownership stays with the page so links remain shareable. */
export default function RangePicker({ range, from, to, compare, onChange }) {
  const c = strings.chartKit;
  return <div className="metric-controls"><label>{c.range}<select value={range} onChange={(e) => onChange({ range: e.target.value })}>{Object.entries(c.ranges).map(([id, label]) => <option key={id} value={id}>{label}</option>)}</select></label>
    {range === 'custom' && <><label>{c.from} (UTC)<input type="datetime-local" value={(from || '').slice(0, 16)} onChange={(e) => onChange({ from: e.target.value ? e.target.value + ':00Z' : '' })} /></label><label>{c.to} (UTC)<input type="datetime-local" value={(to || '').slice(0, 16)} onChange={(e) => onChange({ to: e.target.value ? e.target.value + ':00Z' : '' })} /></label></>}
    <label><input type="checkbox" checked={compare} onChange={(e) => onChange({ compare: e.target.checked })} />{c.compare}</label>
  </div>;
}
