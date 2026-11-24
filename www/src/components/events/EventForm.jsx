import { EventFormats, eventCategories, localInputValue } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useProperty } from '../../context/PropertyContext.jsx';
import { deviceTimeZone, timeZoneOptions } from '../../utils/engagement.js';
import { countryOptions } from '../../utils/countries.js';

const t = strings.events;

/** A blank event form (member proposals and the admin editor share it). */
export function emptyEventForm(zone = deviceTimeZone()) {
  return {
    title: '', summary: '', description: '', category: '', format: 'InPerson', startLocal: '', endLocal: '', timeZone: zone, isAllDay: false,
    venueId: '', locationName: '', line1: '', line2: '', locality: '', region: '', postalCode: '', countryCode: '', addressVisibility: 'Exact',
    platformLabel: '', joinUrl: '', capacity: '', externalUrl: ''
  };
}

/** The form for an existing Event (local wall times in the event's zone). */
export function eventFormFrom(event) {
  if (!event) return emptyEventForm();
  const zone = event.timeZone || deviceTimeZone();
  const loc = event.location || {};
  const address = loc.address || {};
  const toLocal = (utc) => (event.isAllDay ? localInputValue(utc, zone).slice(0, 10) : localInputValue(utc, zone));
  return {
    title: event.title || '', summary: event.summary || '', description: event.description || '', category: (event.category && event.category.key) || '', format: event.format || 'InPerson',
    startLocal: event.startUtc ? toLocal(event.startUtc) : '', endLocal: event.endUtc ? toLocal(event.endUtc) : '', timeZone: zone, isAllDay: Boolean(event.isAllDay),
    venueId: (loc.venue && loc.venue.id) || '', locationName: loc.name || '', line1: address.line1 || '', line2: address.line2 || '', locality: address.locality || '', region: address.region || '',
    postalCode: address.postalCode || '', countryCode: address.countryCode || '', addressVisibility: loc.addressVisibility || 'Exact',
    platformLabel: (event.online && event.online.platformLabel) || '', joinUrl: (event.online && event.online.joinUrl) || '',
    capacity: event.capacity === null || event.capacity === undefined ? '' : String(event.capacity), externalUrl: event.externalUrl || ''
  };
}

/** The shape `validateEventForm` checks (shared helper input). */
export function validationShape(form, extra = {}) {
  const inPerson = form.format !== 'Online';
  return {
    ...form,
    location: inPerson ? { venueId: form.venueId || null, name: form.locationName, address: { line1: form.line1, locality: form.locality, countryCode: form.countryCode } } : null,
    online: form.format !== 'InPerson' ? { platformLabel: form.platformLabel, joinUrl: form.joinUrl } : null,
    ...extra
  };
}

/**
 * The create or edit body (section 4.7): local wall times plus the zone, location by venue or by
 * name and address, online details, and capacity (empty means unlimited).
 */
export function eventBodyFrom(form) {
  const body = {
    title: form.title.trim(), summary: form.summary.trim(), description: form.description.trim(), category: form.category || null, format: form.format,
    startLocal: form.startLocal, endLocal: form.endLocal, timeZone: form.timeZone, isAllDay: Boolean(form.isAllDay),
    capacity: String(form.capacity).trim() === '' ? null : Number(form.capacity), externalUrl: form.externalUrl.trim() || null
  };
  if (form.isAllDay) {
    body.startLocal = String(form.startLocal).slice(0, 10);
    body.endLocal = String(form.endLocal).slice(0, 10);
  }
  if (form.format !== 'Online') {
    body.location = form.venueId
      ? { venueId: form.venueId }
      : {
        name: form.locationName.trim() || null,
        address: form.line1 || form.locality ? { line1: form.line1.trim(), line2: form.line2.trim() || null, locality: form.locality.trim(), region: form.region.trim(), postalCode: form.postalCode.trim(), countryCode: form.countryCode } : null,
        addressVisibility: form.addressVisibility || 'Exact'
      };
  } else body.location = null;
  body.online = form.format !== 'InPerson' ? { platformLabel: form.platformLabel.trim() || null, joinUrl: form.joinUrl.trim() || null } : null;
  return body;
}

/**
 * The shared event fields. `venueField` (admin) replaces the free location with a venue picker
 * when given. `problems` maps field keys to codes from `validateEventForm`.
 * @param {{ form: object, set: (changes: object) => void, problems?: object, idPrefix: string, disabled?: boolean, venueField?: React.ReactNode }} props
 */
