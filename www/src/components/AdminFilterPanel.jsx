import { useMemo, useState } from 'react';
import { LabelMatch } from '@trelliskit/shared';
import strings from '../strings.js';
import { isoToLocalInput, localInputToIso } from '../utils/time.js';
import { formatDateTime } from '../utils/format.js';
import Icon from './Icon.jsx';
import LabelInput from './LabelInput.jsx';
import TagRows, { rowsToTags, tagsToRows } from './TagRows.jsx';
import { metadataSuggesters } from './metadataSuggest.js';

const c = strings.admin.common;
const m = strings.metadata;
const f = strings.admin.filterPanel;

function toDraft(fields, values) {
  const draft = {};
  for (const field of fields) {
    const v = values[field.key];
    if (field.type === 'checkbox') draft[field.key] = v === true || v === 'true';
    else if (field.type === 'datetime') draft[field.key] = isoToLocalInput(v);
    else draft[field.key] = v || '';
  }
  return draft;
}

/** Chip text for an active form filter, or '' when the field is not set. */
export function fieldChipText(field, value) {
  if (value === undefined || value === null || value === '' || value === false || value === 'false') return '';
  if (field.type === 'checkbox') return field.label;
  if (field.type === 'select') {
    const option = (field.options || []).find((o) => o.value === value);
    return f.chip(field.label, option ? option.label : value);
  }
  if (field.type === 'datetime') return f.chip(field.label, formatDateTime(value));
  return f.chip(field.label, value);
}

function Chip({ text, onRemove }) {
  return (
    <span className="chip">
      {text}
      <button type="button" className="chip-remove" onClick={onRemove} aria-label={m.removeFilter(text)}><Icon name="close" size={10} /></button>
    </span>
  );
}

