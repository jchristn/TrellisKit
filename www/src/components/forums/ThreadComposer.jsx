import { useId, useState } from 'react';
import AudienceField, { useAudiencePicker } from '../AudienceField.jsx';
import {
  FORUM_LIMITS, ThreadType, emptyPollDraft, metadataForSave, metadataOf, pollDraftFromPoll, pollEditPayload, pollOptionsLocked, pollPayload, threadCreateBody,
  validatePollDraft, validatePollEdit, validateThreadDraft
} from '@trelliskit/shared';
import strings from '../../strings.js';
import { Banner, ErrorBanner } from '../States.jsx';
import { errorText, mutedText, composerErrorText } from '../../utils/engagement.js';
import { forumErrors } from '../../utils/forums.js';
import MarkdownEditor from '../feed/MarkdownEditor.jsx';
import AttachmentList from '../feed/AttachmentList.jsx';
import useMediaUploads from '../feed/useMediaUploads.js';
import { mapFieldErrors } from '../feed/PostComposer.jsx';
import MetadataEditor from '../MetadataEditor.jsx';
import PollBuilder from './PollBuilder.jsx';
import { useAccessPrompt } from '../AccessPrompt.jsx';

const t = strings.forums;
const c = t.composer;

/**
 * Thread composer (PHASE3_CONTRACT.md sections 4.4 and 4.5).
 *
 * - `create`: a type picker limited to the forum's `viewer.allowedThreadTypes`, title, Markdown
 *   body with mentions, attachments (Feedback requires a video), the poll builder for Poll
 *   threads, the review focus for Feedback threads, and labels and tags.
 * - `edit`: the author's edit of title, body, attachments, focus, labels, and tags, and of an open
 *   poll (section 6.4): every poll field before the first vote, only the close time after it
 *   (a `409 PollOptionsLocked` from a vote that arrived meanwhile switches to the locked form).
 * - `metadata`: a moderator's edit of title, type (Discussion or Question), labels, and tags only;
 *   moderators never edit a member's words.
 *
 * @param {{ client: object, forum?: object, thread?: object|null, mode?: 'create'|'edit'|'metadata', perms: object,
 *   onSaved: (thread: object) => void, onCancel?: () => void, uploadOptions?: object, now?: Date }} props
 */
