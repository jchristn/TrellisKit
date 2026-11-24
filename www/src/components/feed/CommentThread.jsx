import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { commentTargetOf, conversationPath, metadataForSave, metadataOf, normalizeEnumerationResult, scalarLength } from '@trelliskit/shared';
import MetadataEditor from '../MetadataEditor.jsx';
import useEnumerationUrlState from '../../hooks/useEnumerationUrlState.js';
import MetadataFilterBar from '../MetadataFilterBar.jsx';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { featureAccess } from '@trelliskit/shared';
import { AccessPromptCard } from '../AccessPrompt.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import ActionMenu from '../ActionMenu.jsx';
import ConfirmModal from '../ConfirmModal.jsx';
import Icon from '../Icon.jsx';
import { ErrorBanner, Loading, EmptyState } from '../States.jsx';
import { copyText } from '../../utils/clipboard.js';
import { authorName, errorText, postPath, postingPermissions } from '../../utils/engagement.js';
import { formatDateTime } from '../../utils/format.js';
import { addChildren, addTopLevelPage, emptyTree, insertComment, removeComment, replaceComment, toggleCollapsed } from '../../utils/commentTree.js';
import AuthorLine from './AuthorLine.jsx';
import MarkdownBody from './MarkdownBody.jsx';
import MetadataBadges from '../MetadataBadges.jsx';
import MarkdownEditor from './MarkdownEditor.jsx';
import MediaGallery from './MediaGallery.jsx';
import LinkPreviewCard from './LinkPreviewCard.jsx';
import ReactionBar from './ReactionBar.jsx';
import EditHistoryDialog from './EditHistoryDialog.jsx';
import ReportDialog from './ReportDialog.jsx';
import BlockDialog from './BlockDialog.jsx';
import CommentComposer, { COMMENT_MAX_LENGTH } from './CommentComposer.jsx';

const cm = strings.comments;
const SORTS = ['top', 'new', 'old'];
/** Comment sorts as enumeration orderings. */
export const SORT_ORDERING = Object.freeze({ top: 'Top', new: 'CreatedDescending', old: 'CreatedAscending' });
const PAGE = { maxResults: 20, depth: 3, childLimit: 5 };

/** A tree page request (EnumerationQuery with the tree limits as collection filters). */
export function treeQuery(sort, extra = {}) {
  const { continuationToken, maxResults = PAGE.maxResults, depth = PAGE.depth, childLimit = PAGE.childLimit, meta = null } = extra;
  return { ...(meta || {}), ordering: SORT_ORDERING[sort] || 'Top', maxResults, continuationToken, filters: { depth, childLimit } };
}

/** A tree page response as `{ items, nextCursor, commentCount }` (EnumerationResult or older shapes). */
export function treePage(response) {
  const r = normalizeEnumerationResult(response);
  return { items: r.objects, nextCursor: r.endOfResults ? null : r.continuationToken, commentCount: typeof r.extra.commentCount === 'number' ? r.extra.commentCount : undefined };
}
const PLACEHOLDER_TEXT = { Deleted: cm.deleted, Removed: cm.removed, Hidden: cm.hidden, BlockedAuthor: cm.blockedAuthor };

function Placeholder({ comment }) {
  return <p className="comment-placeholder muted">{PLACEHOLDER_TEXT[comment.state] || cm.deleted}</p>;
}

