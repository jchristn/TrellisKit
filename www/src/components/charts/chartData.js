/** Plot and export only observed finite numbers. Suppressed values never enter geometry or CSV. */
export function observed(point) {
  return point && !point.suppressed && typeof point.value === 'number' && Number.isFinite(point.value) ? point.value : null;
}

export function boundedSeries(series) {
  return (Array.isArray(series) ? series : []).slice(0, 21).map((item) => ({ ...item, points: (item.points || []).slice(0, 2000) }));
}

/** Contiguous paths stop at every missing or privacy-suppressed bucket. */
export function segments(points) {
  const groups = [];
  let group = [];
  for (const point of points) {
    if (observed(point) === null) { if (group.length) groups.push(group); group = []; }
    else group.push(point);
  }
  if (group.length) groups.push(group);
  return groups;
}

export function formatValue(value, unit = '') {
  if (!Number.isFinite(value)) return '';
  if (unit === 'percent') return value.toLocaleString(undefined, { maximumFractionDigits: 2 }) + '%';
  return value.toLocaleString(undefined, { maximumFractionDigits: 3 });
}

export function lastObserved(points) {
  return [...(points || [])].reverse().find((point) => observed(point) !== null);
}

/** Mitigate spreadsheet formula execution while preserving a portable RFC 4180 CSV. */
export function csvCell(value) {
  let text = String(value ?? '');
  if (/^[\s]*[=+@-]/.test(text)) text = "'" + text;
  return '"' + text.replace(/"/g, '""') + '"';
}

export function csvForSeries(series, previous = []) {
  const rows = [['period', 'series', 'startUtc', 'endUtc', 'value', 'status']];
  for (const [period, items] of [['current', series], ['previous', previous]]) {
    for (const item of boundedSeries(items)) for (const point of item.points) {
      rows.push([period, item.key, point.startUtc || point.label, point.endUtc, observed(point) ?? '', point.suppressed ? 'fewer than 5' : observed(point) === null ? 'no data' : point.complete === false ? 'partial' : 'complete']);
    }
  }
  return rows.map((row) => row.map(csvCell).join(',')).join('\r\n');
}

export function downloadCsv(title, csv) {
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = String(title || 'metrics').replace(/[^a-zA-Z0-9_-]+/g, '-') + '.csv';
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

export function timeLabel(value, zone = 'UTC') {
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return String(value || '');
  try { return new Intl.DateTimeFormat(undefined, { timeZone: zone, dateStyle: 'short', timeStyle: 'short' }).format(date); }
  catch { return date.toISOString(); }
}

export function previousChange(current, previous) {
  if (!Number.isFinite(current) || !Number.isFinite(previous)) return null;
  if (previous === 0) return current === 0 ? 0 : null;
  return (current - previous) / Math.abs(previous) * 100;
}
