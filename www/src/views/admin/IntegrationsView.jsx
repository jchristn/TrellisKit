import { useCallback, useEffect, useMemo, useState } from 'react';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import { groupFields } from '../../utils/settings.js';
import PageHeader from '../../components/PageHeader.jsx';
import PasswordInput from '../../components/PasswordInput.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';

const s = strings.admin.integrations;

/** Whether a property setting is write-only (a secret: never returned, only `isSet`). */
export function isSecretField(field) {
  return field.type === 'Secret' || (field.isSet !== null && field.isSet !== undefined && (field.value === null || field.value === undefined) && field.type !== 'Boolean');
}

function initialValue(field) {
  if (isSecretField(field)) return '';
  if (field.type === 'Boolean') return Boolean(field.value);
  return field.value === null || field.value === undefined ? '' : String(field.value);
}

/** Draft state for every field: `value` (what the input shows), `replace` (secret input shown), `clear`. */
export function initialDrafts(fields) {
  const drafts = {};
  for (const field of fields || []) drafts[field.key] = { value: initialValue(field), replace: false, clear: false };
  return drafts;
}

/**
 * The `PUT /admin/property/settings` body: changed keys only in `values` (a secret only when a new
 * value was typed; an emptied non-secret is null, which clears it), and `clear` for secrets the
 * administrator chose to clear.
 */
export function buildSettingsUpdate(fields, drafts) {
  const values = {};
  const clear = [];
  for (const field of fields || []) {
    const d = drafts[field.key];
    if (!d) continue;
    if (isSecretField(field)) {
      if (d.clear) clear.push(field.key);
      else if (d.replace && d.value) values[field.key] = d.value;
      continue;
    }
    if (field.type === 'Boolean') {
      if (Boolean(d.value) !== Boolean(field.value)) values[field.key] = Boolean(d.value);
      continue;
    }
    const before = initialValue(field);
    const text = String(d.value).trim();
    if (text === before.trim()) continue;
    if (!text) values[field.key] = null;
    else values[field.key] = field.type === 'Integer' ? Number(text) : text;
  }
  return { values, clear };
}

function validate(field, draft) {
  if (field.type !== 'Integer' || isSecretField(field)) return '';
  const text = String(draft.value).trim();
  if (!text) return '';
  if (!/^-?\d+$/.test(text)) return s.integerInvalid;
  const n = Number(text);
  if (typeof field.min === 'number' && n < field.min) return s.range(field.min, field.max);
  if (typeof field.max === 'number' && n > field.max) return s.range(field.min, field.max);
  return '';
}

function SecretControl({ id, field, draft, onChange, disabled, describedBy, invalid }) {
  const set = Boolean(field.isSet);
  if (draft.clear) {
    return (
      <div className="copy-row">
        <Badge tone="warning">{s.willClear}</Badge>
        <button type="button" className="link-button small" onClick={() => onChange({ clear: false })} disabled={disabled}>{s.undoClear}</button>
      </div>
    );
  }
  return (
    <div className="stack-form">
      <div className="copy-row">
        <Badge tone={set ? 'success' : 'neutral'}>{set ? s.secretSet : s.secretNotSet}</Badge>
        {set && <code className="mono muted" aria-hidden="true">{'•'.repeat(8)}</code>}
        {!draft.replace && (
          <button type="button" className="button button-secondary button-sm" onClick={() => onChange({ replace: true, value: '' })} disabled={disabled}>
            {set ? s.replace : s.setSecret}
          </button>
        )}
        {draft.replace && (
          <button type="button" className="button button-secondary button-sm" onClick={() => onChange({ replace: false, value: '' })} disabled={disabled}>{strings.common.cancel}</button>
        )}
        {set && !field.inherited && !draft.replace && (
          <button type="button" className="button button-danger-outline button-sm" onClick={() => onChange({ clear: true, replace: false, value: '' })} disabled={disabled}>{s.clearSecret}</button>
        )}
      </div>
      {draft.replace && (
        <PasswordInput
          id={id}
          value={draft.value}
          onChange={(ev) => onChange({ value: ev.target.value })}
          autoComplete="new-password"
          placeholder={s.secretPlaceholder}
          disabled={disabled}
          describedBy={describedBy}
          invalid={invalid}
        />
      )}
    </div>
  );
}

