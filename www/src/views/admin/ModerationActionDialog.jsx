import { useState } from 'react';
import { MODERATION_REASON_MIN, MODERATION_TEXT_MAX, ReportReasons, defaultNotifyAuthor } from '@trelliskit/shared';
import strings from '../../strings.js';
import Modal from '../../components/Modal.jsx';
import { ErrorBanner } from '../../components/States.jsx';
import { errorText } from '../../utils/engagement.js';

const s = strings.admin.moderation;

/** Validate an action form; returns `{ reason?, note? }` messages. */
export function validateModerationForm({ action, reason, note }) {
  const errors = {};
  const r = String(reason || '').trim();
  if (action !== 'Dismiss' && (r.length < MODERATION_REASON_MIN || r.length > MODERATION_TEXT_MAX)) errors.reason = s.actReasonInvalid;
  if (String(note || '').length > MODERATION_TEXT_MAX) errors.note = s.actNoteTooLong;
  return errors;
}

/**
 * Confirm a moderation action (contract section 9.3) or a dismissal (10.2): a reason (report reason
 * codes offered as quick picks, or free text), an internal note, notify the author (default on for
 * Remove and Restore), and resolve open reports (default on).
 * @param {{ action: 'Hide'|'Unhide'|'Remove'|'Restore'|'Dismiss', targetType: string, targetId: string, api: object, suggested?: string, onClose: () => void, onDone: (result: object) => void }} props
 */
function ModerationActionDialog({ action, targetType, targetId, api, suggested = '', onClose, onDone }) {
  const [reason, setReason] = useState(suggested);
  const [note, setNote] = useState('');
  const [notifyAuthor, setNotifyAuthor] = useState(defaultNotifyAuthor(action));
  const [resolveReports, setResolveReports] = useState(true);
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const what = (s.targetTypes[targetType] || targetType).toLowerCase();
  const label = s.act[action];

  const submit = async (e) => {
    e.preventDefault();
    const found = validateModerationForm({ action, reason, note });
    setErrors(found);
    if (Object.keys(found).length) return;
    setBusy(true);
    setError('');
    try {
      const result = action === 'Dismiss'
        ? await api.dismissReports(targetType, targetId, note.trim() || reason.trim())
        : await api.moderate({ targetType, targetId, action, reason: reason.trim(), note: note.trim() || undefined, notifyAuthor, resolveReports });
      onDone(result);
    } catch (err) {
      setError(err && err.status === 403 ? s.forbidden : errorText(err));
      setBusy(false);
    }
  };

  return (
    <Modal open size="sm" onClose={busy ? () => {} : onClose} title={s.actTitle(label, what)}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="moderation-action-form" className={'button ' + (action === 'Remove' || action === 'Hide' ? 'button-danger' : 'button-primary')} disabled={busy}>{label}</button>
        </>
      )}
    >
      <form id="moderation-action-form" className="stack-form" onSubmit={submit} noValidate>
        <p className="confirm-message">{s.actBody[action]}</p>
        <ErrorBanner message={error} />
        {action !== 'Dismiss' && (
          <div className="field">
            <label className="label-text" htmlFor="mod-reason">{s.actReason}</label>
            <input id="mod-reason" value={reason} onChange={(e) => setReason(e.target.value)} list="mod-reason-codes" maxLength={MODERATION_TEXT_MAX} aria-invalid={Boolean(errors.reason)} aria-describedby="mod-reason-hint" disabled={busy} />
            <datalist id="mod-reason-codes">{ReportReasons.map((code) => <option key={code} value={strings.safety.reasons[code].label} />)}</datalist>
            <span id="mod-reason-hint" className="muted small">{s.actReasonHint}</span>
            {errors.reason && <span className="field-error" role="alert">{errors.reason}</span>}
          </div>
        )}
        <div className="field">
          <label className="label-text" htmlFor="mod-note">{s.actNote}</label>
          <textarea id="mod-note" rows={2} value={note} onChange={(e) => setNote(e.target.value)} disabled={busy} aria-invalid={Boolean(errors.note)} />
          {errors.note && <span className="field-error" role="alert">{errors.note}</span>}
        </div>
        {action !== 'Dismiss' && (
          <>
            <label className="check-field"><input type="checkbox" checked={notifyAuthor} onChange={(e) => setNotifyAuthor(e.target.checked)} disabled={busy} /><span>{s.notifyAuthor}</span></label>
            <label className="check-field"><input type="checkbox" checked={resolveReports} onChange={(e) => setResolveReports(e.target.checked)} disabled={busy} /><span>{s.resolveReports}</span></label>
          </>
        )}
      </form>
    </Modal>
  );
}

export default ModerationActionDialog;
