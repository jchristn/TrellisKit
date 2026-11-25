import { useId } from 'react';
import { CatalogApparatusFields, catalogApparatusOptions } from '@trelliskit/shared';
import strings from '../../../strings.js';

const t = strings.exerciseCatalog.apparatus;

/** Retain source-specific setup when selected equipment changes until explicit correction or clearing. */
export default function CatalogApparatusEditor({ value, onChange, equipment, config }) {
  const fieldId = useId();
  const setup = value || {};
  const options = catalogApparatusOptions(config, equipment, setup.apparatusKey);
  const hasSetup = CatalogApparatusFields.some((key) => setup[key]);
  if (!options.length && !hasSetup) return null;
  return <fieldset className="fieldset stack"><legend>{t.title}</legend><p className="muted small">{t.hint}</p>
    <label className="field"><span className="label-text">{t.apparatusKey}</span><select value={setup.apparatusKey || ''} onChange={(event) => onChange({ ...setup, apparatusKey: event.target.value || null })}><option value="">{strings.exerciseCatalog.notSpecified}</option>{options.map((item) => <option key={item.key} value={item.key}>{item.label}</option>)}</select></label>
    {['manufacturer', 'model', 'setupText', 'springSetupText'].map((key) => <label className="field" key={key}><span className="label-text" id={fieldId + key}>{t[key]}</span>{key.endsWith('Text') ? <textarea rows={3} maxLength={500} aria-labelledby={fieldId + key} aria-describedby={key === 'springSetupText' ? fieldId + 'springHint' : undefined} value={setup[key] || ''} onChange={(event) => onChange({ ...setup, [key]: event.target.value })} /> : <input maxLength={120} aria-labelledby={fieldId + key} value={setup[key] || ''} onChange={(event) => onChange({ ...setup, [key]: event.target.value })} />}{key === 'springSetupText' && <span className="muted small" id={fieldId + 'springHint'}>{t.springHint}</span>}</label>)}
    <button className="button button-secondary" type="button" disabled={!hasSetup} onClick={() => onChange({})}>{t.clear}</button>
  </fieldset>;
}
