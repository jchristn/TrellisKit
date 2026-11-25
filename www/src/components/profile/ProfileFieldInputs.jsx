import { useEffect, useId, useMemo, useState } from 'react';
import {
  PROFILE_HISTORY_DEFAULT_MAX, PROFILE_OTHER_TEXT_MAX, PROFILE_RADIO_MAX_OPTIONS, PROFILE_SUB_TEXT_DEFAULT_MAX, countryOptions, emptyHistoryEntry,
  findOption, findProfileField, isFieldRequired, normalizeEnumerationResult, optionColor, pickerOptions, rankHasLevel, rankMaxLevel
} from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useDebounce from '../../hooks/useDebounce.js';
import Icon from '../Icon.jsx';
import { RankSwatch } from './ProfileFieldDisplay.jsx';

const s = strings.profileFields;

/** DOM id for a field or sub-field input. */
export function fieldInputId(idPrefix, key) {
  return (idPrefix || 'pf') + '-' + String(key).replace(/[^A-Za-z0-9_-]/g, '-');
}

function ErrorText({ id, text }) {
  if (!text) return null;
  return <span className="field-error" id={id}>{text}</span>;
}

function inputType(type) {
  switch (type) {
    case 'Url': return 'url';
    case 'Email': return 'email';
    case 'Phone': return 'tel';
    case 'Date': return 'date';
    case 'Number': return 'number';
    default: return 'text';
  }
}

function textMax(field) {
  const v = field.validation || {};
  if (v.maxLength) return v.maxLength;
  return field.type === 'LongText' ? 2000 : field.type === 'Text' ? 200 : undefined;
}

/** A rank option select with its color swatch, plus a level select when the field has levels. */
function RankInput({ field, value, onChange, id, disabled, invalid, describedBy, withLevel = true, placeholder }) {
  const options = pickerOptions(field, value && value.key);
  const key = (value && value.key) || '';
  const hasLevel = withLevel && key && rankHasLevel(field, key);
  const max = rankMaxLevel(field, key);
  const levelLabel = (field.rank && field.rank.levelLabel) || '';
  const setKey = (next) => onChange({ key: next, level: next ? Math.min(Number((value && value.level) || 0), rankMaxLevel(field, next)) : 0 });
  return (
    <div className="rank-input">
      <div className="rank-select">
        <select id={id} value={key} onChange={(e) => setKey(e.target.value)} disabled={disabled} aria-invalid={invalid || undefined} aria-describedby={describedBy}>
          <option value="">{placeholder || s.choose(field.label)}</option>
          {options.map((o) => <option key={o.key} value={o.key}>{o.label}</option>)}
        </select>
        <RankSwatch color={optionColor(field, key)} />
      </div>
      {hasLevel && (
        <div className="rank-level">
          <label className="label-text" htmlFor={id + '-level'}>{levelLabel}</label>
          <select id={id + '-level'} value={Number((value && value.level) || 0)} onChange={(e) => onChange({ key, level: Number(e.target.value) })} disabled={disabled}>
            {Array.from({ length: max + 1 }, (_, n) => <option key={n} value={n}>{n}</option>)}
          </select>
        </div>
      )}
    </div>
  );
}

function SingleSelectInput({ field, value, onChange, id, disabled, invalid, describedBy }) {
  const current = value || { key: '', otherText: '' };
  const options = pickerOptions(field, current.key);
  const selected = findOption(field, current.key);
  const set = (key) => onChange({ key, otherText: current.otherText || '' });
  return (
    <>
      {options.length <= PROFILE_RADIO_MAX_OPTIONS ? (
        <div className="option-list" role="radiogroup" aria-labelledby={id + '-label'} aria-describedby={describedBy} id={id}>
          {options.map((o) => (
            <label key={o.key} className={'option-row' + (current.key === o.key ? ' selected' : '')}>
              <input type="radio" name={id} value={o.key} checked={current.key === o.key} onChange={() => set(o.key)} disabled={disabled} />
              {o.color && <RankSwatch color={o.color} />}
              <span>{o.label}</span>
            </label>
          ))}
          {!isFieldRequired(field) && current.key && (
            <button type="button" className="button button-ghost button-sm" onClick={() => set('')} disabled={disabled}>{s.none}</button>
          )}
        </div>
      ) : (
        <select id={id} value={current.key} onChange={(e) => set(e.target.value)} disabled={disabled} aria-invalid={invalid || undefined} aria-describedby={describedBy}>
          <option value="">{s.choose(field.label)}</option>
          {options.map((o) => <option key={o.key} value={o.key}>{o.label}</option>)}
        </select>
      )}
      {selected && selected.freeText && (
        <input
          type="text" className="other-text" maxLength={PROFILE_OTHER_TEXT_MAX} placeholder={s.otherPlaceholder} aria-label={s.otherText(selected.label)}
          value={current.otherText || ''} onChange={(e) => onChange({ key: current.key, otherText: e.target.value })} disabled={disabled}
        />
      )}
    </>
  );
}

