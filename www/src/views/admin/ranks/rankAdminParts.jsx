/**
 * Shared pieces of the admin rank, curriculum, and attendance pages: the property's Rank fields,
 * a field selector, accessible tabs, error text, and a member picker search.
 */
import { useCallback, useMemo, useRef } from 'react';
import strings from '../../../strings.js';
import { useAdminProfileFields } from '../adminProfileFields.js';
import { errorText } from '../../../utils/engagement.js';
import { memberItem } from '../../../components/admin/EntityPicker.jsx';

const a = strings.ranks.admin;

/** The property's Rank profile fields (admin definitions, with `rank.awarding`). */
export function useRankFields() {
  const fields = useAdminProfileFields();
  return useMemo(() => fields.filter((f) => f && f.type === 'Rank'), [fields]);
}

/** Error text of a failed admin rank call. */
export function rankAdminError(err) {
  return errorText(err, { ...a.errors, ...strings.ranks.errors }, strings.ranks.errors.generic);
}

/** Bounded member search for EntityPicker (admin members for SystemAdmin, else member search). */
export function useMemberSearch(client, isSystemAdmin) {
  return useCallback(async (term, signal) => {
    const result = isSystemAdmin && client.enumerateAdminMembers
      ? await client.enumerateAdminMembers({ search: term, maxResults: 10 }, signal)
      : await client.searchMembers({ search: term, maxResults: 10 }, signal);
    return ((result && result.objects) || []).map(memberItem).filter(Boolean);
  }, [client, isSystemAdmin]);
}

export function FieldSelect({ fields, value, onChange, id = 'rank-field' }) {
  if (fields.length <= 1) return null;
  return (
    <label className="field" htmlFor={id}>
      <span className="label-text">{a.field}</span>
      <select id={id} value={value} onChange={(e) => onChange(e.target.value)}>
        {fields.map((f) => <option key={f.key} value={f.key}>{f.label || f.key}</option>)}
      </select>
    </label>
  );
}

/** A tablist with arrow-key movement. `tabs` is `[{ id, label }]`. */
export function Tabs({ tabs, value, onChange, label }) {
  const refs = useRef({});
  const move = (event, index) => {
    const delta = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
    if (!delta) return;
    event.preventDefault();
    const next = tabs[(index + delta + tabs.length) % tabs.length];
    onChange(next.id);
    if (refs.current[next.id]) refs.current[next.id].focus();
  };
  return (
    <div role="tablist" aria-label={label} className="segmented tab-bar">
      {tabs.map((tab, index) => (
        <button
          key={tab.id} ref={(el) => { refs.current[tab.id] = el; }} type="button" role="tab" id={'tab-' + tab.id} aria-selected={value === tab.id}
          aria-controls={'panel-' + tab.id} tabIndex={value === tab.id ? 0 : -1} className={'segment' + (value === tab.id ? ' active' : '')}
          onClick={() => onChange(tab.id)} onKeyDown={(e) => move(e, index)}
        >
          {tab.label}
        </button>
      ))}
    </div>
  );
}
