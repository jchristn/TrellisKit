import { useEffect, useId, useRef, useState } from 'react';
import { REPORT_DETAILS_MAX, ReportReasons, reportProblems } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import Modal from '../Modal.jsx';
import { ErrorBanner } from '../States.jsx';
import { errorText } from '../../utils/engagement.js';
import BlockDialog from './BlockDialog.jsx';

const s = strings.safety;

/**
 * Report a post, comment, or member (contract section 9.1): reason codes with descriptions,
 * optional details (required for "Something else"), a thank-you step, and an offer to block the
 * author afterwards (never for staff, never for yourself).
 * @param {{ target: { type: 'FeedPost'|'Comment'|'User', id: string }, author?: object|null, onClose: () => void, onBlocked?: (user: object) => void }} props
 */
function ReportDialog({ target, author = null, onClose, onBlocked }) {
  const { client, me } = useAuth();
  const formId = useId();
  const [reason, setReason] = useState('');
  const [details, setDetails] = useState('');
  const [problems, setProblems] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);
  const [blocking, setBlocking] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const formRef = useRef(null);

  // Bring the first validation or server error into view (the reason list can push it below
  // the fold on small screens) and move focus to the field that needs attention.
  useEffect(() => {
    if (!attempt || !formRef.current) return;
    const first = formRef.current.querySelector('.field-error, .banner');
    if (!first) return;
    if (typeof first.scrollIntoView === 'function') first.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    const field = problems.details ? formRef.current.querySelector('textarea') : (problems.reason ? formRef.current.querySelector('input[type="radio"]') : null);
    if (field) field.focus({ preventScroll: true });
  }, [attempt, problems, error]);
  const what = s.reportWhat[target.type] || s.reportWhat.FeedPost;
  const canOfferBlock = author && author.id && !author.isDeleted && !author.isStaff && (!me || author.id !== me.id) && author.handle;

  const submit = async (e) => {
    e.preventDefault();
    const found = reportProblems({ reason, details });
    setProblems(found);
    setError('');
    if (Object.keys(found).length) {
      setAttempt((n) => n + 1);
      return;
    }
    setBusy(true);
    try {
      const body = { targetType: target.type, targetId: target.id, reason };
      if (details.trim()) body.details = details.trim();
      await client.createReport(body);
      setDone(true);
    } catch (err) {
      setError(errorText(err, s.reportErrors));
      setAttempt((n) => n + 1);
    } finally {
      setBusy(false);
    }
  };

  if (blocking) return <BlockDialog user={author} onClose={onClose} onDone={(user) => { if (onBlocked) onBlocked(user); onClose(); }} />;

  if (done) {
    return (
      <Modal
        open
        size="sm"
        onClose={onClose}
        title={s.thanks}
        footer={(
          <>
            {canOfferBlock && <button type="button" className="button button-danger-outline" onClick={() => setBlocking(true)}>{s.alsoBlock(author.handle)}</button>}
            <button type="button" className="button button-primary" onClick={onClose}>{s.done}</button>
          </>
        )}
      >
        <p>{(s.thanksBodyFor && s.thanksBodyFor[target.type]) || s.thanksBody}</p>
        {canOfferBlock && <p className="muted small">{s.alsoBlockBody}</p>}
      </Modal>
    );
  }

  const messages = { reasonRequired: s.reasonRequired, detailsRequired: s.detailsRequired, detailsTooLong: s.detailsTooLong };
  return (
    <Modal
      open
      size="md"
      onClose={busy ? () => {} : onClose}
      title={s.reportTitle(what)}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form={formId} className="button button-danger" disabled={busy}>{busy ? s.sending : s.submitReport}</button>
        </>
      )}
    >
      <form id={formId} ref={formRef} className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} />
        <fieldset className="reason-list" aria-invalid={Boolean(problems.reason)}>
          <legend className="label-text">{s.reasonLabel}</legend>
          {ReportReasons.map((code) => (
            <label key={code} className={'reason-option' + (reason === code ? ' selected' : '')}>
              <input type="radio" name={formId + '-reason'} value={code} checked={reason === code} onChange={() => setReason(code)} disabled={busy} />
              <span>
                <strong>{s.reasons[code].label}</strong>
                <span className="muted small">{s.reasons[code].description}</span>
              </span>
            </label>
          ))}
          {problems.reason && <span className="field-error" role="alert">{messages[problems.reason]}</span>}
        </fieldset>
        <div className="field">
          <label className="label-text" htmlFor={formId + '-details'}>{reason === 'Other' ? s.detailsLabel : s.detailsOptional}</label>
          <textarea id={formId + '-details'} rows={3} value={details} maxLength={REPORT_DETAILS_MAX + 50} onChange={(e) => setDetails(e.target.value)} disabled={busy} aria-invalid={Boolean(problems.details)} />
          <span className="muted small">{details.length} / {REPORT_DETAILS_MAX}</span>
          {problems.details && <span className="field-error" role="alert">{messages[problems.details]}</span>}
        </div>
      </form>
    </Modal>
  );
}

export default ReportDialog;
