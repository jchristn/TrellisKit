import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import ActionMenu from '../ActionMenu.jsx';
import ConfirmModal from '../ConfirmModal.jsx';
import Modal from '../Modal.jsx';
import Icon from '../Icon.jsx';
import { Badge } from '../Badges.jsx';
import { copyText } from '../../utils/clipboard.js';
import { authorName, errorText, postPath, postingPermissions } from '../../utils/engagement.js';
import { formatDateTime } from '../../utils/format.js';
import AuthorLine from './AuthorLine.jsx';
import MarkdownBody from './MarkdownBody.jsx';
import MetadataBadges from '../MetadataBadges.jsx';
import MediaGallery from './MediaGallery.jsx';
import LinkPreviewCard from './LinkPreviewCard.jsx';
import ReactionBar from './ReactionBar.jsx';
import EditHistoryDialog from './EditHistoryDialog.jsx';
import ReportDialog from './ReportDialog.jsx';
import BlockDialog from './BlockDialog.jsx';
import PostComposer from './PostComposer.jsx';
import { DiscussDialog } from '../forums/ThreadDialogs.jsx';
import { appForumPath, appThreadPath, canPromote, forumErrors } from '../../utils/forums.js';
import { useProperty } from '../../context/PropertyContext.jsx';
import { capabilityAvailable } from '../../views/member/memberRoutes.js';
import { AccessModel, accessAction, accessOf, isLockedItem, lockedRequirement } from '@trelliskit/shared';
import { AccessActions, LockBadge } from '../AccessPrompt.jsx';

const f = strings.feed;

/** Feed item kinds this client renders (Phase 2 section 5.1, plus promoted threads from Phase 3 section 8.1); others are skipped. */
export const KNOWN_POST_KINDS = Object.freeze(['Original', 'PromotedThread']);

const STATE_TONES = { Scheduled: 'info', Processing: 'warning', Hidden: 'warning', Removed: 'danger' };

/** Comment and reaction target for a card (always `commentTarget`, contract section 5.1). */
export function postTarget(post) {
  return post.commentTarget && post.commentTarget.type ? post.commentTarget : { type: 'FeedPost', id: post.id };
}

/**
 * One feed post: author, state badges, title, Markdown body, media, link preview, edited marker
 * (opens the edit history), reactions, comment count, and the actions the `viewer` object allows
 * (edit, delete, pin, report, block the author).
 *
 * @param {{ post: object, onChange?: (post: object) => void, onDeleted?: (post: object) => void, onBlocked?: (user: object) => void, detail?: boolean, headingLevel?: 2|1 }} props
 */
/**
 * A locked stub (`locked: true`, `viewer.canOpen: false`; ACCESS_MODELS_CONTRACT.md section 3.5):
 * author, title, and dates only, with a lock badge and the upgrade (or sign-in) call to action.
 * It never links to the detail and never loads comments or reactions.
 */
export function LockedPostCard({ post, headingLevel = 2 }) {
  const { fill, property } = useProperty();
  const required = lockedRequirement(post);
  const action = accessAction(property, required);
  const Heading = headingLevel === 1 ? 'h1' : 'h2';
  const text = action === 'signIn' ? strings.access.lockedSignInBody : action === 'upgrade' ? strings.access.lockedPostBody : strings.access.lockedNoPurchase;
  return (
    <article className="post-card post-card-locked" aria-labelledby={'post-' + post.id + '-label'} data-locked="true">
      <header className="post-head">
        <AuthorLine user={post.author || {}} time={post.publishedUtc || post.createdUtc} />
        <div className="post-head-actions">
          <div className="badge-row">
            {post.isPinned && <Badge tone="info"><Icon name="thumbtack" size={12} /> {f.pinned}</Badge>}
            <LockBadge feature="feed" />
          </div>
        </div>
      </header>
      <Heading id={'post-' + post.id + '-label'} className="post-title">{post.title || strings.access.lockedPostTitle}</Heading>
      <div className="locked-body">
        <Icon name="lock" size={16} />
        <p className="small">{fill(text)}</p>
        <AccessActions required={required} feature="feed" />
      </div>
    </article>
  );
}

function PostCard(props) {
  if (isLockedItem(props.post)) return <LockedPostCard post={props.post} headingLevel={props.headingLevel} />;
  return <OpenPostCard {...props} />;
}

