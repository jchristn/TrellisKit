import { FlashList } from '@shopify/flash-list';
import { EventFormats, directoryConfig, eventCategories } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback, useRef, useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { filtersToQuery } from '../../content/enumeration';
import { describeFeatureError, useDirectoryStrings } from '../../content/phase4';
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
import TextField from '../TextField';
import { FilterButton } from '../filters/FilterBar';
import MapSearchLayout from '../maps/MapSearchLayout';
import PlaceLookup, { placeTarget } from '../maps/PlaceLookup';
import ResultsSheet, { SheetSnap } from '../maps/ResultsSheet';
import SearchMap from '../maps/SearchMap';
import { useMapFeatures, useMapSearch } from '../maps/useMapSearch';
import EventCard from './EventCard';

const t = strings.events;
const m = strings.events.mapSearch;
const EventPageSize = 25;
const WHEN = ['Upcoming', 'Past', 'All'];
const RSVP = ['Going', 'Interested', 'Waitlisted', 'None'];
const DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

function toggle(list, value) {
  const current = list || [];
  return current.includes(value) ? current.filter((x) => x !== value) : current.concat(value);
}

/**
 * A typed `YYYY-MM-DD` as the UTC instant of that local midnight (plus `addDays`), or null when it
 * is empty or not a real date.
 */
export function localDateToUtc(text, addDays = 0) {
  const match = DATE.exec(String(text || '').trim());
  if (!match) return null;
  const [y, mo, d] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const check = new Date(y, mo - 1, d);
  if (check.getFullYear() !== y || check.getMonth() !== mo - 1 || check.getDate() !== d) return null;
  return new Date(y, mo - 1, d + addDays).toISOString();
}

/**
 * The typed filters of the events map search (docs/PHASE4_CONTRACT.md section 4.2): when, the date
 * range (the To date is inclusive), categories, formats, spots left, and the caller's RSVP status.
 * The map sends these; the list adds `bbox` and, with Include online, `includeOnline`.
 */
export function eventMapFilters(filters, signedIn = true) {
  const out = { when: WHEN.includes(filters.when) ? filters.when : 'Upcoming' };
  const after = localDateToUtc(filters.fromDate);
  const before = localDateToUtc(filters.toDate, 1);
  if (after) out.startsAfterUtc = after;
  if (before) out.startsBeforeUtc = before;
  if (filters.category && filters.category.length) out.category = filters.category;
  if (filters.format && filters.format.length) out.format = filters.format;
  if (filters.hasSpots) out.hasSpots = true;
  if (signedIn && filters.rsvpStatus && filters.rsvpStatus.length) out.rsvpStatus = filters.rsvpStatus;
  return out;
}

/**
 * Map-first event search (docs/PHASE4_CONTRACT.md sections 4.2 and 16): the same model as the
 * venue map search. Events with a place show as markers and clusters from the events map
 * enumeration of the searched area; the results sheet lists the area's events (StartAscending,
 * StartDescending for Past) with place lookup, Near me, and the advanced filters (search, labels,
 * tags, when, date range, categories, formats, spots left, my RSVP, and Include online: online
 * events have no place, so they show only in the list). Tapping a point selects its event and
 * highlights its row; a second tap or a row opens the event.
 */
