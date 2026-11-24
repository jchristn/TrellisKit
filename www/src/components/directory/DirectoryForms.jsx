import { AddressVisibilities, SocialLinkKinds, VisitorPolicies, directoryConfig, findProfileField, profileFieldsOf, programOptions, rankOptions } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useProperty } from '../../context/PropertyContext.jsx';
import { countryOptions } from '../../utils/countries.js';
import Icon from '../Icon.jsx';

const v = strings.venues;

/** Rank options `{ value, label }` of the profile field named by `directory.instructorRankFieldKey` (legacy rank values as a fallback). */
export function instructorRankOptions(property) {
  const key = directoryConfig(property).instructorRankFieldKey;
  if (!key) return [];
  const field = findProfileField(profileFieldsOf(property), key);
  const options = field && Array.isArray(field.options) ? field.options.filter((o) => o && o.key && !o.isRetired) : [];
  return options.length ? options.map((o) => ({ value: o.key, label: o.label || o.key })) : rankOptions(property);
}

/** Venue form values from a Venue (or blank). */
export function venueFormFrom(venue, { addressVisibility = 'Exact' } = {}) {
  const a = (venue && venue.address) || {};
  return {
    name: (venue && venue.name) || '', affiliation: (venue && venue.affiliation) || '', summary: (venue && venue.summary) || '', description: (venue && venue.description) || '',
    line1: a.line1 || '', line2: a.line2 || '', locality: a.locality || '', region: a.region || '', postalCode: a.postalCode || '', countryCode: a.countryCode || '',
    addressVisibility: (venue && venue.addressVisibility) || addressVisibility, phone: (venue && venue.phone) || '', email: (venue && venue.email) || '',
    websiteUrl: (venue && venue.websiteUrl) || '', scheduleText: (venue && venue.scheduleText) || '',
    programs: ((venue && venue.programs) || []).map((p) => (typeof p === 'object' ? p.key : p)), visitorPolicy: (venue && venue.visitorPolicy) || 'Unknown',
    visitorPolicyNote: (venue && venue.visitorPolicyNote) || '', socialLinks: ((venue && venue.socialLinks) || []).map((l) => ({ kind: l.kind, url: l.url }))
  };
}

/** API venue fields (section 3.7 field names) from the form. */
export function venueFieldsFrom(form) {
  const trim = (x) => String(x || '').trim();
  return {
    name: trim(form.name), affiliation: trim(form.affiliation) || null, summary: trim(form.summary) || null, description: trim(form.description) || null,
    address: { line1: trim(form.line1), line2: trim(form.line2) || null, locality: trim(form.locality), region: trim(form.region), postalCode: trim(form.postalCode), countryCode: form.countryCode },
    addressVisibility: form.addressVisibility, phone: trim(form.phone) || null, email: trim(form.email) || null, websiteUrl: trim(form.websiteUrl) || null,
    scheduleText: trim(form.scheduleText) || null, programs: form.programs.slice(), visitorPolicy: form.visitorPolicy, visitorPolicyNote: trim(form.visitorPolicyNote) || null,
    socialLinks: form.socialLinks.filter((l) => trim(l.url)).map((l) => ({ kind: l.kind, url: trim(l.url) }))
  };
}

/** Instructor form values from an Instructor (or blank). */
export function instructorFormFrom(instructor) {
  return {
    displayName: (instructor && instructor.displayName) || '', bio: (instructor && instructor.bio) || '', affiliation: (instructor && instructor.affiliation) || '',
    rank: (instructor && instructor.rank) || '', rankLevel: (instructor && instructor.rankLevel) || 0, websiteUrl: (instructor && instructor.websiteUrl) || '',
    socialLinks: ((instructor && instructor.socialLinks) || []).map((l) => ({ kind: l.kind, url: l.url }))
  };
}

