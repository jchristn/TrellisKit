import { FlashList } from '@shopify/flash-list';
import { formatRelativeTime } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, RefreshControl } from 'react-native';
import { useAuth } from '../../src/auth/AuthProvider';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import ErrorState from '../../src/components/ErrorState';
import ListDetailLayout, { DetailPrompt } from '../../src/components/ListDetailLayout';
import Loading from '../../src/components/Loading';
import AnnouncementDetail from '../../src/components/announcements/AnnouncementDetail';
import { buildEnumerationQuery, filtersToQuery } from '../../src/content/enumeration';
import { describeContentError } from '../../src/content/errors';
import { useCursorList } from '../../src/hooks/useCursorList';
import { useLayout } from '../../src/hooks/useLayout';
import { useListFilters } from '../../src/hooks/useListFilters';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

const t = strings.announcements;

function AnnouncementRow({ item, onOpen, selected }) {
  const theme = useTheme();
  return (
    <Pressable onPress={() => onOpen(item)} accessibilityRole="button" accessibilityLabel={item.title} accessibilityState={{ selected }} testID={'announcement-' + item.id} style={({ pressed }) => ({ paddingVertical: theme.spacing.md, paddingHorizontal: theme.spacing.sm, gap: theme.spacing.xxs, borderRadius: theme.radii.md, backgroundColor: selected ? theme.colors.surface : 'transparent', opacity: pressed ? 0.7 : 1 })}>
      <AppText variant="label">{item.title}</AppText>
      {item.sentUtc ? (
        <AppText variant="caption" color="textMuted">
          {formatRelativeTime(item.sentUtc)}
        </AppText>
      ) : null}
    </Pressable>
  );
}

/** Announcements I received (`POST /announcements/enumerate`), newest first, with search, label and tag filters, and load more. */
export default function AnnouncementsScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const { isWide } = useLayout();
  const [filters, setFilters] = useListFilters('announcements');
  const [selected, setSelected] = useState(null);
  const fetchPage = useCallback((continuationToken) => api.enumerateAnnouncements(buildEnumerationQuery({ continuationToken, maxResults: 25, filters })), [api, filters]);
  const list = useCursorList(fetchPage, { key: JSON.stringify(filtersToQuery(filters)) });
  const open = useCallback((item) => (isWide ? setSelected(item) : router.push('/announcements/' + item.id)), [isWide, router]);

  const content =
    list.status === 'loading' ? (
      <Loading label={strings.common.loading} testID="announcements-loading" />
    ) : (
      <FlashList
        testID="announcements-list"
        data={list.items}
        keyExtractor={(item) => item.id}
        extraData={selected}
        renderItem={({ item }) => <AnnouncementRow item={item} onOpen={open} selected={Boolean(selected && selected.id === item.id)} />}
        contentContainerStyle={{ padding: theme.spacing.lg }}
        ListEmptyComponent={
          list.status === 'ready' ? (
            <AppText color="textMuted" testID="announcements-empty">
              {t.empty}
            </AppText>
          ) : null
        }
        ListFooterComponent={list.hasMore ? <Button label={t.loadMore} variant="ghost" onPress={list.loadMore} disabled={list.loadingMore} testID="announcements-load-more" /> : null}
        onEndReached={list.loadMore}
        onEndReachedThreshold={0.5}
        refreshControl={<RefreshControl refreshing={list.refreshing} onRefresh={list.refresh} tintColor={theme.colors.primary} />}
      />
    );

  return (
    <ListDetailLayout
      testID="screen-announcements"
      filterProps={{ filters, onChange: setFilters, targetType: 'Announcement', testID: 'announcements-filters' }}
      header={list.status === 'error' ? <ErrorState title={t.loadFailedTitle} message={describeContentError(list.error)} onRetry={list.reload} testID="announcements-error" /> : null}
      list={content}
      detail={selected ? <AnnouncementDetail key={selected.id} announcementId={selected.id} initial={selected} /> : <DetailPrompt text={t.selectAnnouncement} testID="announcements-select" />}
    />
  );
}