function CommentEditor({ comment, client, onSaved, onCancel, isStaff }) {
  const [body, setBody] = useState(comment.body || '');
  const [metadata, setMetadata] = useState(() => metadataOf(comment));
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const save = async (e) => {
    e.preventDefault();
    if (!body.trim() && !(comment.attachments || []).length) {
      setError(strings.composer.bodyRequired);
      return;
    }
    if (scalarLength(body) > COMMENT_MAX_LENGTH) {
      setError(strings.composer.tooLong(COMMENT_MAX_LENGTH));
      return;
    }
    setBusy(true);
    try {
      onSaved(await client.updateComment(comment.id, { body: body.trim(), ...metadataForSave(metadata, { isStaff }) }));
    } catch (err) {
      setError(errorText(err, strings.composer.errors));
      setBusy(false);
    }
  };
  return (
    <form className="comment-edit" onSubmit={save} noValidate>
      <MarkdownEditor label={cm.edit} hideLabel value={body} onChange={setBody} client={client} maxLength={COMMENT_MAX_LENGTH} allowHeadings={false} rows={3} disabled={busy} context={{ type: comment.targetType, id: comment.targetId }} error={error} autoFocus />
      <MetadataEditor value={metadata} onChange={(next) => setMetadata(next)} isStaff={isStaff} client={client} targetType="Comment" disabled={busy} collapsible={!metadataOf(comment).labels.length && !Object.keys(metadataOf(comment).tags).length} />
      <div className="form-actions">
        <button type="button" className="button button-secondary button-sm" onClick={onCancel} disabled={busy}>{strings.common.cancel}</button>
        <button type="submit" className="button button-primary button-sm" disabled={busy}>{strings.composer.submitSave}</button>
      </div>
    </form>
  );
}

