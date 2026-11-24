import { useMemo, useState } from 'react';
import {
  PROFILE_MAX_FILTERS,
  buildProfileFilters,
  countryOptions,
  pickerOptions,
  profileFilterChips,
  profileFilterOperators,
  rankHasLevel,
  rankMaxLevel,
  removeProfileFilter
} from '@trelliskit/shared';
import strings from '../../strings.js';
import Icon from '../../components/Icon.jsx';

const s = strings.admin.memberProfileFilters;
const d = strings.directory;

function TriState({ id, value, onChange, label }) {
  const current = value === true || value === 'true' ? 'true' : value === false || value === 'false' ? 'false' : '';
  return (
    <div className="field field-compact">
      <label className="label-text" htmlFor={id}>{label}</label>
      <select id={id} className="select-compact" value={current} onChange={(e) => onChange(e.target.value === '' ? null : e.target.value === 'true')}>
        <option value="">{d.any}</option>
        <option value="true">{d.hasValue}</option>
        <option value="false">{d.hasNoValue}</option>
      </select>
    </div>
  );
}

function Checks({ name, options, selected, onChange }) {
  const list = Array.isArray(selected) ? selected : [];
  const toggle = (key) => onChange(list.includes(key) ? list.filter((k) => k !== key) : [...list, key]);
  return (
    <div className="chip-options" role="group" aria-label={name}>
      {options.map((o) => (
        <label key={o.key} className={'chip-option' + (list.includes(o.key) ? ' selected' : '')}>
          <input type="checkbox" checked={list.includes(o.key)} onChange={() => toggle(o.key)} />
          {o.color && <span className="rank-swatch" style={{ backgroundColor: o.color }} aria-hidden="true" />}
          <span>{o.label}</span>
        </label>
      ))}
    </div>
  );
}

function Input({ id, label, value, onChange, type = 'text' }) {
  return (
    <div className="field field-compact">
      <label className="label-text" htmlFor={id}>{label}</label>
      <input id={id} type={type} className="input-compact" value={value === null || value === undefined ? '' : String(value)} onChange={(e) => onChange(e.target.value || null)} />
    </div>
  );
}

function OptionSelect({ id, label, value, options, onChange }) {
  return (
    <div className="field field-compact">
      <label className="label-text" htmlFor={id}>{label}</label>
      <select id={id} className="select-compact" value={value || ''} onChange={(e) => onChange(e.target.value || null)}>
        <option value="">{d.any}</option>
        {options.map((o) => <option key={o.key} value={o.key}>{o.label}</option>)}
      </select>
    </div>
  );
}

/**
 * The filter controls of one filterable field for every operator its type allows (REST_API.md
 * "Member filters"): rank `in`, `minRank`, `maxRank`, and `minLevel`; select and country `in`;
 * multi-select `any` or `all`; text `eq` and `prefix`; number and date `gte` and `lte`; Boolean
 * `eq`; and the `hasValue` tri-state for every type.
 */
