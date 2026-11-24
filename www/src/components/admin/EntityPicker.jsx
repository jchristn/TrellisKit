import { useEffect, useId, useState } from 'react';
import strings from '../../strings.js';
import Icon from '../Icon.jsx';

const DEBOUNCE_MS = 250;

/** Most results a picker asks for per search (a bounded enumeration, never the whole collection). */
export const PICKER_MAX_RESULTS = 10;

const p = strings.picker;

/**
 * Search-as-you-type picker for admin forms (members, venues, instructors): the admin types a
 * name, the picker runs one bounded search (`search(term, signal)` resolves at most
 * `PICKER_MAX_RESULTS` `{ id, label, detail? }` items, debounced), and the choice is shown by name
 * with its id as secondary text, never as a raw id input. Keyboard: arrows move, Enter picks,
 * Escape closes, Backspace on an empty input removes the last choice.
 *
 * @param {{ id?: string, label: string, hint?: string, value: Array<{ id: string, label: string, detail?: string }>,
 *   onChange: (items: Array<{ id: string, label: string, detail?: string }>) => void,
 *   search: (term: string, signal: AbortSignal) => Promise<Array<{ id: string, label: string, detail?: string }>>,
 *   multiple?: boolean, max?: number, placeholder?: string, disabled?: boolean, minLength?: number, error?: string }} props
 */
