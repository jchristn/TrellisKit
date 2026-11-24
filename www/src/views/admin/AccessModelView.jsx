import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  AccessFeatures, AccessModel, AccessModels, ContentAudience, accessSwitchConsequences, accessUpdateBody, validateAccessForm
} from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import PageHeader from '../../components/PageHeader.jsx';
import Modal from '../../components/Modal.jsx';
import Icon from '../../components/Icon.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import { formatDateTime } from '../../utils/format.js';

const s = strings.admin.accessModel;
const route = strings.admin.routes.access;
const featureLabels = strings.access.features;
const audienceLabels = strings.access.audiences;

function formFrom(policy) {
  return {
    model: policy.model || AccessModel.FullyGated,
    anonymousBrowsing: Boolean(policy.anonymousBrowsing),
    gatedFeatures: Array.isArray(policy.gatedFeatures) ? policy.gatedFeatures.slice() : [],
    // FullyGated always uses Members, so a stored value there is stale and never shown or saved.
    defaultAudience: policy.model === AccessModel.FullyGated ? '' : (policy.configuredDefaultAudience || '')
  };
}

function sameForm(a, b) {
  return JSON.stringify(accessUpdateBody(a)) === JSON.stringify(accessUpdateBody(b));
}

/** The audience a model uses when none is configured (mirrors AccessPolicy.EffectiveDefaultAudience). */
function modelDefaultAudience(model) {
  return model === AccessModel.Open ? ContentAudience.SignedIn : ContentAudience.Members;
}

/**
 * /admin/access (ACCESS_MODELS_CONTRACT.md sections 1, 4, and 6): the property's access model over
 * `GET`/`PUT /api/v1/admin/property/access`. Explains each model, edits anonymous browsing, the
 * gated features (all eight keys), and the default audience, and confirms a model change with its
 * consequences (a paid property switching to Open keeps billing at the stores until members
 * cancel there). The backend audits every change as `property.access.update`.
 */
