import { useEffect, useRef, useState } from 'react';
import { View } from 'react-native';
import { comments as commentsApi } from '../../api/community';
import { useAuth } from '../../auth/AuthProvider';
import { contentFieldErrors, describeContentError } from '../../content/errors';
import { metadataChanged, metadataOf, metadataPayload } from '../../content/metadata';
import { attachPermissions, ComposerLimits, isStaffUser } from '../../content/permissions';
import { composerError, useUploads } from '../../media/useUploads';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Notice from '../Notice';
import MetadataEditor from '../filters/MetadataEditor';
import AttachButtons from './AttachButtons';
import MentionInput from './MentionInput';
import UploadTray from './UploadTray';
import { useAccessPrompt } from '../AccessPrompt';
import { useKillSwitch } from '../../appConfig/AppContentProvider';

/** Delay before an empty, blurred composer collapses back to one line. */
export const CollapseDelayMs = 200;

/**
 * Comment composer under a post: a new top-level comment, a reply (`replyTo`), or an edit
 * (`editing`). Markdown subset without headings, @mention autocomplete, and one image or video
 * (comments.maxAttachments) when the member may attach media. Remount it (a `key`) to switch
 * between modes. A new top-level comment starts collapsed to a one-line field so it does not take
 * over a phone screen; it expands (attachments, labels and tags, Send) on focus and stays open
 * while it has text, attachments, metadata, or an error. Replies and edits open expanded.
 * @param {object} props
 * @param {object} props.post FeedPost (its commentTarget, viewer, and id for mention context).
 * @param {object|null} [props.replyTo]
 * @param {object|null} [props.editing]
 * @param {(comment: object, mode: 'create'|'edit') => void} props.onDone
 * @param {() => void} [props.onCancel]
 */
export default function CommentComposer({ post, replyTo = null, editing = null, onDone, onCancel, transport }) {
  const accessPrompt = useAccessPrompt();
  const theme = useTheme();
  const { api, user } = useAuth();
  const [body, setBody] = useState(editing ? editing.body || '' : '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [fieldError, setFieldError] = useState(null);
  const [initialMeta] = useState(() => metadataOf(editing));
  const [meta, setMeta] = useState(() => metadataOf(editing));
  const [focused, setFocused] = useState(false);
  const blurTimer = useRef(null);
  useEffect(() => () => clearTimeout(blurTimer.current), []);
  const uploads = useUploads({ api, initial: editing ? editing.attachments || [] : [], transport });
  const target = post.commentTarget || { type: 'FeedPost', id: post.id };
  const viewerSource = editing ? editing.viewer : replyTo ? replyTo.viewer : post.viewer;
  const permissions = attachPermissions(user, viewerSource || post.viewer);
  const t = strings.comments;
  const max = ComposerLimits.commentAttachments;
  // The comments kill switch pauses commenting everywhere (LIVE_UPDATES_CONTRACT.md section 13.2).
  const commentsKill = useKillSwitch('comments');

  async function submit() {
    setError(null);
    setFieldError(null);
    const text = body.trim();
    if (!text && !uploads.mediaIds.length) return setFieldError(strings.composer.empty);
    if (text.length > ComposerLimits.commentBody) return setFieldError(strings.composer.tooLong(ComposerLimits.commentBody));
    if (uploads.busy || uploads.failed) return setError(strings.composer.waitForUploads);
    setBusy(true);
    try {
      let result;
      const metaBody = !editing || metadataChanged(meta, initialMeta) ? metadataPayload(meta, { staff: isStaffUser(user) }) : {};
      if (editing) result = await commentsApi.update(api, editing.id, { body: text, mediaIds: uploads.mediaIds, ...metaBody });
      else result = await commentsApi.create(api, target, { body: text, parentCommentId: replyTo ? replyTo.id : undefined, mediaIds: uploads.mediaIds, ...metaBody });
      setBody('');
      onDone(result, editing ? 'edit' : 'create');
    } catch (e) {
      if (accessPrompt.handle(e)) return;
      const fields = contentFieldErrors(e);
      if (fields.body) setFieldError(fields.body);
      setError(describeContentError(e));
    } finally {
      setBusy(false);
    }
    return undefined;
  }

  const hasMeta = Boolean((meta.labels && meta.labels.length) || (meta.tags && Object.keys(meta.tags).length));
  const expanded = Boolean(replyTo || editing || focused || body.length || uploads.items.length || hasMeta || error || fieldError);
  const context = editing ? t.editing : replyTo ? (replyTo.author && replyTo.author.handle ? t.replyingTo(replyTo.author.handle) : t.replyingToDeleted) : null;

  if (commentsKill.killed) return <Notice testID="comment-composer-off">{commentsKill.message || strings.appContent.commentsOff}</Notice>;

  return (
    <View style={{ gap: theme.spacing.sm }} testID="comment-composer">
      {context ? (
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}>
          <AppText variant="caption" color="textMuted" style={{ flex: 1 }} testID="comment-composer-context">
            {context}
          </AppText>
          {onCancel ? <Button label={editing ? t.cancelEdit : t.cancelReply} variant="ghost" onPress={onCancel} style={{ alignSelf: 'auto' }} testID="comment-composer-cancel" /> : null}
        </View>
      ) : null}
      <MentionInput
        label={replyTo ? t.writeReply : t.write}
        hint={strings.composer.commentHint}
        error={fieldError}
        value={body}
        onChangeText={setBody}
        contextType={target.type}
        contextId={target.id}
        maxLength={ComposerLimits.commentBody}
        autoFocus={Boolean(replyTo || editing)}
        compact={!expanded}
        onFocus={() => {
          clearTimeout(blurTimer.current);
          setFocused(true);
        }}
        onBlur={() => {
          // A short delay lets a tap on Attach or Send land before an empty composer collapses.
          clearTimeout(blurTimer.current);
          blurTimer.current = setTimeout(() => setFocused(false), CollapseDelayMs);
        }}
        testID="comment-composer-body"
      />
      {expanded ? (
        <View style={{ gap: theme.spacing.sm }} testID="comment-composer-expanded">
          <UploadTray uploads={uploads} testID="comment-uploads" />
          <AttachButtons permissions={permissions} remaining={max - uploads.items.length} max={max} videoAllowed onPicked={uploads.add} testID="comment-attach" />
          <MetadataEditor value={meta} onChange={setMeta} targetType="Comment" testID="comment-metadata" />
          <Notice tone="error" testID="comment-composer-error">
            {composerError(error, uploads, strings.composer.waitForUploads)}
          </Notice>
          <Button label={editing ? t.save : t.send} onPress={submit} disabled={busy} testID="comment-composer-submit" />
        </View>
      ) : null}
    </View>
  );
}