export function AdminProfileFilterControl({ field, value, onChange }) {
  const ops = value || {};
  const allowed = profileFilterOperators(field.type);
  const set = (op, next) => onChange({ ...ops, [op]: next });
  const id = 'apf-' + field.key;
  const countries = useMemo(() => (field.type === 'Country' ? countryOptions() : []), [field.type]);
  const controls = [];
  switch (field.type) {
    case 'Rank': {
      const options = pickerOptions(field, ops.minRank, ops.maxRank, ops.in || []);
      const levelKey = ops.minRank || (options.find((o) => rankHasLevel(field, o.key)) || {}).key;
      controls.push(<Checks key="in" name={field.label + ' ' + d.equals} options={options} selected={ops.in} onChange={(list) => set('in', list.length ? list : null)} />);
      controls.push(<OptionSelect key="min" id={id + '-min'} label={d.minRank} value={ops.minRank} options={options} onChange={(v) => set('minRank', v)} />);
      controls.push(<OptionSelect key="max" id={id + '-max'} label={d.maxRank} value={ops.maxRank} options={options} onChange={(v) => set('maxRank', v)} />);
      if (levelKey && rankHasLevel(field, levelKey)) {
        controls.push(
          <div key="level" className="field field-compact">
            <label className="label-text" htmlFor={id + '-level'}>{d.minLevel(field.rank.levelLabel)}</label>
            <select id={id + '-level'} className="select-compact" value={ops.minLevel || ''} onChange={(e) => set('minLevel', e.target.value || null)}>
              <option value="">{d.any}</option>
              {Array.from({ length: rankMaxLevel(field, levelKey) }, (_, i) => i + 1).map((n) => <option key={n} value={n}>{n}</option>)}
            </select>
          </div>
        );
      }
      break;
    }
    case 'SingleSelect':
      controls.push(<Checks key="in" name={field.label} options={pickerOptions(field, ops.in || [])} selected={ops.in} onChange={(list) => set('in', list.length ? list : null)} />);
      break;
    case 'MultiSelect': {
      const op = ops.all ? 'all' : 'any';
      controls.push(<Checks key="keys" name={field.label} options={pickerOptions(field, ops[op] || [])} selected={ops[op]} onChange={(list) => onChange({ ...ops, any: null, all: null, [op]: list.length ? list : null })} />);
      controls.push(
        <div key="mode" className="field field-compact">
          <label className="label-text" htmlFor={id + '-mode'}>{s.match}</label>
          <select id={id + '-mode'} className="select-compact" value={op} onChange={(e) => onChange({ ...ops, any: null, all: null, [e.target.value]: ops[op] || null })}>
            <option value="any">{d.matchAny}</option>
            <option value="all">{d.matchAll}</option>
          </select>
        </div>
      );
      break;
    }
    case 'Country': {
      const list = ops.in || [];
      controls.push(
        <div key="add" className="field field-compact">
          <label className="label-text" htmlFor={id + '-add'}>{s.addCountry}</label>
          <select id={id + '-add'} className="select-compact" value="" onChange={(e) => e.target.value && set('in', [...list.filter((x) => x !== e.target.value), e.target.value])}>
            <option value="">{d.any}</option>
            {countries.filter((x) => !list.includes(x.code)).map((x) => <option key={x.code} value={x.code}>{x.name}</option>)}
          </select>
        </div>
      );
      if (list.length) {
        controls.push(
          <div key="list" className="token-chips">
            {list.map((code) => {
              const name = (countries.find((x) => x.code === code) || {}).name || code;
              return (
                <span key={code} className="chip">
                  {name}
                  <button type="button" className="chip-remove" onClick={() => set('in', list.filter((x) => x !== code).length ? list.filter((x) => x !== code) : null)} aria-label={s.removeCountry(name)}><Icon name="close" size={10} /></button>
                </span>
              );
            })}
          </div>
        );
      }
      break;
    }
    case 'Text':
    case 'Email':
      if (allowed.includes('eq')) controls.push(<Input key="eq" id={id + '-eq'} label={d.equals} value={ops.eq} onChange={(v) => set('eq', v)} />);
      if (allowed.includes('prefix')) controls.push(<Input key="prefix" id={id + '-prefix'} label={d.startsWith} value={ops.prefix} onChange={(v) => set('prefix', v)} />);
      break;
    case 'Number':
      controls.push(<Input key="eq" id={id + '-eq'} type="number" label={d.equals} value={ops.eq} onChange={(v) => set('eq', v)} />);
      controls.push(<Input key="gte" id={id + '-gte'} type="number" label={d.from} value={ops.gte} onChange={(v) => set('gte', v)} />);
      controls.push(<Input key="lte" id={id + '-lte'} type="number" label={d.to} value={ops.lte} onChange={(v) => set('lte', v)} />);
      break;
    case 'Date':
      controls.push(<Input key="gte" id={id + '-gte'} type="date" label={d.from} value={ops.gte} onChange={(v) => set('gte', v)} />);
      controls.push(<Input key="lte" id={id + '-lte'} type="date" label={d.to} value={ops.lte} onChange={(v) => set('lte', v)} />);
      break;
    case 'Boolean':
      controls.push(
        <div key="eq" className="field field-compact">
          <label className="label-text" htmlFor={id + '-eq'}>{d.equals}</label>
          <select id={id + '-eq'} className="select-compact" value={ops.eq === true || ops.eq === 'true' ? 'true' : ops.eq === false || ops.eq === 'false' ? 'false' : ''} onChange={(e) => set('eq', e.target.value === '' ? null : e.target.value === 'true')}>
            <option value="">{d.any}</option>
            <option value="true">{strings.profileFields.yes}</option>
            <option value="false">{strings.profileFields.no}</option>
          </select>
        </div>
      );
      break;
    default:
      break;
  }
  if (allowed.includes('hasValue')) controls.push(<TriState key="has" id={id + '-has'} label={d.hasValueLabel} value={ops.hasValue} onChange={(v) => set('hasValue', v)} />);
  return (
    <fieldset className="fieldset" data-field={field.key}>
      <legend className="label-text">{field.label} <code className="mono muted small">{field.key}</code></legend>
      {controls}
    </fieldset>
  );
}