function EntityPicker({ id, label, hint, value, onChange, search, multiple = false, max = 10, placeholder, disabled = false, minLength = 2, error = '' }) {
  const autoId = useId();
  const inputId = id || 'picker-' + autoId.replace(/[^a-zA-Z0-9_-]/g, '');
  const listId = inputId + '-list';
  const selected = Array.isArray(value) ? value : [];
  const [text, setText] = useState('');
  const [results, setResults] = useState([]);
  const [status, setStatus] = useState('idle');
  const [active, setActive] = useState(0);
  const full = multiple ? selected.length >= max : selected.length >= 1;
  const chosen = new Set(selected.map((x) => x.id));
  const options = results.filter((x) => !chosen.has(x.id));

  useEffect(() => {
    const term = text.trim();
    if (term.length < minLength) {
      setResults([]);
      setStatus('idle');
      return undefined;
    }
    const controller = new AbortController();
    setStatus('loading');
    const timer = setTimeout(() => {
      Promise.resolve(search(term, controller.signal)).then(
        (list) => {
          if (controller.signal.aborted) return;
          const next = (Array.isArray(list) ? list : []).filter((x) => x && x.id).slice(0, PICKER_MAX_RESULTS);
          setResults(next);
          setActive(0);
          setStatus(next.length ? 'ready' : 'empty');
        },
        () => {
          if (controller.signal.aborted) return;
          setResults([]);
          setStatus('error');
        }
      );
    }, DEBOUNCE_MS);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [text, search, minLength]);

  const pick = (item) => {
    if (!item) return;
    setText('');
    setResults([]);
    setStatus('idle');
    if (selected.some((x) => x.id === item.id)) return;
    onChange(multiple ? [...selected, item].slice(0, max) : [item]);
  };
  const remove = (item) => onChange(selected.filter((x) => x.id !== item.id));
  const open = options.length > 0;

  const onKeyDown = (e) => {
    if (open && e.key === 'ArrowDown') { e.preventDefault(); setActive((i) => (i + 1) % options.length); return; }
    if (open && e.key === 'ArrowUp') { e.preventDefault(); setActive((i) => (i - 1 + options.length) % options.length); return; }
    if (e.key === 'Escape' && (open || status !== 'idle')) { e.preventDefault(); e.stopPropagation(); setResults([]); setStatus('idle'); return; }
    if (e.key === 'Enter') {
      e.preventDefault();
      if (open) pick(options[active]);
      return;
    }
    if (e.key === 'Backspace' && !text && selected.length) remove(selected[selected.length - 1]);
  };

  const describedBy = [hint ? inputId + '-hint' : '', inputId + '-status', error ? inputId + '-error' : ''].filter(Boolean).join(' ');
  return (
    <div className="field entity-picker">
      <label className="label-text" htmlFor={inputId}>{label}</label>
      <div className={'chip-input' + (disabled ? ' disabled' : '')}>
        {selected.map((item) => (
          <span key={item.id} className="chip entity-chip" title={item.id}>
            <span>{item.label || item.id}</span>
            {item.detail && <span className="muted small entity-chip-detail">{item.detail}</span>}
            <button type="button" className="chip-remove" onClick={() => remove(item)} disabled={disabled} aria-label={p.remove(item.label || item.id)} title={p.remove(item.label || item.id)}>
              <Icon name="close" size={10} />
            </button>
          </span>
        ))}
        {!full && (
          <span className="chip-input-wrap">
            <input
              id={inputId}
              value={text}
              onChange={(e) => setText(e.target.value)}
              onKeyDown={onKeyDown}
              onBlur={() => setTimeout(() => { setResults([]); setStatus('idle'); }, 150)}
              placeholder={placeholder || p.placeholder}
              disabled={disabled}
              role="combobox"
              aria-autocomplete="list"
              aria-expanded={open}
              aria-controls={open ? listId : undefined}
              aria-activedescendant={open ? listId + '-' + active : undefined}
              aria-describedby={describedBy}
              aria-invalid={Boolean(error)}
              autoComplete="off"
            />
            {open && (
              <ul id={listId} className="suggest-list" role="listbox" aria-label={p.results(label)}>
                {options.map((option, i) => (
                  <li key={option.id} id={listId + '-' + i} role="option" aria-selected={i === active} className={'suggest-option' + (i === active ? ' active' : '')} onMouseDown={(e) => { e.preventDefault(); pick(option); }}>
                    <span>{option.label || option.id}</span>
                    {option.detail && <span className="muted small"> {option.detail}</span>}
                  </li>
                ))}
              </ul>
            )}
          </span>
        )}
      </div>
      <span id={inputId + '-status'} className="muted small" role="status" aria-live="polite">
        {status === 'loading' ? p.searching : status === 'empty' ? p.noResults : status === 'error' ? p.searchFailed : full && multiple ? p.full(max) : ''}
      </span>
      {hint && <span id={inputId + '-hint'} className="muted small">{hint}</span>}
      {error && <span id={inputId + '-error'} className="field-error" role="alert">{error}</span>}
    </div>
  );
}

/** Picker item for a member (admin member row or UserSummary). */
export function memberItem(user) {
  if (!user) return null;
  const name = user.displayName || user.name || user.handle || user.id;
  const detail = user.email || (user.handle ? '@' + user.handle : '');
  return { id: user.id, label: name, detail };
}

/** Picker item for a venue. */
export function venueItem(venue) {
  if (!venue) return null;
  const place = venue.address ? [venue.address.locality, venue.address.region || venue.address.countryCode].filter(Boolean).join(', ') : '';
  return { id: venue.id, label: venue.name || venue.id, detail: place };
}

/** Picker item for an instructor. */
export function instructorItem(instructor) {
  if (!instructor) return null;
  return { id: instructor.id, label: instructor.displayName || instructor.name || instructor.id, detail: instructor.affiliation || '' };
}

/** Picker item for an idea. */
export function ideaItem(idea) {
  if (!idea) return null;
  return { id: idea.id, label: idea.title || idea.id, detail: (idea.status && (idea.status.label || idea.status.key)) || '' };
}

function objectsOf(result) {
  if (!result) return [];
  if (Array.isArray(result.objects)) return result.objects;
  if (Array.isArray(result.items)) return result.items;
  return Array.isArray(result) ? result : [];
}

/**
 * Bounded search functions for the pickers. Members use the admin member enumeration for a
 * SystemAdmin and the member search route for other staff (event managers and directory
 * moderators cannot list members in the console). Venues and instructors use the admin directory
 * routes for a SystemAdmin and directory staff (`directoryStaff`), and the member directory routes for
 * other staff (an event manager picking a venue cannot read the admin directory, which answers 403).
 * @param {object} client the console's ApiClient
 * @param {{ isSystemAdmin?: boolean, directoryStaff?: boolean }} [options]
 */
export function pickerSearches(client, { isSystemAdmin = false, directoryStaff = false } = {}) {
  const adminDirectory = isSystemAdmin || directoryStaff;
  return {
    members: async (term, signal) => {
      const result = isSystemAdmin && client.enumerateAdminMembers
        ? await client.enumerateAdminMembers({ search: term, maxResults: PICKER_MAX_RESULTS }, signal)
        : await client.searchMembers({ search: term, maxResults: PICKER_MAX_RESULTS }, signal);
      return objectsOf(result).map(memberItem).filter(Boolean);
    },
    venues: async (term, signal) => {
      const query = { search: term, maxResults: PICKER_MAX_RESULTS };
      return objectsOf(await (adminDirectory ? client.adminEnumerateVenues(query, signal) : client.enumerateVenues(query, signal))).map(venueItem).filter(Boolean);
    },
    ideas: async (term, signal) => {
      const query = { search: term, maxResults: PICKER_MAX_RESULTS };
      return objectsOf(await (client.adminEnumerateIdeas ? client.adminEnumerateIdeas(query, signal) : client.enumerateIdeas(query, signal))).map(ideaItem).filter(Boolean);
    },
    instructors: async (term, signal) => {
      const query = { search: term, maxResults: PICKER_MAX_RESULTS };
      return objectsOf(await (adminDirectory ? client.adminEnumerateInstructors(query, signal) : client.enumerateInstructors(query, signal))).map(instructorItem).filter(Boolean);
    }
  };
}

export default EntityPicker;
