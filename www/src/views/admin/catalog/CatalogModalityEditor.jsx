import { useId } from 'react';
import { CatalogDescriptorFields, catalogDescriptorOptions, catalogDescriptorProfile } from '@trelliskit/shared';
import strings from '../../../strings.js';

const t = strings.exerciseCatalog;

/** Deliberate replacement preserves incompatible values across program changes for correction. */
export default function CatalogModalityEditor({ value, onChange, program, config }) {
  const fieldId = useId();
  const details = value || { localizedNames: [], familyKey: null };
  const profile = catalogDescriptorProfile(config, program);
  const names = details.localizedNames || [];
  const update = (index, changes) => onChange({ ...details, localizedNames: names.map((name, i) => i === index ? { ...name, ...changes } : name) });
  const fields = CatalogDescriptorFields.filter(({ key, group }) => profile?.[group]?.length || details[key]);
  const hasDetails = names.length || fields.some(({ key }) => details[key]) || details.notationText;
  if (!profile && !hasDetails) return null;
  return <fieldset className="fieldset stack"><legend>{t.descriptors}</legend><p className="muted small">{t.descriptorsHint}</p>
    {fields.map(({ key, group, labelKey }) => <label className="field" key={key}><span className="label-text" id={fieldId + key}>{profile?.[labelKey] || t[key]}</span><select aria-labelledby={fieldId + key} aria-describedby={key === 'levelKey' ? fieldId + 'levelHint' : undefined} value={details[key] || ''} onChange={(event) => onChange({ ...details, [key]: event.target.value || null })}><option value="">{t.notSpecified}</option>{catalogDescriptorOptions(config, program, group, details[key]).map((item) => <option key={item.key} value={item.key}>{item.label}</option>)}</select>{key === 'levelKey' && <span className="muted small" id={fieldId + 'levelHint'}>{t.levelHint}</span>}</label>)}
    {(profile?.notationSystems?.length || details.notationSystemKey || details.notationText) ? <label className="field"><span className="label-text" id={fieldId + 'notationText'}>{t.notationText}</span><textarea aria-labelledby={fieldId + 'notationText'} aria-describedby={fieldId + 'notationHint'} rows={3} maxLength={500} value={details.notationText || ''} onChange={(event) => onChange({ ...details, notationText: event.target.value })} /><span className="muted small" id={fieldId + 'notationHint'}>{t.notationHint}</span></label> : null}
    {names.map((name, index) => <fieldset className="fieldset stack" key={index}><legend>{t.localizedName} {index + 1}</legend>
      <label className="field"><span className="label-text">{t.nameLanguage} {index + 1}</span><select value={name.languageTag} onChange={(event) => update(index, { languageTag: event.target.value })} required><option value="">{t.chooseDescriptor}</option>{catalogDescriptorOptions(config, program, 'nameLanguages', name.languageTag).map((item) => <option key={item.key} value={item.key}>{item.label}</option>)}</select></label>
      <label className="field"><span className="label-text">{t.localizedName} {index + 1}</span><input value={name.text} maxLength={160} required onChange={(event) => update(index, { text: event.target.value })} /></label>
      <button className="button button-secondary" type="button" aria-label={t.remove + ' ' + t.localizedName.toLowerCase() + ' ' + (index + 1)} onClick={() => onChange({ ...details, localizedNames: names.filter((_, i) => i !== index) })}>{t.remove}</button>
    </fieldset>)}
    <div className="form-actions"><button className="button button-secondary" type="button" disabled={names.length >= 10 || !profile?.nameLanguages?.length} onClick={() => onChange({ ...details, localizedNames: [...names, { languageTag: profile.nameLanguages[0].key, text: '' }] })}>{t.addLocalizedName}</button><button className="button button-secondary" type="button" disabled={!hasDetails} onClick={() => onChange({})}>{t.clearDescriptors}</button></div>
  </fieldset>;
}
