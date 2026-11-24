import { collectEnumeration } from '@trelliskit/shared';
import { useCallback, useState } from 'react';
import strings from '../../strings.js';
import { usePlatformAuth } from '../../context/PlatformAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';

const d = strings.platform.detail;

function initialDraft(field) {
  if (field.type === 'Secret') return '';
  if (field.type === 'Boolean') return Boolean(field.value);
  return field.value === null || field.value === undefined ? '' : String(field.value);
}

/**
 * The `PUT /platform/properties/{id}/settings` body from a draft: changed non-secret values,
 * secrets only when a new value was typed (secrets are write-only), and the keys to clear.
 * @param {Array<object>} fields the GET body's fields
 * @param {Record<string, any>} drafts
 * @param {string[]} clear
 * @returns {{ values: Record<string, any>, clear: string[] }}
 */
export function buildIntegrationUpdate(fields, drafts, clear) {
  const values = {};
  for (const field of fields) {
    if (clear.includes(field.key)) continue;
    const draft = drafts[field.key];
    if (field.type === 'Secret') {
      if (typeof draft === 'string' && draft !== '') values[field.key] = draft;
      continue;
    }
    if (draft === initialDraft(field)) continue;
    if (field.type === 'Integer') values[field.key] = draft === '' ? null : Number(draft);
    else values[field.key] = draft;
  }
  return { values, clear: [...clear] };
}

function FieldControl({ field, id, value, onChange, disabled }) {
  if (field.type === 'Boolean') {
    return (
      <label className="toggle">
        <input id={id} type="checkbox" role="switch" checked={Boolean(value)} onChange={(e) => onChange(e.target.checked)} disabled={disabled} />
        <span className="toggle-track" aria-hidden="true"><span className="toggle-thumb" /></span>
        <span>{value ? d.enabledValue : d.disabledValue}</span>
      </label>
    );
  }
  if (field.type === 'Enum' || (Array.isArray(field.options) && field.options.length)) {
    const options = field.options || [];
    return (
      <select id={id} className="select-field" value={value} onChange={(e) => onChange(e.target.value)} disabled={disabled}>
        {!options.includes(value) && <option value={value}>{value}</option>}
        {options.map((o) => <option key={o} value={o}>{o}</option>)}
      </select>
    );
  }
  if (field.type === 'Secret') {
    return <input id={id} type="password" autoComplete="new-password" value={value} onChange={(e) => onChange(e.target.value)} placeholder={field.isSet ? d.secretReplace : d.secretEnter} disabled={disabled} />;
  }
  if (field.type === 'Integer') {
    return <input id={id} type="number" inputMode="numeric" step="1" className="input-number" min={typeof field.min === 'number' ? field.min : undefined} max={typeof field.max === 'number' ? field.max : undefined} value={value} onChange={(e) => onChange(e.target.value)} disabled={disabled} />;
  }
  return <input id={id} type="text" value={value} onChange={(e) => onChange(e.target.value)} disabled={disabled} />;
}