function PropertySettingRow({ field, draft, onChange, error, disabled }) {
  const id = 'psetting-' + field.key.replace(/[^A-Za-z0-9_-]/g, '-');
  const helpId = id + '-help';
  const errorId = id + '-error';
  const describedBy = [helpId, error ? errorId : null].filter(Boolean).join(' ');
  const secret = isSecretField(field);
  let control;
  if (secret) {
    control = <SecretControl id={id} field={field} draft={draft} onChange={onChange} disabled={disabled} describedBy={describedBy} invalid={Boolean(error)} />;
  } else if (field.type === 'Boolean') {
    control = (
      <label className="toggle">
        <input id={id} type="checkbox" role="switch" checked={Boolean(draft.value)} onChange={(ev) => onChange({ value: ev.target.checked })} disabled={disabled} aria-describedby={describedBy} />
        <span className="toggle-track" aria-hidden="true"><span className="toggle-thumb" /></span>
        <span>{draft.value ? s.on : s.off}</span>
      </label>
    );
  } else if (field.type === 'Enum') {
    const options = field.options || [];
    control = (
      <select id={id} className="select-field" value={draft.value} onChange={(ev) => onChange({ value: ev.target.value })} disabled={disabled} aria-describedby={describedBy} aria-invalid={Boolean(error)}>
        {!options.includes(draft.value) && <option value={draft.value}>{draft.value || s.notSetOption}</option>}
        {options.map((o) => <option key={o} value={o}>{o}</option>)}
      </select>
    );
  } else {
    control = (
      <input
        id={id}
        type={field.type === 'Integer' ? 'number' : 'text'}
        className={field.type === 'Integer' ? 'input-number' : undefined}
        min={typeof field.min === 'number' ? field.min : undefined}
        max={typeof field.max === 'number' ? field.max : undefined}
        value={draft.value}
        onChange={(ev) => onChange({ value: ev.target.value })}
        disabled={disabled}
        placeholder={field.inherited ? s.inheritedPlaceholder : ''}
        aria-describedby={describedBy}
        aria-invalid={Boolean(error)}
      />
    );
  }
  return (
    <div className={'setting-row' + (error ? ' has-error' : '')} data-testid={'psetting-' + field.key}>
      <div className="setting-label">
        <label htmlFor={id} className="setting-name">{field.label}</label>
        <div className="setting-badges">
          {field.inherited && <Badge tone="info" title={s.inheritedTitle}>{s.inherited}</Badge>}
          {field.applyMode === 'Restart' && <Badge tone="warning">{strings.admin.settings.restartRequired}</Badge>}
        </div>
        <code className="mono muted small setting-key">{field.key}</code>
      </div>
      <div className="setting-control">
        {control}
        <p id={helpId} className="setting-help muted small">
          {field.description}
          {field.type === 'Integer' && typeof field.min === 'number' && typeof field.max === 'number' && <> {strings.admin.settings.rangeHint(field.min, field.max)}.</>}
          {secret && <> {s.secretHint}</>}
          {field.inherited && <> {s.inheritedNote}</>}
        </p>
        {error && <p id={errorId} className="field-error" role="alert">{error}</p>}
      </div>
    </div>
  );
}

function EmailTestPanel() {
  const { api } = useAdminAuth();
  const [to, setTo] = useState('');
  const [result, setResult] = useState(null);
  const [busy, setBusy] = useState(false);
  const run = async (ev) => {
    ev.preventDefault();
    setBusy(true);
    setResult(null);
    try {
      setResult(await api.testIntegration('email', to.trim() || undefined));
    } catch (err) {
      const fe = getFieldErrors(err);
      setResult({ ok: false, message: fe.to ? String(fe.to) : errorMessage(err, strings.common.errorGeneric) });
    } finally {
      setBusy(false);
    }
  };
  return (
    <section className="panel" aria-labelledby="integration-test-title">
      <div className="panel-header"><h2 id="integration-test-title">{s.testTitle}</h2></div>
      <form className="stack-form" onSubmit={run} noValidate>
        <p className="muted small">{s.testHint}</p>
        <div className="form-grid">
          <div className="field">
            <label className="label-text" htmlFor="integration-test-to">{s.testTo}</label>
            <input id="integration-test-to" type="email" value={to} onChange={(ev) => setTo(ev.target.value)} placeholder={s.testToPlaceholder} disabled={busy} />
          </div>
        </div>
        <div>
          <button type="submit" className="button button-secondary button-sm" disabled={busy}><Icon name="message" size={14} /><span>{busy ? s.testing : s.testButton}</span></button>
        </div>
        {result && <Banner tone={result.ok ? 'success' : 'danger'} icon={result.ok ? 'check' : 'alert'}>{result.message || (result.ok ? s.testOk : s.testFailed)}</Banner>}
      </form>
    </section>
  );
}

