import { FlashList } from '@shopify/flash-list';
import { VisitorPolicies, directoryConfig, programOptions } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback, useRef, useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { filtersToQuery } from '../../content/enumeration';
import { describeFeatureError, useDirectoryStrings } from '../../content/phase4';
import { useCursorList } from '../../hooks/useCursorList';
import { useListFilters } from '../../hooks/useListFilters';
import { useProperty } from '../../property';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import CheckRow from '../CheckRow';
import ChipGroup from '../ChipGroup';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import { FilterButton } from '../filters/FilterBar';
import MapSearchLayout from '../maps/MapSearchLayout';
import PlaceLookup, { placeTarget } from '../maps/PlaceLookup';
import ResultsSheet, { SheetSnap } from '../maps/ResultsSheet';
import SearchMap from '../maps/SearchMap';
import { useMapFeatures, useMapSearch } from '../maps/useMapSearch';
import VenueCard from './VenueCard';
import VenuePreviewSheet from './VenuePreviewSheet';

const VenuePageSize = 25;

function toggle(list, value) {
  const current = list || [];
  return current.includes(value) ? current.filter((x) => x !== value) : current.concat(value);
}

/** The typed venue filters of the map search (docs/PHASE4_CONTRACT.md section 3.2); the list adds `bbox`, the map sends the rest. */
export function venueMapFilters(filters) {
  const out = {};
  if (filters.programs && filters.programs.length) out.programs = filters.programs;
  if (filters.visitorPolicy && filters.visitorPolicy.length) out.visitorPolicy = filters.visitorPolicy;
  if (filters.verifiedOnly) out.verifiedOnly = true;
  return out;
}

/**
 * Map-first venue search (docs/PHASE4_CONTRACT.md sections 3 and 16): the map fills the screen at
 * the last camera of the session or the property's default center and zoom; its first view is
 * searched at once, and after the camera moves a "Search this area" pill searches the visible box.
 * Markers and clusters come from the map enumeration of the searched area (a cluster tap zooms in
 * and searches there; a point tap selects the venue and highlights its row, a second tap opens
 * it). The results sheet drags between collapsed, half, and full and holds the place lookup,
 * Near me, the advanced filters (search, labels, tags, programs, visitor policy, verified,
 * permanently closed), and the venues of the area. `onUnavailable` falls back to the list.
 */