function CommentNode({ id, tree, ctx, focusId }) {
  const { me, client, toast } = ctx;
  const comment = tree.byId[id];
  const [mode, setMode] = useState(null);
  const [dialog, setDialog] = useState(null);
  const [loadingMore, setLoadingMore] = useState(false);
  if (!comment) return null;
  const viewer = comment.viewer || {};
  const childIds = tree.childIds[id] || [];
  const more = tree.more[id];
  const collapsed = Boolean(tree.collapsed[id]);
  const author = comment.author;
  const placeholder = !comment.body && !(comment.attachments || []).length && comment.state !== 'Visible';
  const isSelf = Boolean(me && author && author.id === me.id);
  const link = ctx.linkFor(comment.id);
  const hiddenCount = (comment.descendantCount || childIds.length || 0);

  const loadMore = async () => {
    setLoadingMore(true);
    try {
      const token = more ? more.continuationToken || more.cursor : null;
      const page = await client.listCommentChildren(id, treeQuery(ctx.sort, { continuationToken: token || undefined }));
      ctx.setTree((t) => addChildren(t, id, treePage(page)));
    } catch (err) {
      toast.error(errorText(err));
    } finally {
      setLoadingMore(false);
    }
  };

  const items = [];
  if (!placeholder) items.push({ label: cm.copyLink, icon: 'link', onSelect: () => copyText(window.location.origin + link).then(() => toast.success(strings.feed.linkCopied), () => {}) });
  if (viewer.canEdit) items.push({ label: cm.edit, icon: 'edit', onSelect: () => setMode('edit') });
  if (viewer.canReport && !isSelf) items.push({ label: cm.report, icon: 'flag', onSelect: () => setDialog('report') });
  if (!isSelf && author && author.id && !author.isDeleted && !author.isStaff && !placeholder) items.push({ label: strings.feed.blockAuthor(authorName(author)), icon: 'ban', danger: true, onSelect: () => setDialog('block') });
  if (!placeholder && ctx.extras && ctx.extras.items) items.push(...ctx.extras.items(comment));
  if (viewer.canDelete) items.push({ label: cm.delete, icon: 'trash', danger: true, onSelect: () => setDialog('delete') });

  const remove = async () => {
    try {
      await client.deleteComment(comment.id);
      ctx.setTree((t) => removeComment(t, comment.id));
      toast.success(cm.deletedToast);
    } catch (err) {
      toast.error(errorText(err));
    }
    setDialog(null);
  };

  return (
    <li className={'comment' + (focusId === id ? ' comment-focus' : '') + (collapsed ? ' comment-collapsed' : '')} id={'comment-' + id} data-depth={comment.depth}>
      <div className="comment-rail">
        <button type="button" className="comment-collapse" onClick={() => ctx.setTree((t) => toggleCollapsed(t, id))} aria-expanded={!collapsed} aria-label={collapsed ? cm.expand : cm.collapse} title={collapsed ? cm.expand : cm.collapse}>
          <Icon name={collapsed ? 'plus' : 'chevronDown'} size={12} />
        </button>
        {!collapsed && childIds.length > 0 && <span className="comment-line" aria-hidden="true" onClick={() => ctx.setTree((t) => toggleCollapsed(t, id))} />}
      </div>
      <div className="comment-main">
        <div className="comment-head">
          {placeholder ? (collapsed ? <span className="muted small">{PLACEHOLDER_TEXT[comment.state] || cm.deleted}</span> : <span />) : (
            <AuthorLine
              user={author}
              size={28}
              time={comment.createdUtc}
              timeTo={link}
              extra={(
                <>
                  {comment.replyToUser && comment.replyToUser.handle && <span className="comment-replying">{cm.replyingTo(comment.replyToUser.handle)}</span>}
                  {ctx.extras && ctx.extras.badges ? ctx.extras.badges(comment) : null}
                  <MetadataBadges labels={comment.labels} tags={comment.tags} max={4} />
                  {comment.editedUtc && (
                    <>
                      <span aria-hidden="true">·</span>
                      <button type="button" className="link-button edited-button" onClick={() => setDialog('history')} title={strings.feed.editedTitle(formatDateTime(comment.editedUtc))}>{cm.edited}</button>
                    </>
                  )}
                </>
              )}
            />
          )}
          {collapsed && <span className="muted small comment-collapsed-note">{cm.collapsedCount(hiddenCount)}</span>}
          {!collapsed && items.length > 0 && <ActionMenu label={cm.menu} items={items} />}
        </div>
        {!collapsed && (
          <>
            {comment.moderation && <p className="post-note post-moderation small" role="note">{cm.moderationNote(comment.moderation.state, comment.moderation.reason)}</p>}
            {mode === 'edit' ? (
              <CommentEditor comment={comment} client={client} isStaff={ctx.perms.isStaff} onCancel={() => setMode(null)} onSaved={(saved) => { ctx.setTree((t) => replaceComment(t, saved)); setMode(null); toast.success(cm.savedToast); }} />
            ) : placeholder ? <Placeholder comment={comment} /> : (
              <>
                <MarkdownBody body={comment.body} mentions={comment.mentions} allowHeadings={false} className="comment-body" />
                <MediaGallery items={comment.attachments} compact />
                {comment.linkPreview && <LinkPreviewCard preview={comment.linkPreview} />}
              </>
            )}
            {!placeholder && mode !== 'edit' && (
              <div className="comment-actions">
                <ReactionBar target={{ type: 'Comment', id: comment.id }} value={comment} canReact={viewer.canReact !== false} onChange={(fields) => ctx.setTree((t) => replaceComment(t, { ...t.byId[id], ...fields }))} />
                {viewer.canReply && (
                  <button type="button" className="link-button small comment-reply-button" onClick={() => setMode(mode === 'reply' ? null : 'reply')} aria-expanded={mode === 'reply'}>
                    <Icon name="reply" size={14} /><span>{cm.reply}</span>
                  </button>
                )}
              </div>
            )}
            {mode === 'reply' && (
              <CommentComposer
                client={client}
                target={ctx.target}
                parent={comment}
                viewer={viewer}
                isAdmin={ctx.perms.isAdmin}
                isStaff={ctx.perms.isStaff}
                uploadOptions={ctx.uploadOptions}
                autoFocus
                onCancel={() => setMode(null)}
                onCreated={(created) => { ctx.setTree((t) => insertComment(t, created)); setMode(null); toast.success(cm.postedToast); }}
              />
            )}
            {childIds.length > 0 && (
              <ul className="comment-children">
                {childIds.map((childId) => <CommentNode key={childId} id={childId} tree={tree} ctx={ctx} focusId={focusId} />)}
              </ul>
            )}
            {more && (more.remainingCount > 0 || more.cursor) && (
              <button type="button" className="link-button small comment-more" onClick={loadMore} disabled={loadingMore}>
                {loadingMore ? strings.common.loading : (childIds.length === 0 ? cm.continueThread : cm.moreReplies(more.remainingCount || 1))}
              </button>
            )}
          </>
        )}
      </div>
      {dialog === 'history' && <EditHistoryDialog kind="comment" item={comment} client={client} onClose={() => setDialog(null)} />}
      {dialog === 'report' && <ReportDialog target={{ type: 'Comment', id: comment.id }} author={author} onClose={() => setDialog(null)} />}
      {dialog === 'block' && <BlockDialog user={author} onClose={() => setDialog(null)} onDone={() => ctx.setTree((t) => replaceComment(t, { ...t.byId[id], state: 'BlockedAuthor', body: null, attachments: [], mentions: [], linkPreview: null, author: null }))} />}
      <ConfirmModal open={dialog === 'delete'} title={cm.deletedTitle} message={cm.deletedBody} confirmLabel={cm.delete} onCancel={() => setDialog(null)} onConfirm={remove} />
    </li>
  );
}

