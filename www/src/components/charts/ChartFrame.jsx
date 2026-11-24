import { useId, useState } from 'react';
import strings from '../../strings.js';
const copy = strings.chartKit;
import { boundedSeries, csvForSeries, downloadCsv, formatValue, lastObserved, observed, previousChange, timeLabel } from './chartData.js';
import './charts.css';

/** Accessible table, summary and export shared by every visualization. */
export default function ChartFrame({ title, series = [], previous = [], unit = '', timeZone = 'UTC', completeThroughUtc, summary, children, defaultOpen = false, onPointClick, canPointClick = () => true, tableContent, showTableLabel = copy.showTable, hideTableLabel = copy.hideTable }) {
  const id = useId();
  const [open, setOpen] = useState(defaultOpen);
  const data = boundedSeries(series);
  const count = data.reduce((total, item) => total + item.points.filter((p) => observed(p) !== null).length, 0);
  const readings = data.map((item) => {
    const current = observed(lastObserved(item.points));
    const before = observed(lastObserved(previous.find((old) => old.key === item.key)?.points));
    const delta = previousChange(current, before);
    return copy.lastValue(item.label || item.key, current === null ? copy.missing : formatValue(current, unit), unit === 'percent' ? '' : unit) + (delta === null ? '' : ' ' + copy.periodChange(formatValue(delta, 'percent')));
  }).join(' ');
  const description = summary || (count ? readings + ' ' + copy.knownPoints(count) : copy.noData);
  return <figure className="metric-chart panel" aria-labelledby={id + '-title'} aria-describedby={id + '-summary'}>
    <header className="metric-chart-header"><h3 id={id + '-title'}>{title}</h3><div className="metric-chart-actions">
      <button type="button" className="button button-secondary button-sm" aria-expanded={open} aria-controls={id + '-table'} onClick={() => setOpen((v) => !v)}>{open ? hideTableLabel : showTableLabel}</button>
      <button type="button" className="button button-secondary button-sm" onClick={() => downloadCsv(title, csvForSeries(data, previous))}>{copy.download}</button>
    </div></header>
    <figcaption id={id + '-summary'} className="metric-summary">{description}</figcaption>
    <p className="muted small">{copy.timezone}: {timeZone}{completeThroughUtc ? ' · ' + copy.completeThrough + ': ' + timeLabel(completeThroughUtc, timeZone) : ''}</p>
    <div className="metric-plot">{children}</div>
    {open && <div id={id + '-table'} className="table-scroll">{tableContent || <table className="data-table"><caption>{title}</caption><thead><tr><th>{copy.series}</th><th>{copy.time}</th><th>{copy.value}{unit ? ' (' + unit + ')' : ''}</th><th>{copy.status}</th></tr></thead><tbody>
      {[{ items: data, old: false }, { items: boundedSeries(previous), old: true }].flatMap(({ items, old }) => items.flatMap((item) => item.points.map((point, i) => <tr key={(old ? 'old-' : '') + item.key + '-' + i}>
        <th scope="row">{item.label || item.key}{old ? ' · ' + copy.previous : ''}</th>
        <td>{timeLabel(point.startUtc || point.label, timeZone)}</td>
        <td>{onPointClick && !old && observed(point) !== null && canPointClick(point, item) ? <button type="button" className="link-button" onClick={() => onPointClick(point, item)}>{formatValue(observed(point), unit)}</button> : point.suppressed ? copy.suppressed : observed(point) === null ? copy.missing : formatValue(point.value, unit)}</td>
        <td>{point.complete === false ? copy.partial : copy.complete}</td>
      </tr>)))}
    </tbody></table>}</div>}
  </figure>;
}
