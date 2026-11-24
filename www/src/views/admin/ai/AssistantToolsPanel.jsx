import { useCallback, useEffect, useState } from 'react';
import { LabelMatch } from '@trelliskit/shared';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useToast } from '../../../context/ToastContext.jsx';
import { errorMessage } from '../../../utils/api.js';
import MetadataFilterBar from '../../../components/MetadataFilterBar.jsx';
import Modal from '../../../components/Modal.jsx';
import { Badge } from '../../../components/Badges.jsx';
import { ErrorBanner } from '../../../components/States.jsx';
import { TOOL_LIMITS, canonicalToolNames } from './aiAdminUtils.js';

const s = strings.ai.admin.assistants;
const t = s.tools;

function FieldError({ text }) {
  return text ? <span className="field-error small ai-error" role="alert">{text}</span> : null;
}

/** The registry, searched and filtered by label and tag, paged with "load more". */
function useToolRegistry(client) {
  const [meta, setMeta] = useState({ labels: [], labelMatch: LabelMatch.All, tags: {}, search: '' });
  const [state, setState] = useState({ items: [], token: null, loading: true, error: null });
  const query = useCallback((extra = {}) => ({
    maxResults: 100,
    search: meta.search || undefined,
    labels: meta.labels && meta.labels.length ? meta.labels : undefined,
    labelMatch: meta.labelMatch,
    tags: meta.tags && Object.keys(meta.tags).length ? meta.tags : undefined,
    ...extra
  }), [meta]);
  useEffect(() => {
    const controller = new AbortController();
    setState((x) => ({ ...x, loading: true, error: null }));
    client.enumerateAssistantTools(query(), controller.signal).then(
      (page) => setState({ items: (page && page.objects) || [], token: page && !page.endOfResults ? page.continuationToken || null : null, loading: false, error: null }),
      (error) => { if (error.name !== 'AbortError') setState({ items: [], token: null, loading: false, error }); }
    );
    return () => controller.abort();
  }, [client, query]);
  const more = async () => {
    if (!state.token) return;
    const page = await client.enumerateAssistantTools(query({ continuationToken: state.token }));
    setState((x) => ({ ...x, items: x.items.concat((page && page.objects) || []), token: page && !page.endOfResults ? page.continuationToken || null : null }));
  };
  const replace = (tool) => setState((x) => ({ ...x, items: x.items.map((i) => (i.name === tool.name ? tool : i)) }));
  return { meta, setMeta, ...state, more, replace };
}

/** Turn one tool off or on for every assistant of the property (audited), with an optional reason. */
function KillSwitchDialog({ tool, onClose, onDone }) {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const disabling = !tool.disabled;
  const label = tool.label || tool.name;
  const submit = async () => {
    setBusy(true);
    setError('');
    try {
      const body = { disabled: disabling };
      if (reason.trim()) body.reason = reason.trim();
      const updated = await client.updateAssistantTool(tool.name, body);
      toast.success(disabling ? t.disabledToast(label) : t.enabledToast(label));
      onDone(updated && updated.name ? updated : { ...tool, disabled: disabling, disabledReason: body.reason || null });
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };
  return (
    <Modal open size="sm" onClose={busy ? () => {} : onClose} title={disabling ? t.disableTitle(label) : t.enableTitle(label)}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="button" className={'button ' + (disabling ? 'button-danger' : 'button-primary')} onClick={submit} disabled={busy}>{disabling ? t.disable : t.enable}</button>
        </>
      )}
    >
      <p className="muted small">{t.killSwitchHint}</p>
      <ErrorBanner message={error} />
      <div className="field">
        <label className="label-text" htmlFor="tool-kill-reason">{t.reason}</label>
        <input id="tool-kill-reason" value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} />
      </div>
    </Modal>
  );
}

const LIMIT_FIELDS = ['maxToolIterations', 'maxToolCallsPerTurn', 'toolCallTimeoutMs', 'maxToolOutputCharacters', 'maxTokensPerTurn'];

/**
 * The assistant editor's Tools tab (AI contract section 8): the master switch, a checkbox per
 * registered tool (aliases are never their own checkbox), the loop limits with their bounds, the
 * activity toggle, and the property-wide kill switch per tool. Field errors use the server's keys
 * (`tools.allowed`, `tools.maxToolIterations`).
 * @param {{ form: object, setForm: Function, fieldErrors: Record<string, string> }} props
 */