/**
 * Threaded comments for a post or a forum thread (Phase 2 contract section 7, Phase 3 section
 * 4.10): sort top, new, or old; a page of top-level comments with nested replies; collapse; "load
 * more replies" and "continue this thread"; replies (flattened by the server at the maximum depth,
 * shown as "replying to @handle"); edit with history; delete placeholders; and the permalink view
 * of one comment with its ancestors. Every call goes to the owner's `commentTarget` (one
 * conversation), and permalinks point at the conversation's original object.
 *
 * `extras` adds per-comment menu items and badges (accepted answer, feedback review), and
 * `canComment` overrides the composer rule (forum threads: not locked, Published).
 *
 * @param {{ post: object, focusId?: string|null, onCountChange?: (count: number) => void, uploadOptions?: object,
 *   canComment?: boolean, extras?: { items?: (comment: object) => object[], badges?: (comment: object) => React.ReactNode }, viewAllPath?: string, linkPrefix?: string }} props
 */
/**
 * The "sign in to join in" or upgrade prompt shown in place of the comment composer when the
 * caller cannot comment because of the access model (signed out, or a gated feature).
 */
function joinPrompt(property, me, feature) {
  const access = featureAccess(property, me, feature, 'write');
  if (access.allowed) return null;
  if (!me) return <AccessPromptCard compact required={access.requires} feature={feature} title={strings.access.joinIn} body={strings.access.joinInBody} />;
  return <AccessPromptCard compact required={access.requires} feature={feature} />;
}

