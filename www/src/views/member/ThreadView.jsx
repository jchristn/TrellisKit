import { useCallback, useMemo, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { commentTargetOf, threadActions, threadCanComment, threadCanReact } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import Modal from '../../components/Modal.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import AuthorLine from '../../components/feed/AuthorLine.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import MediaGallery from '../../components/feed/MediaGallery.jsx';
import LinkPreviewCard from '../../components/feed/LinkPreviewCard.jsx';
import ReactionBar from '../../components/feed/ReactionBar.jsx';
import CommentThread from '../../components/feed/CommentThread.jsx';
import EditHistoryDialog from '../../components/feed/EditHistoryDialog.jsx';
import ReportDialog from '../../components/feed/ReportDialog.jsx';
import BlockDialog from '../../components/feed/BlockDialog.jsx';
import ThreadBadges from '../../components/forums/ThreadBadges.jsx';
import PollCard from '../../components/forums/PollCard.jsx';
import FollowButton from '../../components/forums/FollowButton.jsx';
import ThreadComposer from '../../components/forums/ThreadComposer.jsx';
import { FeedbackStatusDialog, LockDialog, MoveDialog, PromoteDialog } from '../../components/forums/ThreadDialogs.jsx';
import ModerationActionDialog from '../admin/ModerationActionDialog.jsx';
import { copyText } from '../../utils/clipboard.js';
import { authorName, errorText, postPath, postingPermissions } from '../../utils/engagement.js';
import { appForumPath, appThreadPath, forumErrors } from '../../utils/forums.js';
import { formatDateTime } from '../../utils/format.js';

const t = strings.forums;

/** The accepted answer or the feedback review, shown above the comment tree (sections 4.8 and 7). */
function HighlightedComment({ thread }) {
  if (thread.type === 'Question' && thread.acceptedAnswer) {
    const c = thread.acceptedAnswer;
    return (
      <section className="panel accepted-answer" aria-labelledby="accepted-title">
        <h2 id="accepted-title" className="feed-section-title"><Icon name="check" size={14} /> {t.acceptedAnswer}</h2>
        <AuthorLine user={c.author} size={28} time={c.createdUtc} timeTo={appThreadPath(thread.id, c.id)} />
        <MarkdownBody body={c.body} mentions={c.mentions} allowHeadings={false} className="comment-body" />
        <MediaGallery items={c.attachments} compact />
      </section>
    );
  }
  if (thread.type === 'Feedback' && thread.feedback && thread.feedback.reviewCommentId) {
    return (
      <section className="panel accepted-answer" aria-labelledby="review-title">
        <h2 id="review-title" className="feed-section-title"><Icon name="check" size={14} /> {t.review}</h2>
        {thread.feedback.reviewer && <p className="small">{t.by(authorName(thread.feedback.reviewer))}</p>}
        <Link to={appThreadPath(thread.id, thread.feedback.reviewCommentId)} className="button button-secondary button-sm">{t.review}</Link>
      </section>
    );
  }
  return null;
}

/**
 * /app/threads/:id (PHASE3_CONTRACT.md section 19): the thread with its badges, body,
 * attachments, link preview, poll card, feedback focus, accepted answer or review, reactions and
 * comments on the conversation target (one conversation for linked and promoted threads), the
 * follow button, edit history, and the menu the `viewer` object allows (edit, pin, lock, move,
 * hide, remove, promote, close poll, review status, report, delete). `?comment=` opens a comment
 * in context (notification links).
 */
function ThreadView() {
  const { id } = useParams();
  const [searchParams] = useSearchParams();
  const focusId = searchParams.get('comment');
  const navigate = useNavigate();
  const { client, me } = useAuth();
  const toast = useToast();
  const perms = postingPermissions(me);
  const fetcher = useCallback((signal) => client.getThread(id, signal), [client, id]);
  const query = useApiQuery(fetcher);
  const [override, setOverride] = useState(null);
  const [dialog, setDialog] = useState(null);
  const [treeKey, setTreeKey] = useState(0);
  const thread = override && override.id === id ? override : query.data;
  useDocumentMeta(strings.member.metaTitle(thread ? thread.title : t.title));
  const target = useMemo(() => (thread ? commentTargetOf(thread, 'Thread') : null), [thread]);

  const back = thread && thread.forum
    ? <Link to={appForumPath(thread.forum)} className="back-link small"><Icon name="chevronLeft" size={14} /><span>{t.backToForum(thread.forum.name)}</span></Link>
    : <Link to="/app/forums" className="back-link small"><Icon name="chevronLeft" size={14} /><span>{t.backToForums}</span></Link>;

  if (query.error && query.error.status === 404) {
    return (
      <div className="member-view">
        {back}
        <EmptyState icon="info" title={t.threadNotFound} action={<Link to="/app/forums" className="button button-primary">{t.backToForums}</Link>}>{t.threadNotFoundBody}</EmptyState>
      </div>
    );
  }
  if (!thread) {
    return (
      <div className="member-view">
        {back}
        {query.loading && <Loading />}
        <ErrorBanner message={query.error ? errorText(query.error, forumErrors(), t.threadsLoadError) : ''} onRetry={query.reload} />
      </div>
    );
  }

  const viewer = thread.viewer || {};
  const author = thread.author || {};
  const isAuthor = Boolean(me && author.id === me.id);
  const update = (next) => setOverride(next && next.id ? next : { ...thread, ...(next || {}) });
  const closeDialog = () => setDialog(null);

  const act = async (fn, message, { reloadTree = false } = {}) => {
    try {
      const next = await fn();
      if (next && next.id === thread.id) update(next);
      else if (next && next.options) update({ ...thread, poll: next });
      if (reloadTree) setTreeKey((k) => k + 1);
      toast.success(message);
    } catch (err) {
      toast.error(errorText(err, forumErrors()));
    }
  };

  const remove = async () => {
    try {
      await client.deleteThread(thread.id);
      toast.success(t.deleted);
      navigate(thread.forum ? appForumPath(thread.forum) : '/app/forums', { replace: true });
    } catch (err) {
      toast.error(errorText(err, forumErrors()));
      closeDialog();
    }
  };

  const labels = {
    edit: [t.edit, 'edit', () => setDialog('edit')],
    history: [t.history, 'clock', () => setDialog('history')],
    pin: [t.pin, 'thumbtack', () => act(() => client.pinThread(thread.id), t.pinnedToast)],
    unpin: [t.unpin, 'thumbtack', () => act(() => client.unpinThread(thread.id), t.unpinnedToast)],
    lock: [t.lock, 'lock', () => setDialog('lock')],
    unlock: [t.unlock, 'lock', () => act(() => client.unlockThread(thread.id), t.unlockedToast)],
    move: [t.move, 'forum', () => setDialog('move')],
    editMetadata: [t.editMetadata, 'edit', () => setDialog('metadata')],
    hide: [t.hide, 'eyeOff', () => setDialog('mod:Hide')],
    unhide: [t.unhide, 'eye', () => setDialog('mod:Unhide')],
    remove: [t.remove, 'ban', () => setDialog('mod:Remove')],
    restore: [t.restore, 'refresh', () => setDialog('mod:Restore')],
    promote: [t.promote, 'feed', () => setDialog('promote')],
    unpromote: [t.unpromote, 'feed', () => act(() => client.unpromoteThread(thread.id), t.unpromotedToast)],
    closePoll: [t.closePoll, 'clock', () => act(() => client.closePoll(thread.poll.id), t.pollClosedToast)],
    feedbackStatus: [t.setFeedbackStatus, 'check', () => setDialog('feedback')],
    report: [t.report, 'flag', () => setDialog('report')],
    delete: [t.delete, 'trash', () => setDialog('delete')]
  };
  const items = [{ label: t.copyLink, icon: 'link', onSelect: () => copyText(window.location.origin + appThreadPath(thread.id)).then(() => toast.success(t.linkCopied), () => {}) }];
  for (const key of threadActions(thread, { isAuthor })) {
    const [label, icon, onSelect] = labels[key];
    items.push({ label, icon, onSelect, danger: ['remove', 'delete', 'hide'].includes(key) });
  }
  if (me && !isAuthor && author.id && !author.isDeleted && !author.isStaff) items.push({ label: strings.feed.blockAuthor(authorName(author)), icon: 'ban', danger: true, onSelect: () => setDialog('block') });

  const commentExtras = {
    badges: (comment) => (
      <>
        {comment.isAcceptedAnswer && <Badge tone="success">{t.acceptedAnswer}</Badge>}
        {comment.isFeedbackReview && <Badge tone="info">{t.review}</Badge>}
      </>
    ),
    items: (comment) => {
      const out = [];
      if (thread.type === 'Question' && viewer.canAcceptAnswer && comment.state === 'Visible') {
        out.push(comment.isAcceptedAnswer || (thread.acceptedAnswer && thread.acceptedAnswer.id === comment.id)
          ? { label: t.clearAnswer, icon: 'close', onSelect: () => act(() => client.clearAcceptedAnswer(thread.id), t.answerCleared, { reloadTree: true }) }
          : { label: t.acceptAnswer, icon: 'check', onSelect: () => act(() => client.acceptAnswer(thread.id, comment.id), t.answerAccepted, { reloadTree: true }) });
      }
      if (thread.type === 'Feedback' && viewer.canSetFeedbackStatus && comment.state === 'Visible' && !comment.isFeedbackReview) {
        out.push({ label: t.chooseReview, icon: 'check', onSelect: () => act(() => client.setFeedbackStatus(thread.id, 'Reviewed', comment.id), t.reviewChosen, { reloadTree: true }) });
      }
      return out;
    }
  };

  const modAction = dialog && dialog.startsWith('mod:') ? dialog.slice(4) : null;
  const linked = thread.kind === 'LinkedFeedPost' && thread.source;

  return (
    <div className="member-view thread-view">
      {back}
      <article className="post-card post-detail thread-detail" aria-labelledby="thread-title">
        <header className="post-head">
          <AuthorLine
            user={author}
            time={thread.publishedUtc || thread.createdUtc}
            extra={thread.editedUtc ? (
              <>
                <span aria-hidden="true">·</span>
                <button type="button" className="link-button edited-button" onClick={() => setDialog('history')} title={strings.feed.editedTitle(formatDateTime(thread.editedUtc))}>{t.edited}</button>
              </>
            ) : null}
          />
          <div className="post-head-actions">
            {viewer.canFollow !== false && (
              <FollowButton
                followed={Boolean(thread.isFollowed)}
                label={t.followThreadLabel}
                unfollowLabel={t.unfollowThreadLabel}
                onToggle={async (next) => {
                  const result = next ? await client.followThread(thread.id) : await client.unfollowThread(thread.id);
                  update({ ...thread, isFollowed: result && typeof result.isFollowed === 'boolean' ? result.isFollowed : next });
                }}
              />
            )}
            <ActionMenu label={t.menu} items={items} />
          </div>
        </header>
        <ThreadBadges thread={thread} />
        <h1 id="thread-title" className="post-title">{thread.title}</h1>
        {thread.isLocked && <Banner tone="warning" icon="lock">{t.lockedNote(thread.lockReason)}</Banner>}
        {thread.state === 'Processing' && <p className="post-note muted small">{t.processingNote}</p>}
        {thread.moderation && <p className="post-note post-moderation small" role="note">{t.moderationNote(thread.moderation.state, thread.moderation.reason)}</p>}
        {linked && <p className="post-note small">{t.linkedNote} <Link to={postPath(thread.source.id)}>{t.openFeedPost}</Link></p>}
        <MarkdownBody body={thread.body} mentions={thread.mentions} allowHeadings />
        {thread.type === 'Feedback' && thread.feedback && thread.feedback.focus && (
          <div className="feedback-focus"><strong>{t.feedbackFocus}</strong><p>{thread.feedback.focus}</p></div>
        )}
        <MediaGallery items={thread.attachments} />
        {thread.linkPreview && <LinkPreviewCard preview={thread.linkPreview} />}
        {thread.poll && <PollCard key={thread.poll.id} poll={thread.poll} thread={thread} onChange={(poll) => update({ ...thread, poll })} />}
        <footer className="post-foot">
          <ReactionBar target={target} value={thread} canReact={threadCanReact(thread)} onChange={(fields) => update({ ...thread, ...fields })} />
          <span className="muted small">{t.commentCount(thread.commentCount || 0)}</span>
        </footer>
      </article>
      <HighlightedComment thread={thread} />
      <CommentThread
        key={thread.id + ':' + (focusId || '') + ':' + treeKey}
        post={thread}
        focusId={focusId}
        canComment={threadCanComment(thread)}
        extras={commentExtras}
        viewAllPath={appThreadPath(thread.id)}
        accessFeature="forums"
        onCountChange={(count) => { if (count !== thread.commentCount && !focusId) update({ ...thread, commentCount: count }); }}
      />

      {dialog === 'edit' && (
        <Modal open size="lg" onClose={closeDialog} title={t.editTitle}>
          <ThreadComposer client={client} thread={thread} forum={null} mode="edit" perms={{ ...perms, canAttachMedia: viewer.canAttachMedia !== false && perms.canAttachMedia, canAttachVideo: viewer.canAttachVideo !== false && perms.canAttachVideo }} onSaved={(saved) => { update(saved); closeDialog(); toast.success(t.composer.saved); }} onCancel={closeDialog} />
        </Modal>
      )}
      {dialog === 'metadata' && (
        <Modal open size="lg" onClose={closeDialog} title={t.editMetadata}>
          <ThreadComposer client={client} thread={thread} mode="metadata" perms={{ ...perms, isStaff: true }} onSaved={(saved) => { update(saved); closeDialog(); toast.success(t.composer.saved); }} onCancel={closeDialog} />
        </Modal>
      )}
      {dialog === 'history' && <EditHistoryDialog kind="thread" item={thread} client={client} onClose={closeDialog} />}
      {dialog === 'report' && <ReportDialog target={target.type === 'FeedPost' ? target : { type: 'Thread', id: thread.id }} author={author} onClose={closeDialog} />}
      {dialog === 'block' && <BlockDialog user={author} onClose={closeDialog} onDone={() => navigate('/app/forums', { replace: true })} />}
      {dialog === 'lock' && <LockDialog thread={thread} onClose={closeDialog} onDone={(next) => { update(next); closeDialog(); toast.success(t.lockedToast); }} />}
      {dialog === 'move' && <MoveDialog thread={thread} onClose={closeDialog} onDone={(next) => { update(next); closeDialog(); toast.success(t.movedToast(next && next.forum ? next.forum.name : '')); }} />}
      {dialog === 'promote' && <PromoteDialog thread={thread} isAdmin={perms.isAdmin} onClose={closeDialog} onDone={(card) => { update({ ...thread, promotion: { feedPostId: card && card.id, promotedBy: null, promotedUtc: card && card.publishedUtc } }); closeDialog(); toast.success(t.promotedToast); query.reload(); }} />}
      {dialog === 'feedback' && <FeedbackStatusDialog thread={thread} isAuthor={isAuthor} onClose={closeDialog} onDone={(next) => { update(next); closeDialog(); toast.success(t.feedbackStatusSaved); }} />}
      {modAction && (
        <ModerationActionDialog
          action={modAction}
          targetType="Thread"
          targetId={thread.id}
          api={{ moderate: (body) => client.createModerationAction(body) }}
          onClose={closeDialog}
          onDone={() => { closeDialog(); toast.success(strings.admin.moderation.actDone(strings.admin.moderation.act[modAction])); query.reload(); setOverride(null); }}
        />
      )}
      <ConfirmModal open={dialog === 'delete'} title={t.deleteTitle} message={t.deleteBody} confirmLabel={t.delete} onCancel={closeDialog} onConfirm={remove} />
    </div>
  );
}

export default ThreadView;
