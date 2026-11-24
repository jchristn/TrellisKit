import { useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { comments as commentsApi } from '../../api/community';
import { isPlaceholder, replyCount } from '../../content/comments';
import { describeContentError } from '../../content/errors';
import { reactionFields } from '../../content/reactions';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import IconButton from '../IconButton';
import ActionMenu from './ActionMenu';
import AuthorRow from './AuthorRow';
import ConfirmSheet from './ConfirmSheet';
import EditHistorySheet from './EditHistorySheet';
import LinkPreviewCard from './LinkPreviewCard';
import MarkdownBody from './MarkdownBody';
import MediaGallery from './MediaGallery';
import ReactionBar from './ReactionBar';
import ReportSheet from './ReportSheet';
import { useBlock } from './useBlock';
import MetadataBadges from '../MetadataBadges';

/** Indent per level, capped so deep threads stay readable on a phone. */
const INDENT = 12;
const MAX_VISUAL_DEPTH = 6;

/**
 * One comment row in the tree: indent and thread line by depth, placeholder text for deleted,
 * removed, hidden, or blocked comments (contract 7.2), or the full comment with author,
 * "replying to", body, one attachment, link preview, reactions, Reply, collapse, and the menu.
 * `extraItems` adds menu items (accept an answer) and `badges` shows markers (accepted answer).
 */
export default function CommentItem({ comment, depth, collapsed, onToggleCollapse, onReply, onEdit, onChange, onDeleted, onAuthorBlocked, highlighted = false, extraItems = null, badges = null }) {
  const theme = useTheme();
  const { api, user } = useAuth();
  const [menuOpen, setMenuOpen] = useState(false);
  const [sheet, setSheet] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const blocker = useBlock(api);
  const id = 'comment-' + comment.id;
  const indent = Math.min(depth, MAX_VISUAL_DEPTH) * INDENT;
  const viewer = comment.viewer || {};
  const author = comment.author;
  const isMine = Boolean(user && author && author.id === user.id);
  const replies = replyCount(comment);
  const placeholder = isPlaceholder(comment);

  const container = (children) => (
    <View testID={id} style={{ marginLeft: indent, borderLeftWidth: depth > 0 ? 2 : 0, borderLeftColor: theme.colors.border, paddingLeft: depth > 0 ? theme.spacing.md : 0, paddingVertical: theme.spacing.sm, gap: theme.spacing.sm, backgroundColor: highlighted ? theme.colors.accentSoft : undefined }} accessibilityLabel={depth > 0 ? strings.comments.depthLabel(depth) : undefined}>
      {children}
    </View>
  );

  const collapseButton =
    replies > 0 || collapsed ? (
      <IconButton
        icon={collapsed ? 'chevron-forward' : 'chevron-down'}
        label={collapsed ? strings.comments.expand(replies) : strings.comments.collapse}
        text={collapsed ? strings.comments.expand(replies) : undefined}
        onPress={() => onToggleCollapse(comment.id)}
        color="textMuted"
        testID={id + '-collapse'}
      />
    ) : null;

  if (placeholder) {
    return container(
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}>
        <AppText color="textMuted" style={{ flex: 1, fontStyle: 'italic' }} testID={id + '-placeholder'}>
          {strings.comments.placeholders[comment.state] || strings.comments.placeholders.Deleted}
        </AppText>
        {collapseButton}
      </View>
    );
  }

  const items = [];
  if (viewer.canEdit) items.push({ key: 'edit', label: strings.postMenu.edit, onPress: () => onEdit(comment) });
  if (comment.editedUtc) items.push({ key: 'history', label: strings.postMenu.history, onPress: () => setSheet('history') });
  if (viewer.canReport && !isMine) items.push({ key: 'report', label: strings.postMenu.report, onPress: () => setSheet('report') });
  if (author && author.id && !author.isDeleted && !author.isStaff && !isMine) items.push({ key: 'block', label: strings.postMenu.block(author.handle || author.displayName), onPress: () => setSheet('block'), destructive: true });
  if (extraItems) items.push(...extraItems);
  if (viewer.canDelete) items.push({ key: 'delete', label: strings.postMenu.delete, onPress: () => setSheet('delete'), destructive: true });

  async function remove() {
    setBusy(true);
    setError(null);
    try {
      await commentsApi.remove(api, comment.id);
      setSheet(null);
      onDeleted(comment.id);
    } catch (e) {
      setError(describeContentError(e));
    } finally {
      setBusy(false);
    }
  }

  async function confirmBlock() {
    const result = await blocker.block(author);
    if (result.ok) {
      setSheet(null);
      if (onAuthorBlocked) onAuthorBlocked(author.id);
    }
  }

  return container(
    <>
      <View style={{ flexDirection: 'row', alignItems: 'flex-start', gap: theme.spacing.xs }}>
        <View style={{ flex: 1 }}>
          <AuthorRow author={author} createdUtc={comment.createdUtc} editedUtc={comment.editedUtc} onOpenHistory={() => setSheet('history')} size={32} testID={id + '-byline'} />
        </View>
        {items.length ? <IconButton icon="ellipsis-horizontal" label={strings.postMenu.label} onPress={() => setMenuOpen(true)} color="textMuted" testID={id + '-menu'} /> : null}
      </View>
      {badges}
      <MetadataBadges labels={comment.labels} tags={comment.tags} max={4} testID={id + '-metadata'} />
      {comment.moderation ? <AppText color="warning">{strings.feed.moderationNote(comment.moderation.state, comment.moderation.reason)}</AppText> : null}
      {comment.replyToCommentId ? (
        <AppText variant="caption" color="textMuted" testID={id + '-replying-to'}>
          {comment.replyToUser && comment.replyToUser.handle ? strings.comments.replyingTo(comment.replyToUser.handle) : strings.comments.replyingToDeleted}
        </AppText>
      ) : null}
      <MarkdownBody body={comment.body} mentions={comment.mentions} headings={false} testID={id + '-body'} />
      {comment.linkPreview ? <LinkPreviewCard preview={comment.linkPreview} /> : null}
      <MediaGallery attachments={comment.attachments} compact testID={id + '-media'} />
      {error ? (
        <AppText color="danger" accessibilityRole="alert">
          {error}
        </AppText>
      ) : null}
      <ReactionBar
        target={{ type: 'Comment', id: comment.id }}
        fields={reactionFields(comment)}
        onChange={(fields) => onChange({ ...comment, ...fields })}
        canReact={viewer.canReact !== false}
        testID={id + '-reactions'}
        trailing={
          <View style={{ flexDirection: 'row', alignItems: 'center' }}>
            {viewer.canReply ? <IconButton icon="arrow-undo-outline" label={strings.comments.reply} accessibilityHint={strings.comments.replyHint} text={strings.comments.reply} onPress={() => onReply(comment)} color="textMuted" testID={id + '-reply'} /> : null}
            {collapseButton}
          </View>
        }
      />
      <ActionMenu visible={menuOpen} items={items} onClose={() => setMenuOpen(false)} testID={id + '-actions'} />
      {sheet === 'history' ? <EditHistorySheet targetType="Comment" current={comment} onClose={() => setSheet(null)} /> : null}
      {sheet === 'report' ? <ReportSheet target={{ targetType: 'Comment', targetId: comment.id }} author={author} onClose={() => setSheet(null)} onBlocked={(u) => onAuthorBlocked && onAuthorBlocked(u.id)} /> : null}
      <ConfirmSheet visible={sheet === 'delete'} title={strings.postMenu.deleteCommentTitle} body={strings.postMenu.deleteCommentBody} confirmLabel={strings.postMenu.delete} onConfirm={remove} onClose={() => setSheet(null)} busy={busy} error={error} testID={id + '-delete'} />
      <ConfirmSheet
        visible={sheet === 'block'}
        title={author ? strings.block.confirmTitle(author.handle || author.displayName) : ''}
        body={strings.block.confirmBody}
        confirmLabel={strings.block.block}
        onConfirm={confirmBlock}
        onClose={() => setSheet(null)}
        busy={blocker.busy}
        error={blocker.error}
        testID={id + '-block'}
      />
    </>
  );
}