function EventFormFields({ form, set, problems = {}, idPrefix, disabled = false, venueField = null }) {
  const { property } = useProperty();
  const categories = eventCategories(property);
  const f = t.form;
  const text = (code) => (code ? t.problems[code] || code : '');
  // Server field errors name address parts `location.address.<key>`; show them on the matching input.
  const problemFor = (key, problemKey) => problems[problemKey] || problems['location.address.' + key] || problems['location.' + key];
  const field = (key, label, input, hint, problemKey = key) => (
    <div className="field">
      <label className="label-text" htmlFor={idPrefix + '-' + key}>{label}</label>
      {input}
      {hint && <span className="muted small">{hint}</span>}
      {problemFor(key, problemKey) && <span className="field-error" role="alert">{text(problemFor(key, problemKey))}</span>}
    </div>
  );
  const input = (key, props = {}) => <input id={idPrefix + '-' + key} value={form[key]} onChange={(e) => set({ [key]: e.target.value })} disabled={disabled} {...props} />;
  const dateType = form.isAllDay ? 'date' : 'datetime-local';

  return (
    <>
      {field('title', f.title, input('title', { maxLength: 140, required: true }))}
      {field('summary', f.summary, input('summary', { maxLength: 300 }))}
      {field('description', f.description, <textarea id={idPrefix + '-description'} rows={5} value={form.description} onChange={(e) => set({ description: e.target.value })} disabled={disabled} maxLength={10000} />)}
      <div className="form-grid">
        {categories.length > 0 && field('category', f.category, (
          <select id={idPrefix + '-category'} value={form.category} onChange={(e) => set({ category: e.target.value })} disabled={disabled}>
            <option value="">{f.noCategory}</option>
            {categories.map((c) => <option key={c.key} value={c.key}>{c.label}</option>)}
          </select>
        ))}
        {field('format', f.format, (
          <select id={idPrefix + '-format'} value={form.format} onChange={(e) => set({ format: e.target.value })} disabled={disabled}>
            {EventFormats.map((x) => <option key={x} value={x}>{t.formats[x]}</option>)}
          </select>
        ))}
      </div>
      <label className="check-field">
        <input type="checkbox" checked={form.isAllDay} onChange={(e) => set({ isAllDay: e.target.checked, startLocal: String(form.startLocal).slice(0, e.target.checked ? 10 : 16), endLocal: String(form.endLocal).slice(0, e.target.checked ? 10 : 16) })} disabled={disabled} />
        <span>{f.allDay}</span>
      </label>
      <div className="form-grid">
        {field('startLocal', f.start, input('startLocal', { type: dateType, required: true }))}
        {field('endLocal', f.end, input('endLocal', { type: dateType, required: true }))}
        {field('timeZone', f.timeZone, (
          <select id={idPrefix + '-timeZone'} value={form.timeZone} onChange={(e) => set({ timeZone: e.target.value })} disabled={disabled}>
            {timeZoneOptions(form.timeZone).map((z) => <option key={z} value={z}>{z}</option>)}
          </select>
        ))}
      </div>
      {form.format !== 'Online' && (
        <fieldset className="fieldset">
          <legend>{t.location}</legend>
          {venueField}
          {!form.venueId && (
            <>
              {field('locationName', f.locationName, input('locationName', { maxLength: 120 }), null, 'location')}
              <div className="form-grid">
                {field('line1', f.line1, input('line1', { maxLength: 200 }))}
                {field('line2', f.line2, input('line2', { maxLength: 200 }))}
                {field('locality', f.locality, input('locality', { maxLength: 100 }))}
                {field('region', f.region, input('region', { maxLength: 100 }))}
                {field('postalCode', f.postalCode, input('postalCode', { maxLength: 20 }))}
                {field('countryCode', f.countryCode, (
                  <select id={idPrefix + '-countryCode'} value={form.countryCode} onChange={(e) => set({ countryCode: e.target.value })} disabled={disabled}>
                    <option value="">-</option>
                    {countryOptions().map((c) => <option key={c.code} value={c.code}>{c.name}</option>)}
                  </select>
                ))}
              </div>
            </>
          )}
        </fieldset>
      )}
      {form.format !== 'InPerson' && (
        <fieldset className="fieldset">
          <legend>{t.online}</legend>
          <div className="form-grid">
            {field('platformLabel', f.platformLabel, input('platformLabel', { maxLength: 80 }), null, 'online')}
            {field('joinUrl', f.joinUrl, input('joinUrl', { type: 'url', maxLength: 500, placeholder: 'https://' }), null, 'online.joinUrl')}
          </div>
        </fieldset>
      )}
      <div className="form-grid">
        {field('capacity', f.capacity, input('capacity', { type: 'number', min: 1, max: 100000 }), f.capacityHint)}
        {field('externalUrl', f.externalUrl, input('externalUrl', { type: 'url', maxLength: 500, placeholder: 'https://' }))}
      </div>
    </>
  );
}

export default EventFormFields;
