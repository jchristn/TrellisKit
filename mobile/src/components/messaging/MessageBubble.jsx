import { Pressable, View } from 'react-native';
import { displayBody, hasOriginal, isEdited, isGone, receiptText, timeLabel } from '../../messaging/format';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import LinkButton from '../LinkButton';
import LinkPreviewCard from '../feed/LinkPreviewCard';
import MediaGallery from '../feed/MediaGallery';
import ReactionBar from '../feed/ReactionBar';

const t = strings.messaging;

/**
 * One message in a conversation (contract section 2.2, as-built note B): the quoted reply, the
 * text (or the "Message deleted" and "Message removed" tombstones), attachments (hidden in a
 * pending request), the "Edited" marker with View original, reactions from the property set, the
 * receipt under my newest message, and Retry and Discard on a failed optimistic send. A long
 * press opens the message actions.
 * @param {object} props
 * @param {object} props.message
 * @param {boolean} props.mine
 * @param {string|null} [props.receipt] Receipt state shown under the message.
 * @param {boolean} [props.attachmentsHidden] A pending request: attachments are not delivered yet.
 */
export default function MessageBubble({ message, mine, receipt, attachmentsHidden = false, authorName, onLongPress, onShowOriginal, onRetry, onDiscard, onReactionsChange, testID }) {
  const theme = useTheme();
  const gone = isGone(message);
  const failed = message.pending && message.sendState === 'Failed';
  const id = testID || 'message-' + (message.id || message.clientMessageId);
  const viewer = message.viewer || {};
  const attachments = Array.isArray(message.attachments) ? message.attachments : [];
  const bubbleColor = gone ? 'transparent' : mine ? theme.colors.primary : theme.colors.surface;
  const textColor = gone ? 'textMuted' : mine ? 'onPrimary' : 'text';
  const time = message.createdUtc ? timeLabel(message.createdUtc) : '';
  const label = (authorName ? authorName + ', ' : '') + displayBody(message) + (time ? ', ' + time : '') + (isEdited(message) ? ', ' + t.edited : '');

  return (
    <View style={{ alignItems: mine ? 'flex-end' : 'flex-start', paddingVertical: theme.spacing.xxs, gap: theme.spacing.xxs }} testID={id}>
      <Pressable
        onLongPress={!message.pending && !gone ? onLongPress : undefined}
        delayLongPress={350}
        accessibilityRole={!message.pending && !gone && onLongPress ? 'button' : undefined}
        accessibilityLabel={label}
        accessibilityHint={!message.pending && !gone && onLongPress ? t.messageActions(authorName || '') : undefined}
        accessibilityActions={!message.pending && !gone && onLongPress ? [{ name: 'longpress', label: t.messageActions(authorName || '') }] : undefined}
        onAccessibilityAction={(e) => (e.nativeEvent.actionName === 'longpress' && onLongPress ? onLongPress() : undefined)}
        style={{
          maxWidth: '82%',
          backgroundColor: bubbleColor,
          borderRadius: theme.radii.lg,
          borderWidth: gone ? 1 : 0,
          borderColor: theme.colors.border,
          paddingHorizontal: theme.spacing.md,
          paddingVertical: theme.spacing.sm,
          gap: theme.spacing.xs,
          opacity: message.pending && !failed ? 0.7 : 1
        }}
        testID={id + '-bubble'}
      >
        {message.replyTo && !gone ? (
          <View style={{ borderLeftWidth: 3, borderLeftColor: theme.colors.border, paddingLeft: theme.spacing.sm }} testID={id + '-reply'}>
            <AppText variant="caption" color={textColor} style={{ fontWeight: '700' }}>
              {message.replyTo.authorDisplayName || t.deletedMember}
            </AppText>
            <AppText variant="caption" color={textColor}>
              {message.replyTo.isDeleted ? t.deleted : message.replyTo.excerpt || ''}
            </AppText>
          </View>
        ) : null}
        {displayBody(message) ? (
          <AppText color={textColor} style={gone ? { fontStyle: 'italic' } : undefined} testID={id + '-body'} selectable={!gone}>
            {displayBody(message)}
          </AppText>
        ) : null}
        {!gone && message.linkPreview ? <LinkPreviewCard preview={message.linkPreview} testID={id + '-preview'} /> : null}
        {!gone && attachments.length ? (
          attachmentsHidden ? (
            <AppText variant="caption" color={textColor} testID={id + '-attachments-hidden'}>
              {t.attachmentHidden}
            </AppText>
          ) : (
            <MediaGallery attachments={attachments} compact testID={id + '-media'} />
          )
        ) : null}
      </Pressable>
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm, flexWrap: 'wrap', justifyContent: mine ? 'flex-end' : 'flex-start' }}>
        {time ? (
          <AppText variant="caption" color="textMuted" importantForAccessibility="no" accessibilityElementsHidden>
            {time}
          </AppText>
        ) : null}
        {isEdited(message) ? (
          <AppText variant="caption" color="textMuted" testID={id + '-edited'}>
            {t.edited}
          </AppText>
        ) : null}
        {hasOriginal(message) && onShowOriginal ? <LinkButton label={t.viewOriginal} onPress={onShowOriginal} testID={id + '-original'} /> : null}
        {receipt ? (
          <AppText variant="caption" color={receipt === 'Failed' ? 'danger' : 'textMuted'} testID={id + '-receipt'}>
            {receiptText(receipt)}
          </AppText>
        ) : null}
      </View>
      {failed ? (
        <View style={{ flexDirection: 'row', gap: theme.spacing.sm }}>
          <Button label={t.retry} accessibilityHint={t.retryHint} variant="secondary" onPress={onRetry} style={{ alignSelf: 'auto' }} testID={id + '-retry'} />
          <Button label={t.discard} accessibilityHint={t.discardHint} variant="ghost" onPress={onDiscard} style={{ alignSelf: 'auto' }} testID={id + '-discard'} />
        </View>
      ) : null}
      {!gone && !message.pending && message.id && (message.reactionTotal || 0) > 0 ? (
        <ReactionBar target={{ type: 'Message', id: message.id }} fields={message} onChange={onReactionsChange} canReact={viewer.canReact !== false} testID={id + '-reactions'} />
      ) : null}
    </View>
  );
}
