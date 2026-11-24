import { POLL_LIMITS, PollResultsVisibilities, PollVoterVisibilities } from '@trelliskit/shared';
import strings from '../../strings.js';
import Icon from '../Icon.jsx';
import { deviceTimeZone } from '../../utils/engagement.js';
import { isoToLocalInput, localInputToIso } from '../../utils/time.js';

const p = strings.forums.poll;

/**
 * Poll builder for a Poll thread (PHASE3_CONTRACT.md section 6.1 with resolved decisions 4 and
 * 5): 2 to `polls.maxOptions` options (add, remove, reorder), single or multiple choice with an
 * optional maximum, an optional close time in the member's local time (sent as UTC), when
 * results show, whether votes can change, and whether voters are visible. Controlled.
 *
 * `locked` (section 6.4: the poll received its first vote) keeps every control read-only except
 * the close time, which may move later or earlier but not into the past.
 *
 * @param {{ value: object, onChange: (draft: object) => void, problems?: Record<string,string>, disabled?: boolean, idPrefix?: string, maxOptions?: number, locked?: boolean }} props
 */
function PollBuilder({ value, onChange, problems = {}, disabled = false, idPrefix = 'poll', maxOptions = POLL_LIMITS.maxOptions, locked = false }) {
  const options = value.options || [];
  const set = (changes) => onChange({ ...value, ...changes });
  const setOption = (i, label) => set({ options: options.map((o, j) => (j === i ? { ...o, label } : o)) });
  const move = (i, delta) => {
    const j = i + delta;
    if (j < 0 || j >= options.length) return;
    const next = options.slice();
    [next[i], next[j]] = [next[j], next[i]];
    set({ options: next });
  };
  return (
    <fieldset className="poll-builder" disabled={disabled}>
      <legend className="label-text">{p.builderTitle}</legend>
      {locked && <p className="muted small" role="note">{p.lockedNote}</p>}
      <fieldset className="poll-builder-locked" disabled={locked}>
      <ol className="poll-builder-options">
        {options.map((o, i) => (
          <li key={i} className="poll-builder-option">
            <label className="visually-hidden" htmlFor={idPrefix + '-option-' + i}>{p.option(i + 1)}</label>
            <input id={idPrefix + '-option-' + i} value={o.label} onChange={(e) => setOption(i, e.target.value)} maxLength={POLL_LIMITS.optionMax + 10} placeholder={p.option(i + 1)} />
            <button type="button" className="icon-button" onClick={() => move(i, -1)} disabled={i === 0} aria-label={p.moveUp(i + 1)} title={p.moveUp(i + 1)}><Icon name="chevronUp" size={14} /></button>
            <button type="button" className="icon-button" onClick={() => move(i, 1)} disabled={i === options.length - 1} aria-label={p.moveDown(i + 1)} title={p.moveDown(i + 1)}><Icon name="chevronDown" size={14} /></button>
            <button type="button" className="icon-button" onClick={() => set({ options: options.filter((_, j) => j !== i) })} disabled={options.length <= POLL_LIMITS.minOptions} aria-label={p.removeOption(i + 1)} title={p.removeOption(i + 1)}><Icon name="trash" size={14} /></button>
          </li>
        ))}
      </ol>
      {problems.options && <span className="field-error" role="alert">{p.problems[problems.options] || problems.options}</span>}
      <button type="button" className="button button-secondary button-sm" onClick={() => set({ options: options.concat({ label: '' }) })} disabled={options.length >= maxOptions}><Icon name="plus" size={14} /><span>{p.addOption}</span></button>
      </fieldset>
      <div className="composer-options">
        <label className="toggle">
          <input type="checkbox" disabled={locked} checked={Boolean(value.multipleChoice)} onChange={(e) => set({ multipleChoice: e.target.checked, maxChoices: e.target.checked ? value.maxChoices : null })} />
          <span className="toggle-track"><span className="toggle-thumb" /></span>
          <span>{p.multipleChoice}</span>
        </label>
        {value.multipleChoice && (
          <div className="field field-compact">
            <label className="label-text" htmlFor={idPrefix + '-max'}>{p.maxChoices}</label>
            <input id={idPrefix + '-max'} disabled={locked} type="number" min={2} max={options.length} value={value.maxChoices || ''} onChange={(e) => set({ maxChoices: e.target.value ? Number(e.target.value) : null })} aria-invalid={Boolean(problems.maxChoices)} />
            {problems.maxChoices && <span className="field-error" role="alert">{p.problems[problems.maxChoices]}</span>}
          </div>
        )}
        <div className="field field-compact">
          <label className="label-text" htmlFor={idPrefix + '-closes'}>{p.closesAt}</label>
          <input id={idPrefix + '-closes'} type="datetime-local" value={value.closesUtc ? isoToLocalInput(value.closesUtc) : ''} onChange={(e) => set({ closesUtc: e.target.value ? localInputToIso(e.target.value) : null })} aria-describedby={idPrefix + '-closes-hint'} aria-invalid={Boolean(problems.closesUtc)} />
          <span id={idPrefix + '-closes-hint'} className="muted small">{p.closesHint(deviceTimeZone())}</span>
          {problems.closesUtc && <span className="field-error" role="alert">{p.problems[problems.closesUtc]}</span>}
        </div>
        <div className="field field-compact">
          <label className="label-text" htmlFor={idPrefix + '-results'}>{p.resultsVisibility}</label>
          <select id={idPrefix + '-results'} disabled={locked} value={value.resultsVisibility || 'AfterVote'} onChange={(e) => set({ resultsVisibility: e.target.value })}>
            {PollResultsVisibilities.map((v) => <option key={v} value={v}>{p.resultsOptions[v]}</option>)}
          </select>
        </div>
        <div className="field field-compact">
          <label className="label-text" htmlFor={idPrefix + '-voters'}>{p.voterVisibility}</label>
          <select id={idPrefix + '-voters'} disabled={locked} value={value.voterVisibility || 'Anonymous'} onChange={(e) => set({ voterVisibility: e.target.value })}>
            {PollVoterVisibilities.map((v) => <option key={v} value={v}>{p.voterOptions[v]}</option>)}
          </select>
        </div>
        <label className="toggle">
          <input type="checkbox" disabled={locked} checked={value.allowVoteChange !== false} onChange={(e) => set({ allowVoteChange: e.target.checked })} />
          <span className="toggle-track"><span className="toggle-thumb" /></span>
          <span>{p.allowVoteChange}</span>
        </label>
      </div>
    </fieldset>
  );
}

export default PollBuilder;
