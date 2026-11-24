import { useRef, useState } from 'react';
import { EMAIL_TEMPLATE_BODY_MAX, EMAIL_TEMPLATE_LINE_MAX, unknownTemplateTokens } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import Modal from '../../components/Modal.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, ErrorBanner, Loading } from '../../components/States.jsx';

const s = strings.admin.email;
const PARTS = Object.freeze(['subject', 'preheader', 'heading', 'body']);
const DEFAULT_KEY = { subject: 'defaultSubject', preheader: 'defaultPreheader', heading: 'defaultHeading', body: 'defaultBody' };

/** Form values of a template: the override text (empty means the engine default). */
export function templateValues(template) {
  const o = (template && template.override) || {};
  return { subject: o.subject || '', preheader: o.preheader || '', heading: o.heading || '', body: o.body || '' };
}

/** `PUT` and preview body: empty parts are null (the engine default for that part). */
export function templateBody(values) {
  const body = {};
  for (const part of PARTS) {
    const text = String(values[part] || '').replace(/\r\n/g, '\n');
    body[part] = text.trim() ? text.trim() : null;
  }
  return body;
}

/**
 * Client checks for an override: one line of at most 200 characters for subject, preheader, and
 * heading, a body of at most 5000, and only the template's own `{tokens}`.
 */
export function validateTemplate(values, tokens) {
  const errors = {};
  for (const part of PARTS) {
    const text = String(values[part] || '');
    const max = part === 'body' ? EMAIL_TEMPLATE_BODY_MAX : EMAIL_TEMPLATE_LINE_MAX;
    if (part !== 'body' && /[\r\n]/.test(text.trim())) errors[part] = s.editor.oneLine;
    else if (text.trim().length > max) errors[part] = s.editor.tooLong(max);
    else {
      const unknown = unknownTemplateTokens(text, tokens);
      if (unknown.length) errors[part] = s.editor.unknownTokens(unknown.map((t) => '{' + t + '}').join(', '));
    }
  }
  return errors;
}

/**
 * Template override editor: subject, preheader, heading, and body (defaults shown as
 * placeholders), insertable token chips, Save, Reset to default, Preview (sandboxed HTML and
 * text), and Send test.
 */
