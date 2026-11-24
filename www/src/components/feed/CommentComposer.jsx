import { useState } from 'react';
import { DEFAULT_MEDIA_LIMITS, metadataForSave, scalarLength } from '@trelliskit/shared';
import strings from '../../strings.js';
import { ErrorBanner } from '../States.jsx';
import { authorName, errorText, composerErrorText } from '../../utils/engagement.js';
import MarkdownEditor from './MarkdownEditor.jsx';
import MetadataEditor from '../MetadataEditor.jsx';
import AttachmentList from './AttachmentList.jsx';
import useMediaUploads from './useMediaUploads.js';
import { useAccessPrompt } from '../AccessPrompt.jsx';

const c = strings.composer;
export const COMMENT_MAX_LENGTH = 5000;

/**
 * Comment or reply composer (contract section 7.5): Markdown without headings, mention
 * autocomplete in the post's context, and one image or video when the member may attach media.
 * @param {{ client: object, target: { type: string, id: string }, parent?: object|null, viewer: object, isAdmin?: boolean,
 *   onCreated: (comment: object) => void, onCancel?: () => void, autoFocus?: boolean, uploadOptions?: object }} props
 */
function CommentComposer({ client, target, parent = null, viewer = {}, isAdmin = false, isStaff = false, onCreated, onCancel, autoFocus = false, uploadOptions = {} }) {
  const [body, setBody] = useState('');
  const accessPrompt = useAccessPrompt();
  const [metadata, setMetadata] = useState({ labels: [], tags: {} });
  const [metaKey, setMetaKey] = useState(0);
  const [error, setError] = useState('');
  const [fieldError, setFieldError] = useState('');
  const [busy, setBusy] = useState(false);
  const allowMedia = Boolean(viewer.canAttachMedia) && DEFAULT_MEDIA_LIMITS.maxCommentAttachments > 0;
  const uploads = useMediaUploads({
    client,
    maxCount: DEFAULT_MEDIA_LIMITS.maxCommentAttachments,
    maxVideos: 1,
    allowMedia,
    allowVideo: Boolean(viewer.canAttachVideo),
    isAdmin,
    onError: setError,
    ...uploadOptions
  });
  const label = parent ? c.replyLabel(authorName(parent.author)) : c.commentLabel;

  const submit = async (e) => {
    e.preventDefault();
    setError('');
    if (uploads.busy) {
      setError(c.waitForUploads);
      return;
    }
    if (!body.trim() && uploads.mediaIds.length === 0) {
      setFieldError(c.bodyRequired);
      return;
    }
    if (scalarLength(body) > COMMENT_MAX_LENGTH) {
      setFieldError(c.tooLong(COMMENT_MAX_LENGTH));
      return;
    }
    setFieldError('');
    setBusy(true);
    try {
      const payload = { body: body.trim(), mediaIds: uploads.mediaIds, ...metadataForSave(metadata, { isStaff }) };
      if (parent) payload.parentCommentId = parent.id;
      const created = await client.createComment(target, payload);
      setBody('');
      setMetadata({ labels: [], tags: {} });
      setMetaKey((k) => k + 1);
      uploads.reset();
      onCreated(created);
    } catch (err) {
      if (accessPrompt.handle(err)) return;
      const fe = err && err.fieldErrors;
      if (fe && fe.body) setFieldError(fe.body);
      setError(errorText(err, c.errors));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form className={'comment-composer' + (parent ? ' comment-reply-composer' : '')} onSubmit={submit} noValidate>
      <ErrorBanner message={composerErrorText(error, uploads)} onDismiss={() => setError('')} />
      <MarkdownEditor
        label={label}
        hideLabel={!parent}
        value={body}
        onChange={setBody}
        client={client}
        maxLength={COMMENT_MAX_LENGTH}
        allowHeadings={false}
        placeholder={parent ? c.replyPlaceholder : c.commentPlaceholder}
        rows={parent ? 3 : 3}
        disabled={busy}
        context={target}
        error={fieldError}
        autoFocus={autoFocus}
      />
      <MetadataEditor key={metaKey} value={metadata} onChange={(next) => setMetadata(next)} isStaff={isStaff} client={client} targetType="Comment" disabled={busy} collapsible />
      <div className="comment-composer-actions">
        <AttachmentList uploads={uploads} allowMedia={allowMedia} allowVideo={Boolean(viewer.canAttachVideo)} maxCount={DEFAULT_MEDIA_LIMITS.maxCommentAttachments} label={viewer.canAttachVideo ? c.attachOne : c.attachImages} disabled={busy} showAlt />
        <div className="form-actions">
          {onCancel && <button type="button" className="button button-secondary button-sm" onClick={onCancel} disabled={busy}>{c.cancel}</button>}
          <button type="submit" className="button button-primary button-sm" disabled={busy}>{busy ? c.submitting : (parent ? c.submitReply : c.submitComment)}</button>
        </div>
      </div>
    </form>
  );
}

export default CommentComposer;