function ThreadComposer({ client, forum = null, thread = null, mode = 'create', perms, onSaved, onCancel, uploadOptions = {}, now }) {
  const formId = useId();
  const creating = mode === 'create';
  const metadataOnly = mode === 'metadata';
  const allowedTypes = creating ? ((forum && forum.viewer && forum.viewer.allowedThreadTypes) || (forum && forum.allowedThreadTypes) || []) : metadataOnly && thread && (thread.type === 'Discussion' || thread.type === 'Question') ? ['Discussion', 'Question'] : [thread ? thread.type : ThreadType.Discussion];
  const [type, setType] = useState(thread ? thread.type : allowedTypes[0] || '');
  const [title, setTitle] = useState(thread ? thread.title || '' : '');
  const [body, setBody] = useState(thread ? thread.body || '' : '');
  const [focus, setFocus] = useState(thread && thread.feedback ? thread.feedback.focus || '' : '');
  const editablePoll = mode === 'edit' && thread && thread.poll && !thread.poll.isClosed ? thread.poll : null;
  const [poll, setPoll] = useState(() => (editablePoll ? pollDraftFromPoll(editablePoll) : emptyPollDraft()));
  const [pollLocked, setPollLocked] = useState(() => pollOptionsLocked(editablePoll));
  const accessPrompt = useAccessPrompt();
  const picker = useAudiencePicker({ feature: 'forums', isSystemAdmin: Boolean(perms.isAdmin), floor: forum && forum.audience });
  const [metadata, setMetadata] = useState(() => metadataOf(thread));
  const [metaProblems, setMetaProblems] = useState([]);
  const [problems, setProblems] = useState({});
  const [pollProblems, setPollProblems] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const forumAllowsMedia = !forum || forum.allowMedia !== false;
  const forumAllowsVideo = !forum || forum.allowVideo !== false;
  const allowMedia = Boolean(perms.canAttachMedia) && forumAllowsMedia;
  const allowVideo = Boolean(perms.canAttachVideo) && forumAllowsVideo && allowMedia;
  const uploads = useMediaUploads({
    client,
    initial: thread ? thread.attachments || [] : [],
    maxCount: FORUM_LIMITS.attachments,
    maxVideos: FORUM_LIMITS.videosPerThread,
    allowMedia,
    allowVideo,
    isAdmin: perms.isAdmin,
    onError: setError,
    ...uploadOptions
  });
  const videoCount = uploads.items.filter((x) => x.kind === 'Video' || (x.media && x.media.kind === 'Video')).length;

  if (creating && !allowedTypes.length) return <Banner tone="info">{c.noTypes}</Banner>;

  const submit = async (e) => {
    e.preventDefault();
    setError('');
    if (uploads.busy) {
      setError(strings.composer.waitForUploads);
      return;
    }
    const foundPoll = creating && type === ThreadType.Poll ? validatePollDraft(poll, { now: now || new Date() })
      : editablePoll ? validatePollEdit(poll, { locked: pollLocked, now: now || new Date() }) : {};
    const found = validateThreadDraft({
      type, title, body: metadataOnly ? 'x' : body, mediaCount: uploads.mediaIds.length, videoCount: metadataOnly ? 1 : videoCount, focus, allowedTypes: creating || metadataOnly ? allowedTypes : undefined, pollProblems: foundPoll
    });
    setProblems(found);
    setPollProblems(foundPoll);
    if (Object.keys(found).length || metaProblems.length) return;
    const meta = metadataForSave(metadata, { isStaff: Boolean(perms.isStaff) });
    setBusy(true);
    try {
      let saved;
      if (creating) {
        saved = await client.createThread(forum.id, threadCreateBody({ type, title, body, mediaIds: uploads.mediaIds, poll: type === ThreadType.Poll ? pollPayload(poll) : null, focus, audience: picker.visible ? picker.value : null, ...meta }));
      } else if (metadataOnly) {
        const changes = { title: title.trim(), ...meta };
        if (type !== thread.type) changes.type = type;
        saved = await client.updateThread(thread.id, changes);
      } else {
        const changes = { title: title.trim(), body: body.trim(), mediaIds: uploads.mediaIds, ...meta };
        if (thread.type === ThreadType.Feedback) changes.feedback = { focus: focus.trim() };
        const pollChanges = editablePoll ? pollEditPayload(poll, pollLocked ? { ...editablePoll, totalVoters: Math.max(1, Number(editablePoll.totalVoters) || 0) } : editablePoll) : null;
        if (pollChanges) changes.poll = pollChanges;
        saved = await client.updateThread(thread.id, changes);
      }
      uploads.reset();
      onSaved(saved);
    } catch (err) {
      if (accessPrompt.handle(err)) return;
      if (err && err.code === 'PollOptionsLocked') {
        setPollLocked(true);
        setPoll((draft) => ({ ...pollDraftFromPoll(editablePoll), closesUtc: draft.closesUtc }));
      }
      const fields = mapFieldErrors(err && err.fieldErrors);
      setProblems((current) => ({ ...current, ...fields }));
      setError(errorText(err, forumErrors()));
    } finally {
      setBusy(false);
    }
  };

  const text = (code) => (code ? c.problems[code] || code : '');
  const bodyOptional = type === ThreadType.Poll || type === ThreadType.Feedback;

  return (
    <form id={formId} className="composer thread-composer" onSubmit={submit} noValidate>
      {perms.muted && <Banner tone="warning" icon="mute">{mutedText(perms)}</Banner>}
      {perms.postingBlocked && <Banner tone="warning" icon="ban">{strings.composer.postingNotAllowed}</Banner>}
      <ErrorBanner message={composerErrorText(error, uploads)} onDismiss={() => setError('')} />
      {allowedTypes.length > 1 && (
        <fieldset className="type-picker" aria-invalid={Boolean(problems.type)}>
          <legend className="label-text">{c.typeLabel}</legend>
          <div className="type-picker-options">
            {allowedTypes.map((value) => (
              <label key={value} className={'type-option' + (type === value ? ' selected' : '')}>
                <input type="radio" name={formId + '-type'} value={value} checked={type === value} onChange={() => setType(value)} disabled={busy} />
                <span>
                  <strong>{t.types[value]}</strong>
                  <span className="muted small">{t.typeHints[value]}</span>
                </span>
              </label>
            ))}
          </div>
          {problems.type && <span className="field-error" role="alert">{text(problems.type)}</span>}
        </fieldset>
      )}
      <div className="field">
        <label className="label-text" htmlFor={formId + '-title'}>{type === ThreadType.Poll ? strings.forums.poll.title + ': ' + c.titleLabel : c.titleLabel}</label>
        <input id={formId + '-title'} value={title} onChange={(e) => setTitle(e.target.value)} maxLength={FORUM_LIMITS.titleMax + 20} disabled={busy} aria-invalid={Boolean(problems.title)} aria-describedby={formId + '-title-hint'} />
        <span id={formId + '-title-hint'} className="muted small">{c.titleHint}</span>
        {problems.title && <span className="field-error" role="alert">{text(problems.title)}</span>}
      </div>
      {!metadataOnly && (
        <MarkdownEditor
          id={formId + '-body'}
          label={bodyOptional ? c.bodyOptional : c.bodyLabel}
          value={body}
          onChange={setBody}
          client={client}
          maxLength={FORUM_LIMITS.bodyMax}
          allowHeadings
          placeholder={c.bodyPlaceholder}
          rows={6}
          disabled={busy}
          context={thread ? { type: 'Thread', id: thread.id } : null}
          error={text(problems.body)}
        />
      )}
      {!metadataOnly && type === ThreadType.Feedback && (
        <div className="field">
          <label className="label-text" htmlFor={formId + '-focus'}>{c.focusLabel}</label>
          <textarea id={formId + '-focus'} rows={2} value={focus} onChange={(e) => setFocus(e.target.value)} maxLength={FORUM_LIMITS.focusMax + 20} disabled={busy} aria-invalid={Boolean(problems.focus)} />
          {problems.focus && <span className="field-error" role="alert">{text(problems.focus)}</span>}
        </div>
      )}
      {!metadataOnly && (
        <>
          {type === ThreadType.Feedback && <p className="muted small">{c.videoRequired}</p>}
          <AttachmentList uploads={uploads} allowMedia={allowMedia} allowVideo={allowVideo} maxCount={FORUM_LIMITS.attachments} label={type === ThreadType.Feedback ? c.attachVideo : c.attach} disabled={busy} />
          {problems.media && <span className="field-error" role="alert">{text(problems.media)}</span>}
        </>
      )}
      {creating && picker.visible && <AudienceField id={formId + '-audience'} picker={picker} disabled={busy} />}
      {creating && type === ThreadType.Poll && <PollBuilder value={poll} onChange={setPoll} problems={pollProblems} disabled={busy} idPrefix={formId + '-poll'} />}
      {editablePoll && <PollBuilder value={poll} onChange={setPoll} problems={pollProblems} disabled={busy} idPrefix={formId + '-poll'} locked={pollLocked} />}
      <MetadataEditor value={metadata} onChange={(next, found) => { setMetadata(next); setMetaProblems(found); }} isStaff={Boolean(perms.isStaff)} client={client} targetType="Thread" disabled={busy} collapsible={!metadataOf(thread).labels.length && !Object.keys(metadataOf(thread).tags).length} />
      <div className="form-actions">
        {onCancel && <button type="button" className="button button-secondary" onClick={onCancel} disabled={busy}>{c.cancel}</button>}
        <button type="submit" className="button button-primary" disabled={busy || (!metadataOnly && (perms.muted || perms.postingBlocked))}>{busy ? c.submitting : creating ? c.submit : c.submitSave}</button>
      </div>
    </form>
  );
}

export default ThreadComposer;
