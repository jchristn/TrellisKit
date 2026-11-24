import { useCallback, useId, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import strings from '../../strings.js';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import {
  RANGE_PERIOD, STORAGE_CLASSES, USAGE_BUCKETS, USAGE_GROUPS, USAGE_METERS, USAGE_PERIODS,
  buildDataTable, bucketAllowed, chartData, describeChart, describeStacked, findMeter, formatChange,
  formatMeterValue, formatZonedDateTime, localDateOf, meterLabel, parseUsageState, periodLengthDays,
  serializeUsageState, stackedData, summaryIsEmpty, usageQuery, validateRange
} from '../../utils/usage.js';
import KpiCard from '../KpiCard.jsx';
import Icon from '../Icon.jsx';
import { Banner, EmptyState, ErrorBanner, Loading } from '../States.jsx';
import UsageChart from './UsageChart.jsx';
import '../../usage.css';
import TableColumns from '../TableColumns.jsx';

const u = strings.usage;

function browserZone() {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}

/**
 * Period picker: segmented buttons for the nine periods plus "Custom range", which reveals two
 * local date inputs and Apply. Range errors are validated before the URL changes.
 */
export function UsagePeriodPicker({ state, onChange, today, disabled = false }) {
  const [customOpen, setCustomOpen] = useState(state.period === RANGE_PERIOD);
  const [draft, setDraft] = useState({ from: state.from, to: state.to });
  const [errors, setErrors] = useState({});
  const id = 'usage-' + useId().replace(/[^a-zA-Z0-9_-]/g, '');
  const custom = customOpen || state.period === RANGE_PERIOD;

  const choose = (period) => {
    setCustomOpen(false);
    setErrors({});
    onChange({ period, from: '', to: '', bucket: state.bucket });
  };
  const apply = (e) => {
    e.preventDefault();
    const next = validateRange(draft.from, draft.to, today);
    setErrors(next);
    if (Object.keys(next).length) return;
    onChange({ period: RANGE_PERIOD, from: draft.from, to: draft.to, bucket: state.bucket });
  };

  return (
    <div className="usage-period">
      <div className="segmented usage-segmented" role="group" aria-label={u.periodLabel}>
        {USAGE_PERIODS.map((period) => (
          <button key={period} type="button" className={'segment' + (!custom && state.period === period ? ' active' : '')} aria-pressed={!custom && state.period === period} onClick={() => choose(period)} disabled={disabled}>
            {u.periods[period]}
          </button>
        ))}
        <button type="button" className={'segment' + (custom ? ' active' : '')} aria-pressed={custom} aria-expanded={custom} aria-controls={id + '-range'} onClick={() => setCustomOpen(true)} disabled={disabled}>
          {u.periods.Range}
        </button>
      </div>
      {custom && (
        <form id={id + '-range'} className="usage-range" onSubmit={apply} noValidate>
          <div className="field">
            <label className="label-text" htmlFor={id + '-from'}>{u.from}</label>
            <input id={id + '-from'} type="date" value={draft.from} max={today} onChange={(e) => setDraft((d) => ({ ...d, from: e.target.value }))} aria-invalid={Boolean(errors.from)} aria-describedby={errors.from ? id + '-from-error' : undefined} />
            {errors.from && <span id={id + '-from-error'} className="field-error">{u.rangeErrors[errors.from]}</span>}
          </div>
          <div className="field">
            <label className="label-text" htmlFor={id + '-to'}>{u.to}</label>
            <input id={id + '-to'} type="date" value={draft.to} max={today} onChange={(e) => setDraft((d) => ({ ...d, to: e.target.value }))} aria-invalid={Boolean(errors.to)} aria-describedby={errors.to ? id + '-to-error' : undefined} />
            {errors.to && <span id={id + '-to-error'} className="field-error">{u.rangeErrors[errors.to]}</span>}
          </div>
          <button type="submit" className="button button-primary button-sm" disabled={disabled}>{u.apply}</button>
        </form>
      )}
    </div>
  );
}

/** The accessible "Show data" table of a chart. */
function ChartDataTable({ title, table }) {
  return (
    <div className="table-scroll usage-data-table">
      <TableColumns id="tk.usage.UsageDashboard"><table className="data-table">
        <caption>{u.dataCaption(title)}</caption>
        <thead>
          <tr>{table.columns.map((col, i) => <th key={col} scope="col" className={i ? 'num' : undefined}>{col}</th>)}</tr>
        </thead>
        <tbody>
          {table.rows.map((row) => (
            <tr key={row.key}>
              <th scope="row">{row.label}</th>
              {row.cells.map((cell, i) => <td key={i} className="num">{cell}</td>)}
            </tr>
          ))}
        </tbody>
      </table></TableColumns>
    </div>
  );
}

/** One chart card: title, text alternative, the decorative canvas, and a "Show data" toggle. */
function ChartCard({ chart, summary }) {
  const [open, setOpen] = useState(false);
  const id = 'usage-' + useId().replace(/[^a-zA-Z0-9_-]/g, '');
  const tz = summary.timeZone || 'UTC';
  const view = useMemo(() => {
    if (chart.type === 'stacked') {
      const stacked = stackedData(summary, chart.meters);
      return { data: stacked.data, layers: stacked.layers, text: describeStacked(summary, chart.meters, chart.title), table: buildDataTable(summary, chart.meters, { total: true }) };
    }
    return { data: chartData(summary, chart.meters), layers: null, text: describeChart(summary, chart.meters[0], chart.title), table: buildDataTable(summary, chart.meters) };
  }, [chart, summary]);
  const series = useMemo(() => chart.meters.map((m) => ({ label: meterLabel(m) })), [chart]);
  return (
    <figure className="panel usage-chart-card" aria-labelledby={id + '-title'}>
      <div className="usage-chart-head">
        <h3 id={id + '-title'} className="usage-chart-title">{chart.title}</h3>
        <button type="button" className="button button-secondary button-sm" aria-expanded={open} aria-controls={id + '-data'} onClick={() => setOpen((v) => !v)}>
          {open ? u.hideData : u.showData}
        </button>
      </div>
      <div className="usage-chart-img" role="img" aria-label={view.text}>
        <UsageChart data={view.data} layers={view.layers} series={series} type={chart.type} unit={chart.unit} timeZone={tz} />
      </div>
      <figcaption className="muted small usage-chart-caption">{view.text}</figcaption>
      {open && <div id={id + '-data'}><ChartDataTable title={chart.title} table={view.table} /></div>}
    </figure>
  );
}

/** A summary card: total, previous total, and the change with an up, down, or flat tone. */
function MeterCard({ meter, info }) {
  const change = formatChange(meter.change);
  const good = change.tone === 'flat' ? 'neutral' : ((change.tone === 'up') !== Boolean(info && info.upIsBad) ? 'good' : 'bad');
  const arrow = change.tone === 'up' ? 'chevronUp' : change.tone === 'down' ? 'chevronDown' : null;
  return (
    <KpiCard
      label={meterLabel(meter.meter)}
      value={formatMeterValue(meter.total, meter.unit)}
      hint={u.previous(formatMeterValue(meter.previousTotal, meter.unit))}
    >
      {meter.exact === false && <span className="kpi-hint">{u.lowerBound}</span>}
      <span className={'usage-change usage-change-' + change.tone + ' usage-change-' + good} title={u.changeLabel(change.text)}>
        {arrow && <Icon name={arrow} size={12} />}
        <span className="visually-hidden">{u.changeLabel('')}</span>
        <span>{change.text}</span>
        {change.tone !== 'flat' && <span className="visually-hidden"> ({u.tones[change.tone]})</span>}
      </span>
    </KpiCard>
  );
}

/** Chart definitions of a group from the meters present in the summary. */
export function groupCharts(summary, group) {
  const charts = [];
  if (group === 'storage' && STORAGE_CLASSES.some((id) => findMeter(summary, id))) {
    charts.push({ key: 'storage-stacked', title: u.storageByClass, type: 'stacked', unit: 'bytes', meters: STORAGE_CLASSES.filter((id) => findMeter(summary, id)) });
  }
  for (const info of USAGE_METERS) {
    if (info.group !== group || !info.chart || !findMeter(summary, info.id)) continue;
    const meter = findMeter(summary, info.id);
    charts.push({ key: info.id, title: meterLabel(info.id), type: info.chart, unit: meter.unit || info.unit, meters: [info.id] });
  }
  return charts;
}

function groupCards(summary, group) {
  const known = USAGE_METERS.filter((m) => m.group === group && m.card).map((m) => ({ info: m, meter: findMeter(summary, m.id) })).filter((x) => x.meter);
  return known;
}

/**
 * The usage page shared by the property admin console and the platform console
 * (docs/USAGE_METERING_CONTRACT.md section 8). Period, range, and bucket live in the URL.
 *
 * @param {{ load: (query: object, signal: AbortSignal) => Promise<object>, exportFile: (query: object, format: 'csv'|'json') => Promise<any> }} props
 */
function UsageDashboard({ load, exportFile }) {
  const toast = useToast();
  const [searchParams, setSearchParams] = useSearchParams();
  const state = parseUsageState(searchParams);
  const stateKey = serializeUsageState(state).toString();
  const [exporting, setExporting] = useState('');

  const rangeErrors = state.period === RANGE_PERIOD ? validateRange(state.from, state.to, '') : {};
  const valid = Object.keys(rangeErrors).length === 0;
  const query = useMemo(() => usageQuery(parseUsageState(stateKey)), [stateKey]);
  const fetcher = useCallback((signal) => load(query, signal), [load, query]);
  const result = useApiQuery(fetcher, { enabled: valid });
  const summary = result.data;

  const tz = (summary && summary.timeZone) || browserZone();
  const now = summary && summary.generatedUtc ? new Date(summary.generatedUtc) : new Date();
  const today = localDateOf(now, tz);
  const lengthDays = periodLengthDays(state, { timeZone: tz, now, weekStartsOn: (summary && summary.weekStartsOn) || 'Monday' });

  const update = (next) => setSearchParams(serializeUsageState(next, searchParams), { replace: false });

  const doExport = async (format) => {
    setExporting(format);
    try {
      // The export takes period, from, and to (and `granularity`, not `bucket`; contract section 7.1).
      const { bucket: _bucket, ...exportQuery } = query;
      await exportFile(exportQuery, format);
      toast.success(u.exported);
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setExporting('');
    }
  };

  const fieldErrors = result.error ? getFieldErrors(result.error) : {};
  const errorText = result.error ? [u.loadError, errorMessage(result.error, ''), ...Object.values(fieldErrors)].filter(Boolean).join(' ') : '';

  return (
    <div className="usage-dashboard">
      <section className="panel usage-toolbar" aria-label={u.periodLabel}>
        <UsagePeriodPicker key={state.period === RANGE_PERIOD ? stateKey : 'preset'} state={state} onChange={update} today={today} disabled={result.loading} />
        <div className="usage-toolbar-row">
          <label className="inline-field">
            <span className="label-text">{u.bucketLabel}</span>
            <select className="select-compact" value={state.bucket} onChange={(e) => update({ ...state, bucket: e.target.value })} disabled={result.loading}>
              <option value="">{u.bucketAuto}</option>
              {USAGE_BUCKETS.map((b) => <option key={b} value={b} disabled={lengthDays > 0 && !bucketAllowed(b, lengthDays)}>{u.buckets[b]}</option>)}
            </select>
          </label>
          <div className="usage-actions">
            <button type="button" className="icon-button" onClick={result.reload} disabled={result.loading || !valid} aria-label={u.refresh} title={u.refresh}>
              <Icon name="refresh" className={result.loading ? 'spin' : ''} />
            </button>
            <button type="button" className="button button-secondary button-sm" onClick={() => doExport('csv')} disabled={Boolean(exporting) || !valid}>
              <Icon name="download" size={14} /> {u.exportCsv}
            </button>
            <button type="button" className="button button-secondary button-sm" onClick={() => doExport('json')} disabled={Boolean(exporting) || !valid}>
              <Icon name="download" size={14} /> {u.exportJson}
            </button>
          </div>
        </div>
        {summary && (
          <p className="usage-meta muted small">
            <span>{u.timeZone(summary.timeZone || 'UTC')}</span>
            <span>{u.range(formatZonedDateTime(summary.fromUtc, tz), formatZonedDateTime(summary.toUtc, tz))}</span>
            {summary.dataThroughUtc && <span>{u.dataThrough(formatZonedDateTime(summary.dataThroughUtc, tz))}</span>}
          </p>
        )}
      </section>

      {!valid && <ErrorBanner message={u.rangeErrors[rangeErrors.from || rangeErrors.to]} />}
      {summary && summary.backfillComplete === false && <Banner tone="warning" icon="clock">{u.backfilling}</Banner>}
      {result.error && <ErrorBanner message={errorText} onRetry={result.reload} />}
      {valid && result.loading && !summary && <Loading label={u.loading} />}
      {summary && summaryIsEmpty(summary) && (
        <EmptyState icon="activity" title={u.empty}>{u.emptyHint}</EmptyState>
      )}
      {summary && !summaryIsEmpty(summary) && USAGE_GROUPS.map((group) => {
        const cards = groupCards(summary, group);
        const charts = groupCharts(summary, group);
        if (!cards.length && !charts.length) return null;
        return (
          <section key={group} className="usage-group" aria-labelledby={'usage-group-' + group}>
            <h2 id={'usage-group-' + group} className="usage-group-title">{u.groups[group]}</h2>
            {cards.length > 0 && (
              <div className="usage-cards">
                {cards.map(({ info, meter }) => <MeterCard key={info.id} info={info} meter={meter} />)}
              </div>
            )}
            {charts.length > 0 && (
              <div className="usage-charts">
                {charts.map((chart) => <ChartCard key={chart.key} chart={chart} summary={summary} />)}
              </div>
            )}
          </section>
        );
      })}
    </div>
  );
}

export default UsageDashboard;