function MultiSelectInput({ field, value, onChange, id, disabled, describedBy }) {
  const current = value || { keys: [], otherText: '' };
  const options = pickerOptions(field, current.keys);
  const toggle = (key) => onChange({ ...current, keys: current.keys.includes(key) ? current.keys.filter((k) => k !== key) : [...current.keys, key] });
  const free = current.keys.map((k) => findOption(field, k)).find((o) => o && o.freeText);
  return (
    <>
      <div className="option-list" id={id} aria-describedby={describedBy}>
        {options.map((o) => (
          <label key={o.key} className={'option-row' + (current.keys.includes(o.key) ? ' selected' : '')}>
            <input type="checkbox" checked={current.keys.includes(o.key)} onChange={() => toggle(o.key)} disabled={disabled} />
            <span>{o.label}</span>
          </label>
        ))}
      </div>
      {free && (
        <input
          type="text" className="other-text" maxLength={PROFILE_OTHER_TEXT_MAX} placeholder={s.otherPlaceholder} aria-label={s.otherText(free.label)}
          value={current.otherText || ''} onChange={(e) => onChange({ ...current, otherText: e.target.value })} disabled={disabled}
        />
      )}
    </>
  );
}

function CountryInput({ value, onChange, id, disabled, invalid, describedBy, required }) {
  const countries = useMemo(() => countryOptions(), []);
  return (
    <select id={id} value={value || ''} onChange={(e) => onChange(e.target.value)} disabled={disabled} aria-invalid={invalid || undefined} aria-describedby={describedBy} autoComplete="country">
      <option value="">{required ? s.choose('') : s.notSet}</option>
      {countries.map((c) => <option key={c.code} value={c.code}>{c.name}</option>)}
    </select>
  );
}

function BooleanInput({ field, value, onChange, id, disabled, describedBy }) {
  return (
    <label className="switch-row">
      <input id={id} type="checkbox" role="switch" checked={value === true} onChange={(e) => onChange(e.target.checked)} disabled={disabled} aria-describedby={describedBy} />
      <span>{value === true ? s.yes : value === false ? s.no : s.notSet}</span>
      {value !== null && !isFieldRequired(field) && (
        <button type="button" className="button button-ghost button-sm" onClick={() => onChange(null)} disabled={disabled}>{s.none}</button>
      )}
    </label>
  );
}

/**
 * A member name with member search: typing searches members; picking one links the member
 * (`userId`), and any typed name is kept as a free name when nobody is picked.
 */
