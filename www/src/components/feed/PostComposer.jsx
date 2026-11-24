import { useMemo, useState } from 'react';
import { AccessModel, DEFAULT_MEDIA_LIMITS, accessOf, metadataForSave, metadataOf, scalarLength } from '@trelliskit/shared';
import AudienceField, { useAudiencePicker } from '../AudienceField.jsx';
import strings from '../../strings.js';
import EntityPicker, { pickerSearches } from '../admin/EntityPicker.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { ErrorBanner, Banner } from '../States.jsx';
import { deviceTimeZone, errorText, mutedText, composerErrorText } from '../../utils/engagement.js';
import { isoToLocalInput, localInputToIso } from '../../utils/time.js';
import MarkdownEditor from './MarkdownEditor.jsx';
import MetadataEditor from '../MetadataEditor.jsx';
import AttachmentList from './AttachmentList.jsx';
import LinkPreviewCard from './LinkPreviewCard.jsx';
import useMediaUploads from './useMediaUploads.js';
import useLinkPreview from './useLinkPreview.js';
import { useAccessPrompt } from '../AccessPrompt.jsx';

const c = strings.composer;
const TITLE_MAX = 140;
const MIN_LEAD_MS = 60 * 1000;
const MAX_LEAD_MS = 365 * 86400 * 1000;

/** Validate a draft; returns `{ field: message }` (empty when valid). */
export function validatePostDraft({ title, body, mediaCount, publishAt, maxBodyLength }, now = new Date()) {
  const errors = {};
  if (scalarLength(title) > TITLE_MAX) errors.title = c.titleTooLong;
  if (!String(body || '').trim() && mediaCount === 0) errors.body = c.bodyRequired;
  if (scalarLength(body) > maxBodyLength) errors.body = c.tooLong(maxBodyLength);
  if (publishAt) {
    const at = new Date(publishAt).getTime();
    if (!Number.isFinite(at) || at - now.getTime() < MIN_LEAD_MS || at - now.getTime() > MAX_LEAD_MS) errors.publishAtUtc = c.scheduleInvalid;
  }
  return errors;
}

/** Map server `fieldErrors` keys (including `mediaIds[1]`) onto composer fields. */
export function mapFieldErrors(fieldErrors) {
  const out = {};
  for (const [key, message] of Object.entries(fieldErrors || {})) {
    if (key.startsWith('mediaIds')) out.media = message;
    else if (key.startsWith('altText') || key.includes('altText')) out.media = message;
    else out[key] = message;
  }
  return out;
}

/**
 * Feed post composer (contract sections 5.4 and 5.5) for members with posting rights and for the
 * admin console. Title, Markdown body with preview and mention autocomplete, attachments
 * (uploads with progress, resume, processing state, alt text), a link preview card that can be
 * removed, scheduling in the member's local time (sent as UTC), comments on or off, and for
 * `SystemAdmin` the public sample toggle and pin.
 *
 * @param {{ client: object, perms: object, post?: object|null, onSubmit: (body: object, post: object|null) => Promise<object>,
 *   onSaved: (post: object) => void, onCancel?: () => void, admin?: boolean, initial?: object, maxBodyLength?: number,
 *   uploadOptions?: object, showAuthorField?: boolean }} props
 */
