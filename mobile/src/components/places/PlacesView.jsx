import { FlashList } from '@shopify/flash-list';
import { collectEnumeration, MapProvider, VisitorPolicies, directoryConfig, mobileMapProvider, programOptions } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback, useState } from 'react';
import { RefreshControl, StyleSheet, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { filtersToQuery } from '../../content/enumeration';
import { describeFeatureError, useDirectoryStrings } from '../../content/phase4';
import { useCursorList } from '../../hooks/useCursorList';
import { useListFilters } from '../../hooks/useListFilters';
import { roundedCurrentPosition } from '../../native/location';
import { mapModule } from '../../native/maps';
import { useProperty } from '../../property';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import CheckRow from '../CheckRow';
import ChipGroup from '../ChipGroup';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import Sheet from '../Sheet';
import TextField from '../TextField';
import { FilterButton } from '../filters/FilterBar';
import PlacesMapSearch from './PlacesMapSearch';
import VenueCard from './VenueCard';

/** Venue page size (within the member maximum). */
export const VenuePageSize = 25;

/** The typed venue filters for a filter state and an optional `near` (docs/PHASE4_CONTRACT.md section 3.2). */
export function venueCollectionFilters(filters, near) {
  const out = {};
  if (filters.programs && filters.programs.length) out.programs = filters.programs;
  if (filters.visitorPolicy && filters.visitorPolicy.length) out.visitorPolicy = filters.visitorPolicy;
  if (filters.verifiedOnly) out.verifiedOnly = true;
  if (near) out.near = { lat: near.lat, lng: near.lng, radiusKm: near.radiusKm || 50 };
  return out;
}

function toggle(list, value) {
  const current = list || [];
  return current.includes(value) ? current.filter((x) => x !== value) : current.concat(value);
}

/**
 * The directory half of the Map tab (docs/PHASE4_CONTRACT.md sections 3 and 16). With a map
 * (MapLibre or the platform map, per the property) the search is map first (PlacesMapSearch);
 * with the provider `None`, or a map that cannot load, it is the list: search, programs, visitor
 * policy, verified, label, and tag filters (a bottom sheet), "Near me" behind an explanation and
 * the foreground location permission (rounded to about 1 km), and place search through the
 * server's geocoder.
 */
export default function PlacesView({ inset = false, testID = 'places' }) {
  const theme = useTheme();
  const property = useProperty();
  const provider = mobileMapProvider(property);
  const [mapFailed, setMapFailed] = useState(() => provider === MapProvider.None || !mapModule(provider));
  const unavailable = useCallback(() => setMapFailed(true), []);
  const config = directoryConfig(property);
  return <View style={{ flex: 1 }}>
    {(config.introduction || config.listingNotice) ? <View style={{ padding: theme.spacing.md, gap: theme.spacing.xs }}>
      {config.introduction ? <AppText>{config.introduction}</AppText> : null}
      {config.listingNotice ? <AppText variant="caption" color="textMuted">{config.listingNotice}</AppText> : null}
    </View> : null}
    {!mapFailed ? <PlacesMapSearch provider={provider} onUnavailable={unavailable} inset={inset} testID={testID} /> : <PlacesList provider={provider} testID={testID} />}
  </View>;
}

function PlacesList({ provider, testID }) {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { api, user } = useAuth();
  const t = useDirectoryStrings();
  const config = directoryConfig(property);
  const [filters, setFilters] = useListFilters('venues');
  const [near, setNear] = useState(null);
  const [prompt, setPrompt] = useState(false);
  const [locationNote, setLocationNote] = useState(null);
  const [placeText, setPlaceText] = useState('');
  const [places, setPlaces] = useState(null);
  const [placeError, setPlaceError] = useState(null);
  const collection = venueCollectionFilters(filters, near);
  const ordering = filters.ordering || (filters.search ? 'Relevance' : near ? 'DistanceAscending' : 'NameAscending');
  const key = JSON.stringify([filtersToQuery({ ...filters, ordering: null }), collection, ordering]);

  const fetchPage = useCallback(
    (continuationToken) => api.enumerateVenues({ ...filtersToQuery(filters), ordering, continuationToken, maxResults: VenuePageSize, filters: collection }),
    // key stands for filters, collection, and ordering.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [api, key]
  );
  const list = useCursorList(fetchPage, { key });
  const openVenue = useCallback((venue) => router.push('/venues/' + venue.id), [router]);

  async function locateMe() {
    setPrompt(false);
    setLocationNote(null);
    const result = await roundedCurrentPosition();
    if (result.status === 'ok') setNear({ ...result.position, label: t.nearYou });
    else setLocationNote(result.status === 'denied' ? t.locationDenied : t.locationFailed);
  }

  async function searchPlace() {
    const text = placeText.trim();
    if (text.length < 2) return;
    setPlaceError(null);
    try {
      const result = await collectEnumeration((page, signal) => api.geocodePlace(text, page, signal), { maxResults: 5 });
      setPlaces(result.objects);
    } catch (e) {
      setPlaces(null);
      setPlaceError(e && e.code === 'IntegrationNotConfigured' ? t.placeSearchFailed : describeFeatureError(e, 'places', t));
    }
  }

  const programs = programOptions(property);
  const extra = (draft, setDraft) => (
    <View style={{ gap: theme.spacing.md }}>
      {programs.length ? <ChipGroup label={t.programs} options={programs.map((p) => ({ value: p.key, label: p.label }))} values={draft.programs || []} onToggle={(value) => setDraft((d) => ({ ...d, programs: toggle(d.programs, value) }))} testID={testID + '-programs'} /> : null}
      <ChipGroup label={t.visitorPolicyLabel} options={VisitorPolicies.filter((v) => v !== 'Unknown').map((value) => ({ value, label: t.visitorPolicy[value] }))} values={draft.visitorPolicy || []} onToggle={(value) => setDraft((d) => ({ ...d, visitorPolicy: toggle(d.visitorPolicy, value) }))} testID={testID + '-visitors'} />
      <CheckRow label={t.verifiedOnly} value={Boolean(draft.verifiedOnly)} onValueChange={(verifiedOnly) => setDraft((d) => ({ ...d, verifiedOnly }))} testID={testID + '-verified'} />
    </View>
  );
  const extraChips = (f) => {
    const chips = [];
    for (const key of f.programs || []) chips.push({ key: 'program-' + key, text: (programs.find((p) => p.key === key) || { label: key }).label, remove: { ...f, programs: f.programs.filter((x) => x !== key) } });
    for (const value of f.visitorPolicy || []) chips.push({ key: 'visitors-' + value, text: t.visitorPolicy[value], remove: { ...f, visitorPolicy: f.visitorPolicy.filter((x) => x !== value) } });
    if (f.verifiedOnly) chips.push({ key: 'verified', text: t.verifiedOnly, remove: { ...f, verifiedOnly: false } });
    return chips;
  };
  const orderings = ['NameAscending', near ? 'DistanceAscending' : null, 'CreatedDescending', filters.search ? 'Relevance' : null].filter(Boolean).map((value) => ({ value, label: t.orderings[value] }));
  const submissionsOn = config.submissions && Boolean(user);

  const controls = (
    <View style={{ gap: theme.spacing.md, paddingBottom: theme.spacing.md }}>
      <FilterButton filters={filters} onChange={setFilters} targetType="Venue" orderings={orderings} searchLabel={t.search} sheetOnly extra={extra} extraChips={extraChips} testID={testID + '-filters'} />
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
        {near ? (
          <Button label={t.clearNear} variant="secondary" accessibilityLabel={t.nearActive(near.label) + '. ' + t.clearNear} onPress={() => setNear(null)} style={{ alignSelf: 'auto' }} testID={testID + '-clear-near'} />
        ) : (
          <Button label={t.nearMe} accessibilityHint={t.nearMeHint} variant="secondary" onPress={() => setPrompt(true)} style={{ alignSelf: 'auto' }} testID={testID + '-near-me'} />
        )}
        {submissionsOn ? <Button label={t.addVenue} variant="ghost" onPress={() => router.push({ pathname: '/directory/submit', params: { type: 'NewVenue' } })} style={{ alignSelf: 'auto' }} testID={testID + '-add'} /> : null}
        {user ? <Button label={t.mySubmissions} variant="ghost" onPress={() => router.push('/directory/submissions')} style={{ alignSelf: 'auto' }} testID={testID + '-submissions'} /> : null}
      </View>
      {near ? (
        <AppText color="textMuted" testID={testID + '-near-label'}>
          {t.nearActive(near.label)}
        </AppText>
      ) : null}
      {locationNote ? (
        <AppText color="warning" accessibilityRole="alert" testID={testID + '-location-note'}>
          {locationNote}
        </AppText>
      ) : null}
      <View style={{ gap: theme.spacing.sm }}>
        <TextField label={t.searchPlace} hint={t.searchPlaceHint} value={placeText} onChangeText={setPlaceText} maxLength={200} returnKeyType="search" onSubmitEditing={searchPlace} testID={testID + '-place'} />
        {placeError ? (
          <AppText color="danger" accessibilityRole="alert" testID={testID + '-place-error'}>
            {placeError}
          </AppText>
        ) : null}
        {places ? (
          places.length ? (
            places.map((candidate, i) => (
              <Button
                key={candidate.label + i}
                label={candidate.label}
                variant="ghost"
                onPress={() => {
                  setNear({ lat: candidate.lat, lng: candidate.lng, label: candidate.label });
                  setPlaces(null);
                }}
                style={{ alignSelf: 'auto' }}
                testID={testID + '-place-' + i}
              />
            ))
          ) : (
            <AppText color="textMuted">{t.noPlaces}</AppText>
          )
        ) : null}
      </View>
      {provider !== MapProvider.None ? <AppText color="textMuted" testID={testID + '-map-unavailable'}>{t.mapUnavailable}</AppText> : null}
      {list.status === 'error' ? <ErrorState title={t.loadFailedTitle} message={describeFeatureError(list.error, 'places', t)} onRetry={list.reload} testID={testID + '-error'} /> : null}
      {list.status === 'ready' && !list.items.length ? (
        <Card testID={testID + '-empty'}>
          <AppText>{t.empty}</AppText>
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

  const listView = (
    <FlashList
      testID={testID + '-list'}
      data={list.status === 'loading' ? [] : list.items}
      keyExtractor={(venue) => venue.id}
      renderItem={({ item }) => (
        <View style={{ paddingBottom: theme.spacing.sm }}>
          <VenueCard venue={item} onPress={openVenue} terms={t} />
        </View>
      )}
      ListHeaderComponent={
        <>
          {controls}
          {list.status === 'loading' ? <Loading label={t.loading} testID={testID + '-loading'} /> : null}
        </>
      }
      ListFooterComponent={footer}
      onEndReached={list.loadMore}
      onEndReachedThreshold={0.5}
      refreshControl={<RefreshControl refreshing={list.refreshing} onRefresh={list.refresh} tintColor={theme.colors.primary} />}
      contentContainerStyle={{ padding: theme.spacing.lg }}
    />
  );

  return (
    <View style={styles.fill} testID={testID}>
      {listView}
      <Sheet visible={prompt} title={t.locationPrompt.title} onClose={() => setPrompt(false)} scroll={false} testID={testID + '-location-sheet'}>
        <View style={{ gap: theme.spacing.md }}>
          <AppText>{t.locationPrompt.body}</AppText>
          <Button label={t.locationPrompt.allow} onPress={locateMe} testID={testID + '-location-allow'} />
          <Button label={t.locationPrompt.notNow} variant="ghost" onPress={() => setPrompt(false)} testID={testID + '-location-cancel'} />
        </View>
      </Sheet>
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 }
});

