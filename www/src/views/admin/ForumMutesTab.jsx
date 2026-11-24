import { useCallback, useId, useMemo, useState } from 'react';
import { MODERATION_REASON_MIN, MODERATION_TEXT_MAX } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import useForumList from '../../hooks/useForumList.js';
import { authorName, errorText } from '../../utils/engagement.js';
import { forumErrors } from '../../utils/forums.js';
import { formatShortDateTime } from '../../utils/format.js';
import { localInputToIso } from '../../utils/time.js';
import Modal from '../../components/Modal.jsx';
import EntityPicker, { pickerSearches } from '../../components/admin/EntityPicker.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import TableColumns from '../../components/TableColumns.jsx';

const s = strings.admin.forumMutes;
const col = s.columns;

/** Mute a member in one forum (PHASE3_CONTRACT.md section 11.3); no end date mutes until lifted (resolved decision 15). */
function CreateMuteDialog({ userId: fixedUser, onClose, onDone }) {
  const { client, isSystemAdmin } = useAdminAuth();
  const formId = useId();
  const forums = useForumList(client, null, { admin: isSystemAdmin });
  const searches = useMemo(() => pickerSearches(client, { isSystemAdmin }), [client, isSystemAdmin]);
  const [userPick, setUserPick] = useState([]);
  const userId = fixedUser || (userPick[0] ? userPick[0].id : '');
  const [forumId, setForumId] = useState('');
  const [until, setUntil] = useState('');
  const [reason, setReason] = useState('');
  const [notifyMember, setNotifyMember] = useState(true);
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const submit = async (e) => {
    e.preventDefault();
    const found = {};
    if (!userId.trim()) found.userId = s.userRequired;
    if (!forumId) found.forumId = s.forumRequired;
    const r = reason.trim();
    if (r.length < MODERATION_REASON_MIN || r.length > MODERATION_TEXT_MAX) found.reason = s.reasonRequired;
    setErrors(found);
    if (Object.keys(found).length) return;
    setBusy(true);
    setError('');
    try {
      onDone(await client.createForumMute({ userId: userId.trim(), forumId, untilUtc: until ? localInputToIso(until) : null, reason: r, notifyMember }));
    } catch (err) {
      setError(errorText(err, forumErrors()));
      setBusy(false);
    }
  };
  return (
    <Modal open size="md" onClose={busy ? () => {} : onClose} title={s.create}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form={formId} className="button button-danger" disabled={busy}>{s.create}</button>
        </>
      )}
    >
      <form id={formId} className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} />
        {!fixedUser && (
          <div className="field">
            <EntityPicker id={formId + '-user'} label={s.user} value={userPick} onChange={setUserPick} search={searches.members} placeholder={strings.admin.eventsAdmin.memberPlaceholder} disabled={busy} error={errors.userId || ''} />
          </div>
        )}
        <div className="field">
          <label className="label-text" htmlFor={formId + '-forum'}>{s.forum}</label>
          {forums.loading ? <Loading inline /> : (
            <select id={formId + '-forum'} value={forumId} onChange={(e) => setForumId(e.target.value)} disabled={busy} aria-invalid={Boolean(errors.forumId)}>
              <option value="">{strings.forums.chooseForum}</option>
              {forums.forums.map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}
            </select>
          )}
          {errors.forumId && <span className="field-error" role="alert">{errors.forumId}</span>}
        </div>
        <div className="field">
          <label className="label-text" htmlFor={formId + '-until'}>{s.until}</label>
          <input id={formId + '-until'} type="datetime-local" value={until} onChange={(e) => setUntil(e.target.value)} disabled={busy} aria-describedby={formId + '-until-hint'} />
          <span id={formId + '-until-hint'} className="muted small">{s.untilHint}</span>
        </div>
        <div className="field">
          <label className="label-text" htmlFor={formId + '-reason'}>{s.reason}</label>
          <textarea id={formId + '-reason'} rows={2} value={reason} onChange={(e) => setReason(e.target.value)} maxLength={MODERATION_TEXT_MAX} disabled={busy} aria-invalid={Boolean(errors.reason)} />
          {errors.reason && <span className="field-error" role="alert">{errors.reason}</span>}
        </div>
        <label className="toggle">
          <input type="checkbox" checked={notifyMember} onChange={(e) => setNotifyMember(e.target.checked)} disabled={busy} />
          <span className="toggle-track"><span className="toggle-thumb" /></span>
          <span>{s.notifyMember}</span>
        </label>
      </form>
    </Modal>
  );
}

