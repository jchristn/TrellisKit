import { useState } from 'react';
import { audienceChoices, audiencesWithin, composerAudiences } from '@trelliskit/shared';
import strings from '../strings.js';
import { useProperty } from '../context/PropertyContext.jsx';

const s = strings.access;

/**
 * Audience picker state for a composer (ACCESS_MODELS_CONTRACT.md section 8.9): the options the
 * access model allows, defaulting to `access.defaultAudience` (`Members` in a gated feature). An
 * existing object's audience is kept even when the current model no longer offers it.
 *
 * `floor` limits the options to audiences at least as narrow (a thread inside its forum).
 *
 * @param {{ feature?: string|null, isSystemAdmin?: boolean, current?: string|null, floor?: string|null }} options
 * @returns {{ options: string[], value: string, setValue: (value: string) => void, visible: boolean }}
 */
export function useAudiencePicker({ feature = null, isSystemAdmin = false, current = null, floor = null } = {}) {
  const { property } = useProperty();
  const config = composerAudiences(property, { isSystemAdmin, feature });
  const options = audienceChoices(audiencesWithin(config.options, floor), current);
  const [value, setValue] = useState(() => (current && options.includes(current) ? current : (options.includes(config.defaultAudience) ? config.defaultAudience : options[0])));
  return { options, value, setValue, visible: options.length > 1 };
}

/**
 * The labeled audience select with a one-line hint for the chosen audience.
 * @param {{ id: string, picker: ReturnType<typeof useAudiencePicker>, disabled?: boolean, label?: string }} props
 */
function AudienceField({ id, picker, disabled = false, label = s.audienceLabel }) {
  const { fill } = useProperty();
  return (
    <div className="field field-compact audience-field">
      <label className="label-text" htmlFor={id}>{label}</label>
      <select id={id} value={picker.value} onChange={(e) => picker.setValue(e.target.value)} disabled={disabled} aria-describedby={id + '-hint'}>
        {picker.options.map((a) => <option key={a} value={a}>{s.audiences[a] || a}</option>)}
      </select>
      <span id={id + '-hint'} className="muted small">{fill(s.audienceHints[picker.value] || '')}</span>
    </div>
  );
}

export default AudienceField;
