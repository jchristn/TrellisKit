import { useId, useState } from 'react';
import { Audiences, FORUM_LIMITS, feedbackTransitions } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useForumList from '../../hooks/useForumList.js';
import { errorText } from '../../utils/engagement.js';
import { forumErrors } from '../../utils/forums.js';
import Modal from '../Modal.jsx';
import { ErrorBanner, Loading } from '../States.jsx';

const t = strings.forums;

function useSubmit(onDone) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const run = async (fn) => {
    setBusy(true);
    setError('');
    try {
      const result = await fn();
      onDone(result);
    } catch (err) {
      setError(errorText(err, forumErrors()));
      setBusy(false);
    }
  };
  return { busy, error, run };
}

function DialogFooter({ formId, busy, label, onClose, danger = false }) {
  return (
    <>
      <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
      <button type="submit" form={formId} className={'button ' + (danger ? 'button-danger' : 'button-primary')} disabled={busy}>{busy ? strings.common.loading : label}</button>
    </>
  );
}

/** Lock a thread with an optional reason shown on the thread (section 4.7). */
export function LockDialog({ thread, onClose, onDone }) {
  const { client } = useAuth();
  const formId = useId();
  const [reason, setReason] = useState('');
  const { busy, error, run } = useSubmit(onDone);
  return (
    <Modal open size="md" onClose={busy ? () => {} : onClose} title={t.lockTitle} footer={<DialogFooter formId={formId} busy={busy} label={t.lock} onClose={onClose} />}>
      <form id={formId} className="stack-form" onSubmit={(e) => { e.preventDefault(); run(() => client.lockThread(thread.id, reason.trim())); }}>
        <p>{t.lockBody}</p>
        <ErrorBanner message={error} />
        <div className="field">
          <label className="label-text" htmlFor={formId + '-reason'}>{t.lockReason}</label>
          <textarea id={formId + '-reason'} rows={2} maxLength={FORUM_LIMITS.lockReasonMax} value={reason} onChange={(e) => setReason(e.target.value)} disabled={busy} />
        </div>
      </form>
    </Modal>
  );
}

/** Forum picker of non-archived forums, optionally limited to those allowing a thread type. */
function ForumSelect({ id, value, onChange, excludeId, threadType, disabled }) {
  const { client } = useAuth();
  const list = useForumList(client);
  if (list.loading) return <Loading inline />;
  const forums = list.forums.filter((f) => !f.isArchived && f.id !== excludeId && (!threadType || (f.allowedThreadTypes || []).includes(threadType)));
  return (
    <>
      <ErrorBanner message={list.error ? t.loadError : ''} onRetry={list.reload} />
      <select id={id} value={value} onChange={(e) => onChange(e.target.value, forums.find((f) => f.id === e.target.value))} disabled={disabled} required>
        <option value="">{t.chooseForum}</option>
        {forums.map((f) => <option key={f.id} value={f.id}>{f.section ? f.section + ': ' + f.name : f.name}</option>)}
      </select>
    </>
  );
}

/** Move a thread to another forum the moderator also covers (section 4.7). */
export function MoveDialog({ thread, onClose, onDone }) {
  const { client } = useAuth();
  const formId = useId();
  const [forumId, setForumId] = useState('');
  const [reason, setReason] = useState('');
  const [notifyAuthor, setNotifyAuthor] = useState(true);
  const { busy, error, run } = useSubmit(onDone);
  return (
    <Modal open size="md" onClose={busy ? () => {} : onClose} title={t.moveTitle} footer={<DialogFooter formId={formId} busy={busy} label={t.move} onClose={onClose} />}>
      <form id={formId} className="stack-form" onSubmit={(e) => { e.preventDefault(); if (forumId) run(() => client.moveThread(thread.id, { forumId, reason: reason.trim(), notifyAuthor })); }}>
        <ErrorBanner message={error} />
        <div className="field">
          <label className="label-text" htmlFor={formId + '-forum'}>{t.moveForum}</label>
          <ForumSelect id={formId + '-forum'} value={forumId} onChange={setForumId} excludeId={thread.forum && thread.forum.id} threadType={thread.type} disabled={busy} />
        </div>
        <div className="field">
          <label className="label-text" htmlFor={formId + '-reason'}>{t.moveReason}</label>
          <input id={formId + '-reason'} value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} disabled={busy} />
        </div>
        <label className="toggle">
          <input type="checkbox" checked={notifyAuthor} onChange={(e) => setNotifyAuthor(e.target.checked)} disabled={busy} />
          <span className="toggle-track"><span className="toggle-thumb" /></span>
          <span>{t.moveNotify}</span>
        </label>
      </form>
    </Modal>
  );
}

/**
 * Promote a thread to the feed (section 8.1): optional card title and excerpt, pin (SystemAdmin
 * only), an audience that can only narrow, and the new-feed-post notification (on by default,
 * resolved decision 6).
 */