/**
 * Forum mutes (PHASE3_CONTRACT.md section 11.3): the active (or all) mutes in the caller's scope,
 * mute a member in a forum, and lift a mute. With `userId` it lists one member's mutes (the member
 * detail drawer).
 * @param {{ userId?: string, compact?: boolean }} props
 */
function ForumMutesTab({ userId = null, compact = false }) {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [activeOnly, setActiveOnly] = useState(true);
  const [creating, setCreating] = useState(false);
  const fetchPage = useCallback((cursor, signal) => client.enumerateForumMutes({ continuationToken: cursor, maxResults: compact ? 10 : 50, filters: { activeOnly: activeOnly ? true : 'false', userId: userId || undefined } }, signal), [client, activeOnly, userId, compact]);
  const list = useCursorList(fetchPage);
  const end = async (row) => {
    try {
      await client.endForumMute(row.id);
      toast.success(s.ended);
      list.refresh();
    } catch (err) {
      toast.error(errorText(err, forumErrors()));
    }
  };
  const Heading = compact ? 'h3' : 'h2';
  return (
    <section className={compact ? 'detail-block' : 'panel'} aria-labelledby={'forum-mutes-' + (userId || 'all')}>
      <div className="row-between">
        <Heading id={'forum-mutes-' + (userId || 'all')}>{s.title}</Heading>
        <div className="badge-row">
          <label className="small"><input type="checkbox" checked={activeOnly} onChange={(e) => setActiveOnly(e.target.checked)} /> {s.activeOnly}</label>
          <button type="button" className="button button-secondary button-sm" onClick={() => setCreating(true)}>{s.create}</button>
        </div>
      </div>
      <ErrorBanner message={list.error ? errorText(list.error) : ''} onRetry={list.refresh} />
      {list.loading && <Loading inline />}
      {list.loaded && !list.items.length && !list.loading && !list.error && <p className="muted small">{s.empty}</p>}
      {list.items.length > 0 && (
        <div className="table-scroll">
          <TableColumns id="tk.admin.ForumMutesTab"><table className="data-table">
            <caption className="visually-hidden">{s.title}</caption>
            <thead><tr>{(userId ? ['forum', 'until', 'reason', 'by', 'actions'] : ['member', 'forum', 'until', 'reason', 'by', 'created', 'actions']).map((k) => <th key={k} scope="col">{k === 'actions' ? <span className="visually-hidden">{col.actions}</span> : col[k]}</th>)}</tr></thead>
            <tbody>
              {list.items.map((row) => (
                <tr key={row.id}>
                  {!userId && <td>{authorName(row.user)}</td>}
                  <td>{row.forum ? row.forum.name : '-'}</td>
                  <td className="nowrap">{row.untilUtc ? formatShortDateTime(row.untilUtc) : s.indefinite}</td>
                  <td>{row.reason}</td>
                  <td>{row.mutedBy ? authorName(row.mutedBy) : '-'}</td>
                  {!userId && <td className="nowrap">{formatShortDateTime(row.createdUtc)}</td>}
                  <td className="cell-actions">{!row.endedUtc && <button type="button" className="button button-secondary button-sm" onClick={() => end(row)}>{s.end}</button>}</td>
                </tr>
              ))}
            </tbody>
          </table></TableColumns>
        </div>
      )}
      {list.loaded && !list.done && !list.loading && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{s.loadMore}</button>}
      {creating && <CreateMuteDialog userId={userId} onClose={() => setCreating(false)} onDone={() => { setCreating(false); toast.success(s.created); list.refresh(); }} />}
    </section>
  );
}

export default ForumMutesTab;