function MemberNameInput({ value, onChange, id, disabled, invalid, describedBy }) {
  const { client } = useAuth() || {};
  const current = value || { name: '', userId: null };
  const [focused, setFocused] = useState(false);
  const [results, setResults] = useState([]);
  const query = useDebounce(current.userId ? '' : String(current.name || '').trim().replace(/^@/, ''), 300);
  useEffect(() => {
    if (!focused || !query || query.length > 30 || !client || typeof client.searchMembers !== 'function') {
      setResults([]);
      return undefined;
    }
    const controller = new AbortController();
    client.searchMembers({ search: query, maxResults: 6 }, controller.signal).then(
      (body) => { if (!controller.signal.aborted) setResults(normalizeEnumerationResult(body).objects); },
      () => { if (!controller.signal.aborted) setResults([]); }
    );
    return () => controller.abort();
  }, [client, query, focused]);
  return (
    <div className="member-name-input">
      <div className="input-with-action">
        <input
          id={id} type="text" maxLength={100} autoComplete="off" value={current.name || ''} disabled={disabled} aria-invalid={invalid || undefined} aria-describedby={describedBy}
          onChange={(e) => onChange({ name: e.target.value, userId: null })}
          onFocus={() => setFocused(true)} onBlur={() => setTimeout(() => setFocused(false), 150)}
        />
        {current.userId && (
          <span className="badge badge-info member-linked" title={s.memberLinked}>
            <Icon name="link" size={12} /><span>{s.memberLinked}</span>
            <button type="button" className="icon-button" aria-label={s.memberUnlink} onClick={() => onChange({ name: current.name, userId: null })} disabled={disabled}><Icon name="close" size={12} /></button>
          </span>
        )}
      </div>
      {focused && results.length > 0 && (
        <ul className="member-suggestions" role="listbox" aria-label={s.memberResults}>
          {results.map((user) => (
            <li key={user.id}>
              <button type="button" role="option" aria-selected="false" onMouseDown={(e) => e.preventDefault()} onClick={() => { onChange({ name: user.displayName || user.handle, userId: user.id }); setResults([]); }}>
                <strong>{user.displayName}</strong>{user.handle && <span className="muted small"> @{user.handle}</span>}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function SubFieldInput({ sub, value, onChange, id, disabled, invalid, describedBy, allFields }) {
  switch (sub.type) {
    case 'RankValue': {
      const rankField = findProfileField(allFields, sub.rankFieldKey);
      if (!rankField) return null;
      return <RankInput field={{ ...rankField, label: sub.label || rankField.label }} value={value} onChange={onChange} id={id} disabled={disabled} invalid={invalid} describedBy={describedBy} withLevel={sub.withLevel} />;
    }
    case 'Member': return <MemberNameInput value={value} onChange={onChange} id={id} disabled={disabled} invalid={invalid} describedBy={describedBy} />;
    case 'SingleSelect':
      return (
        <select id={id} value={value || ''} onChange={(e) => onChange(e.target.value)} disabled={disabled} aria-invalid={invalid || undefined}>
          <option value="">{s.choose(sub.label)}</option>
          {(sub.options || []).filter((o) => !o.retired || o.key === value).map((o) => <option key={o.key} value={o.key}>{o.label}</option>)}
        </select>
      );
    case 'Boolean':
      return <label className="switch-row"><input id={id} type="checkbox" role="switch" checked={value === true} onChange={(e) => onChange(e.target.checked)} disabled={disabled} /><span>{value === true ? s.yes : s.no}</span></label>;
    case 'Date': {
      const max = sub.notInFuture ? new Date().toISOString().slice(0, 10) : undefined;
      return <input id={id} type="date" max={max} value={value || ''} onChange={(e) => onChange(e.target.value)} disabled={disabled} aria-invalid={invalid || undefined} aria-describedby={describedBy} />;
    }
    case 'Number':
      return <input id={id} type="number" inputMode="decimal" min={sub.minValue ?? undefined} max={sub.maxValue ?? undefined} value={value || ''} onChange={(e) => onChange(e.target.value)} disabled={disabled} aria-invalid={invalid || undefined} />;
    case 'Url':
      return <input id={id} type="url" inputMode="url" value={value || ''} onChange={(e) => onChange(e.target.value)} disabled={disabled} aria-invalid={invalid || undefined} />;
    case 'Text':
      return <input id={id} type="text" maxLength={sub.maxLength || PROFILE_SUB_TEXT_DEFAULT_MAX} value={value || ''} onChange={(e) => onChange(e.target.value)} disabled={disabled} aria-invalid={invalid || undefined} aria-describedby={describedBy} />;
    default: return null;
  }
}

/** History list editor: add, remove, reorder, at most `validation.maxEntries` entries. */
function HistoryInput({ field, value, onChange, idPrefix, disabled, errors, allFields }) {
  const list = Array.isArray(value) ? value : [];
  const max = (field.validation && field.validation.maxEntries) || PROFILE_HISTORY_DEFAULT_MAX;
  const subs = (field.subFields || []).slice().sort((a, b) => (a.position || 0) - (b.position || 0)).filter((sub) => sub.type !== 'RankValue' || findProfileField(allFields, sub.rankFieldKey));
  const update = (index, key, next) => onChange(list.map((entry, i) => (i === index ? { ...entry, values: { ...entry.values, [key]: next } } : entry)));
  const move = (index, delta) => {
    const target = index + delta;
    if (target < 0 || target >= list.length) return;
    const next = list.slice();
    [next[index], next[target]] = [next[target], next[index]];
    onChange(next);
  };
  return (
    <div className="history-editor">
      <div className="row-between">
        <span className="muted small">{s.historyHint(max)}</span>
        <button type="button" className="button button-secondary button-sm" onClick={() => onChange([...list, emptyHistoryEntry(field)])} disabled={disabled || list.length >= max}>
          <Icon name="plus" size={14} /><span>{s.historyAdd}</span>
        </button>
      </div>
      {list.length === 0 && <p className="muted small">{s.historyEmpty}</p>}
      {list.length >= max && <p className="muted small">{s.historyMax(max)}</p>}
      <ol className="history-list">
        {list.map((entry, index) => (
          <li key={entry.rowKey || entry.id || index} className="history-row" style={{ '--history-fields': Math.min(subs.length, 4) || 1 }} aria-label={s.historyEntry(index + 1)}>
            {subs.map((sub) => {
              const errorKey = 'profile.' + field.key + '[' + index + '].' + sub.key;
              const id = fieldInputId(idPrefix, field.key + '-' + (entry.rowKey || index) + '-' + sub.key);
              const error = errors && errors[errorKey];
              return (
                <div className="field" key={sub.key}>
                  <label className="label-text" htmlFor={id} id={id + '-label'}>{sub.label}{sub.required && <span className="required-mark" aria-hidden="true"> *</span>}</label>
                  <SubFieldInput sub={sub} value={entry.values ? entry.values[sub.key] : null} onChange={(next) => update(index, sub.key, next)} id={id} disabled={disabled} invalid={Boolean(error)} describedBy={error ? id + '-error' : undefined} allFields={allFields} />
                  <ErrorText id={id + '-error'} text={error} />
                </div>
              );
            })}
            <div className="history-actions">
              <button type="button" className="icon-button" onClick={() => move(index, -1)} disabled={disabled || index === 0} aria-label={s.historyUp(index + 1)} title={s.historyUp(index + 1)}><Icon name="chevronUp" size={14} /></button>
              <button type="button" className="icon-button" onClick={() => move(index, 1)} disabled={disabled || index === list.length - 1} aria-label={s.historyDown(index + 1)} title={s.historyDown(index + 1)}><Icon name="chevronDown" size={14} /></button>
              <button type="button" className="icon-button history-remove" onClick={() => onChange(list.filter((_, i) => i !== index))} disabled={disabled} aria-label={s.historyRemove(index + 1)} title={s.historyRemove(index + 1)}><Icon name="trash" size={14} /></button>
            </div>
            {errors && errors['profile.' + field.key + '[' + index + ']'] && <ErrorText text={errors['profile.' + field.key + '[' + index + ']']} />}
          </li>
        ))}
      </ol>
    </div>
  );
}

/**
 * The control for one field (section 9.11 `control`), by type. `value` is the form value from
 * `toFormValue`; `errors` maps `profile.<key>` (and nested History keys) to text. `locked` (text)
 * makes the field read-only with that explanation (a verified rank, server 409 RankVerified).
 */
export function ProfileFieldInput({ field, value, onChange, errors = {}, disabled: disabledProp = false, idPrefix = 'pf', allFields = [], optional = false, locked = null }) {
  const disabled = disabledProp || Boolean(locked);
  const generated = useId();
  const id = fieldInputId(idPrefix, field.key) || generated;
  const errorKey = 'profile.' + field.key;
  const error = errors[errorKey];
  const required = isFieldRequired(field);
  const helpId = field.helpText ? id + '-help' : undefined;
  const lockId = locked ? id + '-locked' : null;
  const describedBy = [helpId, lockId, error ? id + '-error' : null].filter(Boolean).join(' ') || undefined;
  const common = { id, disabled, invalid: Boolean(error), describedBy };
  let control;
  switch (field.type) {
    case 'LongText':
      control = <textarea id={id} rows={4} maxLength={textMax(field)} placeholder={field.placeholder || undefined} value={value || ''} onChange={(e) => onChange(e.target.value)} disabled={disabled} aria-invalid={Boolean(error) || undefined} aria-describedby={describedBy} />;
      break;
    case 'Text': case 'Url': case 'Email': case 'Phone': case 'Date': case 'Number': {
      const v = field.validation || {};
      const extra = field.type === 'Number'
        ? { inputMode: v.integerOnly ? 'numeric' : 'decimal', step: v.integerOnly ? 1 : 'any', min: v.minValue ?? undefined, max: v.maxValue ?? undefined }
        : field.type === 'Date'
          ? { min: v.minDate || undefined, max: v.notInFuture ? new Date().toISOString().slice(0, 10) : (v.maxDate || undefined) }
          : { maxLength: field.type === 'Text' ? textMax(field) : field.type === 'Email' ? 254 : field.type === 'Url' ? 2048 : 16 };
      const placeholder = field.placeholder || (field.type === 'Phone' ? '+15551234567' : field.type === 'Url' ? 'https://' : undefined);
      control = <input id={id} type={inputType(field.type)} placeholder={placeholder} value={value || ''} onChange={(e) => onChange(e.target.value)} disabled={disabled} aria-invalid={Boolean(error) || undefined} aria-describedby={describedBy} {...extra} />;
      break;
    }
    case 'Boolean': control = <BooleanInput field={field} value={value} onChange={onChange} {...common} />; break;
    case 'Country': control = <CountryInput value={value} onChange={onChange} required={required} {...common} />; break;
    case 'SingleSelect': control = <SingleSelectInput field={field} value={value} onChange={onChange} {...common} />; break;
    case 'MultiSelect': control = <MultiSelectInput field={field} value={value} onChange={onChange} {...common} />; break;
    case 'Rank': control = <RankInput field={field} value={value} onChange={onChange} placeholder={field.placeholder || undefined} {...common} />; break;
    case 'History': control = <HistoryInput field={field} value={value} onChange={onChange} idPrefix={idPrefix} disabled={disabled} errors={errors} allFields={allFields} />; break;
    default: return null;
  }
  const grouped = field.type === 'History' || field.type === 'MultiSelect' || (field.type === 'SingleSelect' && pickerOptions(field).length <= PROFILE_RADIO_MAX_OPTIONS);
  const label = (
    <>
      {field.label}
      {required && <span className="required-mark" aria-label={s.required}> *</span>}
      {!required && optional && <span className="muted small"> ({s.optional.toLowerCase()})</span>}
    </>
  );
  const baseHint = field.helpText
    ? <span id={helpId} className="muted small field-help">{field.helpText}</span>
    : field.type === 'Phone' ? <span className="muted small field-help">{s.phoneHint}</span> : null;
  const hint = locked ? <>{baseHint}<span id={lockId} className="muted small field-help field-locked" data-locked="true">{locked}</span></> : baseHint;
  if (grouped) {
    return (
      <fieldset className={'fieldset profile-field profile-field-' + field.type.toLowerCase()} data-field={field.key}>
        <legend className="label-text" id={id + '-label'}>{label}</legend>
        {hint}
        {control}
        <ErrorText id={id + '-error'} text={error} />
      </fieldset>
    );
  }
  return (
    <div className={'field profile-field profile-field-' + field.type.toLowerCase()} data-field={field.key}>
      <label className="label-text" htmlFor={id} id={id + '-label'}>{label}</label>
      {control}
      {hint}
      <ErrorText id={id + '-error'} text={error} />
    </div>
  );
}

/**
 * Inputs for a list of fields, in the given order. Short single-line fields sit in a grid; long
 * text, groups, and history take the full width.
 * `locked` maps field keys to the read-only explanation of a locked field (verified ranks).
 * @param {{ fields: object[], allFields?: object[], values: Record<string, any>, onChange: (key: string, value: any) => void, errors?: Record<string,string>, disabled?: boolean, idPrefix?: string, markOptional?: boolean, locked?: Record<string,string> }} props
 */
export function ProfileFieldsEditor({ fields, allFields, values, onChange, errors = {}, disabled = false, idPrefix = 'pf', markOptional = false, locked = null }) {
  const every = allFields || fields;
  const compact = (f) => ['Text', 'Number', 'Date', 'Url', 'Email', 'Phone', 'Country', 'Boolean'].includes(f.type) || (f.type === 'SingleSelect' && pickerOptions(f).length > PROFILE_RADIO_MAX_OPTIONS) || f.type === 'Rank';
  const groups = [];
  for (const field of fields || []) {
    const last = groups[groups.length - 1];
    if (compact(field) && last && last.compact) last.items.push(field);
    else groups.push({ compact: compact(field), items: [field] });
  }
  return (
    <div className="profile-fields-editor">
      {groups.map((group) => {
        const items = group.items.map((field) => (
          <ProfileFieldInput
            key={field.key} field={field} value={values ? values[field.key] : undefined} onChange={(next) => onChange(field.key, next)}
            errors={errors} disabled={disabled} idPrefix={idPrefix} allFields={every} optional={markOptional} locked={locked ? locked[field.key] || null : null}
          />
        ));
        return group.compact ? <div className="form-grid" key={group.items[0].key}>{items}</div> : items;
      })}
    </div>
  );
}

export default ProfileFieldsEditor;