function FilterForm({ fields, values, meta, onApply, onClear, validate, client, targetType, label, disabled }) {
  const [draft, setDraft] = useState(() => toDraft(fields, values));
  const [labels, setLabels] = useState(meta.labels || []);
  const [labelMatch, setLabelMatch] = useState(meta.labelMatch || LabelMatch.All);
  const [rows, setRows] = useState(() => tagsToRows(meta.tags));
  const metaCount = (meta.labels || []).length + Object.keys(meta.tags || {}).length;
  const [open, setOpen] = useState(false);
  const [error, setError] = useState('');
  const suggest = useMemo(() => metadataSuggesters(client, targetType), [client, targetType]);

  const set = (key, type) => (e) => {
    const value = type === 'checkbox' ? e.target.checked : e.target.value;
    setDraft((d) => ({ ...d, [key]: value }));
  };

  const submit = (e) => {
    e.preventDefault();
    const out = {};
    for (const field of fields) {
      const v = draft[field.key];
      if (field.type === 'datetime') out[field.key] = localInputToIso(v);
      else if (field.type === 'checkbox') out[field.key] = Boolean(v);
      else out[field.key] = typeof v === 'string' ? v.trim() : v;
    }
    const problem = validate ? validate(out) : '';
    setError(problem || '');
    if (problem) return;
    onApply(out, { labels, labelMatch, tags: rowsToTags(rows, { allowHasKey: true }) });
  };

  const clear = () => {
    setDraft(toDraft(fields, {}));
    setLabels([]);
    setRows([]);
    setError('');
    onClear();
  };

  const metaId = 'filter-meta-' + (targetType || 'list');
  return (
    <form className="filter-panel" onSubmit={submit} aria-label={label}>
      <div className="filter-bar">
        {fields.map((field) => {
          const id = 'filter-' + field.key;
          if (field.type === 'checkbox') {
            return (
              <label key={field.key} className="check-field filter-check" htmlFor={id}>
                <input id={id} type="checkbox" checked={Boolean(draft[field.key])} onChange={set(field.key, 'checkbox')} disabled={disabled} />
                <span>{field.label}</span>
              </label>
            );
          }
          return (
            <label key={field.key} className={'field field-compact' + (field.grow ? ' field-grow' : '')} htmlFor={id}>
              <span className="label-text">{field.label}</span>
              {field.type === 'select' ? (
                <select id={id} className="select-compact" value={draft[field.key]} onChange={set(field.key)} disabled={disabled}>
                  <option value="">{c.any}</option>
                  {field.options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              ) : (
                <input
                  id={id}
                  type={field.type === 'datetime' ? 'datetime-local' : 'text'}
                  className={'input-compact' + (field.mono ? ' mono' : '')}
                  value={draft[field.key]}
                  onChange={set(field.key)}
                  placeholder={field.placeholder}
                  disabled={disabled}
                  list={field.list}
                />
              )}
            </label>
          );
        })}
        <button type="button" className="button button-secondary button-sm" onClick={() => setOpen((v) => !v)} aria-expanded={open} aria-controls={metaId}>
          <Icon name="list" size={14} /><span>{metaCount ? m.editorCount(metaCount) : m.filters}</span>
        </button>
        <div className="filter-actions">
          <button type="submit" className="button button-primary button-sm" disabled={disabled}>
            <Icon name="filter" size={14} /><span>{c.applyFilters}</span>
          </button>
          <button type="button" className="button button-secondary button-sm" onClick={clear} disabled={disabled}>{c.clearFilters}</button>
        </div>
      </div>
      {open && (
        <div id={metaId} className="filter-meta" role="group" aria-label={m.filters}>
          <div className="meta-label-row">
            <LabelInput label={m.labels} values={labels} onChange={setLabels} suggest={suggest.labels} placeholder={m.labelsPlaceholder} />
            <div className="segmented" role="radiogroup" aria-label={m.labelMatch}>
              {[LabelMatch.All, LabelMatch.Any].map((mode) => (
                <button key={mode} type="button" role="radio" aria-checked={labelMatch === mode} className={'segment' + (labelMatch === mode ? ' active' : '')} onClick={() => setLabelMatch(mode)}>{m.match[mode]}</button>
              ))}
            </div>
          </div>
          <TagRows label={m.tags} rows={rows} onChange={setRows} suggestKeys={suggest.tagKeys} suggestValues={suggest.tagValues} allowHasKey />
        </div>
      )}
      {error && <div className="filter-errors" role="alert"><span>{error}</span></div>}
    </form>
  );
}

/**
 * The one filter panel of an admin list (docs/ENUMERATION_AND_METADATA.md section 3): the list's
 * form filters plus label (All/Any) and tag filters with autocomplete, applied together by one
 * "Apply filters", and every active filter (form, label, and tag) as a removable chip with
 * "Clear all". State lives in the URL through `useTableState`.
 *
 * @param {{ fields: Array<{ key: string, label: string, type?: 'text'|'select'|'checkbox'|'datetime', options?: Array<{ value: string, label: string }>,
 *   placeholder?: string, grow?: boolean, mono?: boolean, list?: string }>, table: object, client: object, targetType: string,
 *   values?: object, validate?: (values: object) => string, label?: string, disabled?: boolean }} props
 *   `values` overrides the form's starting values (for example a defaulted status); chips always reflect the URL.
 */
function AdminFilterPanel({ fields, table, client, targetType, values, validate, label = strings.admin.requests.filtersTitle, disabled = false }) {
  const meta = table.meta;
  const chips = [];
  for (const field of fields) {
    const text = fieldChipText(field, table.filters[field.key]);
    if (text) chips.push({ id: 'f:' + field.key, text, remove: () => table.applyAll({ ...table.filters, [field.key]: '' }, meta) });
  }
  for (const labelValue of meta.labels) {
    chips.push({ id: 'l:' + labelValue, text: m.labelChip(labelValue), remove: () => table.applyAll(table.filters, { ...meta, labels: meta.labels.filter((x) => x !== labelValue) }) });
  }
  for (const [key, v] of Object.entries(meta.tags)) {
    const tags = { ...meta.tags };
    delete tags[key];
    chips.push({ id: 't:' + key, text: v === '*' ? m.hasKeyChip(key) : m.tagChip(key, v), remove: () => table.applyAll(table.filters, { ...meta, tags }) });
  }

  return (
    <div className="admin-filters">
      <FilterForm
        key={table.filterKey}
        fields={fields}
        values={values || table.filters}
        meta={meta}
        onApply={table.applyAll}
        onClear={table.clearAll}
        validate={validate}
        client={client}
        targetType={targetType}
        label={label}
        disabled={disabled}
      />
      {chips.length > 0 && (
        <div className="active-filters" role="group" aria-label={m.activeFilters}>
          <span className="muted small">{f.activeCount(chips.length)}</span>
          {chips.map((chip) => <Chip key={chip.id} text={chip.text} onRemove={chip.remove} />)}
          {meta.labels.length > 1 && <span className="muted small">{m.matchNote[meta.labelMatch || LabelMatch.All]}</span>}
          <button type="button" className="link-button small" onClick={table.clearAll}>{m.clearAll}</button>
        </div>
      )}
    </div>
  );
}

export default AdminFilterPanel;