function AccessModelView() {
  const { api } = useAdminAuth();
  const { setRuntimeProperty } = useProperty();
  const toast = useToast();
  const fetcher = useCallback((signal) => api.getPropertyAccess(signal), [api]);
  const query = useApiQuery(fetcher);
  const [policy, setPolicy] = useState(null);
  const [form, setForm] = useState(null);
  const [problems, setProblems] = useState({});
  const [summary, setSummary] = useState('');
  const [confirming, setConfirming] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!query.data) return;
    setPolicy(query.data);
    setForm(formFrom(query.data));
  }, [query.data]);

  const consequences = useMemo(() => (policy && form ? accessSwitchConsequences(policy.model, form.model) : []), [policy, form]);

  if (!form || !policy) {
    return (
      <div className="view">
        <PageHeader title={route.label} summary={route.summary} />
        {query.loading && <Loading />}
        {query.error && <ErrorBanner message={s.loadError + ' ' + errorMessage(query.error, '')} onRetry={query.reload} />}
      </div>
    );
  }

  const changed = !sameForm(form, formFrom(policy));
  const set = (patch) => {
    setForm((current) => ({ ...current, ...patch }));
    setProblems({});
    setSummary('');
  };
  const toggleFeature = (key) => set({ gatedFeatures: form.gatedFeatures.includes(key) ? form.gatedFeatures.filter((k) => k !== key) : [...form.gatedFeatures, key] });
  const fullyGated = form.model === AccessModel.FullyGated;
  const partial = form.model === AccessModel.PartiallyGated;
  const audienceOptions = form.model === AccessModel.Open ? [ContentAudience.Public, ContentAudience.SignedIn] : [ContentAudience.Public, ContentAudience.SignedIn, ContentAudience.Members];
  const effectiveAudience = fullyGated ? ContentAudience.Members : (form.defaultAudience || modelDefaultAudience(form.model));

  const save = async () => {
    setConfirming(false);
    const found = validateAccessForm(form);
    if (Object.keys(found).length) {
      setProblems(found);
      setSummary(s.fixBelow);
      return;
    }
    setSaving(true);
    try {
      const saved = await api.updatePropertyAccess(accessUpdateBody(form));
      setPolicy(saved);
      setForm(formFrom(saved));
      toast.success(s.saved);
      // Refresh the console's copy of GET /property so navigation and pickers follow the new model.
      api.client.property().then((body) => { if (body && typeof body === 'object') setRuntimeProperty(body); }, () => {});
    } catch (err) {
      const fe = getFieldErrors(err);
      const mapped = {};
      for (const [key, message] of Object.entries(fe)) mapped[key.replace(/\[\d+\]$/, '')] = Array.isArray(message) ? message.join(' ') : String(message);
      setProblems(mapped);
      setSummary(Object.keys(mapped).length ? s.fixBelow : errorMessage(err, strings.common.errorGeneric));
    } finally {
      setSaving(false);
    }
  };

  const submit = (ev) => {
    ev.preventDefault();
    if (!changed) {
      toast.info(s.noChanges);
      return;
    }
    if (consequences.length) setConfirming(true);
    else save();
  };

  const problemText = (key) => (problems[key] ? s.problems[problems[key]] || problems[key] : '');

  return (
    <div className="view access-model-view">
      <PageHeader
        title={route.label}
        summary={route.summary}
        actions={<button type="button" className="button button-secondary button-sm" onClick={query.reload} disabled={query.loading}><Icon name="refresh" size={14} /><span>{strings.common.refresh}</span></button>}
      />
      <ErrorBanner message={summary} onDismiss={() => setSummary('')} />

      <section className="panel" aria-labelledby="access-status-title">
        <h2 id="access-status-title" className="visually-hidden">{s.current}</h2>
        <dl className="kv-grid">
          <div className="kv-item"><dt>{s.current}</dt><dd><Badge tone="info">{s.models[policy.model] ? s.models[policy.model].label : policy.model}</Badge></dd></div>
          <div className="kv-item"><dt>{s.purchases}</dt><dd><Badge tone={policy.purchasesEnabled ? 'success' : 'neutral'}>{policy.purchasesEnabled ? s.purchasesOn : s.purchasesOff}</Badge></dd></div>
          <div className="kv-item"><dt>{s.signupCodes}</dt><dd><Badge tone={policy.signupCodesEnabled ? 'success' : 'neutral'}>{policy.signupCodesEnabled ? s.purchasesOn : s.purchasesOff}</Badge></dd></div>
          <div className="kv-item"><dt>{s.revision}</dt><dd className="mono">{policy.revision}</dd></div>
          <div className="kv-item"><dt>{s.updated}</dt><dd>{policy.updatedUtc ? formatDateTime(policy.updatedUtc) : s.never}</dd></div>
          {policy.updatedByUserId && <div className="kv-item"><dt>{s.updatedBy}</dt><dd><CopyableId value={policy.updatedByUserId} /></dd></div>}
        </dl>
        <p className="muted small">{s.auditNote} <Link to="/admin/audit">{s.history}</Link></p>
      </section>

      <form className="panel form-stack" onSubmit={submit} noValidate aria-label={route.label}>
        <fieldset className="type-picker" aria-invalid={Boolean(problems.model)}>
          <legend className="label-text">{s.modelLegend}</legend>
          <div className="type-picker-options">
            {AccessModels.map((model) => (
              <label key={model} className={'type-option' + (form.model === model ? ' selected' : '')}>
                <input type="radio" name="access-model" value={model} checked={form.model === model} onChange={() => set({ model, defaultAudience: model === AccessModel.Open && form.defaultAudience === ContentAudience.Members ? '' : form.defaultAudience })} disabled={saving} />
                <span>
                  <strong>{s.models[model].label}</strong>
                  {policy.model === model && <> <Badge tone="info">{s.current}</Badge></>}
                  <span className="muted small">{s.models[model].summary}</span>
                  <span className="muted small">{s.models[model].details}</span>
                </span>
              </label>
            ))}
          </div>
          {problems.model && <span className="field-error" role="alert">{problemText('model')}</span>}
        </fieldset>

        {consequences.length > 0 && (
          <div className="banner banner-warning" role="status">
            <Icon name="alert" />
            <ul className="banner-text access-consequences">
              {consequences.map((key) => <li key={key}>{s.consequences[key]}</li>)}
            </ul>
          </div>
        )}

        <label className="toggle">
          <input type="checkbox" checked={form.anonymousBrowsing} onChange={(e) => set({ anonymousBrowsing: e.target.checked })} disabled={saving} aria-describedby="access-anon-hint" />
          <span className="toggle-track"><span className="toggle-thumb" /></span>
          <span>{s.anonymousLabel}</span>
        </label>
        <p id="access-anon-hint" className="muted small">{s.anonymousHint}{fullyGated ? ' ' + s.anonymousIgnored : ''}</p>

        <fieldset className="fieldset" aria-invalid={Boolean(problems.gatedFeatures)} aria-describedby="access-gated-hint">
          <legend className="label-text">{s.gatedLegend}</legend>
          <p id="access-gated-hint" className="muted small">{partial ? s.gatedHint : s.gatedIgnored}</p>
          <div className="checkbox-grid">
            {AccessFeatures.map((key) => (
              <label key={key} className="check-field">
                <input type="checkbox" checked={form.gatedFeatures.includes(key)} onChange={() => toggleFeature(key)} disabled={saving} />
                <span>
                  <strong>{featureLabels[key]}</strong>
                  <span className="muted small"> {s.featureHints[key]}</span>
                </span>
              </label>
            ))}
          </div>
          {problems.gatedFeatures && <span className="field-error" role="alert">{problemText('gatedFeatures')}</span>}
        </fieldset>

        <div className="field">
          <label className="label-text" htmlFor="access-audience">{s.audienceLabel}</label>
          <select id="access-audience" value={fullyGated ? ContentAudience.Members : form.defaultAudience} onChange={(e) => set({ defaultAudience: e.target.value })} disabled={saving || fullyGated} aria-describedby="access-audience-hint" aria-invalid={Boolean(problems.defaultAudience)}>
            <option value="">{s.audienceModelDefault(audienceLabels[modelDefaultAudience(form.model)])}</option>
            {audienceOptions.map((a) => <option key={a} value={a}>{audienceLabels[a]}</option>)}
          </select>
          <span id="access-audience-hint" className="muted small">{s.audienceHint} {s.effective(audienceLabels[effectiveAudience])}</span>
          {form.model === AccessModel.Open && <span className="muted small">{s.audienceMembersInOpen}</span>}
          {problems.defaultAudience && <span className="field-error" role="alert">{problemText('defaultAudience')}</span>}
        </div>

        <div className="form-actions">
          <button type="button" className="button button-secondary" onClick={() => { setForm(formFrom(policy)); setProblems({}); setSummary(''); }} disabled={saving || !changed}>{strings.common.cancel}</button>
          <button type="submit" className="button button-primary" disabled={saving || !changed}>{saving ? s.saving : s.save}</button>
        </div>
      </form>

      {confirming && (
        <Modal
          open
          onClose={() => setConfirming(false)}
          title={s.confirmTitle}
          size="md"
          footer={(
            <>
              <button type="button" className="button button-secondary" onClick={() => setConfirming(false)}>{strings.common.cancel}</button>
              <button type="button" className={'button ' + (form.model === AccessModel.Open ? 'button-danger' : 'button-primary')} onClick={save}>{s.confirmCta}</button>
            </>
          )}
        >
          <div className="access-confirm">
            <p><strong>{s.models[policy.model].label}</strong> <Icon name="chevronRight" size={12} /> <strong>{s.models[form.model].label}</strong></p>
            <ul>
              {consequences.map((key) => <li key={key}>{s.consequences[key]}</li>)}
            </ul>
            <p className="muted small">{s.auditNote}</p>
          </div>
        </Modal>
      )}
    </div>
  );
}

export default AccessModelView;
