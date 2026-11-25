import { FlashList } from '@shopify/flash-list';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ScrollView, View } from 'react-native';
import KeyboardAvoider from '../KeyboardAvoider';
import { comments as commentsApi, feed as feedApi } from '../../api/community';
import { useAuth } from '../../auth/AuthProvider';
import { appendChildren, appendTopLevel, flattenTree, hideAuthor, insertComment, removeComment, replaceComment } from '../../content/comments';
import { describeContentError } from '../../content/errors';
import { canWrite } from '../../content/permissions';
import { readPage } from '../../content/paging';
import { filtersToQuery } from '../../content/enumeration';
import { useLayout } from '../../hooks/useLayout';
import { useListFilters } from '../../hooks/useListFilters';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import ChipGroup from '../ChipGroup';
import ErrorState from '../ErrorState';
import { FilterButton } from '../filters/FilterBar';
import Loading from '../Loading';
import CommentComposer from './CommentComposer';
import CommentItem from './CommentItem';
import PostCard, { LockedPostCard } from './PostCard';
import { accessDenial, featureAccess, isLockedItem } from '@trelliskit/shared';
import { useProperty } from '../../property';
import { AccessPromptCard } from '../AccessPrompt';
import { SaveRow } from '../SaveButton';

/** Tree page parameters (contract 7.3 defaults). */
export const TreePage = Object.freeze({ limit: 20, depth: 3, childLimit: 5 });

const SORTS = ['top', 'new', 'old'];

/**
 * A post (or, with `loadItem` and `renderCard`, any conversation owner such as a forum thread)
 * with its threaded comments: the full card, sort (Top, Newest, Oldest), the tree
 * with collapse and "load more replies", and the comment composer (new comment, reply, edit).
 * With `commentId` (a notification or permalink, `/feed/{id}?comment=`) it shows that comment
 * in context (its ancestors, then its replies) with "View all comments".
 * @param {object} props
 * @param {string} props.postId
 * @param {object} [props.initialPost] Shown while the post reloads.
 * @param {string|null} [props.commentId]
 * @param {(post: object) => void} [props.onPostChange] Keeps a feed list in sync.
 * @param {(id: string) => void} [props.onDeleted]
 * @param {(userId: string) => void} [props.onAuthorBlocked]
 * @param {(api: object, id: string) => Promise<object>} [props.loadItem] Loads the owner (default: the feed post).
 * @param {(args: { item: object, onChange: Function, onDeleted?: Function, onAuthorBlocked: Function }) => React.ReactNode} [props.renderCard]
 * @param {(item: object, user: object) => boolean} [props.canComment] Whether the composer shows.
 * @param {{ items?: (comment: object) => object[], badges?: (comment: object) => React.ReactNode }} [props.commentExtras]
 * @param {number} [props.treeKey] Reload the tree when this changes (accepted answer, review).
 * @param {{ title: string, body: string }} [props.goneText]
 */
