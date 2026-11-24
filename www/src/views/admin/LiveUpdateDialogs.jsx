import { useState } from 'react';
import strings from '../../strings.js';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import Modal from '../../components/Modal.jsx';
import { ErrorBanner } from '../../components/States.jsx';

/**
 * A small form dialog used by the live update pages. `fields` are
 * `{ key, label, type: 'text'|'number'|'select'|'textarea'|'checkbox'|'datetime-local', options?, help?, required?, maxLength? }`.
 * `onSubmit(values)` may throw an ApiError; its message and field errors are shown and the dialog stays open.
 * Remount with a new `key` to reset.
 */
export function FormDialog({ open, title, body, fields, initial = {}, submitLabel = strings.common.save, danger = false, onSubmit, onClose, children }) {
  const [values, setValues] = useState(() => {
    const start = {};
    for (const field of fields) start[field.key] = initial[field.key] !== undefined ? initial[field.key] : field.type === 'checkbox' ? false : '';
    return start;
  });
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const set = (field) => (e) => {
    const value = field.type === 'checkbox' ? e.target.checked : e.target.value;
    setValues((v) => ({ ...v, [field.key]: value }));
  };

  const submit = async (e) => {
    e.preventDefault();
    const missing = {};
    for (const field of fields) if (field.required && String(values[field.key] ?? '').trim() === '') missing[field.key] = strings.common.required || 'Required.';
    if (Object.keys(missing).length) {
      setErrors(missing);
      return;
    }
    setBusy(true);
    setError('');
    setErrors({});
    try {
      await onSubmit(values);
    } catch (err) {
      setErrors(getFieldErrors(err));
      setError(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  const formId = 'lu-form-' + String(title).replace(/[^a-z0-9]+/gi, '-').toLowerCase();
  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form={formId} className={'button ' + (danger ? 'button-danger' : 'button-primary')} disabled={busy}>{submitLabel}</button>
        </>
      )}
    >
      <form id={formId} onSubmit={submit} noValidate>
        {body && <p className="muted">{body}</p>}
        {error && <ErrorBanner message={error} />}
        {fields.map((field) => {
          const id = formId + '-' + field.key;
          if (field.type === 'checkbox') {
            return (
              <div className="field" key={field.key}>
                <label className="toggle"><input id={id} type="checkbox" checked={Boolean(values[field.key])} onChange={set(field)} disabled={busy} /> {field.label}</label>
                {field.help && <span className="field-hint">{field.help}</span>}
              </div>
            );
          }
          return (
            <div className="field" key={field.key}>
              <label className="label-text" htmlFor={id}>{field.label}</label>
              {field.type === 'select' ? (
                <select id={id} value={values[field.key]} onChange={set(field)} disabled={busy}>
                  {(field.options || []).map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              ) : field.type === 'textarea' ? (
                <textarea id={id} rows={field.rows || 6} className={field.mono ? 'mono' : undefined} value={values[field.key]} onChange={set(field)} disabled={busy} maxLength={field.maxLength} aria-invalid={Boolean(errors[field.key])} spellCheck="false" autoComplete="off" />
              ) : (
                <input id={id} type={field.type || 'text'} value={values[field.key]} onChange={set(field)} disabled={busy} maxLength={field.maxLength} min={field.min} max={field.max} step={field.step} aria-invalid={Boolean(errors[field.key])} autoComplete="off" />
              )}
              {field.help && <span className="field-hint">{field.help}</span>}
              {errors[field.key] && <span className="field-error">{errors[field.key]}</span>}
            </div>
          );
        })}
        {children}
      </form>
    </Modal>
  );
}

/** ISO time to a datetime-local input value. */
export function toLocalInput(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const pad = (n) => String(n).padStart(2, '0');
  return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + 'T' + pad(d.getHours()) + ':' + pad(d.getMinutes());
}

/** datetime-local input value to ISO. */
export function fromLocalInput(value) {
  if (!value) return null;
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
}

/** Comma-separated text to a trimmed list. */
export function splitList(text) {
  return String(text || '').split(',').map((v) => v.trim()).filter(Boolean);
}

/** Bytes as KB or MB. */
export function formatBytes(bytes) {
  const n = Number(bytes) || 0;
  if (n >= 1048576) return (n / 1048576).toFixed(1) + ' MB';
  if (n >= 1024) return Math.round(n / 1024) + ' KB';
  return n + ' B';
}
