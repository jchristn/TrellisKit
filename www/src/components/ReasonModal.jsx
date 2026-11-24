import { useState } from 'react';
import strings from '../strings.js';
import Modal from './Modal.jsx';
import { ErrorBanner } from './States.jsx';

const c = strings.admin.common;

/**
 * Confirmation that requires a reason (ban, revoke, end comp). `onConfirm(reason, extra)` may
 * throw; its message is shown and the dialog stays open. `children` can render extra fields.
 * Remount with a new `key` to reset.
 */
function ReasonModal({ open, title, message, confirmLabel = strings.common.confirm, danger = true, onConfirm, onCancel, children, validateExtra }) {
  const [reason, setReason] = useState('');
  const [error, setError] = useState('');
  const [fieldError, setFieldError] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    if (!reason.trim()) {
      setFieldError(c.reasonRequired);
      return;
    }
    const extraError = validateExtra ? validateExtra() : '';
    if (extraError) {
      setError(extraError);
      return;
    }
    setFieldError('');
    setError('');
    setBusy(true);
    try {
      await onConfirm(reason.trim());
    } catch (err) {
      setError((err && err.message) || strings.common.errorGeneric);
      setBusy(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={busy ? () => {} : onCancel}
      title={title}
      size="sm"
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onCancel} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="reason-form" className={'button ' + (danger ? 'button-danger' : 'button-primary')} disabled={busy}>{confirmLabel}</button>
        </>
      )}
    >
      <form id="reason-form" className="stack-form" onSubmit={submit} noValidate>
        {message && <p className="confirm-message">{message}</p>}
        <ErrorBanner message={error} />
        {children}
        <div className="field">
          <label className="label-text" htmlFor="reason-input">{c.reason}</label>
          <textarea id="reason-input" rows={3} value={reason} onChange={(e) => setReason(e.target.value)} disabled={busy} aria-invalid={Boolean(fieldError)} aria-describedby="reason-hint" />
          <span id="reason-hint" className="muted small">{c.reasonHint}</span>
          {fieldError && <span className="field-error" role="alert">{fieldError}</span>}
        </div>
      </form>
    </Modal>
  );
}

export default ReasonModal;