/**
 * /admin/integrations: this property's settings-backed integrations (`GET/PUT
 * /admin/property/settings`), grouped by section. Secrets are write-only (Set or Not set, Replace,
 * Clear); `inherited` marks values that come from the deployment. Only changed keys are saved.
 */
function IntegrationsView() {
  const { api } = useAdminAuth();
  const toast = useToast();
  const fetcher = useCallback((signal) => api.getPropertySettings(signal), [api]);
  const query = useApiQuery(fetcher);
  const [fields, setFields] = useState(null);
  const [drafts, setDrafts] = useState({});
  const [serverErrors, setServerErrors] = useState({});
  const [summary, setSummary] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!query.data) return;
    const list = query.data.fields || [];
    setFields(list);
    setDrafts(initialDrafts(list));
    setServerErrors({});
  }, [query.data]);

  const groups = useMemo(() => groupFields(fields || []), [fields]);
  const update = useMemo(() => buildSettingsUpdate(fields || [], drafts), [fields, drafts]);
  const changed = Object.keys(update.values).length + update.clear.length;
  const clientErrors = {};
  for (const field of fields || []) {
    const message = drafts[field.key] ? validate(field, drafts[field.key]) : '';
    if (message) clientErrors[field.key] = message;
  }

  const setDraft = (key, patch) => {
    setDrafts((prev) => ({ ...prev, [key]: { ...prev[key], ...patch } }));
    setServerErrors((prev) => {
      if (!prev[key]) return prev;
      const next = { ...prev };
      delete next[key];
      return next;
    });
  };

  const save = async (ev) => {
    ev.preventDefault();
    if (changed === 0) {
      toast.info(s.noChanges);
      return;
    }
    if (Object.keys(clientErrors).length) {
      setSummary(s.fixBelow);
      return;
    }
    setSaving(true);
    setSummary('');
    try {
      const res = await api.updatePropertySettings(update);
      const list = (res && res.fields) || fields;
      setFields(list);
      setDrafts(initialDrafts(list));
      setServerErrors({});
      toast.success(s.saved(changed));
    } catch (err) {
      const fe = getFieldErrors(err);
      const mapped = {};
      for (const [key, message] of Object.entries(fe)) mapped[key.replace(/^values\./, '')] = Array.isArray(message) ? message.join(' ') : String(message);
      setServerErrors(mapped);
      setSummary(Object.keys(mapped).length ? s.fixBelow + (mapped.clear ? ' ' + mapped.clear : '') : errorMessage(err, strings.common.errorGeneric));
    } finally {
      setSaving(false);
    }
  };

  const route = strings.admin.routes.integrations;
  return (
    <div className="view">
      <PageHeader title={route.label} summary={route.summary} actions={<button type="button" className="button button-secondary button-sm" onClick={query.reload} disabled={query.loading}><Icon name="refresh" size={14} /><span>{strings.common.refresh}</span></button>} />
      {query.loading && !fields && <Loading />}
      {query.error && <ErrorBanner message={s.loadError + ' ' + errorMessage(query.error, '')} onRetry={query.reload} />}
      {fields && fields.length === 0 && <EmptyState icon="gear" title={s.empty} />}
      {fields && fields.length > 0 && (
        <form className="settings-form" onSubmit={save} noValidate>
          {summary && <ErrorBanner message={summary} onDismiss={() => setSummary('')} />}
          {groups.map((group) => (
            <fieldset key={group.section} className="settings-section" disabled={saving}>
              <legend><span>{group.section}</span><span className="muted small">{strings.admin.settings.sectionCount(group.fields.length)}</span></legend>
              {group.fields.map((field) => (
                <PropertySettingRow
                  key={field.key}
                  field={field}
                  draft={drafts[field.key] || { value: '', replace: false, clear: false }}
                  onChange={(patch) => setDraft(field.key, patch)}
                  error={serverErrors[field.key] || clientErrors[field.key]}
                  disabled={saving}
                />
              ))}
            </fieldset>
          ))}
          <div className="form-actions sticky-actions">
            <span className="muted small" aria-live="polite">{changed ? s.changedCount(changed) : ''}</span>
            <button type="button" className="button button-secondary" onClick={() => { setDrafts(initialDrafts(fields)); setServerErrors({}); setSummary(''); }} disabled={saving || changed === 0}>{s.discard}</button>
            <button type="submit" className="button button-primary" disabled={saving || changed === 0}>{saving ? s.saving : s.save}</button>
          </div>
        </form>
      )}
      <EmailTestPanel />
    </div>
  );
}

export default IntegrationsView;
