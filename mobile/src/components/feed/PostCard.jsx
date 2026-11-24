import { useRouter } from 'expo-router';
import { Pressable, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { reactionFields } from '../../content/reactions';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Card from '../Card';
import IconButton from '../IconButton';
import ActionMenu from './ActionMenu';
import AuthorRow from './AuthorRow';
import MetadataBadges from '../MetadataBadges';
import ConfirmSheet from './ConfirmSheet';
import EditHistorySheet from './EditHistorySheet';
import LinkPreviewCard from './LinkPreviewCard';
import MarkdownBody from './MarkdownBody';
import MediaGallery from './MediaGallery';
import ReactionBar from './ReactionBar';
import ReportSheet from './ReportSheet';
import { useBlock } from './useBlock';
import { usePostActions } from './usePostActions';
import Button from '../Button';
import { accessAction, isLockedItem, lockedRequirement } from '@trelliskit/shared';
import { useProperty } from '../../property';
import { AccessActions, LockBadge } from '../AccessPrompt';

/**
 * A locked stub (`locked: true`, `viewer.canOpen: false`; ACCESS_MODELS_CONTRACT.md section 3.5):
 * author, title, and date only, with a lock badge and the upgrade (native in-app purchase) or
 * sign-in call to action. It never opens the detail and never loads comments or reactions.
 */
export function LockedPostCard({ post, testID }) {
  const theme = useTheme();
  const property = useProperty();
  const required = lockedRequirement(post);
  const action = accessAction(property, required);
  const id = testID || 'post-' + post.id;
  const body = action === 'signIn' ? strings.access.lockedSignInBody : action === 'upgrade' ? strings.access.lockedPostBody : strings.access.lockedNoPurchase;
  return (
    <Card testID={id} accessibilityLabel={(post.title || strings.access.lockedPostTitle) + ', ' + strings.access.lockBadge}>
      <AuthorRow author={post.author} createdUtc={post.publishedUtc || post.createdUtc} testID={id + '-byline'} />
      <LockBadge testID={id + '-locked'} />
      <AppText variant="heading" accessibilityRole="header">
        {post.title || strings.access.lockedPostTitle}
      </AppText>
      <AppText color="textMuted">{body}</AppText>
      <View style={{ gap: theme.spacing.sm }}>
        <AccessActions required={required} testID={id + '-access'} />
      </View>
    </Card>
  );
}

/**
 * A feed post: author, pinned and state labels, title, Markdown body, link preview, media,
 * reactions, comment count, and the action menu. In the feed the card opens the post; on the
 * detail screen (`detail`) it is static. A promoted thread card (Phase 3 section 8.1) shows "From
 * the {forum} forum" and opens the thread; a post opened for discussion links its thread. Reactions
 * and reports always go to `commentTarget`.
 * @param {object} props
 * @param {object} props.post FeedPost.
 * @param {(post: object) => void} props.onChange Replace the post (reactions, pin, edits).
 * @param {() => void} [props.onOpen] Open the post (feed only).
 * @param {(id: string) => void} [props.onDeleted]
 * @param {(userId: string) => void} [props.onAuthorBlocked]
 * @param {boolean} [props.selected] Highlight (tablet list with the post open beside it).
 */
export default function PostCard(props) {
  if (isLockedItem(props.post)) return <LockedPostCard post={props.post} testID={props.testID} />;
  return <OpenPostCard {...props} />;
}

function OpenPostCard({ post, onChange, onOpen, onDeleted, onAuthorBlocked, detail = false, selected = false, testID }) {
  const theme = useTheme();
  const { api, user } = useAuth();
  const actions = usePostActions({ api, user, post, onChange, onDeleted });
  const blocker = useBlock(api);
  const id = testID || 'post-' + post.id;
  const router = useRouter();
  const target = post.commentTarget || { type: 'FeedPost', id: post.id };
  const promoted = post.kind === 'PromotedThread' && post.source && post.source.id;
  const stateLabel = post.state && post.state !== 'Published' ? strings.feed.states[post.state] : null;

  async function confirmBlock() {
    const result = await blocker.block(post.author);
    if (result.ok) {
      actions.setSheet(null);
      if (onAuthorBlocked) onAuthorBlocked(post.author.id);
    }
  }

  const header = (
    <>
      {post.isPinned || stateLabel ? (
        <View style={{ flexDirection: 'row', gap: theme.spacing.sm, flexWrap: 'wrap' }}>
          {post.isPinned ? (
            <AppText variant="caption" color="primary" style={{ fontWeight: '700' }} testID={id + '-pinned'}>
              {strings.feed.pinned}
            </AppText>
          ) : null}
          {stateLabel ? (
            <AppText variant="caption" color="warning" style={{ fontWeight: '700' }} testID={id + '-state'}>
              {stateLabel}
            </AppText>
          ) : null}
        </View>
      ) : null}
      <MetadataBadges labels={post.labels} tags={post.tags} testID={id + '-metadata'} />
      {promoted ? (
        <Button label={strings.forums.fromForum(post.source.forumName || '')} accessibilityHint={strings.forums.fromForumHint} variant="ghost" onPress={() => router.push('/threads/' + post.source.id)} style={{ alignSelf: 'flex-start' }} testID={id + '-forum'} />
      ) : null}
      {post.moderation ? <AppText color="warning">{strings.feed.moderationNote(post.moderation.state, post.moderation.reason)}</AppText> : null}
      {post.title ? (
        <AppText variant="heading" accessibilityRole="header">
          {post.title}
        </AppText>
      ) : null}
    </>
  );

  return (
    <Card testID={id} style={selected ? { borderColor: theme.colors.primary, borderWidth: 2 } : null}>
      <View style={{ flexDirection: 'row', alignItems: 'flex-start', gap: theme.spacing.xs }}>
        <View style={{ flex: 1 }}>
          <AuthorRow author={post.author} createdUtc={post.publishedUtc || post.createdUtc} editedUtc={post.editedUtc} onOpenHistory={() => actions.setSheet('history')} testID={id + '-byline'} />
        </View>
        {actions.items.length ? <IconButton icon="ellipsis-horizontal" label={strings.postMenu.label} onPress={() => actions.setMenuOpen(true)} color="textMuted" testID={id + '-menu'} /> : null}
      </View>
      {onOpen && !detail ? (
        <Pressable onPress={onOpen} accessibilityRole="button" accessibilityHint={strings.feed.openPostHint} accessibilityLabel={[post.title, post.excerpt].filter(Boolean).join('. ') || strings.feed.postTitle} style={{ gap: theme.spacing.md, minHeight: theme.layout.minTouchTarget }} testID={id + '-open'}>
          {header}
          <MarkdownBody body={post.body} mentions={post.mentions} headings testID={id + '-body'} />
        </Pressable>
      ) : (
        <View style={{ gap: theme.spacing.md }}>
          {header}
          <MarkdownBody body={post.body} mentions={post.mentions} headings testID={id + '-body'} />
        </View>
      )}
      {post.linkPreview ? <LinkPreviewCard preview={post.linkPreview} testID={id + '-preview'} /> : null}
      {post.discussion && post.discussion.threadId ? (
        <Button label={strings.forums.discussion(post.discussion.forumName || '')} accessibilityHint={strings.forums.discussionHint} variant="ghost" onPress={() => router.push('/threads/' + post.discussion.threadId)} style={{ alignSelf: 'flex-start' }} testID={id + '-discussion'} />
      ) : null}
      <MediaGallery attachments={post.attachments} testID={id + '-media'} />
      {actions.error ? (
        <AppText color="danger" accessibilityRole="alert">
          {actions.error}
        </AppText>
      ) : null}
      <ReactionBar
        target={target}
        fields={reactionFields(post)}
        onChange={(fields) => onChange({ ...post, ...fields })}
        canReact={post.viewer ? post.viewer.canReact !== false : true}
        testID={id + '-reactions'}
        trailing={
          onOpen && !detail ? (
            <IconButton icon="chatbubble-outline" label={strings.feed.commentCount(post.commentCount || 0)} text={String(post.commentCount || 0)} onPress={onOpen} color="textMuted" testID={id + '-comments'} />
          ) : (
            <AppText variant="caption" color="textMuted">
              {strings.feed.commentCount(post.commentCount || 0)}
            </AppText>
          )
        }
      />
      <ActionMenu visible={actions.menuOpen} items={actions.items} onClose={() => actions.setMenuOpen(false)} testID={id + '-actions'} />
      {actions.sheet === 'history' ? <EditHistorySheet targetType="FeedPost" current={post} onClose={() => actions.setSheet(null)} /> : null}
      {actions.sheet === 'report' ? <ReportSheet target={{ targetType: target.type, targetId: target.id }} author={post.author} onClose={() => actions.setSheet(null)} onBlocked={(u) => onAuthorBlocked && onAuthorBlocked(u.id)} /> : null}
      <ConfirmSheet
        visible={actions.sheet === 'delete'}
        title={strings.postMenu.deleteTitle}
        body={strings.postMenu.deleteBody}
        confirmLabel={strings.postMenu.delete}
        onConfirm={actions.remove}
        onClose={() => actions.setSheet(null)}
        busy={actions.busy}
        error={actions.error}
        testID={id + '-delete'}
      />
      <ConfirmSheet
        visible={actions.sheet === 'block'}
        title={post.author ? strings.block.confirmTitle(post.author.handle || post.author.displayName) : ''}
        body={strings.block.confirmBody}
        confirmLabel={strings.block.block}
        onConfirm={confirmBlock}
        onClose={() => actions.setSheet(null)}
        busy={blocker.busy}
        error={blocker.error}
        testID={id + '-block'}
      />
    </Card>
  );
}
