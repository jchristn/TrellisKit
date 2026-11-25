import { FlashList } from '@shopify/flash-list';
import { ProgressFilters, groupByLevel, libraryConfig, libraryLevels } from '@trelliskit/shared';
import { useFocusEffect, useRouter } from 'expo-router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshControl, ScrollView, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { filtersToQuery } from '../../content/enumeration';
import { describeFeatureError } from '../../content/phase4';
import { useCursorList } from '../../hooks/useCursorList';
import { useListFilters } from '../../hooks/useListFilters';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import CheckRow from '../CheckRow';
import ChipGroup from '../ChipGroup';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import { FilterButton } from '../filters/FilterBar';
import { LessonRow, SeriesCard } from './LibraryCards';

const t = strings.library;

/** Series page size. */
export const SeriesPageSize = 25;

function toggle(list, value) {
  const current = list || [];
  return current.includes(value) ? current.filter((x) => x !== value) : current.concat(value);
}

/** The typed series or lesson filters for a filter state (docs/PHASE4_CONTRACT.md section 5.1). */
export function libraryCollectionFilters(filters, { lessons = false } = {}) {
  const out = {};
  if (filters.levels && filters.levels.length) out.levels = filters.levels;
  if (filters.progress) out.progress = filters.progress;
  if (filters.followedOnly && !lessons) out.followedOnly = true;
  return out;
}

/** The "Continue watching" rows of a page: completed lessons never show (the server excludes them too). */
export function continueItems(page) {
  const objects = (page && Array.isArray(page.objects) && page.objects) || [];
  return objects.filter((item) => item && item.lesson && !(item.progress && item.progress.completed));
}

/**
 * "Continue watching": in-progress lessons the caller can still open (bounded, newest first).
 * Loads them itself unless `items` (ContinueWatchingItem[], for example from the Training
 * overview) are given; `title` and `testID` replace the Library's heading and test id.
 */
export function ContinueRail({ onOpenLesson, items: provided = null, title = t.continueWatching, testID = 'library-continue' }) {
  const theme = useTheme();
  const { api, user } = useAuth();
  const [loaded, setItems] = useState(null);
  const [version, setVersion] = useState(0);
  const own = provided === null;
  const items = own ? loaded : continueItems({ objects: provided });
  // The Library tab stays mounted: refetch whenever it regains focus, so a lesson finished on its
  // own screen leaves the rail when the member comes back.
  useFocusEffect(useCallback(() => setVersion((v) => v + 1), []));
  useEffect(() => {
    if (!user || !own) return undefined;
    let active = true;
    api
      .enumerateContinueWatching({ maxResults: 10 })
      .then((page) => active && setItems(continueItems(page)))
      .catch(() => active && setItems([]));
    return () => {
      active = false;
    };
  }, [api, user, version, own]);
  if (!items || !items.length) return null;
  return (
    <View style={{ gap: theme.spacing.sm }} testID={testID}>
      <AppText variant="heading" accessibilityRole="header">
        {title}
      </AppText>
      <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={{ gap: theme.spacing.sm }}>
        {items.map((item) => (
          <View key={item.lesson.id} style={{ width: 260 }}>
            <LessonRow lesson={item.lesson} progress={item.progress} onPress={onOpenLesson} testID={'continue-' + item.lesson.id} />
          </View>
        ))}
      </ScrollView>
    </View>
  );
}

/**
 * The Library tab home (docs/PHASE4_CONTRACT.md sections 5.6 and 16): Continue watching, the
 * series grouped by level (followed series first), and search over lessons, with level,
 * progress, followed, label, and tag filters. Downloads opens from here when the property allows
 * them. A short note says that progress is saved and that staff can see viewing history
 * (resolved decision 17).
 */