function CommentThread({ post, focusId = null, onCountChange, uploadOptions, canComment: canCommentProp, extras = null, viewAllPath, linkPrefix = '/app', accessFeature = 'feed' }) {
  const { property: accessProperty } = useProperty();
  const { client, me } = useAuth();
  const toast = useToast();
  const perms = postingPermissions(me);
  const target = commentTargetOf(post, 'FeedPost');
  const linkFor = (commentId) => linkPrefix + (conversationPath(target, commentId) || postPath(post.id, commentId).replace(/^\/app/, ''));
  const allPath = viewAllPath || linkFor();
  const [sort, setSort] = useState('top');
  const [meta, setMeta] = useEnumerationUrlState({ prefix: 'c.' });
  const [tree, setTree] = useState(emptyTree);
  const [ancestors, setAncestors] = useState([]);
  const [status, setStatus] = useState({ loading: true, error: null, notFound: false });
  const [loadingMore, setLoadingMore] = useState(false);
  const targetKey = target.type + ':' + target.id;

  const load = useCallback(async (signal) => {
    setStatus({ loading: true, error: null, notFound: false });
    try {
      if (focusId) {
        const body = await client.getComment(focusId, { depth: 5, childLimit: PAGE.childLimit, ordering: SORT_ORDERING[sort] }, signal);
        setAncestors((body && body.ancestors) || []);
        setTree(addTopLevelPage(emptyTree(), { items: body && body.comment ? [body.comment] : [], nextCursor: null }, { replace: true }));
      } else {
        const page = await client.listComments(target, treeQuery(sort, { meta }), signal);
        setAncestors([]);
        setTree(addTopLevelPage(emptyTree(), treePage(page), { replace: true }));
      }
      setStatus({ loading: false, error: null, notFound: false });
    } catch (err) {
      if (signal && signal.aborted) return;
      setStatus({ loading: false, error: err, notFound: Boolean(err && err.status === 404) });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [client, focusId, sort, targetKey, meta]);

  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal);
    return () => controller.abort();
  }, [load]);

  useEffect(() => {
    if (onCountChange && !focusId && !status.loading && !status.error) onCountChange(tree.commentCount);
  }, [tree.commentCount, onCountChange, focusId, status.loading, status.error]);

  useEffect(() => {
    if (!focusId || status.loading) return;
    const el = document.getElementById('comment-' + focusId);
    if (el && el.scrollIntoView) el.scrollIntoView({ block: 'center' });
  }, [focusId, status.loading]);

  const loadMoreTop = async () => {
    setLoadingMore(true);
    try {
      const page = await client.listComments(target, treeQuery(sort, { continuationToken: tree.nextCursor, meta }));
      setTree((t) => addTopLevelPage(t, treePage(page)));
    } catch (err) {
      toast.error(errorText(err, {}, cm.loadError));
    } finally {
      setLoadingMore(false);
    }
  };

  const viewer = post.viewer || {};
  const canComment = canCommentProp !== undefined ? canCommentProp : viewer.canComment && post.commentsEnabled !== false && post.state === 'Published';
  const ctx = { client, me, toast, sort, target, setTree, perms, uploadOptions, linkFor, extras };

  return (
    <section className="comments" id="comments" aria-labelledby="comments-title">
      <div className="comments-head">
        <h2 id="comments-title">{focusId ? cm.permalinkTitle : cm.title}</h2>
        {!focusId && (
          <div className="segmented" role="radiogroup" aria-label={cm.sortLabel}>
            {SORTS.map((value) => (
              <button key={value} type="button" role="radio" aria-checked={sort === value} className={'segment' + (sort === value ? ' active' : '')} onClick={() => setSort(value)}>{cm.sorts[value]}</button>
            ))}
          </div>
        )}
        {focusId && <Link to={allPath + '#comments'} className="button button-secondary button-sm">{cm.viewAll}</Link>}
      </div>

      {!focusId && <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType="Comment" showSearch />}
      {post.commentsEnabled === false && <p className="muted small">{strings.feed.commentsDisabled}</p>}
      {!canComment && !focusId && post.commentsEnabled !== false && post.state === 'Published' && joinPrompt(accessProperty, me, accessFeature)}
      {canComment && !focusId && (
        <CommentComposer client={client} target={target} viewer={viewer} isAdmin={perms.isAdmin} isStaff={perms.isStaff} uploadOptions={uploadOptions} onCreated={(created) => { setTree((t) => insertComment(t, created)); toast.success(cm.postedToast); }} />
      )}

      {status.loading && <Loading />}
      {status.error && (status.notFound && focusId
        ? <EmptyState icon="info" title={cm.notFound} action={<Link to={allPath} className="button button-secondary button-sm">{cm.viewAll}</Link>} />
        : <ErrorBanner message={cm.loadError + ' ' + errorText(status.error, {}, '')} onRetry={() => load()} />)}

      {!status.loading && !status.error && ancestors.length > 0 && (
        <ol className="comment-ancestors" aria-label={cm.parentContext}>
          {ancestors.map((a) => (
            <li key={a.id} className="comment-ancestor">
              {a.body ? (
                <>
                  <AuthorLine user={a.author} size={24} time={a.createdUtc} timeTo={linkFor(a.id)} />
                  <MarkdownBody body={a.body} mentions={a.mentions} allowHeadings={false} className="comment-body" />
                </>
              ) : <Placeholder comment={a} />}
            </li>
          ))}
        </ol>
      )}

      {!status.loading && !status.error && tree.roots.length === 0 && !focusId && <p className="muted comments-empty">{cm.empty}</p>}
      {tree.roots.length > 0 && (
        <ul className="comment-tree">
          {tree.roots.map((id) => <CommentNode key={id} id={id} tree={tree} ctx={ctx} focusId={focusId} />)}
        </ul>
      )}
      {!focusId && tree.nextCursor && !status.loading && (
        <button type="button" className="button button-secondary comments-more" onClick={loadMoreTop} disabled={loadingMore}>{loadingMore ? strings.common.loading : cm.loadMore}</button>
      )}
    </section>
  );
}

export default CommentThread;