export function PromoteDialog({ thread, isAdmin, onClose, onDone }) {
  const { client } = useAuth();
  const formId = useId();
  const [title, setTitle] = useState('');
  const [excerpt, setExcerpt] = useState('');
  const [pin, setPin] = useState(false);
  const [notifyMembers, setNotifyMembers] = useState(true);
  const widest = Audiences.indexOf(thread.audience || 'Members');
  const audiences = Audiences.filter((_, i) => i >= Math.max(0, widest));
  const [audience, setAudience] = useState(audiences[0]);
  const { busy, error, run } = useSubmit(onDone);
  const submit = (e) => {
    e.preventDefault();
    const body = { title: title.trim() || null, excerpt: excerpt.trim() || null, audience, notifyMembers };
    if (isAdmin) body.pin = pin;
    run(() => client.promoteThread(thread.id, body));
  };
  return (
    <Modal open size="md" onClose={busy ? () => {} : onClose} title={t.promoteTitle} footer={<DialogFooter formId={formId} busy={busy} label={t.promote} onClose={onClose} />}>
      <form id={formId} className="stack-form" onSubmit={submit}>
        <p>{t.promoteBody}</p>
        <ErrorBanner message={error} />
        <div className="field">
          <label className="label-text" htmlFor={formId + '-title'}>{t.promoteCardTitle}</label>
          <input id={formId + '-title'} value={title} maxLength={FORUM_LIMITS.promoteTitleMax} placeholder={thread.title} onChange={(e) => setTitle(e.target.value)} disabled={busy} />
        </div>
        <div className="field">
          <label className="label-text" htmlFor={formId + '-excerpt'}>{t.promoteCardExcerpt}</label>
          <textarea id={formId + '-excerpt'} rows={2} value={excerpt} maxLength={FORUM_LIMITS.promoteExcerptMax} onChange={(e) => setExcerpt(e.target.value)} disabled={busy} />
        </div>
        {audiences.length > 1 && (
          <div className="field">
            <label className="label-text" htmlFor={formId + '-audience'}>{t.promoteAudience}</label>
            <select id={formId + '-audience'} value={audience} onChange={(e) => setAudience(e.target.value)} disabled={busy}>
              {audiences.map((a) => <option key={a} value={a}>{t.audiences[a] || a}</option>)}
            </select>
          </div>
        )}
        {isAdmin && (
          <label className="toggle">
            <input type="checkbox" checked={pin} onChange={(e) => setPin(e.target.checked)} disabled={busy} />
            <span className="toggle-track"><span className="toggle-thumb" /></span>
            <span>{t.promotePin}</span>
          </label>
        )}
        <label className="toggle">
          <input type="checkbox" checked={notifyMembers} onChange={(e) => setNotifyMembers(e.target.checked)} disabled={busy} />
          <span className="toggle-track"><span className="toggle-thumb" /></span>
          <span>{t.promoteNotify}</span>
        </label>
      </form>
    </Modal>
  );
}

/** Change a Feedback thread's review status (section 7); `Reviewed` is set from a comment's menu. */
export function FeedbackStatusDialog({ thread, isAuthor, onClose, onDone }) {
  const { client } = useAuth();
  const formId = useId();
  const options = feedbackTransitions(thread, { isAuthor });
  const [status, setStatus] = useState(options[0] || '');
  const { busy, error, run } = useSubmit(onDone);
  return (
    <Modal open size="sm" onClose={busy ? () => {} : onClose} title={t.feedbackStatusTitle} footer={<DialogFooter formId={formId} busy={busy} label={strings.common.save} onClose={onClose} />}>
      <form id={formId} className="stack-form" onSubmit={(e) => { e.preventDefault(); if (status) run(() => client.setFeedbackStatus(thread.id, status)); }}>
        <ErrorBanner message={error} />
        <fieldset className="reason-list">
          <legend className="label-text">{t.feedbackStatusTitle}</legend>
          {options.map((value) => (
            <label key={value} className={'reason-option' + (status === value ? ' selected' : '')}>
              <input type="radio" name={formId + '-status'} value={value} checked={status === value} onChange={() => setStatus(value)} disabled={busy} />
              <span><strong>{value === 'Closed' && isAuthor && !thread.viewer.canSetFeedbackStatus ? t.withdraw : t.feedbackStatus[value]}</strong></span>
            </label>
          ))}
        </fieldset>
      </form>
    </Modal>
  );
}

/** Open a feed post for discussion in a forum (section 8.2). */
export function DiscussDialog({ post, onClose, onDone }) {
  const { client } = useAuth();
  const formId = useId();
  const [forumId, setForumId] = useState('');
  const [title, setTitle] = useState('');
  const { busy, error, run } = useSubmit(onDone);
  return (
    <Modal open size="md" onClose={busy ? () => {} : onClose} title={t.discussTitle} footer={<DialogFooter formId={formId} busy={busy} label={t.discuss} onClose={onClose} />}>
      <form id={formId} className="stack-form" onSubmit={(e) => { e.preventDefault(); if (forumId) run(() => client.discussFeedPost(post.id, { forumId, title: title.trim() || null, labels: [], tags: {} })); }}>
        <p>{t.discussBody}</p>
        <ErrorBanner message={error} />
        <div className="field">
          <label className="label-text" htmlFor={formId + '-forum'}>{t.discussForum}</label>
          <ForumSelect id={formId + '-forum'} value={forumId} onChange={setForumId} threadType="Discussion" disabled={busy} />
        </div>
        <div className="field">
          <label className="label-text" htmlFor={formId + '-title'}>{t.discussThreadTitle}</label>
          <input id={formId + '-title'} value={title} maxLength={FORUM_LIMITS.titleMax} placeholder={post.title || ''} onChange={(e) => setTitle(e.target.value)} disabled={busy} />
        </div>
      </form>
    </Modal>
  );
}