function AssistantToolsPanel({ form, setForm, fieldErrors = {} }) {
  const { client } = useAdminAuth();
  const registry = useToolRegistry(client);
  const [killing, setKilling] = useState(null);
  const allowed = canonicalToolNames(registry.items, form.toolsAllowed);
  const toggle = (tool) => setForm((f) => {
    const current = canonicalToolNames(registry.items, f.toolsAllowed);
    const next = current.includes(tool.name) ? current.filter((n) => n !== tool.name) : current.concat([tool.name]);
    return { ...f, toolsAllowed: next };
  });
  const set = (key) => (e) => {
    const value = e.target.type === 'checkbox' ? e.target.checked : e.target.value;
    setForm((f) => ({ ...f, [key]: value }));
  };
  return (
    <section className="stack-form" aria-label={s.tabs.tools}>
      <label className="checkbox-inline"><input type="checkbox" checked={form.toolsEnabled} onChange={set('toolsEnabled')} /> {t.enabled}</label>
      <p className="muted small">{t.enabledHint}</p>
      <FieldError text={fieldErrors['tools.enabled']} />
      <fieldset className="field" disabled={!form.toolsEnabled}>
        <legend className="label-text">{t.available}</legend>
        <MetadataFilterBar value={registry.meta} onChange={registry.setMeta} client={client} targetType="AssistantTool" showSearch searchLabel={t.search} />
        <FieldError text={fieldErrors['tools.allowed']} />
        <ErrorBanner message={registry.error ? t.loadFailed : ''} />
        {!registry.loading && !registry.error && registry.items.length === 0 && <p className="muted">{t.none}</p>}
        <ul className="ai-tool-registry" data-testid="ai-tool-registry">
          {registry.items.map((tool) => (
            <li key={tool.name} className="ai-tool-registry-item" data-tool={tool.name}>
              <label className="checkbox-inline">
                <input type="checkbox" checked={allowed.includes(tool.name)} onChange={() => toggle(tool)} aria-describedby={'tool-desc-' + tool.name} />
                {' '}<strong>{tool.label || tool.name}</strong> <code className="mono small">{tool.name}</code>
              </label>
              {' '}{tool.worksSignedOut && <Badge tone="info">{t.worksSignedOut}</Badge>}
              {' '}{tool.disabled && <Badge tone="danger">{t.disabledBadge}</Badge>}
              <div id={'tool-desc-' + tool.name} className="muted small">
                {tool.description}
                {tool.aliases && tool.aliases.length > 0 && <> {t.aliases(tool.aliases.join(', '))}</>}
                {tool.disabled && tool.disabledReason && <> {t.disabledReason(tool.disabledReason)}</>}
              </div>
            </li>
          ))}
        </ul>
        {registry.token && <button type="button" className="button button-secondary button-sm" onClick={registry.more}>{t.moreTools}</button>}
      </fieldset>
      <h3>{t.limits}</h3>
      <div className="ai-form-grid">
        {LIMIT_FIELDS.map((key) => (
          <div key={key} className="field">
            <label className="label-text" htmlFor={'as-tool-' + key}>{t[key]}</label>
            <input id={'as-tool-' + key} type="number" min={TOOL_LIMITS[key].min} max={TOOL_LIMITS[key].max} value={form[key]} onChange={set(key)} />
            <FieldError text={fieldErrors['tools.' + key]} />
          </div>
        ))}
      </div>
      <label className="checkbox-inline"><input type="checkbox" checked={form.showToolActivity} onChange={set('showToolActivity')} /> {t.showToolActivity}</label>
      <h3>{t.killSwitch}</h3>
      <p className="muted small">{t.killSwitchHint}</p>
      <ul className="ai-tool-registry" data-testid="ai-tool-kill-switches">
        {registry.items.map((tool) => (
          <li key={tool.name} className="ai-tool-registry-item">
            <span>{tool.label || tool.name}</span>{' '}
            {tool.disabled && <Badge tone="danger">{t.disabledBadge}</Badge>}{' '}
            <button type="button" className="button button-secondary button-sm" onClick={() => setKilling(tool)} aria-label={(tool.disabled ? t.enable : t.disable) + ' ' + (tool.label || tool.name)}>
              {tool.disabled ? t.enable : t.disable}
            </button>
          </li>
        ))}
      </ul>
      {killing && <KillSwitchDialog tool={killing} onClose={() => setKilling(null)} onDone={(tool) => { registry.replace(tool); setKilling(null); }} />}
    </section>
  );
}

export default AssistantToolsPanel;