function PostComposer({ client, perms, post = null, onSubmit, onSaved, onCancel, admin = false, initial = {}, maxBodyLength = 10000, uploadOptions = {}, showAuthorField = false }) {
  const { fill, property } = useProperty();
  const editing = Boolean(post);
  const accessPrompt = useAccessPrompt();
  const source = post || initial;
  // FullyGated keeps the SystemAdmin "Public sample" toggle (Members or Public); Open and
  // PartiallyGated offer the audience picker (ACCESS_MODELS_CONTRACT.md section 8.9).
  const fullyGated = accessOf(property).model === AccessModel.FullyGated;
  const picker = useAudiencePicker({ feature: 'feed', isSystemAdmin: perms.isAdmin, current: source.audience });
  const [title, setTitle] = useState(source.title || '');
  const [body, setBody] = useState(source.body || '');
  const [visibility, setVisibility] = useState(source.visibility || 'Members');
  const [commentsEnabled, setCommentsEnabled] = useState(source.commentsEnabled !== false);
  const [pin, setPin] = useState(false);
  const [showLinkPreview, setShowLinkPreview] = useState(editing ? Boolean(post.linkPreview) || post.showLinkPreview !== false : true);
  const [schedule, setSchedule] = useState(() => (source.publishAtUtc && (!editing || post.state === 'Scheduled') ? isoToLocalInput(source.publishAtUtc) : ''));
  const [authorPick, setAuthorPick] = useState([]);
  const authorUserId = authorPick[0] ? authorPick[0].id : '';
  const authorSearch = useMemo(() => pickerSearches(client, { isSystemAdmin: true }).members, [client]);
  const [metadata, setMetadata] = useState(() => metadataOf(source));
  const [metaProblems, setMetaProblems] = useState([]);
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const canSchedule = !editing || post.state === 'Scheduled';

  const uploads = useMediaUploads({
    client,
    initial: editing ? post.attachments || [] : [],
    maxCount: DEFAULT_MEDIA_LIMITS.maxPostAttachments,
    maxVideos: DEFAULT_MEDIA_LIMITS.maxVideosPerPost,
    allowMedia: perms.canAttachMedia,
    allowVideo: perms.canAttachVideo,
    isAdmin: perms.isAdmin,
    onError: setError,
    ...uploadOptions
  });
  const preview = useLinkPreview(client, body, showLinkPreview, editing ? post.linkPreview : null);

  const submit = async (e) => {
    e.preventDefault();
    if (uploads.busy) {
      setError(c.waitForUploads);
      return;
    }
    const publishAt = canSchedule && schedule ? localInputToIso(schedule) : null;
    const found = validatePostDraft({ title, body, mediaCount: uploads.mediaIds.length, publishAt, maxBodyLength });
    setErrors(found);
    setError('');
    if (Object.keys(found).length || metaProblems.length) return;
    const payload = {
      ...metadataForSave(metadata, { isStaff: Boolean(perms.isStaff) }),
      title: title.trim() || null,
      body: body.trim(),
      mediaIds: uploads.mediaIds,
      commentsEnabled,
      showLinkPreview
    };
    if (fullyGated && perms.isAdmin) payload.visibility = visibility;
    if (!fullyGated && picker.visible) payload.audience = picker.value;
    if (canSchedule) payload.publishAtUtc = publishAt;
    if (!editing && perms.isAdmin) payload.pin = pin;
    if (!editing && admin && showAuthorField && authorUserId) payload.authorUserId = authorUserId;
    setBusy(true);
    try {
      const saved = await onSubmit(payload, post);
      uploads.reset();
      onSaved(saved);
    } catch (err) {
      if (accessPrompt.handle(err)) return;
      setErrors(mapFieldErrors(err && err.fieldErrors));
      setError(errorText(err, c.errors));
    } finally {
      setBusy(false);
    }
  };

  const submitLabel = editing ? c.submitSave : (schedule && canSchedule ? c.submitSchedule : c.submitPost);
  const formId = editing ? 'post-edit-' + post.id : 'post-new';

  return (
    <form id={formId} className="composer" onSubmit={submit} noValidate>
      {perms.muted && <Banner tone="warning" icon="mute">{mutedText(perms)}</Banner>}
      {perms.postingBlocked && <Banner tone="warning" icon="ban">{c.postingNotAllowed}</Banner>}
      <ErrorBanner message={composerErrorText(error, uploads)} onDismiss={() => setError('')} />
      <div className="field">
        <label className="label-text" htmlFor={formId + '-title'}>{c.titleLabel}</label>
        <input id={formId + '-title'} value={title} onChange={(e) => setTitle(e.target.value)} maxLength={TITLE_MAX + 20} disabled={busy} aria-invalid={Boolean(errors.title)} />
        {errors.title && <span className="field-error" role="alert">{errors.title}</span>}
      </div>
      <MarkdownEditor
        id={formId + '-body'}
        label={c.bodyLabel}
        value={body}
        onChange={setBody}
        client={client}
        maxLength={maxBodyLength}
        allowHeadings
        placeholder={c.bodyPlaceholder}
        rows={6}
        disabled={busy}
        context={editing ? { type: 'FeedPost', id: post.id } : null}
        error={errors.body}
      />
      {showLinkPreview && preview && <LinkPreviewCard preview={preview} onRemove={() => setShowLinkPreview(false)} />}
      {!showLinkPreview && (
        <p className="muted small">{c.previewRemoved} <button type="button" className="link-button" onClick={() => setShowLinkPreview(true)}>{c.showPreview}</button></p>
      )}
      <AttachmentList uploads={uploads} allowMedia={perms.canAttachMedia} allowVideo={perms.canAttachVideo} maxCount={DEFAULT_MEDIA_LIMITS.maxPostAttachments} disabled={busy} />
      {errors.media && <span className="field-error" role="alert">{errors.media}</span>}
      <MetadataEditor value={metadata} onChange={(next, problems) => { setMetadata(next); setMetaProblems(problems); }} isStaff={Boolean(perms.isStaff)} client={client} targetType="FeedPost" disabled={busy} collapsible={!metadataOf(source).labels.length && !Object.keys(metadataOf(source).tags).length} />
      <div className="composer-options">
        {canSchedule && (
          <div className="field field-compact">
            <label className="label-text" htmlFor={formId + '-schedule'}>{c.scheduleLabel}</label>
            <input id={formId + '-schedule'} type="datetime-local" value={schedule} onChange={(e) => setSchedule(e.target.value)} disabled={busy} aria-invalid={Boolean(errors.publishAtUtc)} aria-describedby={formId + '-schedule-hint'} />
            <span id={formId + '-schedule-hint'} className="muted small">{c.scheduleHint(deviceTimeZone())}</span>
            {errors.publishAtUtc && <span className="field-error" role="alert">{errors.publishAtUtc}</span>}
          </div>
        )}
        <label className="toggle">
          <input type="checkbox" checked={commentsEnabled} onChange={(e) => setCommentsEnabled(e.target.checked)} disabled={busy} />
          <span className="toggle-track"><span className="toggle-thumb" /></span>
          <span>{c.commentsEnabled}</span>
        </label>
        {!fullyGated && picker.visible && <AudienceField id={formId + '-audience'} picker={picker} disabled={busy} />}
        {fullyGated && perms.isAdmin && (
          <label className="toggle" title={fill(c.publicHint)}>
            <input type="checkbox" checked={visibility === 'Public'} onChange={(e) => setVisibility(e.target.checked ? 'Public' : 'Members')} disabled={busy} aria-describedby={formId + '-public-hint'} />
            <span className="toggle-track"><span className="toggle-thumb" /></span>
            <span>{c.publicLabel}</span>
            <span id={formId + '-public-hint'} className="visually-hidden">{c.publicHint}</span>
          </label>
        )}
        {perms.isAdmin && !editing && (
          <label className="toggle">
            <input type="checkbox" checked={pin} onChange={(e) => setPin(e.target.checked)} disabled={busy} />
            <span className="toggle-track"><span className="toggle-thumb" /></span>
            <span>{c.pinLabel}</span>
          </label>
        )}
        {admin && showAuthorField && !editing && (
          <div className="field field-compact">
            <EntityPicker id={formId + '-author'} label={strings.admin.feedAdmin.authorLabel} hint={strings.admin.feedAdmin.authorHint} value={authorPick} onChange={setAuthorPick} search={authorSearch} placeholder={strings.admin.eventsAdmin.memberPlaceholder} disabled={busy} />
          </div>
        )}
      </div>
      <div className="form-actions">
        {onCancel && <button type="button" className="button button-secondary" onClick={onCancel} disabled={busy}>{c.cancel}</button>}
        <button type="submit" className="button button-primary" disabled={busy || perms.muted || perms.postingBlocked}>{busy ? c.submitting : submitLabel}</button>
      </div>
    </form>
  );
}

export default PostComposer;