function clean(state, fields) {
  return buildProfileFilters(state, fields);
}

/**
 * Profile filters of the admin member search: a collapsible panel of every filterable field
 * (AdminOnly and Staff fields included), applied together, plus removable chips for the active
 * filters. `value` is the applied filter state (from the URL); `onApply` receives the new state.
 */
function MemberProfileFilters({ fields, value, onApply }) {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState(value || {});
  const [lastValue, setLastValue] = useState(value);
  if (lastValue !== value) {
    setLastValue(value);
    setDraft(value || {});
  }
  const applied = clean(value, fields);
  const chips = profileFilterChips(applied, fields);
  const pending = Object.keys(clean(draft, fields)).length;
  const tooMany = pending > PROFILE_MAX_FILTERS;
  const opText = (op) => d.operators[op] || op;

  if (!fields.length) return null;
  return (
    <div className="admin-filters">
      <div className="filter-bar">
        <button type="button" className="button button-secondary button-sm" onClick={() => setOpen((v) => !v)} aria-expanded={open} aria-controls="member-profile-filters">
          <Icon name="filter" size={14} /><span>{chips.length ? s.toggleCount(Object.keys(applied).length) : s.toggle}</span>
        </button>
        <span className="muted small">{s.hint(PROFILE_MAX_FILTERS)}</span>
      </div>
      {open && (
        <div id="member-profile-filters" className="filter-meta" role="group" aria-label={s.title}>
          <div className="profile-filter-grid">
            {fields.map((field) => (
              <AdminProfileFilterControl key={field.key} field={field} value={draft[field.key]} onChange={(ops) => setDraft((x) => ({ ...x, [field.key]: ops }))} />
            ))}
          </div>
          {tooMany && <p className="field-error" role="alert">{s.tooMany(PROFILE_MAX_FILTERS)}</p>}
          <div className="filter-actions">
            <button type="button" className="button button-primary button-sm" disabled={tooMany} onClick={() => onApply(clean(draft, fields))}>
              <Icon name="filter" size={14} /><span>{s.apply}</span>
            </button>
            <button type="button" className="button button-secondary button-sm" onClick={() => { setDraft({}); onApply({}); }}>{s.clear}</button>
          </div>
        </div>
      )}
      {chips.length > 0 && (
        <div className="active-filters" role="group" aria-label={s.activeLabel}>
          {chips.map((chip) => {
            const text = chip.label + ' ' + opText(chip.op) + (chip.op === 'hasValue' ? (chip.value === 'true' ? '' : ': ' + d.hasNoValue) : ': ' + chip.value);
            return (
              <span key={chip.key + '.' + chip.op} className="chip">
                {text}
                <button type="button" className="chip-remove" onClick={() => onApply(removeProfileFilter(applied, chip.key, chip.op))} aria-label={strings.metadata.removeFilter(text)}><Icon name="close" size={10} /></button>
              </span>
            );
          })}
          <button type="button" className="link-button small" onClick={() => onApply({})}>{s.clear}</button>
        </div>
      )}
    </div>
  );
}

export default MemberProfileFilters;
