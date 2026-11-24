import { useCallback, useEffect, useId, useState } from 'react';
import strings from '../strings.js';
import Icon from './Icon.jsx';

function useSuggestions(load, text, deps = []) {
  const [options, setOptions] = useState([]);
  useEffect(() => {
    if (!load) return undefined;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      Promise.resolve(load(text, controller.signal)).then((list) => { if (!controller.signal.aborted) setOptions((list || []).slice(0, 10)); }, () => {});
    }, 200);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [load, text, ...deps]);
  return options;
}

function TagRow({ row, index, onChange, onRemove, suggestKeys, suggestValues, allowHasKey, disabled, idBase }) {
  const m = strings.metadata;
  const keyOptions = useSuggestions(suggestKeys, row.key);
  // Stable per key: a new function each render would rerun the suggestion effect after every result (a request loop).
  const key = row.key.trim();
  const loadValuesFn = useCallback((text, signal) => suggestValues(key, text, signal), [suggestValues, key]);
  const loadValues = suggestValues ? loadValuesFn : null;
  const valueOptions = useSuggestions(row.hasKey ? null : loadValues, row.value, [row.key]);
  const keyList = idBase + '-k' + index;
  const valueList = idBase + '-v' + index;
  return (
    <li className="tag-row">
      <label className="field field-compact">
        <span className="label-text">{m.tagKey}</span>
        <input className="input-compact" value={row.key} onChange={(e) => onChange({ ...row, key: e.target.value })} list={keyList} disabled={disabled} aria-label={m.tagKeyRow(index + 1)} autoComplete="off" />
        <datalist id={keyList}>{keyOptions.map((o) => <option key={o} value={o} />)}</datalist>
      </label>
      <span className="tag-eq" aria-hidden="true">=</span>
      <label className="field field-compact field-grow">
        <span className="label-text">{m.tagValue}</span>
        <input className="input-compact" value={row.hasKey ? '' : row.value} onChange={(e) => onChange({ ...row, value: e.target.value })} list={valueList} disabled={disabled || row.hasKey} placeholder={row.hasKey ? m.anyValue : ''} aria-label={m.tagValueRow(index + 1)} autoComplete="off" />
        <datalist id={valueList}>{valueOptions.map((o) => <option key={o} value={o} />)}</datalist>
      </label>
      {allowHasKey && (
        <label className="check-field tag-has">
          <input type="checkbox" checked={row.hasKey} onChange={(e) => onChange({ ...row, hasKey: e.target.checked })} disabled={disabled} />
          <span>{m.hasKey}</span>
        </label>
      )}
      <button type="button" className="icon-button" onClick={onRemove} disabled={disabled} aria-label={m.removeTag(row.key || index + 1)} title={m.removeTag(row.key || index + 1)}>
        <Icon name="trash" size={14} />
      </button>
    </li>
  );
}

/** Rows for editing a tag dictionary as `[{ key, value, hasKey }]`. */
export function tagsToRows(tags) {
  return Object.entries(tags || {}).map(([key, value]) => ({ key, value: value === '*' ? '' : value, hasKey: value === '*' }));
}

/** A tag dictionary from rows (empty keys skipped; "has key" rows become `*` when allowed). */
export function rowsToTags(rows, { allowHasKey = false } = {}) {
  const out = {};
  for (const row of rows) {
    const key = row.key.trim();
    if (!key) continue;
    out[key] = allowHasKey && row.hasKey ? '*' : row.value;
  }
  return out;
}

/**
 * Editable `key = value` rows with key and value autocomplete; filters also offer "has key"
 * (any value).
 * @param {{ rows: Array<{ key: string, value: string, hasKey: boolean }>, onChange: (rows: object[]) => void, suggestKeys?: Function,
 *   suggestValues?: Function, allowHasKey?: boolean, disabled?: boolean, label: string }} props
 */
function TagRows({ rows, onChange, suggestKeys, suggestValues, allowHasKey = false, disabled = false, label }) {
  const idBase = 'tags-' + useId().replace(/:/g, '');
  const m = strings.metadata;
  return (
    <fieldset className="tag-rows">
      <legend className="label-text">{label}</legend>
      {rows.length > 0 && (
        <ul className="tag-row-list">
          {rows.map((row, i) => (
            <TagRow
              key={i}
              row={row}
              index={i}
              idBase={idBase}
              onChange={(next) => onChange(rows.map((r, j) => (j === i ? next : r)))}
              onRemove={() => onChange(rows.filter((_, j) => j !== i))}
              suggestKeys={suggestKeys}
              suggestValues={suggestValues}
              allowHasKey={allowHasKey}
              disabled={disabled}
            />
          ))}
        </ul>
      )}
      <button type="button" className="button button-secondary button-sm" onClick={() => onChange([...rows, { key: '', value: '', hasKey: false }])} disabled={disabled}>
        <Icon name="plus" size={14} /><span>{m.addTag}</span>
      </button>
    </fieldset>
  );
}

export default TagRows;
