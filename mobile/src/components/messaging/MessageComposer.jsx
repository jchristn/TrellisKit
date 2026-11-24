import { useEffect, useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { useLinkPreview } from '../../hooks/useLinkPreview';
import { attachPermissions } from '../../content/permissions';
import { composerError, useUploads } from '../../media/useUploads';
import { describeMessagingError, displayBody } from '../../messaging/format';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import CheckRow from '../CheckRow';
import Notice from '../Notice';
import TextField from '../TextField';
import AttachButtons from '../feed/AttachButtons';
import LinkPreviewCard from '../feed/LinkPreviewCard';
import UploadTray from '../feed/UploadTray';

/** Message body limit (contract section 2.2, `messaging.maxBodyLength`). */
export const MessageMaxLength = 4000;

/** Attachments per message: 4 images or 1 video (`messaging.maxAttachments`). */
export const MessageMaxAttachments = 4;

const t = strings.messaging;

/**
 * The conversation composer: text, photo and video attachments (uploaded with
 * `purpose: "Message"`, contract section 3.7) when the conversation's `viewer` allows, the reply
 * and edit context with Cancel, and Send (or Save while editing). Sending clears the field at
 * once; the message appears optimistically and failures stay in the list with Retry.
 * @param {object} props
 * @param {object} props.conversation
 * @param {object|null} [props.replyTo] Message being replied to.
 * @param {object|null} [props.editing] My message being edited (one edit, as-built note B).
 * @param {(draft: object) => void} props.onSend
 * @param {(message: object, body: string) => Promise<any>} props.onEdit
 * @param {(text: string) => void} [props.onTyping]
 */
export default function MessageComposer({ conversation, replyTo = null, editing = null, onSend, onEdit, onCancel, onTyping, transport, testID = 'message-composer' }) {
  const theme = useTheme();
  const { api, user } = useAuth();
  const [body, setBody] = useState('');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [showLinkPreview, setShowLinkPreview] = useState(true);
  const link = useLinkPreview(api, editing ? '' : body, showLinkPreview);
  const uploads = useUploads({ api, transport, purpose: 'Message' });
  const viewer = (conversation && conversation.viewer) || {};
  const permissions = attachPermissions(user, { canAttachMedia: viewer.canAttachMedia !== false, canAttachVideo: viewer.canAttachVideo !== false });
  const hasVideo = uploads.items.some((i) => i.file && i.file.kind === 'Video');
  const max = hasVideo ? 1 : MessageMaxAttachments;

  useEffect(() => {
    // Editing starts from the message's text.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setBody(editing ? editing.body || '' : '');
    setError(null);
  }, [editing]);

  function change(text) {
    setBody(text);
    if (onTyping && !editing) onTyping(text);
  }

  async function submit() {
    setError(null);
    const text = body.trim();
    if (editing) {
      if (!text) return setError(t.errors.MessageEmpty);
      setBusy(true);
      try {
        await onEdit(editing, text);
        setBody('');
      } catch (e) {
        setError(describeMessagingError(e));
      } finally {
        setBusy(false);
      }
      return undefined;
    }
    if (!text && !uploads.mediaIds.length) return setError(t.errors.MessageEmpty);
    if (uploads.busy || uploads.failed) return setError(strings.composer.waitForUploads);
    const attachments = uploads.items.filter((i) => i.media && uploads.mediaIds.includes(i.media.id)).map((i) => i.media);
    const draft = { body: text, mediaIds: uploads.mediaIds, attachments, replyToMessageId: replyTo ? replyTo.id : null, showLinkPreview };
    setBody('');
    setShowLinkPreview(true);
    uploads.clear();
    if (onTyping) onTyping('');
    onSend(draft);
    return undefined;
  }

  const context = editing ? t.editing : replyTo ? t.replyingTo((replyTo.author && replyTo.author.displayName) || t.deletedMember) : null;
  return (
    <View style={{ gap: theme.spacing.sm, paddingTop: theme.spacing.sm, borderTopWidth: 1, borderTopColor: theme.colors.border }} testID={testID}>
      {context ? (
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}>
          <View style={{ flex: 1 }}>
            <AppText variant="caption" color="textMuted" testID={testID + '-context'}>
              {context}
            </AppText>
            {replyTo && !editing ? (
              <AppText variant="caption">
                {displayBody(replyTo)}
              </AppText>
            ) : null}
            {editing ? (
              <AppText variant="caption" color="textMuted">
                {t.editOnce}
              </AppText>
            ) : null}
          </View>
          <Button label={editing ? t.cancelEdit : t.cancelReply} variant="ghost" onPress={onCancel} style={{ alignSelf: 'auto' }} testID={testID + '-cancel'} />
        </View>
      ) : null}
      {!editing ? <UploadTray uploads={uploads} testID={testID + '-uploads'} /> : null}
      {!editing && link.preview ? (
        <View style={{ gap: theme.spacing.xxs }}>
          <LinkPreviewCard preview={link.preview} onRemove={() => setShowLinkPreview(false)} testID={testID + '-preview'} />
          <AppText variant="caption" color="textMuted" testID={testID + '-preview-privacy'}>
            {t.linkPreviewPrivacy}
          </AppText>
        </View>
      ) : null}
      {!editing && link.url && !showLinkPreview ? <CheckRow label={strings.composer.showLinkPreview} value={false} onValueChange={(v) => setShowLinkPreview(v)} testID={testID + '-show-preview'} /> : null}
      <View style={{ flexDirection: 'row', alignItems: 'flex-end', gap: theme.spacing.sm }}>
        <View style={{ flex: 1 }}>
          <TextField label={t.composer} hideLabel compact multiline value={body} onChangeText={change} maxLength={MessageMaxLength} placeholder={t.composer} testID={testID + '-body'} />
        </View>
        <Button label={editing ? t.saveEdit : t.send} accessibilityHint={editing ? undefined : t.sendHint} onPress={submit} disabled={busy} style={{ alignSelf: 'auto' }} testID={testID + '-send'} />
      </View>
      {!editing ? <AttachButtons permissions={permissions} remaining={max - uploads.items.length} max={max} videoAllowed={!uploads.items.length} onPicked={uploads.add} testID={testID + '-attach'} /> : null}
      <Notice tone="error" testID={testID + '-error'}>
        {composerError(error, uploads, strings.composer.waitForUploads)}
      </Notice>
    </View>
  );
}
