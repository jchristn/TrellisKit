import { RecurrenceFrequency, Weekdays, buildRecurrenceRule, recurrenceSummary, validateRecurrenceForm } from '@trelliskit/shared';
import strings from '../../strings.js';

const r = strings.admin.eventsAdmin.recurrence;

/**
 * The recurrence editor (resolved decision 11): repeat on or off, frequency, interval, weekdays
 * (weekly), by date or by position ("second Tuesday", "last Friday") for monthly, and ends never,
 * after N times, or on a date, with a live plain English summary.
 * @param {{ form: object, onChange: (form: object) => void, startLocal: string, idPrefix?: string, disabled?: boolean }} props
 */
function RecurrenceEditor({ form, onChange, startLocal, idPrefix = 'rec', disabled = false }) {
  const set = (changes) => onChange({ ...form, ...changes });
  const problems = validateRecurrenceForm(form, startLocal);
  const summary = recurrenceSummary(buildRecurrenceRule(form), { startLocal });
  return (
    <fieldset className="p4-recurrence" aria-label={r.title}>
      <label className="check-field">
        <input type="checkbox" checked={Boolean(form.enabled)} onChange={(e) => set({ enabled: e.target.checked })} disabled={disabled} />
        <span>{r.repeat}</span>
      </label>
      {form.enabled && (
        <>
          <div className="form-grid">
            <label className="field"><span className="label-text">{r.every}</span>
              <input id={idPrefix + '-interval'} type="number" min={1} max={99} value={form.interval} onChange={(e) => set({ interval: e.target.value })} disabled={disabled} aria-invalid={Boolean(problems.interval)} />
            </label>
            <label className="field"><span className="label-text">{r.frequency}</span>
              <select id={idPrefix + '-frequency'} value={form.frequency} onChange={(e) => set({ frequency: e.target.value })} disabled={disabled}>
                {Object.values(RecurrenceFrequency).map((f) => <option key={f} value={f}>{r.frequencies[f]}</option>)}
              </select>
            </label>
          </div>
          {form.frequency === RecurrenceFrequency.Weekly && (
            <div className="p4-weekdays" role="group" aria-label={r.onDays}>
              {Weekdays.map((d) => {
                const on = (form.weekdays || []).includes(d);
                return <button key={d} type="button" className={'chip-toggle' + (on ? ' active' : '')} aria-pressed={on} disabled={disabled} onClick={() => set({ weekdays: on ? form.weekdays.filter((x) => x !== d) : [...(form.weekdays || []), d] })}>{r.weekdays[d]}</button>;
              })}
            </div>
          )}
          {form.frequency === RecurrenceFrequency.Monthly && (
            <div className="stack">
              <label className="check-field"><input type="radio" name={idPrefix + '-monthly'} checked={form.monthlyMode !== 'Position'} onChange={() => set({ monthlyMode: 'Date' })} disabled={disabled} /><span>{r.byDate}</span></label>
              {form.monthlyMode !== 'Position' && (
                <select aria-label={r.monthDay} value={form.monthDay} onChange={(e) => set({ monthDay: Number(e.target.value) })} disabled={disabled}>
                  {Array.from({ length: 31 }, (_, i) => i + 1).map((n) => <option key={n} value={n}>{r.dayN(n)}</option>)}
                  <option value={-1}>{r.lastDay}</option>
                </select>
              )}
              <label className="check-field"><input type="radio" name={idPrefix + '-monthly'} checked={form.monthlyMode === 'Position'} onChange={() => set({ monthlyMode: 'Position' })} disabled={disabled} /><span>{r.byPosition}</span></label>
              {form.monthlyMode === 'Position' && (
                <span className="badge-row">
                  <select aria-label={r.position} value={form.setPosition} onChange={(e) => set({ setPosition: Number(e.target.value) })} disabled={disabled}>
                    {[1, 2, 3, 4, -1].map((n) => <option key={n} value={n}>{r.positions[String(n)]}</option>)}
                  </select>
                  <select aria-label={r.weekday} value={form.positionWeekday} onChange={(e) => set({ positionWeekday: e.target.value })} disabled={disabled}>
                    {Weekdays.map((d) => <option key={d} value={d}>{r.weekdayNames[d]}</option>)}
                  </select>
                </span>
              )}
            </div>
          )}
          <div className="stack">
            <span className="label-text">{r.ends}</span>
            <label className="check-field"><input type="radio" name={idPrefix + '-ends'} checked={form.ends === 'Never'} onChange={() => set({ ends: 'Never' })} disabled={disabled} /><span>{r.never}</span></label>
            <label className="check-field"><input type="radio" name={idPrefix + '-ends'} checked={form.ends === 'After'} onChange={() => set({ ends: 'After' })} disabled={disabled} /><span>{r.after}</span>
              {form.ends === 'After' && <input aria-label={r.count} type="number" min={1} max={200} className="input-compact input-number" value={form.count} onChange={(e) => set({ count: e.target.value })} disabled={disabled} />}
              {form.ends === 'After' && <span>{r.times}</span>}
            </label>
            <label className="check-field"><input type="radio" name={idPrefix + '-ends'} checked={form.ends === 'On'} onChange={() => set({ ends: 'On' })} disabled={disabled} /><span>{r.on}</span>
              {form.ends === 'On' && <input aria-label={r.untilDate} type="date" className="input-compact" value={form.untilDate} onChange={(e) => set({ untilDate: e.target.value })} disabled={disabled} />}
            </label>
          </div>
          {Object.values(problems).map((code) => <span key={code} className="field-error" role="alert">{r.problems[code] || code}</span>)}
          <p className="p4-summary" aria-live="polite" data-testid="recurrence-summary">{summary}</p>
          <p className="muted small">{r.note}</p>
        </>
      )}
    </fieldset>
  );
}

export default RecurrenceEditor;
