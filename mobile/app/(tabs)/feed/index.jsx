import { FlashList } from '@shopify/flash-list';
import { useRouter } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import { RefreshControl, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { feed as feedApi } from '../../../src/api/community';
import { useAuth } from '../../../src/auth/AuthProvider';
import AppText from '../../../src/components/AppText';
import Button from '../../../src/components/Button';
import Card from '../../../src/components/Card';
import { FilterButton, FilterSidePanel } from '../../../src/components/filters/FilterBar';
import ErrorState from '../../../src/components/ErrorState';
import PostCard from '../../../src/components/feed/PostCard';
import PostDetail from '../../../src/components/feed/PostDetail';
import Loading from '../../../src/components/Loading';
import { activeFilterCount, filtersToQuery } from '../../../src/content/enumeration';
import { describeContentError } from '../../../src/content/errors';
import { onContentEvent } from '../../../src/content/events';
import { canCreateFeedPost } from '../../../src/content/permissions';
import { useCursorList } from '../../../src/hooks/useCursorList';
import { useLayout } from '../../../src/hooks/useLayout';
import { useListFilters } from '../../../src/hooks/useListFilters';
import strings from '../../../src/strings';
import { useTheme } from '../../../src/theme';
import { featureAccess } from '@trelliskit/shared';
import { AccessPromptCard } from '../../../src/components/AccessPrompt';
import { BannerSlot, useKillSwitch } from '../../../src/appConfig';
import Notice from '../../../src/components/Notice';
import { useProperty } from '../../../src/property';

/** Post kinds this build renders (contract 5.1: clients skip kinds they do not know; Phase 3 adds promoted threads). */
export const KnownKinds = Object.freeze(['Original', 'PromotedThread']);

/** Feed page size (contract 5.2 default; within the member enumeration maximum). */
export const FeedPageSize = 20;

/** Orderings offered in the feed filters (the server's default keeps pinned posts first). */
export const FeedOrderings = Object.freeze(['CreatedDescending', 'CreatedAscending']);

/** Width of the feed list pane on wide screens (post cards need a little more than other lists). */
const FeedListPaneWidth = (theme) => theme.layout.listPaneWidth + 40;

const isKnown = (post) => Boolean(post && post.id && (!post.kind || KnownKinds.includes(post.kind)));

/**
 * Feed tab: an infinite FlashList over POST /feed/enumerate paged by continuationToken (pinned
 * posts come first on the first page), pull to refresh, search, ordering, label and tag filters
 * (a bottom sheet on phones, a side panel on tablets; kept for the session), and a New post
 * button for members allowed to post. On tablets the selected post opens beside the list; on
 * phones it opens /feed/{id}.
 */
export default function FeedScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api, user } = useAuth();
  const { isWide, width } = useLayout();
  // Filters, list, and detail side by side need room for a readable detail pane (a portrait
  // tablet does not have it); narrower wide windows use the filter sheet instead.
  const filterSidePanel = isWide && width >= theme.layout.filterPanelWidth + FeedListPaneWidth(theme) + theme.layout.minDetailPaneWidth;
  const [selectedId, setSelectedId] = useState(null);
  const [filters, setFilters] = useListFilters('feed');
  const fetchPage = useCallback((continuationToken) => feedApi.list(api, { continuationToken, maxResults: FeedPageSize, filters }), [api, filters]);
  const list = useCursorList(fetchPage, { filter: isKnown, key: JSON.stringify(filtersToQuery(filters)) });
  const orderings = FeedOrderings.map((value) => ({ value, label: strings.filters.orderings[value] }));
  const filtered = activeFilterCount(filters) > 0;
  const { updateItem, upsertItem, setItems } = list;
  const property = useProperty();
  // Posting rights come from Me; a gated feed (PartiallyGated) also needs a membership.
  const writeAccess = featureAccess(property, user, 'feed', 'write');
  // The feedComposer kill switch pauses new posts (LIVE_UPDATES_CONTRACT.md section 13.2); reading continues.
  const composerKill = useKillSwitch('feedComposer');
  const canPost = canCreateFeedPost(user) && writeAccess.allowed && !composerKill.killed;
  const lockedPoster = Boolean(user) && canCreateFeedPost(user) && !writeAccess.allowed;

  useEffect(() => {
    const offPost = onContentEvent('post', (post) => {
      if (isKnown(post)) upsertItem(post, { prepend: post.state === 'Published' });
    });
    const offDeleted = onContentEvent('postDeleted', (id) => updateItem(id, () => null));
    return () => {
      offPost();
      offDeleted();
    };
  }, [updateItem, upsertItem]);

  const changePost = useCallback((post) => updateItem(post.id, (current) => ({ ...current, ...post })), [updateItem]);
  const deletePost = useCallback(
    (id) => {
      updateItem(id, () => null);
      setSelectedId((current) => (current === id ? null : current));
    },
    [updateItem]
  );
  const authorBlocked = useCallback((userId) => setItems((items) => items.filter((p) => !(p.author && p.author.id === userId))), [setItems]);

  const open = (post) => {
    if (post.kind === 'PromotedThread' && post.source && post.source.id) router.push('/threads/' + post.source.id);
    else if (isWide) setSelectedId(post.id);
    else router.push('/feed/' + post.id);
  };

  const header = (
    <View style={{ gap: theme.spacing.md, paddingBottom: theme.spacing.md }}>
      <BannerSlot placement="Home" />
      <BannerSlot placement="Feed" />
      {!user ? <AccessPromptCard required="SignIn" feature="feed" title={strings.access.joinIn} body={strings.access.joinInBody} testID="feed-join-in" /> : null}
      {lockedPoster ? <AccessPromptCard required={writeAccess.requires} feature="feed" testID="feed-composer-locked" /> : null}
      {composerKill.killed && canCreateFeedPost(user) && writeAccess.allowed ? (
        <Notice testID="feed-composer-off">{composerKill.message || strings.appContent.composerOff}</Notice>
      ) : null}
      {canPost ? <Button label={strings.feed.newPost} accessibilityHint={strings.feed.newPostHint} onPress={() => router.push('/compose')} testID="feed-new-post" /> : null}
      <FilterButton filters={filters} onChange={setFilters} targetType="FeedPost" orderings={orderings} searchLabel={strings.filters.searchFeed} sheetOnly={!filterSidePanel} testID="feed-filters" />
      {list.status === 'error' ? <ErrorState title={strings.feed.loadFailedTitle} message={describeContentError(list.error)} onRetry={list.reload} retryHint={strings.feed.retryHint} testID="feed-error" /> : null}
      {list.status === 'ready' && !list.items.length ? (
        <Card testID="feed-empty">
          <AppText variant="heading">{filtered ? strings.filters.noResults : strings.feed.emptyTitle}</AppText>
          {filtered ? null : <AppText color="textMuted">{strings.feed.emptyBody}</AppText>}
        </Card>
      ) : null}
    </View>
  );

  const footer = (
    <View style={{ paddingVertical: theme.spacing.lg, gap: theme.spacing.sm }}>
      {list.loadingMore ? <Loading label={strings.feed.loadingMore} testID="feed-loading-more" /> : null}
      {list.moreError ? (
        <>
          <AppText color="danger" accessibilityRole="alert">
            {strings.feed.loadMoreFailed}
          </AppText>
          <Button label={strings.feed.loadMore} variant="secondary" onPress={list.loadMore} testID="feed-load-more" />
        </>
      ) : null}
    </View>
  );

  const pad = isWide ? theme.spacing.xl : theme.spacing.lg;
  const feedList =
    list.status === 'loading' ? (
      <Loading label={strings.feed.loading} testID="feed-loading" />
    ) : (
      <FlashList
        testID="feed-list"
        data={list.items}
        keyExtractor={(post) => post.id}
        renderItem={({ item }) => (
          <View style={{ paddingBottom: theme.spacing.md }}>
            <PostCard post={item} onChange={changePost} onOpen={() => open(item)} onDeleted={deletePost} onAuthorBlocked={authorBlocked} selected={isWide && item.id === selectedId} />
          </View>
        )}
        ListHeaderComponent={header}
        ListFooterComponent={footer}
        onEndReached={list.loadMore}
        onEndReachedThreshold={0.5}
        refreshControl={<RefreshControl refreshing={list.refreshing} onRefresh={list.refresh} tintColor={theme.colors.primary} accessibilityLabel={strings.feed.refreshing} />}
        contentContainerStyle={{ padding: pad }}
      />
    );

  if (!isWide) {
    return (
      <SafeAreaView testID="screen-feed" edges={['left', 'right']} style={[styles.fill, { backgroundColor: theme.colors.background }]}>
        {feedList}
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView testID="screen-feed" edges={['left', 'right']} style={[styles.fill, styles.row, { backgroundColor: theme.colors.background }]}>
      {filterSidePanel ? <FilterSidePanel filters={filters} onChange={setFilters} targetType="FeedPost" orderings={orderings} searchLabel={strings.filters.searchFeed} testID="feed-filters" /> : null}
      <View testID="split-list-pane" style={{ width: FeedListPaneWidth(theme), borderRightWidth: StyleSheet.hairlineWidth, borderRightColor: theme.colors.border }}>
        {feedList}
      </View>
      <View testID="split-detail-pane" style={[styles.fill, { backgroundColor: theme.colors.surface }]}>
        {selectedId ? (
          <PostDetail key={selectedId} postId={selectedId} initialPost={list.items.find((p) => p.id === selectedId) || null} onPostChange={changePost} onDeleted={deletePost} onAuthorBlocked={authorBlocked} />
        ) : (
          <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', padding: theme.spacing.xl }}>
            <AppText color="textMuted">{strings.feed.selectPost}</AppText>
          </View>
        )}
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  row: { flexDirection: 'row' }
});
