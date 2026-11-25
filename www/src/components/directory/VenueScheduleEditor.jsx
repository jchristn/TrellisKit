import { emptyVenueSchedule } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useDirectoryTerms } from '../../utils/phase4.js';

/** Structured schedule editor shared by staff and member submissions. */
export default function VenueScheduleEditor({ value, onChange, legacyText = '', disabled = false, invalid = false }) {
  const t = strings.venues.scheduleEditor;
  const { d } = useDirectoryTerms();
  if (!value) return <button type="button" className="button button-secondary" disabled={disabled} onClick={() => onChange({ ...emptyVenueSchedule(), notes: legacyText })}>{t.enable}</button>;
  const set = patch => onChange({ ...value, ...patch });
  const groups = [['openingHours', t.hours, t.addHours, 28], ['sessions', d(t.sessions), d(t.addSession), 100]];
  return <fieldset className="fieldset" disabled={disabled}>
    <legend>{t.title}</legend>
    <p className="muted small">{d(t.hint)}</p>
    <label className="field"><span>{t.timeZone}</span><input aria-label={t.timeZone} value={value.timeZone || ''} placeholder="Pacific/Honolulu" onChange={e => set({ timeZone: e.target.value })} /><span className="muted small">{t.timeZoneHint}</span></label>
    {groups.map(([kind, label, add, limit]) => <fieldset className="fieldset" key={kind}>
      <legend>{label}</legend>
      {(value[kind] || []).map((row, i) => {
        const change = patch => set({ [kind]: value[kind].map((r, j) => i === j ? { ...r, ...patch } : r) });
        return <div key={i} className="form-grid">
          <label className="field"><span>{t.day}</span><select value={row.dayOfWeek} onChange={e => change({ dayOfWeek: Number(e.target.value) })}>{strings.venues.scheduleDays.map((day, d) => <option value={d} key={d}>{day}</option>)}</select></label>
          <label className="field"><span>{t.start}</span><input type="time" required value={row.startTime} onChange={e => change({ startTime: e.target.value })} /></label>
          <label className="field"><span>{t.end}</span><input type="time" required value={row.endTime} onChange={e => change({ endTime: e.target.value })} /></label>
          <label className="check-field"><input type="checkbox" checked={row.endsNextDay} onChange={e => change({ endsNextDay: e.target.checked })} /><span>{t.nextDay}</span></label>
          {kind === 'sessions' && <>
            <label className="field"><span>{d(t.name)}</span><input required maxLength={120} value={row.name || ''} onChange={e => change({ name: e.target.value })} /></label>
            <label className="field"><span>{d(t.instructor)}</span><input maxLength={120} value={row.instructor || ''} onChange={e => change({ instructor: e.target.value })} /></label>
          </>}
          <button type="button" className="button button-secondary" onClick={() => set({ [kind]: value[kind].filter((_, j) => j !== i) })}>{t.removeEntry}</button>
        </div>;
      })}
      {(value[kind] || []).length < limit && <button type="button" className="button button-secondary" onClick={() => set({ [kind]: [...(value[kind] || []), { dayOfWeek: 0, startTime: '', endTime: '', endsNextDay: false, ...(kind === 'sessions' ? { name: '', instructor: '' } : {}) }] })}>{add}</button>}
    </fieldset>)}
    <label className="field"><span>{t.notes}</span><textarea maxLength={1000} value={value.notes || ''} onChange={e => set({ notes: e.target.value })} /></label>
    {invalid && <p className="field-error" role="alert">{d(t.invalid)}</p>}
    <button type="button" className="button button-secondary" onClick={() => onChange(null)}>{t.remove}</button>
  </fieldset>;
}
