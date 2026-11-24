import { commentTargetOf, hasModeratorActions, threadActions, threadCanReact } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeContentError } from '../../content/errors';
import { canEditPoll } from '../../content/pollEdit';
import { reactionFields } from '../../content/reactions';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import IconButton from '../IconButton';
import AuthorRow from '../feed/AuthorRow';
import EditHistorySheet from '../feed/EditHistorySheet';
import LinkPreviewCard from '../feed/LinkPreviewCard';
import MarkdownBody from '../feed/MarkdownBody';
import MediaGallery from '../feed/MediaGallery';
import ReactionBar from '../feed/ReactionBar';
import ReportSheet from '../feed/ReportSheet';
import PollCard from './PollCard';
import ThreadActionsSheet, { ModeratorActionKeys } from './ThreadActionsSheet';
import { threadMarkers } from './ThreadCard';

const t = strings.forums;

/**
 * The thread at the top of its detail screen: markers, title, author, locked and moderation
 * notes, the linked feed post, body, review focus, media, link preview, poll, accepted answer or
 * review, reactions on the conversation target (none on locked threads), follow, the member menu,
 * and the moderator actions sheet when `viewer.canModerate`.
 */
export default function ThreadHeader({ thread, onChange, onDeleted, onAuthorBlocked, testID = 'thread-header' }) {
  const theme = useTheme();
  const router = useRouter();
  const { api, user } = useAuth();
  const [sheet, setSheet] = useState(null);
  const [error, setError] = useState(null);
  const isAuthor = Boolean(user && thread.author && thread.author.id === user.id);
  const actions = threadActions(thread, { isAuthor });
  const memberActions = actions.filter((k) => !ModeratorActionKeys.includes(k));
  const target = commentTargetOf(thread, 'Thread');
  const viewer = thread.viewer || {};

  async function toggleFollow() {
    setError(null);
    try {
      const result = thread.isFollowed ? await api.unfollowThread(thread.id) : await api.followThread(thread.id);
      onChange({ ...thread, isFollowed: result && typeof result.isFollowed === 'boolean' ? result.isFollowed : !thread.isFollowed });
    } catch (e) {
      setError(describeContentError(e));
    }
  }

  const accepted = thread.type === 'Question' ? thread.acceptedAnswer : null;
  return (
    <Card testID={testID}>
      <View style={{ flexDirection: 'row', alignItems: 'flex-start', gap: theme.spacing.xs }}>
        <View style={{ flex: 1 }}>
          <AuthorRow author={thread.author} createdUtc={thread.publishedUtc || thread.createdUtc} editedUtc={thread.editedUtc} onOpenHistory={() => setSheet('history')} testID={testID + '-byline'} />
        </View>
        {hasModeratorActions(thread) ? <IconButton icon="shield-checkmark-outline" label={t.actions.moderatorTitle} onPress={() => setSheet('moderator')} color="textMuted" testID={testID + '-moderator'} /> : null}
        {memberActions.length ? <IconButton icon="ellipsis-horizontal" label={t.actions.title} onPress={() => setSheet('menu')} color="textMuted" testID={testID + '-menu'} /> : null}
      </View>
      <AppText variant="caption" color="primary" style={{ fontWeight: '700' }} testID={testID + '-markers'}>
        {threadMarkers(thread).join(' · ')}
      </AppText>
      <AppText variant="title" accessibilityRole="header" testID={testID + '-title'}>
        {thread.title}
      </AppText>
      {thread.isLocked ? (
        <AppText color="warning" testID={testID + '-locked'}>
          {t.lockedNote(thread.lockReason)}
        </AppText>
      ) : null}
      {thread.moderation ? <AppText color="warning">{t.moderationNote(thread.moderation.state, thread.moderation.reason)}</AppText> : null}
      {thread.kind === 'LinkedFeedPost' && thread.source ? (
        <View style={{ gap: theme.spacing.xs }}>
          <AppText color="textMuted">{t.linkedNote}</AppText>
          <Button label={t.openFeedPost} variant="ghost" onPress={() => router.push('/feed/' + thread.source.id)} testID={testID + '-feed-post'} />
        </View>
      ) : null}
      <MarkdownBody body={thread.body || ''} mentions={thread.mentions} headings testID={testID + '-body'} />
      {thread.feedback && thread.feedback.focus ? (
        <View>
          <AppText variant="label">{t.focus}</AppText>
          <AppText>{thread.feedback.focus}</AppText>
        </View>
      ) : null}
      {thread.linkPreview ? <LinkPreviewCard preview={thread.linkPreview} /> : null}
      <MediaGallery attachments={thread.attachments} testID={testID + '-media'} />
      {thread.poll ? <PollCard poll={thread.poll} thread={thread} onChange={(poll) => onChange({ ...thread, poll })} canEdit={canEditPoll(thread.poll, thread, user)} testID="poll" /> : null}
      {accepted ? (
        <View style={{ gap: theme.spacing.xs, borderLeftWidth: 4, borderLeftColor: theme.colors.success, paddingLeft: theme.spacing.md }} testID={testID + '-accepted'}>
          <AppText variant="label" color="success">{t.acceptedAnswer}</AppText>
          <MarkdownBody body={accepted.body || ''} mentions={accepted.mentions} headings={false} />
        </View>
      ) : null}
      {thread.type === 'Feedback' && thread.feedback && thread.feedback.reviewCommentId ? (
        <Button label={t.review} variant="secondary" onPress={() => router.push('/threads/' + thread.id + '?comment=' + thread.feedback.reviewCommentId)} testID={testID + '-review'} />
      ) : null}
      {error ? (
        <AppText color="danger" accessibilityRole="alert">
          {error}
        </AppText>
      ) : null}
      <ReactionBar
        target={target}
        fields={reactionFields(thread)}
        onChange={(fields) => onChange({ ...thread, ...fields })}
        canReact={threadCanReact(thread)}
        testID={testID + '-reactions'}
        trailing={viewer.canFollow !== false ? <Button label={thread.isFollowed ? t.unfollow : t.follow} accessibilityLabel={thread.isFollowed ? t.unfollowThread : t.followThread} variant="ghost" onPress={toggleFollow} style={{ alignSelf: 'auto' }} testID={testID + '-follow'} /> : null}
      />
      <ThreadActionsSheet thread={thread} actions={actions} sheet={sheet && sheet !== 'history' && sheet !== 'report' ? sheet : null} setSheet={(next) => setSheet(next)} onChange={onChange} onDeleted={onDeleted} isAuthor={isAuthor} />
      {sheet === 'history' ? <EditHistorySheet targetType="Thread" current={thread} onClose={() => setSheet(null)} /> : null}
      {sheet === 'report' ? <ReportSheet target={target.type === 'FeedPost' ? { targetType: 'FeedPost', targetId: target.id } : { targetType: 'Thread', targetId: thread.id }} author={thread.author} onClose={() => setSheet(null)} onBlocked={(u) => onAuthorBlocked && onAuthorBlocked(u.id)} /> : null}
    </Card>
  );
}