function IntegrationsForm({ record, data, onSaved }) {
  const { call } = usePlatformAuth();
  const toast = useToast();
  const fields = (data && data.fields) || [];
  const [drafts, setDrafts] = useState(() => Object.fromEntries(fields.map((f) => [f.key, initialDraft(f)])));
  const [clear, setClear] = useState([]);
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const save = async (ev) => {
    ev.preventDefault();
    const body = buildIntegrationUpdate(fields, drafts, clear);
    if (!Object.keys(body.values).length && !body.clear.length) {
      toast.info(d.integrationsNoChanges);
      return;
    }
    setBusy(true);
    setError('');
    setErrors({});
    try {
      const next = await call((client) => client.updatePlatformPropertySettings(record.id, body));
      toast.success(d.integrationsSaved);
      onSaved(next);
    } catch (err) {
      const fe = getFieldErrors(err);
      setErrors(Object.fromEntries(Object.entries(fe).map(([k, v]) => [k.replace(/^values\./, ''), v])));
      setError(errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };

  if (!fields.length) return <p className="muted">{d.integrationsEmpty}</p>;

  const sections = [];
  for (const field of fields) {
    let section = sections.find((s) => s.name === (field.section || ''));
    if (!section) {
      section = { name: field.section || '', fields: [] };
      sections.push(section);
    }
    section.fields.push(field);
  }

  return (
    <form className="settings-form" onSubmit={save} noValidate>
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      {sections.map((section) => (
        <fieldset key={section.name} className="settings-section" disabled={busy}>
          {section.name && <legend><span>{section.name}</span></legend>}
          {section.fields.map((field) => {
            const id = 'integration-' + field.key.replace(/[^A-Za-z0-9_-]/g, '-');
            const clearing = clear.includes(field.key);
            const hasValue = field.type === 'Secret' ? Boolean(field.isSet) : (field.value !== null && field.value !== undefined && field.value !== '');
            return (
              <div key={field.key} className={'setting-row' + (errors[field.key] ? ' has-error' : '')} data-testid={'integration-' + field.key}>
                <div className="setting-label">
                  <label htmlFor={id} className="setting-name">{field.label || field.key}</label>
                  <div className="setting-badges">
                    {field.type === 'Secret' && <Badge tone={field.isSet ? 'success' : 'neutral'}>{field.isSet ? d.secretSet : d.secretNotSet}</Badge>}
                    {field.inherited && <Badge tone="info">{d.inherited}</Badge>}
                    {clearing && <Badge tone="warning">{d.willClear}</Badge>}
                  </div>
                  <code className="mono muted small setting-key">{field.key}</code>
                </div>
                <div className="setting-control">
                  <div className="input-with-button">
                    <FieldControl field={field} id={id} value={drafts[field.key]} onChange={(v) => setDrafts((x) => ({ ...x, [field.key]: v }))} disabled={busy || clearing} />
                    {hasValue && field.type !== 'Boolean' && (
                      <button type="button" className="button button-secondary button-sm" onClick={() => setClear((list) => (clearing ? list.filter((k) => k !== field.key) : [...list, field.key]))} aria-label={(clearing ? d.undoClear : d.clearValue) + ' ' + (field.label || field.key)}>
                        <Icon name={clearing ? 'refresh' : 'close'} size={12} /><span>{clearing ? d.undoClear : d.clearValue}</span>
                      </button>
                    )}
                  </div>
                  {field.description && <p className="setting-help muted small">{field.description}</p>}
                  {errors[field.key] && <p className="field-error" role="alert">{errors[field.key]}</p>}
                </div>
              </div>
            );
          })}
        </fieldset>
      ))}
      <div className="form-actions">
        <button type="submit" className="button button-primary" disabled={busy}>{busy ? d.integrationsSaving : d.integrationsSave}</button>
      </div>
    </form>
  );
}

/** A property's integration settings, read and written by a platform administrator (secrets write-only). */
function PropertyIntegrationsPanel({ record }) {
  const { call } = usePlatformAuth();
  const fetcher = useCallback((signal) => call(async (client) => { const page = await collectEnumeration((q, sig) => client.getPlatformPropertySettings(record.id, q, sig), {}, { signal }); return { ...page, fields: page.objects }; }), [call, record.id]);
  const query = useApiQuery(fetcher);
  const [saved, setSaved] = useState(null);
  const data = saved && (!query.loadedAt || saved.at >= query.loadedAt.getTime()) ? saved.data : query.data;
  return (
    <section className="panel" aria-labelledby="integrations-title">
      <div className="panel-header"><h2 id="integrations-title">{d.integrationsTitle}</h2></div>
      <p className="muted small">{d.integrationsSummary}</p>
      {query.loading && !data && <Loading />}
      {query.error && !data && <ErrorBanner message={d.integrationsLoadError + ' ' + errorMessage(query.error, '')} onRetry={query.reload} />}
      {data && <IntegrationsForm key={saved ? saved.at : 'loaded'} record={record} data={data} onSaved={(next) => setSaved({ data: next, at: Date.now() })} />}
    </section>
  );
}

export default PropertyIntegrationsPanel;
