import { useMemo, useState } from 'react';
import { LabelMatch } from '@trelliskit/shared';
import strings from '../strings.js';
import Icon from './Icon.jsx';
import LabelInput from './LabelInput.jsx';
import TagRows, { rowsToTags, tagsToRows } from './TagRows.jsx';
import { metadataSuggesters } from './metadataSuggest.js';
import { activeMetaCount } from '../hooks/useEnumerationUrlState.js';

const m = strings.metadata;

/**
 * Label and tag filters for any list (docs/ENUMERATION_AND_METADATA.md section 3): a label chip
 * input with autocomplete and an All/Any toggle, `key = value` tag rows with key and value
 * autocomplete and "has key", optional search and ordering, and the active filters as removable
 * chips with "Clear all". The editor opens from a button showing the active count; filters apply
 * explicitly (each change queries the backend).
 *
 * @param {{ value: { labels: string[], labelMatch: string, tags: Record<string,string>, search?: string, ordering?: string },
 *   onChange: (meta: object) => void, client: object, targetType?: string, showSearch?: boolean, searchLabel?: string,
 *   orderings?: Array<{ value: string, label: string }>, isStaff?: boolean }} props
 */
function MetadataFilterBar({ value, onChange, client, targetType, showSearch = false, searchLabel, orderings = null }) {
  const [open, setOpen] = useState(false);
  const [labels, setLabels] = useState(value.labels || []);
  const [labelMatch, setLabelMatch] = useState(value.labelMatch || LabelMatch.All);
  const [rows, setRows] = useState(() => tagsToRows(value.tags));
  const [search, setSearch] = useState(value.search || '');
  const [lastValue, setLastValue] = useState(value);
  const suggest = useMemo(() => metadataSuggesters(client, targetType), [client, targetType]);
  const count = activeMetaCount(value);

  if (lastValue !== value) {
    setLastValue(value);
    setLabels(value.labels || []);
    setLabelMatch(value.labelMatch || LabelMatch.All);
    setRows(tagsToRows(value.tags));
    setSearch(value.search || '');
  }

  const apply = (e) => {
    if (e) e.preventDefault();
    onChange({ ...value, labels, labelMatch, tags: rowsToTags(rows, { allowHasKey: true }), search: search.trim() });
    setOpen(false);
  };
  const clearAll = () => onChange({ ...value, labels: [], labelMatch: LabelMatch.All, tags: {}, search: '' });
  const removeLabel = (label) => onChange({ ...value, labels: value.labels.filter((x) => x !== label) });
  const removeTag = (key) => {
    const tags = { ...value.tags };
    delete tags[key];
    onChange({ ...value, tags });
  };

  return (
    <div className="meta-filters">
      <div className="meta-filters-row">
        {showSearch && (
          <form className="meta-search" onSubmit={apply} role="search">
            <label className="visually-hidden" htmlFor={'meta-search-' + (targetType || 'list')}>{searchLabel || m.search}</label>
            <input id={'meta-search-' + (targetType || 'list')} className="input-compact" type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={searchLabel || m.search} maxLength={200} />
          </form>
        )}
        {orderings && orderings.length > 1 && (
          <label className="inline-field">
            <span className="label-text">{m.ordering}</span>
            <select className="select-compact" value={value.ordering || orderings[0].value} onChange={(e) => onChange({ ...value, ordering: e.target.value })}>
              {orderings.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
            </select>
          </label>
        )}
        <button type="button" className="button button-secondary button-sm" onClick={() => setOpen((v) => !v)} aria-expanded={open}>
          <Icon name="filter" size={14} /><span>{count ? m.filtersCount(count) : m.filters}</span>
        </button>
      </div>
      {open && (
        <form className="meta-filters-panel panel" onSubmit={apply} aria-label={m.filters}>
          <div className="meta-label-row">
            <LabelInput label={m.labels} values={labels} onChange={setLabels} suggest={suggest.labels} placeholder={m.labelsPlaceholder} />
            <div className="segmented" role="radiogroup" aria-label={m.labelMatch}>
              {[LabelMatch.All, LabelMatch.Any].map((mode) => (
                <button key={mode} type="button" role="radio" aria-checked={labelMatch === mode} className={'segment' + (labelMatch === mode ? ' active' : '')} onClick={() => setLabelMatch(mode)}>{m.match[mode]}</button>
              ))}
            </div>
          </div>
          <TagRows label={m.tags} rows={rows} onChange={setRows} suggestKeys={suggest.tagKeys} suggestValues={suggest.tagValues} allowHasKey />
          <div className="form-actions">
            <button type="button" className="button button-secondary button-sm" onClick={() => setOpen(false)}>{strings.common.cancel}</button>
            <button type="submit" className="button button-primary button-sm">{m.apply}</button>
          </div>
        </form>
      )}
      {count > 0 && (
        <div className="active-filters" role="group" aria-label={m.activeFilters}>
          {value.search && <span className="chip">{m.searchChip(value.search)}<button type="button" className="chip-remove" onClick={() => onChange({ ...value, search: '' })} aria-label={m.removeFilter(m.searchChip(value.search))}><Icon name="close" size={10} /></button></span>}
          {(value.labels || []).map((label) => (
            <span key={'l:' + label} className="chip">{m.labelChip(label)}<button type="button" className="chip-remove" onClick={() => removeLabel(label)} aria-label={m.removeFilter(m.labelChip(label))}><Icon name="close" size={10} /></button></span>
          ))}
          {(value.labels || []).length > 1 && <span className="muted small">{m.matchNote[value.labelMatch || LabelMatch.All]}</span>}
          {Object.entries(value.tags || {}).map(([key, v]) => {
            const text = v === '*' ? m.hasKeyChip(key) : m.tagChip(key, v);
            return <span key={'t:' + key} className="chip">{text}<button type="button" className="chip-remove" onClick={() => removeTag(key)} aria-label={m.removeFilter(text)}><Icon name="close" size={10} /></button></span>;
          })}
          <button type="button" className="link-button small" onClick={clearAll}>{m.clearAll}</button>
        </div>
      )}
    </div>
  );
}

export default MetadataFilterBar;