export default function EventsMapSearch({ provider, onUnavailable, inset = false, testID = 'events' }) {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { api, user } = useAuth();
  const d = useDirectoryStrings();
  const config = directoryConfig(property);
  const search = useMapSearch('events', config.map);
  const [filters, setFilters] = useListFilters('events-map');
  const [selected, setSelected] = useState(null);
  const [snapRequest, setSnapRequest] = useState(null);
  const listRef = useRef(null);
  const typed = eventMapFilters(filters, Boolean(user));
  const ordering = filters.ordering || (filters.search ? 'Relevance' : typed.when === 'Past' ? 'StartDescending' : 'StartAscending');
  const query = filtersToQuery({ ...filters, ordering: null });
  const key = JSON.stringify([query, typed, ordering, Boolean(filters.includeOnline)]);
  const boxKey = search.searched ? JSON.stringify(search.searched) : '';

  const fetchPage = useCallback(
    (continuationToken) => api.enumerateEvents({ ...query, ordering, continuationToken, maxResults: EventPageSize, filters: { ...typed, bbox: search.searched, ...(filters.includeOnline ? { includeOnline: true } : {}) } }),
    // key and boxKey stand for the filters and the searched area.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [api, key, boxKey]
  );
  const list = useCursorList(fetchPage, { key: key + boxKey, enabled: Boolean(search.searched) });
  const map = useMapFeatures(
    (page, signal) => api.enumerateEventMap({ ...page, labels: query.labels, labelMatch: query.labelMatch, tags: query.tags, search: query.search, filters: typed }, signal),
    search.searched,
    search.searchedZoom,
    key
  );

  const openEvent = useCallback((event) => router.push('/events/' + event.id), [router]);
  const pressPoint = (event) => {
    if (selected === event.id) {
      openEvent(event);
      return;
    }
    setSelected(event.id);
    setSnapRequest((r) => ({ snap: SheetSnap.Half, seq: (r ? r.seq : 0) + 1 }));
    const index = list.items.findIndex((e) => e.id === event.id);
    if (index >= 0 && listRef.current && listRef.current.scrollToIndex) {
      try {
        listRef.current.scrollToIndex({ index, animated: true });
      } catch {
        // The row is not laid out yet; the highlight still shows when it is.
      }
    }
  };

  const categories = eventCategories(property);
  const extra = (draft, setDraft) => (
    <View style={{ gap: theme.spacing.md }}>
      <ChipGroup label={m.whenLabel} options={WHEN.map((value) => ({ value, label: m.when[value] }))} value={WHEN.includes(draft.when) ? draft.when : 'Upcoming'} onToggle={(when) => setDraft((x) => ({ ...x, when }))} testID={testID + '-when'} />
      <TextField label={m.from} hint={m.dateHint} value={draft.fromDate || ''} onChangeText={(fromDate) => setDraft((x) => ({ ...x, fromDate }))} error={draft.fromDate && !localDateToUtc(draft.fromDate) ? m.dateInvalid : null} maxLength={10} testID={testID + '-from'} />
      <TextField label={m.to} hint={m.dateHint} value={draft.toDate || ''} onChangeText={(toDate) => setDraft((x) => ({ ...x, toDate }))} error={draft.toDate && !localDateToUtc(draft.toDate) ? m.dateInvalid : null} maxLength={10} testID={testID + '-to'} />
      {categories.length ? <ChipGroup label={t.categories} options={categories.map((c) => ({ value: c.key, label: c.label, color: c.color || null }))} values={draft.category || []} onToggle={(value) => setDraft((x) => ({ ...x, category: toggle(x.category, value) }))} testID={testID + '-categories'} /> : null}
      <ChipGroup label={t.format} options={EventFormats.map((value) => ({ value, label: t.formats[value] }))} values={draft.format || []} onToggle={(value) => setDraft((x) => ({ ...x, format: toggle(x.format, value) }))} testID={testID + '-formats'} />
      <CheckRow label={t.hasSpots} value={Boolean(draft.hasSpots)} onValueChange={(hasSpots) => setDraft((x) => ({ ...x, hasSpots }))} testID={testID + '-has-spots'} />
      {user ? <ChipGroup label={m.rsvpLabel} options={RSVP.map((value) => ({ value, label: m.rsvp[value] }))} values={draft.rsvpStatus || []} onToggle={(value) => setDraft((x) => ({ ...x, rsvpStatus: toggle(x.rsvpStatus, value) }))} testID={testID + '-rsvp'} /> : null}
      <CheckRow label={m.includeOnline} value={Boolean(draft.includeOnline)} onValueChange={(includeOnline) => setDraft((x) => ({ ...x, includeOnline }))} testID={testID + '-include-online'}>
        <AppText variant="caption" color="textMuted">
          {m.includeOnlineHint}
        </AppText>
      </CheckRow>
    </View>
  );
  const extraChips = (f) => {
    const chips = [];
    if (f.when && f.when !== 'Upcoming' && WHEN.includes(f.when)) chips.push({ key: 'when', text: m.when[f.when], remove: { ...f, when: 'Upcoming' } });
    if (f.fromDate) chips.push({ key: 'from', text: m.from + ' ' + f.fromDate, remove: { ...f, fromDate: '' } });
    if (f.toDate) chips.push({ key: 'to', text: m.to + ' ' + f.toDate, remove: { ...f, toDate: '' } });
    for (const k of f.category || []) chips.push({ key: 'cat-' + k, text: (categories.find((c) => c.key === k) || { label: k }).label, remove: { ...f, category: f.category.filter((x) => x !== k) } });
    for (const value of f.format || []) chips.push({ key: 'fmt-' + value, text: t.formats[value], remove: { ...f, format: f.format.filter((x) => x !== value) } });
    if (f.hasSpots) chips.push({ key: 'spots', text: t.hasSpots, remove: { ...f, hasSpots: false } });
    for (const value of f.rsvpStatus || []) chips.push({ key: 'rsvp-' + value, text: m.rsvp[value], remove: { ...f, rsvpStatus: f.rsvpStatus.filter((x) => x !== value) } });
    if (f.includeOnline) chips.push({ key: 'online', text: m.includeOnline, remove: { ...f, includeOnline: false } });
    return chips;
  };
  const orderings = ['StartAscending', 'StartDescending', 'CreatedDescending', filters.search ? 'Relevance' : null].filter(Boolean).map((value) => ({ value, label: t.orderings[value] }));

  const header = (
    <View style={{ gap: theme.spacing.sm }}>
      <PlaceLookup t={d} onPlace={(candidate) => search.moveTo(placeTarget(candidate))} onPosition={(pos) => search.moveTo({ lat: pos.lat, lng: pos.lng, zoom: 11 })} testID={testID} />
      <FilterButton filters={filters} onChange={setFilters} targetType="Event" orderings={orderings} searchLabel={t.search} sheetOnly extra={extra} extraChips={extraChips} testID={testID + '-filters'} />
    </View>
  );

  const empty = (
    <View style={{ paddingVertical: theme.spacing.md, gap: theme.spacing.sm }}>
      {list.status === 'loading' ? <Loading label={t.loading} testID={testID + '-loading'} /> : null}
      {list.status === 'error' ? <ErrorState title={t.loadFailedTitle} message={describeFeatureError(list.error, 'events')} onRetry={list.reload} testID={testID + '-error'} /> : null}
      {list.status === 'ready' && !list.items.length ? (
        <Card testID={testID + '-empty'}>
          <AppText>{m.emptyArea}</AppText>
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
    <MapSearchLayout
      inset={inset}
      testID={testID}
      showSearchArea={search.showSearchArea}
      onSearchArea={search.searchArea}
      searchAreaLabel={m.searchArea}
      mapError={map.error ? describeFeatureError(map.error, 'events') : null}
      map={
        <SearchMap
          provider={provider}
          config={config.map}
          features={map.features}
          onViewChange={search.onViewChange}
          onPressItem={pressPoint}
          onCluster={search.expectMove}
          onUnavailable={onUnavailable}
          clusterLabel={m.cluster}
          initial={search.initial}
          focus={search.focus}
          selectedId={selected}
          accessibilityLabel={m.mapLabel}
          testID={testID + '-map'}
        />
      }
    >
      <ResultsSheet title={m.resultsTitle} expandLabel={m.resultsExpand} collapseLabel={m.resultsCollapse} header={header} snapRequest={snapRequest} testID={testID + '-sheet'}>
        <FlashList
          ref={listRef}
          testID={testID + '-list'}
          data={list.status === 'ready' ? list.items : []}
          keyExtractor={(event) => event.id}
          extraData={selected}
          renderItem={({ item }) => (
            <View style={{ paddingBottom: theme.spacing.sm }}>
              <EventCard event={item} onPress={openEvent} selected={item.id === selected} />
            </View>
          )}
          ListHeaderComponent={empty}
          ListFooterComponent={footer}
          onEndReached={list.loadMore}
          onEndReachedThreshold={0.5}
          contentContainerStyle={{ paddingHorizontal: theme.spacing.lg }}
        />
      </ResultsSheet>
    </MapSearchLayout>
  );
}
