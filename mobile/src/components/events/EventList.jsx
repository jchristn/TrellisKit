import { FlashList } from '@shopify/flash-list';
import { EventFormats, eventCategories, eventsConfig } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback, useState } from 'react';
import { RefreshControl, View } from 'react-native';
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
import EventCard from './EventCard';

const t = strings.events;

/** Event page size (within the member maximum). */
export const EventPageSize = 25;

function toggle(list, value) {
  const current = list || [];
  return current.includes(value) ? current.filter((x) => x !== value) : current.concat(value);
}

/** The typed event filters for a list mode and filter state (docs/PHASE4_CONTRACT.md section 4.2). */
export function eventCollectionFilters(mode, filters) {
  const out = {};
  if (mode === 'mine') out.upcoming = true;
  else out.when = mode === 'past' ? 'Past' : 'Upcoming';
  if (filters.category && filters.category.length) out.category = filters.category;
  if (filters.format && filters.format.length) out.format = filters.format;
  if (filters.hasSpots) out.hasSpots = true;
  return out;
}

/**
 * Events (docs/PHASE4_CONTRACT.md sections 4.1, 4.2, and 16): Upcoming, Past, and My events with
 * search, category, format, spots, label, and tag filters, paged by continuation token, plus
 * Propose an event, My proposals, and the calendar feed when the property offers them.
 */
export default function EventList({ onOpen, selectedId = null, testID = 'events' }) {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { api, user } = useAuth();
  const config = eventsConfig(property);
  const [mode, setMode] = useState('upcoming');
  const [filters, setFilters] = useListFilters('events');
  const ordering = filters.ordering || (filters.search ? 'Relevance' : mode === 'past' ? 'StartDescending' : 'StartAscending');
  const collection = eventCollectionFilters(mode, filters);
  const key = mode + JSON.stringify([filtersToQuery({ ...filters, ordering: null }), collection, ordering]);
  const fetchPage = useCallback(
    (continuationToken) => {
      const query = { ...filtersToQuery(filters), ordering, continuationToken, maxResults: EventPageSize, filters: collection };
      return mode === 'mine' ? api.enumerateMyEvents(query) : api.enumerateEvents(query);
    },
    // key stands for the mode, filters, and ordering.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [api, key]
  );
  const list = useCursorList(fetchPage, { key });
  const categories = eventCategories(property);
  const extra = (draft, setDraft) => (
    <View style={{ gap: theme.spacing.md }}>
      {categories.length ? <ChipGroup label={t.categories} options={categories.map((c) => ({ value: c.key, label: c.label, color: c.color || null }))} values={draft.category || []} onToggle={(value) => setDraft((d) => ({ ...d, category: toggle(d.category, value) }))} testID={testID + '-categories'} /> : null}
      <ChipGroup label={t.format} options={EventFormats.map((value) => ({ value, label: t.formats[value] }))} values={draft.format || []} onToggle={(value) => setDraft((d) => ({ ...d, format: toggle(d.format, value) }))} testID={testID + '-formats'} />
      <CheckRow label={t.hasSpots} value={Boolean(draft.hasSpots)} onValueChange={(hasSpots) => setDraft((d) => ({ ...d, hasSpots }))} testID={testID + '-has-spots'} />
    </View>
  );
  const extraChips = (f) => {
    const chips = [];
    for (const key2 of f.category || []) chips.push({ key: 'cat-' + key2, text: (categories.find((c) => c.key === key2) || { label: key2 }).label, remove: { ...f, category: f.category.filter((x) => x !== key2) } });
    for (const value of f.format || []) chips.push({ key: 'fmt-' + value, text: t.formats[value], remove: { ...f, format: f.format.filter((x) => x !== value) } });
    if (f.hasSpots) chips.push({ key: 'spots', text: t.hasSpots, remove: { ...f, hasSpots: false } });
    return chips;
  };
  const orderings = ['StartAscending', 'StartDescending', 'CreatedDescending', filters.search ? 'Relevance' : null].filter(Boolean).map((value) => ({ value, label: t.orderings[value] }));
  const modes = [
    { value: 'upcoming', label: t.upcoming },
    { value: 'past', label: t.past }
  ].concat(user ? [{ value: 'mine', label: t.mine }] : []);

  const header = (
    <View style={{ gap: theme.spacing.md, paddingBottom: theme.spacing.md }}>
      <ChipGroup label={t.whenLabel} hideLabel options={modes} value={mode} onToggle={setMode} testID={testID + '-mode'} />
      <FilterButton filters={filters} onChange={setFilters} targetType="Event" orderings={orderings} searchLabel={t.search} sheetOnly extra={extra} extraChips={extraChips} testID={testID + '-filters'} />
      {user ? (
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
          {config.proposals ? <Button label={t.propose.open} accessibilityHint={t.propose.openHint} variant="secondary" onPress={() => router.push('/events/propose')} style={{ alignSelf: 'auto' }} testID={testID + '-propose'} /> : null}
          {config.proposals ? <Button label={t.proposals.open} variant="ghost" onPress={() => router.push('/events/proposals')} style={{ alignSelf: 'auto' }} testID={testID + '-proposals'} /> : null}
          {config.calendarFeedEnabled ? <Button label={t.calendar.feedOpen} accessibilityHint={t.calendar.feedOpenHint} variant="ghost" onPress={() => router.push('/events/calendar-feed')} style={{ alignSelf: 'auto' }} testID={testID + '-feed'} /> : null}
        </View>
      ) : null}
      {list.status === 'loading' ? <Loading label={t.loading} testID={testID + '-loading'} /> : null}
      {list.status === 'error' ? <ErrorState title={t.loadFailedTitle} message={describeFeatureError(list.error, 'events')} onRetry={list.reload} testID={testID + '-error'} /> : null}
      {list.status === 'ready' && !list.items.length ? (
        <Card testID={testID + '-empty'}>
          <AppText>{mode === 'mine' ? t.mineEmpty : t.empty}</AppText>
        </Card>
      ) : null}
    </View>
  );

  const footer = (
    <View style={{ paddingVertical: theme.spacing.lg, gap: theme.spacing.sm }}>
      {list.loadingMore ? <Loading label={t.loadMore} /> : null}
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

  return (
    <FlashList
      testID={testID}
      data={list.status === 'ready' ? list.items : []}
      keyExtractor={(event) => event.id}
      renderItem={({ item }) => (
        <View style={{ paddingBottom: theme.spacing.sm }}>
          <EventCard event={item} onPress={onOpen} selected={item.id === selectedId} />
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