export default function PostDetail({ postId, initialPost = null, commentId = null, onPostChange, onDeleted, onAuthorBlocked, transport, testID = 'post-detail', loadItem, renderCard, canComment, commentExtras, treeKey = 0, goneText, accessFeature = 'feed' }) {
  const theme = useTheme();
  const { api, user } = useAuth();
  const property = useProperty();
  // A locked stub never loads its detail, comments, or reactions (ACCESS_MODELS_CONTRACT.md section 8.5).
  const lockedStub = isLockedItem(initialPost);
  const { isWide } = useLayout();
  const [post, setPost] = useState(initialPost);
  const [postStatus, setPostStatus] = useState(initialPost ? 'ready' : 'loading');
  const [postError, setPostError] = useState(null);
  const [sort, setSort] = useState('top');
  const [tree, setTree] = useState([]);
  const [ancestors, setAncestors] = useState([]);
  const [cursor, setCursor] = useState(null);
  const [treeStatus, setTreeStatus] = useState('loading');
  const [treeError, setTreeError] = useState(null);
  const [moreBusy, setMoreBusy] = useState(null);
  const [collapsed, setCollapsed] = useState(() => new Set());
  const [showAll, setShowAll] = useState(!commentId);
  const [composer, setComposer] = useState({ mode: 'create', comment: null, n: 0 });
  const [filters, setFilters] = useListFilters('comments');
  const filterKey = JSON.stringify(filtersToQuery({ ...filters, ordering: null }));

  const onPostChangeRef = useRef(onPostChange);
  useEffect(() => {
    onPostChangeRef.current = onPostChange;
  });
  const notify = useCallback((next) => {
    if (onPostChangeRef.current) onPostChangeRef.current(next);
  }, []);

  const postRef = useRef(post);
  useEffect(() => {
    postRef.current = post;
  }, [post]);
  const adjustCount = useCallback(
    (delta) => {
      const current = postRef.current;
      if (!current) return;
      const next = { ...current, commentCount: Math.max(0, (current.commentCount || 0) + delta) };
      postRef.current = next;
      setPost(next);
      notify(next);
    },
    [notify]
  );

  const updatePost = useCallback(
    (next) => {
      setPost(next);
      notify(next);
    },
    [notify]
  );

  const loadPost = useCallback(async () => {
    setPostError(null);
    try {
      const next = loadItem ? await loadItem(api, postId) : await feedApi.get(api, postId);
      setPost(next);
      setPostStatus('ready');
      notify(next);
    } catch (e) {
      setPostError(e);
      setPostStatus((s) => (s === 'ready' && e.status !== 404 ? 'ready' : e.status === 404 ? 'gone' : 'error'));
    }
    // loadItem is a stable module-level loader.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [api, postId, notify]);

  const targetType = post && post.commentTarget ? post.commentTarget.type : 'FeedPost';
  const targetId = post && post.commentTarget ? post.commentTarget.id : postId;
  const target = useMemo(() => ({ type: targetType, id: targetId }), [targetType, targetId]);

  const loadTree = useCallback(async () => {
    setTreeStatus('loading');
    setTreeError(null);
    try {
      if (!showAll && commentId) {
        const result = await commentsApi.get(api, commentId, { depth: TreePage.depth, childLimit: TreePage.childLimit, sort });
        setAncestors((result && result.ancestors) || []);
        setTree(result && result.comment ? [result.comment] : []);
        setCursor(null);
      } else {
        const page = await commentsApi.list(api, target, { sort, ...TreePage, filters: { ...filters, ordering: null } });
        const { items, nextCursor } = readPage(page);
        setAncestors([]);
        setTree(items);
        setCursor(nextCursor);
      }
      setTreeStatus('ready');
    } catch (e) {
      setTreeError(e);
      setTreeStatus('error');
    }
    // filterKey stands for the filters' content.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [api, commentId, showAll, sort, target, filterKey, treeKey]);

  useEffect(() => {
    if (!lockedStub) loadPost();
  }, [loadPost, lockedStub]);

  // A tree reload after an owner change (accepted answer, review) also refreshes the owner.
  useEffect(() => {
    if (treeKey) loadPost();
    // Only treeKey changes trigger this refresh.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [treeKey]);

  // With a custom loader (forum threads) the comment target is only known once the item has loaded;
  // the FeedPost default would ask the feed routes for a thread id.
  const targetKnown = !loadItem || Boolean(post);

  useEffect(() => {
    if (targetKnown && !lockedStub && !isLockedItem(post)) loadTree();
    // The tree loads once the post is known to be readable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loadTree, lockedStub, targetKnown]);

  const loadMoreTop = useCallback(async () => {
    if (!cursor || moreBusy) return;
    setMoreBusy('top');
    try {
      const page = await commentsApi.list(api, target, { sort, ...TreePage, continuationToken: cursor, filters: { ...filters, ordering: null } });
      const { items, nextCursor } = readPage(page);
      setTree((current) => appendTopLevel(current, items));
      setCursor(nextCursor);
    } catch (e) {
      setTreeError(e);
    } finally {
      setMoreBusy(null);
    }
  }, [api, cursor, moreBusy, sort, target, filters]);

  const loadChildren = useCallback(
    async (row) => {
      if (moreBusy) return;
      setMoreBusy(row.parentId);
      try {
        const page = await commentsApi.children(api, row.parentId, { sort, limit: 20, depth: 2, childLimit: TreePage.childLimit, continuationToken: row.cursor || null });
        setTree((current) => appendChildren(current, row.parentId, readPage(page)));
      } catch (e) {
        setTreeError(e);
      } finally {
        setMoreBusy(null);
      }
    },
    [api, moreBusy, sort]
  );

  const toggleCollapse = useCallback((id) => {
    setCollapsed((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }, []);

  const changeComment = useCallback((next) => {
    setTree((current) => replaceComment(current, next));
    setAncestors((current) => current.map((c) => (c.id === next.id ? { ...c, ...next } : c)));
  }, []);

  const deleteComment = useCallback(
    (id) => {
      setTree((current) => removeComment(current, id));
      adjustCount(-1);
    },
    [adjustCount]
  );

  const authorBlocked = useCallback(
    (userId) => {
      setTree((current) => hideAuthor(current, userId));
      setAncestors((current) => hideAuthor(current, userId));
      if (onAuthorBlocked) onAuthorBlocked(userId);
    },
    [onAuthorBlocked]
  );

  const composerDone = useCallback(
    (comment, mode) => {
      if (mode === 'edit') changeComment(comment);
      else {
        setTree((current) => insertComment(current, comment));
        if (comment.parentCommentId) setCollapsed((current) => {
          const next = new Set(current);
          next.delete(comment.parentCommentId);
          return next;
        });
        adjustCount(1);
      }
      setComposer((c) => ({ mode: 'create', comment: null, n: c.n + 1 }));
    },
    [changeComment, adjustCount]
  );

  const rows = useMemo(() => {
    const out = ancestors.map((c, i) => ({ type: 'comment', key: 'a:' + c.id, comment: { ...c, children: [], moreChildren: null }, depth: i, collapsed: false, ancestor: true }));
    return out.concat(flattenTree(tree, collapsed));
  }, [ancestors, tree, collapsed]);

  if (post && isLockedItem(post)) {
    return (
      <View style={{ padding: theme.spacing.lg }} testID={testID + '-locked'}>
        <LockedPostCard post={post} />
      </View>
    );
  }
  const denial = accessDenial(postError);
  if (denial && (!post || postStatus === 'error')) {
    return (
      <View style={{ padding: theme.spacing.lg }} testID={testID + '-denied'}>
        <AccessPromptCard required={denial.required} feature={denial.feature || accessFeature} />
      </View>
    );
  }
  if (postStatus === 'loading' && !post) return <Loading label={strings.common.loading} testID={testID + '-loading'} />;
  if (postStatus === 'gone') {
    return (
      <View style={{ padding: theme.spacing.lg }} testID={testID + '-gone'}>
        <ErrorState title={goneText ? goneText.title : strings.feed.unavailableTitle} message={goneText ? goneText.body : strings.feed.unavailableBody} />
      </View>
    );
  }
  if (!post) {
    return (
      <View style={{ padding: theme.spacing.lg }}>
        <ErrorState title={strings.feed.postLoadFailedTitle} message={describeContentError(postError)} onRetry={loadPost} testID={testID + '-error'} />
      </View>
    );
  }

  const commentsOpen = post.commentsEnabled !== false;
  const mayComment = canComment ? canComment(post, user) && canWrite(user) : commentsOpen && canWrite(user) && (post.viewer ? post.viewer.canComment !== false : true);
  const pad = isWide ? theme.spacing.xl : theme.spacing.lg;

  const header = (
    <View style={{ gap: theme.spacing.lg, paddingHorizontal: pad, paddingTop: pad }}>
      {renderCard ? renderCard({ item: post, onChange: updatePost, onDeleted, onAuthorBlocked: authorBlocked }) : <View><SaveRow targetType="Post" targetId={post.id} testID={testID + '-save'} /><PostCard post={post} onChange={updatePost} onDeleted={onDeleted} onAuthorBlocked={authorBlocked} detail /></View>}
      <View style={{ gap: theme.spacing.sm }}>
        <AppText variant="heading" accessibilityRole="header">
          {strings.comments.title}
        </AppText>
        {showAll ? (
          <>
            <ChipGroup label={strings.comments.sortLabel} options={SORTS.map((s) => ({ value: s, label: strings.comments.sort[s] }))} value={sort} onToggle={setSort} testID="comments-sort" />
            <FilterButton filters={filters} onChange={setFilters} targetType="Comment" searchLabel={strings.filters.searchComments} sheetOnly testID="comments-filters" />
          </>
        ) : (
          <View style={{ gap: theme.spacing.sm }}>
            <AppText color="textMuted">{strings.comments.inContext}</AppText>
            <Button label={strings.comments.viewAll} accessibilityHint={strings.comments.viewAllHint} variant="secondary" onPress={() => setShowAll(true)} testID="comments-view-all" />
          </View>
        )}
        {treeStatus === 'loading' ? <Loading label={strings.common.loading} testID="comments-loading" /> : null}
        {treeStatus === 'error' ? <ErrorState title={strings.comments.loadFailedTitle} message={describeContentError(treeError)} onRetry={loadTree} testID="comments-error" /> : null}
        {treeStatus === 'ready' && !rows.length ? <AppText color="textMuted">{commentsOpen ? strings.comments.empty : strings.comments.disabled}</AppText> : null}
      </View>
    </View>
  );

  const footer = (
    <View style={{ paddingHorizontal: pad, paddingBottom: pad, gap: theme.spacing.md }}>
      {cursor && showAll ? <Button label={strings.comments.loadMore} variant="secondary" onPress={loadMoreTop} disabled={moreBusy === 'top'} testID="comments-load-more" /> : null}
    </View>
  );

  const renderRow = ({ item }) => {
    if (item.type === 'more') {
      return (
        <View style={{ paddingHorizontal: pad, marginLeft: Math.min(item.depth, 6) * 12 }}>
          <Button label={moreBusy === item.parentId ? strings.comments.loadingReplies : strings.comments.moreReplies(item.remainingCount)} variant="ghost" onPress={() => loadChildren(item)} disabled={Boolean(moreBusy)} testID={'more-' + item.parentId} />
        </View>
      );
    }
    return (
      <View style={{ paddingHorizontal: pad }}>
        <CommentItem
          comment={item.comment}
          depth={item.depth}
          collapsed={item.collapsed}
          highlighted={!showAll && item.comment.id === commentId}
          onToggleCollapse={toggleCollapse}
          onReply={(c) => setComposer((s) => ({ mode: 'reply', comment: c, n: s.n + 1 }))}
          onEdit={(c) => setComposer((s) => ({ mode: 'edit', comment: c, n: s.n + 1 }))}
          onChange={changeComment}
          onDeleted={deleteComment}
          onAuthorBlocked={authorBlocked}
          extraItems={commentExtras && commentExtras.items ? commentExtras.items(item.comment) : null}
          badges={commentExtras && commentExtras.badges ? commentExtras.badges(item.comment) : null}
        />
      </View>
    );
  };

  return (
    <KeyboardAvoider style={{ flex: 1 }} iosOffset={96} testID={testID}>
      <View style={{ flex: 1 }}>
        <FlashList data={rows} keyExtractor={(row) => row.key} getItemType={(row) => row.type} renderItem={renderRow} ListHeaderComponent={header} ListFooterComponent={footer} keyboardShouldPersistTaps="handled" testID={testID + '-list'} />
      </View>
      <ScrollView testID={testID + '-composer'} style={{ flexGrow: 0, maxHeight: 360, borderTopWidth: 1, borderTopColor: theme.colors.border, backgroundColor: theme.colors.background }} contentContainerStyle={{ padding: theme.spacing.md }} keyboardShouldPersistTaps="handled">
        {mayComment || composer.mode === 'edit' ? (
          <CommentComposer
            key={composer.mode + ':' + (composer.comment ? composer.comment.id : '') + ':' + composer.n}
            post={post}
            replyTo={composer.mode === 'reply' ? composer.comment : null}
            editing={composer.mode === 'edit' ? composer.comment : null}
            onDone={composerDone}
            onCancel={composer.mode === 'create' ? undefined : () => setComposer((s) => ({ mode: 'create', comment: null, n: s.n + 1 }))}
            transport={transport}
          />
        ) : commentsOpen && !featureAccess(property, user, accessFeature, 'write').allowed ? (
          <AccessPromptCard
            required={featureAccess(property, user, accessFeature, 'write').requires}
            feature={accessFeature}
            title={user ? undefined : strings.access.joinIn}
            body={user ? undefined : strings.access.joinInBody}
            testID="comments-access"
          />
        ) : (
          <AppText color="textMuted" testID="comments-closed">
            {commentsOpen ? strings.comments.notAllowed : strings.comments.disabled}
          </AppText>
        )}
      </ScrollView>
    </KeyboardAvoider>
  );
}