function OpenPostCard({ post, onChange, onDeleted, onBlocked, detail = false, headingLevel = 2 }) {
  const { client, me } = useAuth();
  const propertyContext = useProperty();
  const partial = accessOf(propertyContext.property).model === AccessModel.PartiallyGated;
  const forumsOn = capabilityAvailable(useProperty().property, 'forums');
  const toast = useToast();
  const navigate = useNavigate();
  const perms = postingPermissions(me);
  const [dialog, setDialog] = useState(null);
  const [busy, setBusy] = useState(false);
  const viewer = post.viewer || {};
  const author = post.author || {};
  const isSelf = Boolean(me && author.id && author.id === me.id);
  const promoted = post.kind === 'PromotedThread' && post.source && post.source.id;
  const url = promoted ? appThreadPath(post.source.id) : postPath(post.id);
  const Heading = headingLevel === 1 ? 'h1' : 'h2';

  const update = (next) => { if (onChange) onChange(next); };

  const pin = async (on) => {
    setBusy(true);
    try {
      const next = on ? await client.pinFeedPost(post.id) : await client.unpinFeedPost(post.id);
      update(next || { ...post, isPinned: on });
      toast.success(on ? f.pinnedToast : f.unpinnedToast);
    } catch (err) {
      toast.error(errorText(err, { PinLimitReached: f.pinLimit }));
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    setBusy(true);
    try {
      await client.deleteFeedPost(post.id);
      toast.success(f.deleted);
      setDialog(null);
      if (onDeleted) onDeleted(post);
    } catch (err) {
      toast.error(errorText(err));
    } finally {
      setBusy(false);
    }
  };

  const undiscuss = async () => {
    setBusy(true);
    try {
      const next = await client.undiscussFeedPost(post.id);
      update(next && next.id ? next : { ...post, discussion: null });
      toast.success(strings.forums.undiscussedToast);
    } catch (err) {
      toast.error(errorText(err, forumErrors()));
    } finally {
      setBusy(false);
    }
  };

  const items = [
    { label: f.copyLink, icon: 'link', onSelect: () => copyText(window.location.origin + url).then(() => toast.success(f.linkCopied), () => {}) }
  ];
  if (promoted) items.push({ label: strings.forums.openThread, icon: 'forum', onSelect: () => navigate(url) });
  if (forumsOn && !promoted && canPromote(me) && post.state === 'Published' && !post.discussion) items.push({ label: strings.forums.discuss, icon: 'forum', onSelect: () => setDialog('discuss') });
  if (forumsOn && !promoted && canPromote(me) && post.discussion) items.push({ label: strings.forums.undiscuss, icon: 'forum', onSelect: undiscuss });
  if (viewer.canEdit && !promoted) items.push({ label: f.edit, icon: 'edit', onSelect: () => setDialog('edit') });
  if (viewer.canPin && post.state === 'Published') items.push(post.isPinned ? { label: f.unpin, icon: 'thumbtack', onSelect: () => pin(false) } : { label: f.pin, icon: 'thumbtack', onSelect: () => pin(true) });
  if (viewer.canReport && !isSelf) items.push({ label: f.report, icon: 'flag', onSelect: () => setDialog('report') });
  if (me && !isSelf && author.id && !author.isDeleted && !author.isStaff) items.push({ label: f.blockAuthor(authorName(author)), icon: 'ban', danger: true, onSelect: () => setDialog('block') });
  if (viewer.canDelete) items.push({ label: f.delete, icon: 'trash', danger: true, onSelect: () => setDialog('delete') });

  const failedMedia = (post.attachments || []).some((m) => m.status === 'Failed');
  const badges = (
    <>
      {post.isPinned && <Badge tone="info"><Icon name="thumbtack" size={12} /> {f.pinned}</Badge>}
      {post.state && post.state !== 'Published' && <Badge tone={STATE_TONES[post.state] || 'neutral'}>{f.states[post.state] || post.state}</Badge>}
      {partial && post.audience === 'Members' && <Badge tone="neutral" title={propertyContext.fill(strings.access.audienceHints.Members)}><Icon name="lock" size={12} /> {strings.access.audiences.Members}</Badge>}
      {perms.isAdmin && post.visibility === 'Public' && <Badge tone="success" title={f.publicBadgeTitle}><Icon name="globe" size={12} /> {f.publicBadge}</Badge>}
      {promoted && <Link to={url} className="badge badge-info forum-source-badge"><Icon name="forum" size={12} /> {strings.forums.fromForumBadge(post.source.forumName || '')}</Link>}
    </>
  );

  return (
    <article className={'post-card' + (post.isPinned ? ' post-pinned' : '') + (detail ? ' post-detail' : '')} aria-labelledby={'post-' + post.id + '-label'}>
      <header className="post-head">
        <AuthorLine
          user={author}
          time={post.publishedUtc || post.createdUtc}
          timeTo={detail ? undefined : url}
          extra={post.editedUtc ? (
            <>
              <span aria-hidden="true">·</span>
              <button type="button" className="link-button edited-button" onClick={() => setDialog('history')} title={f.editedTitle(formatDateTime(post.editedUtc))}>{f.edited}</button>
            </>
          ) : null}
        />
        <div className="post-head-actions">
          <div className="badge-row">{badges}<MetadataBadges labels={post.labels} tags={post.tags} /></div>
          <ActionMenu label={f.postMenu} items={items} />
        </div>
      </header>

      {post.state === 'Scheduled' && post.publishAtUtc && <p className="post-note muted small"><Icon name="clock" size={12} /> {f.scheduledFor(formatDateTime(post.publishAtUtc))}</p>}
      {post.state === 'Processing' && <p className="post-note muted small">{failedMedia ? f.failedMediaNote : f.processingNote}</p>}
      {post.moderation && <p className="post-note post-moderation small" role="note">{f.moderationNote(post.moderation.state, post.moderation.reason)}</p>}

      {post.title ? (
        <Heading id={'post-' + post.id + '-label'} className="post-title">
          {detail && !promoted ? post.title : <Link to={url}>{post.title}</Link>}
        </Heading>
      ) : (
        <Heading id={'post-' + post.id + '-label'} className="visually-hidden">{f.permalink + ': ' + authorName(author)}</Heading>
      )}
      <MarkdownBody body={post.body} mentions={post.mentions} allowHeadings />
      <MediaGallery items={post.attachments} />
      {post.linkPreview && <LinkPreviewCard preview={post.linkPreview} />}
      {post.discussion && post.discussion.threadId && (
        <p className="post-note small"><Icon name="forum" size={12} /> <Link to={appThreadPath(post.discussion.threadId)}>{strings.forums.discussion(post.discussion.forumName || '')}</Link>{post.discussion.forumId && <> (<Link to={appForumPath(post.discussion.forumId)} className="muted">{post.discussion.forumName}</Link>)</>}</p>
      )}

      <footer className="post-foot">
        <ReactionBar target={postTarget(post)} value={post} canReact={viewer.canReact !== false && post.state === 'Published'} onChange={(fields) => update({ ...post, ...fields })} />
        {detail && !promoted
          ? <span className="muted small">{f.commentsCount(post.commentCount || 0)}</span>
          : <Link to={url + '#comments'} className="post-comments-link small"><Icon name="message" size={14} /><span>{f.commentsCount(post.commentCount || 0)}</span></Link>}
      </footer>

      {dialog === 'history' && <EditHistoryDialog kind="post" item={post} client={client} onClose={() => setDialog(null)} />}
      {dialog === 'report' && <ReportDialog target={postTarget(post)} author={author} onClose={() => setDialog(null)} />}
      {dialog === 'discuss' && <DiscussDialog post={post} onClose={() => setDialog(null)} onDone={(thread) => { setDialog(null); toast.success(strings.forums.discussedToast); update({ ...post, discussion: { threadId: thread.id, forumId: thread.forum && thread.forum.id, forumName: thread.forum && thread.forum.name } }); }} />}
      {dialog === 'block' && <BlockDialog user={author} onClose={() => setDialog(null)} onDone={() => { if (onBlocked) onBlocked(author); }} />}
      <ConfirmModal open={dialog === 'delete'} title={f.deleteTitle} message={f.deleteBody} confirmLabel={f.delete} busy={busy} onCancel={() => setDialog(null)} onConfirm={remove} />
      {dialog === 'edit' && (
        <Modal open size="lg" onClose={() => setDialog(null)} title={strings.composer.editTitle}>
          <PostComposer
            client={client}
            perms={{ ...perms, canAttachMedia: viewer.canAttachMedia !== undefined ? viewer.canAttachMedia : perms.canAttachMedia, canAttachVideo: viewer.canAttachVideo !== undefined ? viewer.canAttachVideo : perms.canAttachVideo }}
            post={post}
            onSubmit={(body) => client.updateFeedPost(post.id, body)}
            onSaved={(saved) => { toast.success(strings.composer.saved); setDialog(null); update(saved); }}
            onCancel={() => setDialog(null)}
          />
        </Modal>
      )}
    </article>
  );
}

export default PostCard;
