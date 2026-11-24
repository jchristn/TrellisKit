import { useEffect, useId, useState } from 'react';
import strings from '../strings.js';
import Icon from './Icon.jsx';

const SUGGEST_DEBOUNCE_MS = 200;

/**
 * Chip input for labels with autocomplete. Enter, Tab, or a comma adds the typed value; Backspace
 * on an empty input removes the last chip. `suggest(prefix, signal)` resolves suggestion strings
 * (for example from `POST /labels/enumerate`); failures show no suggestions.
 * @param {{ id?: string, label: string, values: string[], onChange: (values: string[]) => void,
 *   suggest?: (prefix: string, signal: AbortSignal) => Promise<string[]>, placeholder?: string, disabled?: boolean, hideLabel?: boolean, hint?: string }} props
 */
function LabelInput({ id, label, values, onChange, suggest, placeholder, disabled = false, hideLabel = false, hint }) {
  const autoId = useId();
  const inputId = id || 'labels-' + autoId;
  const listId = inputId + '-list';
  const [text, setText] = useState('');
  const [options, setOptions] = useState([]);
  const [active, setActive] = useState(0);
  const m = strings.metadata;

  useEffect(() => {
    const prefix = text.trim();
    if (!suggest || !prefix) {
      setOptions([]);
      return undefined;
    }
    const controller = new AbortController();
    const timer = setTimeout(() => {
      Promise.resolve(suggest(prefix, controller.signal)).then(
        (list) => {
          if (controller.signal.aborted) return;
          const lower = new Set(values.map((v) => v.toLowerCase()));
          setOptions((list || []).filter((v) => v && !lower.has(String(v).toLowerCase())).slice(0, 10));
          setActive(0);
        },
        () => { if (!controller.signal.aborted) setOptions([]); }
      );
    }, SUGGEST_DEBOUNCE_MS);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [text, suggest, values]);

  const add = (value) => {
    const v = String(value || '').trim().replace(/,+$/, '');
    setText('');
    setOptions([]);
    if (!v || values.some((x) => x.toLowerCase() === v.toLowerCase())) return;
    onChange([...values, v]);
  };
  const remove = (value) => onChange(values.filter((x) => x !== value));

  const onKeyDown = (e) => {
    const open = options.length > 0;
    if (open && e.key === 'ArrowDown') { e.preventDefault(); setActive((i) => (i + 1) % options.length); return; }
    if (open && e.key === 'ArrowUp') { e.preventDefault(); setActive((i) => (i - 1 + options.length) % options.length); return; }
    if (e.key === 'Escape' && open) { e.preventDefault(); e.stopPropagation(); setOptions([]); return; }
    if (e.key === 'Enter' || e.key === ',' || (e.key === 'Tab' && text.trim())) {
      if (!text.trim() && !open) return;
      e.preventDefault();
      add(open ? options[active] : text);
      return;
    }
    if (e.key === 'Backspace' && !text && values.length) remove(values[values.length - 1]);
  };

  return (
    <div className="label-input field">
      <label className={'label-text' + (hideLabel ? ' visually-hidden' : '')} htmlFor={inputId}>{label}</label>
      <div className={'chip-input' + (disabled ? ' disabled' : '')}>
        {values.map((value) => (
          <span key={value} className={'chip' + (value.toLowerCase().startsWith('staff:') ? ' chip-staff' : '')}>
            <span>{value}</span>
            <button type="button" className="chip-remove" onClick={() => remove(value)} disabled={disabled} aria-label={m.removeLabel(value)} title={m.removeLabel(value)}>
              <Icon name="close" size={10} />
            </button>
          </span>
        ))}
        <span className="chip-input-wrap">
          <input
            id={inputId}
            value={text}
            onChange={(e) => {
              const v = e.target.value;
              if (v.endsWith(',')) add(v);
              else setText(v);
            }}
            onKeyDown={onKeyDown}
            onBlur={() => setTimeout(() => setOptions([]), 150)}
            placeholder={values.length ? '' : placeholder}
            disabled={disabled}
            role="combobox"
            aria-autocomplete="list"
            aria-expanded={options.length > 0}
            aria-controls={options.length ? listId : undefined}
            aria-activedescendant={options.length ? listId + '-' + active : undefined}
            aria-describedby={hint ? inputId + '-hint' : undefined}
            autoComplete="off"
          />
          {options.length > 0 && (
            <ul id={listId} className="suggest-list" role="listbox" aria-label={m.suggestions}>
              {options.map((option, i) => (
                <li key={option} id={listId + '-' + i} role="option" aria-selected={i === active} className={'suggest-option' + (i === active ? ' active' : '')} onMouseDown={(e) => { e.preventDefault(); add(option); }}>{option}</li>
              ))}
            </ul>
          )}
        </span>
      </div>
      {hint && <span id={inputId + '-hint'} className="muted small">{hint}</span>}
    </div>
  );
}

export default LabelInput;