function EmailTemplateEditor({ template, onClose, onSaved }) {
  const { api } = useAdminAuth();
  const [values, setValues] = useState(() => templateValues(template));
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [resetOpen, setResetOpen] = useState(false);
  const [preview, setPreview] = useState(null);
  const [previewTab, setPreviewTab] = useState('html');
  const [previewBusy, setPreviewBusy] = useState(false);
  const [testTo, setTestTo] = useState('');
  const [testResult, setTestResult] = useState(null);
  const [testBusy, setTestBusy] = useState(false);
  const [focus, setFocus] = useState('body');
  const refs = { subject: useRef(null), preheader: useRef(null), heading: useRef(null), body: useRef(null) };
  const tokens = template.tokens || [];

  const set = (part, text) => {
    setValues((v) => ({ ...v, [part]: text }));
    setErrors((prev) => {
      const next = { ...prev };
      delete next[part];
      const unknown = unknownTemplateTokens(text, tokens);
      if (unknown.length) next[part] = s.editor.unknownTokens(unknown.map((t) => '{' + t + '}').join(', '));
      return next;
    });
  };

  const insert = (token) => {
    const part = focus;
    const el = refs[part].current;
    const text = values[part] || '';
    const placeholder = '{' + token + '}';
    const start = el && typeof el.selectionStart === 'number' ? el.selectionStart : text.length;
    const end = el && typeof el.selectionEnd === 'number' ? el.selectionEnd : text.length;
    set(part, text.slice(0, start) + placeholder + text.slice(end));
    requestAnimationFrame(() => {
      if (!el) return;
      el.focus();
      const at = start + placeholder.length;
      try { el.setSelectionRange(at, at); } catch { /* inputs without selection support */ }
    });
  };

  const check = () => {
    const found = validateTemplate(values, tokens);
    setErrors(found);
    if (Object.keys(found).length) setError(s.editor.fixBelow);
    return Object.keys(found).length === 0;
  };

  const serverFail = (err) => {
    const fe = getFieldErrors(err);
    const mapped = {};
    for (const [key, message] of Object.entries(fe)) mapped[key.replace(/^override\./, '')] = Array.isArray(message) ? message.join(' ') : String(message);
    setErrors(mapped);
    setError(errorMessage(err, strings.common.errorGeneric));
  };

  const save = async (ev) => {
    ev.preventDefault();
    if (!check()) return;
    setBusy(true);
    setError('');
    try {
      const saved = await api.updateEmailTemplate(template.key, templateBody(values));
      onSaved(saved, s.editor.saved);
    } catch (err) {
      serverFail(err);
      setBusy(false);
    }
  };

  const reset = async () => {
    setBusy(true);
    try {
      await api.resetEmailTemplate(template.key);
      setResetOpen(false);
      onSaved({ ...template, override: null }, s.editor.resetDone);
    } catch (err) {
      setResetOpen(false);
      serverFail(err);
      setBusy(false);
    }
  };

  const runPreview = async () => {
    if (!check()) return;
    setPreviewBusy(true);
    setError('');
    try {
      setPreview(await api.previewEmailTemplate(template.key, templateBody(values)));
    } catch (err) {
      serverFail(err);
    } finally {
      setPreviewBusy(false);
    }
  };

  const sendTest = async () => {
    setTestBusy(true);
    setTestResult(null);
    try {
      setTestResult(await api.sendTestEmail({ template: template.key, to: testTo.trim() || undefined }));
    } catch (err) {
      const fe = getFieldErrors(err);
      setTestResult({ ok: false, message: fe.to ? String(fe.to) : errorMessage(err, strings.common.errorGeneric) });
    } finally {
      setTestBusy(false);
    }
  };

  const field = (part, label) => {
    const id = 'email-tpl-' + part;
    const max = part === 'body' ? EMAIL_TEMPLATE_BODY_MAX : EMAIL_TEMPLATE_LINE_MAX;
    const props = {
      id,
      ref: refs[part],
      value: values[part],
      onChange: (ev) => set(part, ev.target.value),
      onFocus: () => setFocus(part),
      placeholder: template[DEFAULT_KEY[part]] || '',
      disabled: busy,
      'aria-invalid': Boolean(errors[part]),
      'aria-describedby': id + '-hint' + (errors[part] ? ' ' + id + '-error' : '')
    };
    return (
      <div className="field">
        <label className="label-text" htmlFor={id}>{label}</label>
        {part === 'body' ? <textarea rows={8} {...props} /> : <input type="text" {...props} />}
        <span id={id + '-hint'} className="muted small">{values[part].trim() ? s.editor.counter(values[part].trim().length, max) : s.editor.usingDefault}</span>
        {errors[part] && <span id={id + '-error'} className="field-error" role="alert">{errors[part]}</span>}
      </div>
    );
  };

  return (
    <Modal
      open
      size="lg"
      onClose={busy ? () => {} : onClose}
      title={s.editor.title(template.key)}
      subtitle={template.description}
      headerExtra={template.override ? <Badge tone="info">{s.overridden}</Badge> : <Badge>{s.engineDefault}</Badge>}
      footer={(
        <>
          {template.override && <button type="button" className="button button-danger-outline" onClick={() => setResetOpen(true)} disabled={busy}>{s.editor.reset}</button>}
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="email-template-form" className="button button-primary" disabled={busy}>{busy ? s.editor.saving : s.editor.save}</button>
        </>
      )}
    >
      <form id="email-template-form" className="stack-form" onSubmit={save} noValidate>
        <ErrorBanner message={error} onDismiss={() => setError('')} />
        <p className="muted small">{s.editor.hint}</p>
        <div className="field">
          <span className="label-text">{s.editor.tokens}</span>
          {tokens.length === 0 ? <span className="muted small">{s.editor.noTokens}</span> : (
            <div className="token-chips" role="group" aria-label={s.editor.tokens}>
              {tokens.map((t) => (
                <button key={t} type="button" className="chip" onClick={() => insert(t)} title={s.editor.insertToken(t, s.editor.parts[focus])} disabled={busy}>
                  <code className="mono">{'{' + t + '}'}</code>
                </button>
              ))}
            </div>
          )}
          <span className="muted small">{s.editor.tokensHint(s.editor.parts[focus])}</span>
        </div>
        {field('subject', s.editor.parts.subject)}
        {field('preheader', s.editor.parts.preheader)}
        {field('heading', s.editor.parts.heading)}
        {field('body', s.editor.parts.body)}
      </form>

      <section className="detail-block" aria-labelledby="email-preview-title">
        <div className="row-between">
          <h3 id="email-preview-title">{s.editor.previewTitle}</h3>
          <button type="button" className="button button-secondary button-sm" onClick={runPreview} disabled={previewBusy || busy}>
            <Icon name="eye" size={14} /><span>{previewBusy ? s.editor.previewing : s.editor.preview}</span>
          </button>
        </div>
        {previewBusy && !preview && <Loading inline />}
        {preview && (
          <>
            <dl className="kv-grid">
              <div className="kv-item"><dt>{s.editor.parts.subject}</dt><dd>{preview.subject}</dd></div>
              <div className="kv-item"><dt>{s.editor.parts.preheader}</dt><dd>{preview.preheader || '-'}</dd></div>
            </dl>
            <div className="segmented" role="tablist" aria-label={s.editor.previewTitle}>
              {['html', 'text'].map((id) => (
                <button key={id} type="button" role="tab" aria-selected={previewTab === id} className={'segment' + (previewTab === id ? ' active' : '')} onClick={() => setPreviewTab(id)}>{s.editor.previewTabs[id]}</button>
              ))}
            </div>
            {previewTab === 'html'
              ? <iframe className="email-preview-frame" title={s.editor.previewFrame(template.key)} sandbox="" srcDoc={preview.html} />
              : <pre className="email-preview-text mono small">{preview.text}</pre>}
          </>
        )}
      </section>

      <section className="detail-block" aria-labelledby="email-test-title">
        <h3 id="email-test-title">{s.test.title}</h3>
        <p className="muted small">{s.test.templateHint}</p>
        <div className="form-grid">
          <div className="field">
            <label className="label-text" htmlFor="email-tpl-test-to">{s.test.to}</label>
            <input id="email-tpl-test-to" type="email" value={testTo} onChange={(ev) => setTestTo(ev.target.value)} placeholder={s.test.toPlaceholder} autoComplete="off" />
          </div>
        </div>
        <div>
          <button type="button" className="button button-secondary button-sm" onClick={sendTest} disabled={testBusy}>
            <Icon name="message" size={14} /><span>{testBusy ? s.test.sending : s.test.send}</span>
          </button>
        </div>
        {testResult && <Banner tone={testResult.ok ? 'success' : 'danger'} icon={testResult.ok ? 'check' : 'alert'}>{testResult.message || (testResult.ok ? s.test.ok : s.test.failed)}</Banner>}
      </section>

      <ConfirmModal open={resetOpen} title={s.editor.resetTitle(template.key)} message={s.editor.resetBody} confirmLabel={s.editor.reset} busy={busy} onConfirm={reset} onCancel={() => setResetOpen(false)} />
    </Modal>
  );
}

export default EmailTemplateEditor;