export default function PlacesMapSearch({ provider, onUnavailable, inset = false, testID = 'places' }) {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { api, user } = useAuth();
  const t = useDirectoryStrings();
  const config = directoryConfig(property);
  const search = useMapSearch('places', config.map);
  const [filters, setFilters] = useListFilters('venues-map');
  const [selected, setSelected] = useState(null);
  const [preview, setPreview] = useState(null);
  const [snapRequest, setSnapRequest] = useState(null);
  const listRef = useRef(null);
  const typed = venueMapFilters(filters);
  const ordering = filters.ordering || (filters.search ? 'Relevance' : 'NameAscending');
  const query = filtersToQuery({ ...filters, ordering: null });
  const key = JSON.stringify([query, typed, ordering, Boolean(filters.includeClosed)]);
  const boxKey = search.searched ? JSON.stringify(search.searched) : '';

  const fetchPage = useCallback(
    (continuationToken) => api.enumerateVenues({ ...query, ordering, continuationToken, maxResults: VenuePageSize, filters: { ...typed, ...(filters.includeClosed ? { includeClosed: true } : {}), bbox: search.searched } }),
    // key and boxKey stand for the filters and the searched area.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [api, key, boxKey]
  );
  const list = useCursorList(fetchPage, { key: key + boxKey, enabled: Boolean(search.searched) });
  const map = useMapFeatures(
    (page, signal) => api.enumerateDirectoryMap({ ...page, labels: query.labels, labelMatch: query.labelMatch, tags: query.tags, search: query.search, filters: typed }, signal),
    search.searched,
    search.searchedZoom,
    key
  );

  const openVenue = useCallback((venue) => router.push('/venues/' + venue.id), [router]);
  // A tap on a place opens its preview; the matching row is highlighted in the list behind it.
  const pressPoint = (venue) => {
    setSelected(venue.id);
    setPreview(venue.id);
    setSnapRequest((r) => ({ snap: SheetSnap.Half, seq: (r ? r.seq : 0) + 1 }));
    const index = list.items.findIndex((v) => v.id === venue.id);
    if (index >= 0 && listRef.current && listRef.current.scrollToIndex) {
      try {
        listRef.current.scrollToIndex({ index, animated: true });
      } catch {
        // The row is not laid out yet; the highlight still shows when it is.
      }
    }
  };

  const programs = programOptions(property);
  const extra = (draft, setDraft) => (
    <View style={{ gap: theme.spacing.md }}>
      {programs.length ? <ChipGroup label={t.programs} options={programs.map((p) => ({ value: p.key, label: p.label }))} values={draft.programs || []} onToggle={(value) => setDraft((d) => ({ ...d, programs: toggle(d.programs, value) }))} testID={testID + '-programs'} /> : null}
      <ChipGroup label={t.visitorPolicyLabel} options={VisitorPolicies.filter((v) => v !== 'Unknown').map((value) => ({ value, label: t.visitorPolicy[value] }))} values={draft.visitorPolicy || []} onToggle={(value) => setDraft((d) => ({ ...d, visitorPolicy: toggle(d.visitorPolicy, value) }))} testID={testID + '-visitors'} />
      <CheckRow label={t.verifiedOnly} value={Boolean(draft.verifiedOnly)} onValueChange={(verifiedOnly) => setDraft((d) => ({ ...d, verifiedOnly }))} testID={testID + '-verified'} />
      <CheckRow label={t.includeClosed} value={Boolean(draft.includeClosed)} onValueChange={(includeClosed) => setDraft((d) => ({ ...d, includeClosed }))} testID={testID + '-closed'} />
    </View>
  );
  const extraChips = (f) => {
    const chips = [];
    for (const k of f.programs || []) chips.push({ key: 'program-' + k, text: (programs.find((p) => p.key === k) || { label: k }).label, remove: { ...f, programs: f.programs.filter((x) => x !== k) } });
    for (const value of f.visitorPolicy || []) chips.push({ key: 'visitors-' + value, text: t.visitorPolicy[value], remove: { ...f, visitorPolicy: f.visitorPolicy.filter((x) => x !== value) } });
    if (f.verifiedOnly) chips.push({ key: 'verified', text: t.verifiedOnly, remove: { ...f, verifiedOnly: false } });
    if (f.includeClosed) chips.push({ key: 'closed', text: t.includeClosed, remove: { ...f, includeClosed: false } });
    return chips;
  };
  const orderings = ['NameAscending', 'CreatedDescending', filters.search ? 'Relevance' : null].filter(Boolean).map((value) => ({ value, label: t.orderings[value] }));
  const submissionsOn = config.submissions && Boolean(user);

  const header = (
    <View style={{ gap: theme.spacing.sm }}>
      <PlaceLookup t={t} onPlace={(candidate) => search.moveTo(placeTarget(candidate))} onPosition={(pos) => search.moveTo({ lat: pos.lat, lng: pos.lng, zoom: 11 })} testID={testID} />
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: theme.spacing.sm }}>
        <FilterButton filters={filters} onChange={setFilters} targetType="Venue" orderings={orderings} searchLabel={t.search} sheetOnly extra={extra} extraChips={extraChips} testID={testID + '-filters'} />
        {submissionsOn ? <Button label={t.addVenue} variant="ghost" onPress={() => router.push({ pathname: '/directory/submit', params: { type: 'NewVenue' } })} style={{ alignSelf: 'auto' }} testID={testID + '-add'} /> : null}
        {user ? <Button label={t.mySubmissions} variant="ghost" onPress={() => router.push('/directory/submissions')} style={{ alignSelf: 'auto' }} testID={testID + '-submissions'} /> : null}
      </View>
    </View>
  );

  const empty = (
    <View style={{ paddingVertical: theme.spacing.md, gap: theme.spacing.sm }}>
      {list.status === 'loading' ? <Loading label={t.loading} testID={testID + '-loading'} /> : null}
      {list.status === 'error' ? <ErrorState title={t.loadFailedTitle} message={describeFeatureError(list.error, 'places', t)} onRetry={list.reload} testID={testID + '-error'} /> : null}
      {list.status === 'ready' && !list.items.length ? (
        <Card testID={testID + '-empty'}>
          <AppText>{t.emptyArea}</AppText>
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
      searchAreaLabel={t.searchArea}
      mapError={map.error ? describeFeatureError(map.error, 'places', t) : null}
      map={
        <SearchMap
          provider={provider}
          config={config.map}
          features={map.features}
          onViewChange={search.onViewChange}
          onPressItem={pressPoint}
          onCluster={search.expectMove}
          onUnavailable={onUnavailable}
          clusterLabel={t.cluster}
          initial={search.initial}
          focus={search.focus}
          selectedId={selected}
          accessibilityLabel={t.mapLabel}
          testID={testID + '-map'}
        />
      }
    >
      <ResultsSheet title={t.resultsTitle} expandLabel={t.resultsExpand} collapseLabel={t.resultsCollapse} header={header} snapRequest={snapRequest} testID={testID + '-sheet'}>
        <FlashList
          ref={listRef}
          testID={testID + '-list'}
          data={list.status === 'ready' ? list.items : []}
          keyExtractor={(venue) => venue.id}
          extraData={selected}
          renderItem={({ item }) => (
            <View style={{ paddingBottom: theme.spacing.sm }}>
              <VenueCard venue={item} onPress={openVenue} terms={t} selected={item.id === selected} />
            </View>
          )}
          ListHeaderComponent={empty}
          ListFooterComponent={footer}
          onEndReached={list.loadMore}
          onEndReachedThreshold={0.5}
          contentContainerStyle={{ paddingHorizontal: theme.spacing.lg }}
        />
      </ResultsSheet>
      <VenuePreviewSheet
        venueId={preview}
        t={t}
        onClose={() => setPreview(null)}
        onOpen={(venue) => {
          setPreview(null);
          openVenue(venue);
        }}
        testID={testID + '-preview'}
      />
    </MapSearchLayout>
  );
}
