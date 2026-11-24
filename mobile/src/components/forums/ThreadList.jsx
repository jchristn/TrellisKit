import { FlashList } from '@shopify/flash-list';
import { MemberThreadOrderings, ThreadTypes, isKnownThread, threadFilters } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback, useState } from 'react';
import { RefreshControl, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { activeFilterCount, filtersToQuery } from '../../content/enumeration';
import { describeContentError } from '../../content/errors';
import { useCursorList } from '../../hooks/useCursorList';
import { useListFilters } from '../../hooks/useListFilters';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import ChipGroup from '../ChipGroup';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import MarkdownBody from '../feed/MarkdownBody';
import { ActiveFilters, FilterButton } from '../filters/FilterBar';
import FilterPanel from '../filters/FilterPanel';
import ThreadCard from './ThreadCard';

const t = strings.forums;
/** Thread page size (within the member enumeration maximum). */
export const ThreadPageSize = 20;

/**
 * Threads of one forum (docs/PHASE3_CONTRACT.md sections 4.2 and 19): a FlashList paged by
 * continuation token with pinned threads first, pull to refresh, the ordering (Active, Hot, New,
 * Top) with search and label and tag filters (a bottom sheet on phones; inline next to the
 * results on tablets), thread type chips, follow, mark all read, and New thread.
 */
export default function ThreadList({ forum, onOpen, selectedId = null, onForumChange, filtersInline = false, testID = 'thread-list' }) {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const [filters, setFilters] = useListFilters('forum:' + forum.id, forum.defaultOrdering || 'Active');
  const [types, setTypes] = useState([]);
  const [panelOpen, setPanelOpen] = useState(false);
  const [error, setError] = useState(null);
  const ordering = filters.ordering || (filters.search ? 'Relevance' : forum.defaultOrdering || 'Active');
  const fetchPage = useCallback(
    (continuationToken) => api.enumerateForumThreads(forum.id, { ...filtersToQuery(filters), ordering, continuationToken, maxResults: ThreadPageSize, filters: threadFilters({ types }) }),
    [api, forum.id, filters, ordering, types]
  );
  const list = useCursorList(fetchPage, { filter: isKnownThread, key: forum.id + JSON.stringify(filtersToQuery(filters)) + types.join(',') });
  const orderings = (filters.search ? ['Relevance'] : []).concat(MemberThreadOrderings).map((value) => ({ value, label: t.orderings[value] }));
  const viewer = forum.viewer || {};
  const canCreate = viewer.canCreateThread !== false && (viewer.allowedThreadTypes || []).length > 0 && !forum.isArchived;
  const filtered = activeFilterCount(filters) > 0 || types.length > 0;
  const pinned = list.items.filter((x) => x.isPinned);
  const rows = filters.search ? list.items : pinned.concat(list.items.filter((x) => !x.isPinned));

  async function follow() {
    setError(null);
    try {
      const next = forum.isFollowed ? await api.unfollowForum(forum.id) : await api.followForum(forum.id);
      if (onForumChange) onForumChange(next && next.id ? next : { ...forum, isFollowed: !forum.isFollowed });
    } catch (e) {
      setError(describeContentError(e));
    }
  }

  async function markRead() {
    setError(null);
    try {
      await api.markForumRead(forum.id);
      if (onForumChange) onForumChange({ ...forum, unreadThreadCount: 0 });
      list.setItems((items) => items.map((x) => ({ ...x, isUnread: false })));
    } catch (e) {
      setError(describeContentError(e));
    }
  }

  const header = (
    <View style={{ gap: theme.spacing.md, paddingBottom: theme.spacing.md }}>
      <AppText variant="title" accessibilityRole="header" testID={testID + '-forum-name'}>
        {forum.name}
      </AppText>
      {forum.description ? <AppText color="textMuted">{forum.description}</AppText> : null}
      {forum.isArchived ? <AppText color="warning">{t.archivedNote}</AppText> : null}
      {forum.guidelines ? (
        <Card>
          <AppText variant="label">{t.guidelines}</AppText>
          <MarkdownBody body={forum.guidelines} headings={false} />
        </Card>
      ) : null}
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
        {canCreate ? <Button label={t.newThread} accessibilityHint={t.newThreadHint} onPress={() => router.push({ pathname: '/compose-thread', params: { forumId: forum.id } })} style={{ alignSelf: 'auto' }} testID={testID + '-new'} /> : null}
        {viewer.canFollow !== false ? <Button label={forum.isFollowed ? t.unfollow : t.follow} accessibilityLabel={forum.isFollowed ? t.unfollowForum(forum.name) : t.followForum(forum.name)} variant="secondary" onPress={follow} style={{ alignSelf: 'auto' }} testID={testID + '-follow'} /> : null}
        <Button label={t.markRead} variant="ghost" onPress={markRead} style={{ alignSelf: 'auto' }} testID={testID + '-read'} />
      </View>
      {error ? (
        <AppText color="danger" accessibilityRole="alert">
          {error}
        </AppText>
      ) : null}
      {filtersInline ? (
        <View style={{ gap: theme.spacing.sm }}>
          <Button label={activeFilterCount(filters) ? strings.filters.buttonActive(activeFilterCount(filters)) : strings.filters.button} variant="secondary" onPress={() => setPanelOpen((v) => !v)} style={{ alignSelf: 'auto' }} testID={testID + '-filters-toggle'} />
          <ActiveFilters filters={filters} onChange={setFilters} testID={testID + '-filters'} />
          {panelOpen ? (
            <Card>
              <FilterPanel filters={filters} targetType="Thread" orderings={orderings} searchLabel={t.searchThreads} onApply={(next) => { setPanelOpen(false); setFilters(next); }} testID={testID + '-filters-panel'} />
            </Card>
          ) : null}
        </View>
      ) : (
        <FilterButton filters={filters} onChange={setFilters} targetType="Thread" orderings={orderings} searchLabel={t.searchThreads} sheetOnly testID={testID + '-filters'} />
      )}
      <ChipGroup label={t.typesLabel} options={ThreadTypes.filter((x) => (forum.allowedThreadTypes || ThreadTypes).includes(x)).map((value) => ({ value, label: t.types[value] }))} values={types} onToggle={(value) => setTypes((current) => (current.includes(value) ? current.filter((x) => x !== value) : current.concat(value)))} testID={testID + '-types'} />
      {list.status === 'error' ? <ErrorState title={t.threadsFailedTitle} message={describeContentError(list.error)} onRetry={list.reload} testID={testID + '-error'} /> : null}
      {list.status === 'ready' && !rows.length ? (
        <Card testID={testID + '-empty'}>
          <AppText>{filtered ? strings.filters.noResults : t.threadsEmpty}</AppText>
        </Card>
      ) : null}
    </View>
  );

  const footer = (
    <View style={{ paddingVertical: theme.spacing.lg, gap: theme.spacing.sm }}>
      {list.loadingMore ? <Loading label={t.loadingMore} /> : null}
      {list.moreError ? (
        <>
          <AppText color="danger" accessibilityRole="alert">
            {t.loadMoreFailed}
          </AppText>
          <Button label={t.loadMore} variant="secondary" onPress={list.loadMore} testID={testID + '-load-more'} />
        </>
      ) : null}
    </View>
  );

  if (list.status === 'loading') return <Loading label={t.threadsLoading} testID={testID + '-loading'} />;
  return (
    <FlashList
      testID={testID}
      data={rows}
      keyExtractor={(thread) => thread.id}
      renderItem={({ item }) => (
        <View style={{ paddingBottom: theme.spacing.sm }}>
          <ThreadCard thread={item} onPress={onOpen} selected={item.id === selectedId} />
        </View>
      )}
      ListHeaderComponent={header}
      ListFooterComponent={footer}
      onEndReached={list.loadMore}
      onEndReachedThreshold={0.5}
      refreshControl={<RefreshControl refreshing={list.refreshing} onRefresh={list.refresh} tintColor={theme.colors.primary} />}
      contentContainerStyle={{ padding: theme.spacing.lg }}
    />
  );
}