export default function LibraryHome({ onOpenSeries, selectedSeriesId = null, testID = 'library' }) {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { api, user } = useAuth();
  const config = libraryConfig(property);
  const levels = libraryLevels(property);
  const [filters, setFilters] = useListFilters('library');
  const searching = Boolean(filters.search && filters.search.trim());
  const collection = libraryCollectionFilters(filters, { lessons: searching });
  const ordering = filters.ordering || (searching ? 'Relevance' : 'SortOrderAscending');
  const key = JSON.stringify([searching, filtersToQuery({ ...filters, ordering: null }), collection, ordering]);
  const fetchPage = useCallback(
    (continuationToken) => {
      const query = { ...filtersToQuery(filters), ordering, continuationToken, maxResults: SeriesPageSize, filters: collection };
      return searching ? api.enumerateLessons(query) : api.enumerateSeries(query);
    },
    // key stands for the filters and ordering.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [api, key]
  );
  const list = useCursorList(fetchPage, { key });
  const openLesson = useCallback((lesson) => router.push('/lessons/' + lesson.id), [router]);

  const rows = useMemo(() => {
    if (searching) return list.items.map((lesson) => ({ key: 'l:' + lesson.id, type: 'lesson', lesson }));
    const out = [];
    const followed = list.items.filter((s) => s.isFollowed);
    if (followed.length && !filters.followedOnly) {
      out.push({ key: 'h:followed', type: 'header', text: t.followedSeries });
      for (const s of followed) out.push({ key: 'f:' + s.id, type: 'series', series: s });
    }
    const groups = levels.length ? groupByLevel(property, list.items) : [{ level: null, items: list.items }];
    for (const group of groups) {
      // A heading with nothing under it reads as a broken page; the empty card already says there are no series.
      if (!group.items.length) continue;
      out.push({ key: 'h:' + (group.level ? group.level.key : 'other'), type: 'header', text: group.level ? group.level.label : levels.length ? t.otherLevel : t.allSeries });
      for (const s of group.items) out.push({ key: 's:' + s.id, type: 'series', series: s });
    }
    return out;
  }, [list.items, searching, filters.followedOnly, levels.length, property]);

  const extra = (draft, setDraft) => (
    <View style={{ gap: theme.spacing.md }}>
      {levels.length ? <ChipGroup label={t.levels} options={levels.map((l) => ({ value: l.key, label: l.label, color: l.color || null }))} values={draft.levels || []} onToggle={(value) => setDraft((d) => ({ ...d, levels: toggle(d.levels, value) }))} testID={testID + '-levels'} /> : null}
      {user ? <ChipGroup label={t.progressFilter} options={ProgressFilters.map((value) => ({ value, label: t.progress[value] }))} value={draft.progress || null} onToggle={(value) => setDraft((d) => ({ ...d, progress: d.progress === value ? null : value }))} testID={testID + '-progress'} /> : null}
      {user ? <CheckRow label={t.followedOnly} value={Boolean(draft.followedOnly)} onValueChange={(followedOnly) => setDraft((d) => ({ ...d, followedOnly }))} testID={testID + '-followed'} /> : null}
    </View>
  );
  const extraChips = (f) => {
    const chips = [];
    for (const k of f.levels || []) chips.push({ key: 'level-' + k, text: (levels.find((l) => l.key === k) || { label: k }).label, remove: { ...f, levels: f.levels.filter((x) => x !== k) } });
    if (f.progress) chips.push({ key: 'progress', text: t.progress[f.progress], remove: { ...f, progress: null } });
    if (f.followedOnly) chips.push({ key: 'followed', text: t.followedOnly, remove: { ...f, followedOnly: false } });
    return chips;
  };
  const orderings = (searching ? ['Relevance', 'PublishedDescending', 'TitleAscending', 'DurationAscending'] : ['SortOrderAscending', 'TitleAscending', 'PublishedDescending']).map((value) => ({ value, label: t.orderings[value] }));

  const header = (
    <View style={{ gap: theme.spacing.lg, paddingBottom: theme.spacing.md }}>
      <ContinueRail onOpenLesson={openLesson} />
      <FilterButton filters={filters} onChange={setFilters} targetType={searching ? 'Lesson' : 'Series'} orderings={orderings} searchLabel={t.searchLessons} sheetOnly extra={extra} extraChips={extraChips} testID={testID + '-filters'} />
      {config.downloadsEnabled && user ? <Button label={t.downloads.open} accessibilityHint={t.downloads.openHint} variant="secondary" onPress={() => router.push('/library/downloads')} style={{ alignSelf: 'auto' }} testID={testID + '-downloads'} /> : null}
      {user ? (
        <AppText variant="caption" color="textMuted" testID={testID + '-history-note'}>
          {t.historyNote}
        </AppText>
      ) : null}
      {list.status === 'loading' ? <Loading label={t.loading} testID={testID + '-loading'} /> : null}
      {list.status === 'error' ? <ErrorState title={t.loadFailedTitle} message={describeFeatureError(list.error, 'library')} onRetry={list.reload} testID={testID + '-error'} /> : null}
      {list.status === 'ready' && !list.items.length ? (
        <Card testID={testID + '-empty'}>
          <AppText>{t.empty}</AppText>
        </Card>
      ) : null}
    </View>
  );

  return (
    <FlashList
      testID={testID}
      data={list.status === 'ready' ? rows : []}
      keyExtractor={(row) => row.key}
      getItemType={(row) => row.type}
      renderItem={({ item }) => {
        if (item.type === 'header') {
          return (
            <AppText variant="heading" accessibilityRole="header" style={{ paddingTop: theme.spacing.md, paddingBottom: theme.spacing.sm }}>
              {item.text}
            </AppText>
          );
        }
        if (item.type === 'lesson') {
          return (
            <View style={{ paddingBottom: theme.spacing.sm }}>
              <LessonRow lesson={item.lesson} onPress={openLesson} />
            </View>
          );
        }
        return (
          <View style={{ paddingBottom: theme.spacing.sm }}>
            <SeriesCard series={item.series} onPress={onOpenSeries} selected={item.series.id === selectedSeriesId} testID={(item.key.startsWith('f:') ? 'followed-' : 'series-') + item.series.id} />
          </View>
        );
      }}
      ListHeaderComponent={header}
      ListFooterComponent={
        <View style={{ paddingVertical: theme.spacing.lg }}>
          {list.loadingMore ? <Loading label={t.loadMore} /> : null}
          {list.moreError ? <Button label={t.loadMore} variant="secondary" onPress={list.loadMore} testID={testID + '-load-more'} /> : null}
        </View>
      }
      onEndReached={list.loadMore}
      onEndReachedThreshold={0.5}
      refreshControl={<RefreshControl refreshing={list.refreshing} onRefresh={list.refresh} tintColor={theme.colors.primary} />}
      contentContainerStyle={{ padding: theme.spacing.lg }}
    />
  );
}