/** API instructor fields from the form. */
export function instructorFieldsFrom(form, { withRank = false } = {}) {
  const trim = (x) => String(x || '').trim();
  const out = { displayName: trim(form.displayName), bio: trim(form.bio) || null, affiliation: trim(form.affiliation) || null, websiteUrl: trim(form.websiteUrl) || null, socialLinks: form.socialLinks.filter((l) => trim(l.url)).map((l) => ({ kind: l.kind, url: trim(l.url) })) };
  if (withRank) {
    out.rank = form.rank || null;
    out.rankLevel = Number(form.rankLevel) || 0;
  }
  return out;
}

function SocialLinks({ links, onChange, idPrefix, disabled }) {
  const f = v.fields;
  return (
    <fieldset className="fieldset">
      <legend>{f.socialLinks}</legend>
      {links.map((link, i) => (
        <div key={i} className="form-grid">
          <label className="field"><span className="label-text">{f.linkKind}</span>
            <select value={link.kind} onChange={(e) => onChange(links.map((l, j) => (j === i ? { ...l, kind: e.target.value } : l)))} disabled={disabled}>
              {SocialLinkKinds.map((k) => <option key={k} value={k}>{k}</option>)}
            </select>
          </label>
          <label className="field"><span className="label-text">{f.linkUrl}</span>
            <input id={idPrefix + '-link-' + i} type="url" value={link.url} onChange={(e) => onChange(links.map((l, j) => (j === i ? { ...l, url: e.target.value } : l)))} placeholder="https://" disabled={disabled} />
          </label>
          <button type="button" className="icon-button" onClick={() => onChange(links.filter((_, j) => j !== i))} aria-label={f.removeLink} title={f.removeLink} disabled={disabled}><Icon name="trash" size={14} /></button>
        </div>
      ))}
      {links.length < 8 && <button type="button" className="button button-secondary button-sm" onClick={() => onChange([...links, { kind: 'Website', url: '' }])} disabled={disabled}><Icon name="plus" size={14} /><span>{f.addLink}</span></button>}
    </fieldset>
  );
}

function useField(form, set, problems, idPrefix, disabled) {
  const text = (code) => (code ? v.problems[code] || code : '');
  const field = (key, label, input, hint, problemKey = key) => (
    <div className="field">
      <label className="label-text" htmlFor={idPrefix + '-' + key}>{label}</label>
      {input}
      {hint && <span className="muted small">{hint}</span>}
      {problems[problemKey] && <span className="field-error" role="alert">{text(problems[problemKey])}</span>}
    </div>
  );
  const input = (key, props = {}) => <input id={idPrefix + '-' + key} value={form[key]} onChange={(e) => set({ [key]: e.target.value })} disabled={disabled} {...props} />;
  return { field, input };
}

/**
 * Venue fields for member submissions and the admin editor. `showVisibility` shows the address
 * visibility picker (staff); member submissions are exact (resolved decision 7) and say so.
 */
export function VenueFields({ form, set, problems = {}, idPrefix, disabled = false, showVisibility = false }) {
  const { property } = useProperty();
  const { field, input } = useField(form, set, problems, idPrefix, disabled);
  const f = v.fields;
  const programs = programOptions(property);
  return (
    <>
      {field('name', f.name, input('name', { maxLength: 120, required: true }))}
      <div className="form-grid">
        {field('affiliation', f.affiliation, input('affiliation', { maxLength: 120 }))}
        {field('summary', f.summary, input('summary', { maxLength: 300 }))}
      </div>
      {field('description', f.description, <textarea id={idPrefix + '-description'} rows={4} maxLength={5000} value={form.description} onChange={(e) => set({ description: e.target.value })} disabled={disabled} />)}
      <fieldset className="fieldset">
        <legend>{v.address}</legend>
        <div className="form-grid">
          {field('line1', f.line1, input('line1', { maxLength: 200 }))}
          {field('line2', f.line2, input('line2', { maxLength: 200 }))}
          {field('locality', f.locality, input('locality', { maxLength: 100 }), null, 'address.locality')}
          {field('region', f.region, input('region', { maxLength: 100 }))}
          {field('postalCode', f.postalCode, input('postalCode', { maxLength: 20 }))}
          {field('countryCode', f.countryCode, (
            <select id={idPrefix + '-countryCode'} value={form.countryCode} onChange={(e) => set({ countryCode: e.target.value })} disabled={disabled}>
              <option value="">-</option>
              {countryOptions().map((c) => <option key={c.code} value={c.code}>{c.name}</option>)}
            </select>
          ), null, 'address.countryCode')}
        </div>
        {showVisibility ? field('addressVisibility', f.addressVisibility, (
          <select id={idPrefix + '-addressVisibility'} value={form.addressVisibility} onChange={(e) => set({ addressVisibility: e.target.value })} disabled={disabled}>
            {AddressVisibilities.map((x) => <option key={x} value={x}>{v.visibilities[x]}</option>)}
          </select>
        )) : <p className="muted small">{v.addressNote}</p>}
      </fieldset>
      <div className="form-grid">
        {field('phone', f.phone, input('phone', { maxLength: 40, type: 'tel' }))}
        {field('email', f.email, input('email', { maxLength: 200, type: 'email' }))}
        {field('websiteUrl', f.websiteUrl, input('websiteUrl', { maxLength: 500, type: 'url', placeholder: 'https://' }))}
      </div>
      {field('scheduleText', f.scheduleText, <textarea id={idPrefix + '-scheduleText'} rows={3} maxLength={500} value={form.scheduleText} onChange={(e) => set({ scheduleText: e.target.value })} disabled={disabled} />)}
      {programs.length > 0 && (
        <fieldset className="fieldset">
          <legend>{f.programs}</legend>
          <div className="chip-row">
            {programs.map((p) => {
              const on = form.programs.includes(p.key);
              return <button key={p.key} type="button" className={'chip-toggle' + (on ? ' active' : '')} aria-pressed={on} disabled={disabled} onClick={() => set({ programs: on ? form.programs.filter((x) => x !== p.key) : [...form.programs, p.key] })}>{p.label}</button>;
            })}
          </div>
        </fieldset>
      )}
      <div className="form-grid">
        {field('visitorPolicy', f.visitorPolicy, (
          <select id={idPrefix + '-visitorPolicy'} value={form.visitorPolicy} onChange={(e) => set({ visitorPolicy: e.target.value })} disabled={disabled}>
            {VisitorPolicies.map((x) => <option key={x} value={x}>{v.visitorPolicies[x]}</option>)}
          </select>
        ))}
        {field('visitorPolicyNote', f.visitorPolicyNote, input('visitorPolicyNote', { maxLength: 300 }))}
      </div>
      <SocialLinks links={form.socialLinks} onChange={(socialLinks) => set({ socialLinks })} idPrefix={idPrefix} disabled={disabled} />
    </>
  );
}

/** Instructor fields (rank only when `directory.instructorRankFieldKey` is set). */
export function InstructorFields({ form, set, problems = {}, idPrefix, disabled = false, showRank = false }) {
  const { property } = useProperty();
  const { field, input } = useField(form, set, problems, idPrefix, disabled);
  const f = v.fields;
  const ranks = showRank ? instructorRankOptions(property) : [];
  return (
    <>
      {field('displayName', f.displayName, input('displayName', { maxLength: 120, required: true }))}
      {field('affiliation', f.affiliation, input('affiliation', { maxLength: 120 }))}
      {ranks.length > 0 && field('rank', f.rank, (
        <select id={idPrefix + '-rank'} value={form.rank} onChange={(e) => set({ rank: e.target.value })} disabled={disabled}>
          <option value="">-</option>
          {ranks.map((r) => <option key={r.value} value={r.value}>{r.label}</option>)}
        </select>
      ))}
      {field('bio', f.bio, <textarea id={idPrefix + '-bio'} rows={4} maxLength={5000} value={form.bio} onChange={(e) => set({ bio: e.target.value })} disabled={disabled} />)}
      {field('websiteUrl', f.websiteUrl, input('websiteUrl', { maxLength: 500, type: 'url', placeholder: 'https://' }))}
      <SocialLinks links={form.socialLinks} onChange={(socialLinks) => set({ socialLinks })} idPrefix={idPrefix} disabled={disabled} />
    </>
  );
}
