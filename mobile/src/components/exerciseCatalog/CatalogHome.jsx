import { FlashList } from '@shopify/flash-list';
import { CatalogDescriptorFields, catalogDescriptorOptions, catalogDescriptorProfile, CatalogDifficulties, CatalogKinds } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback } from 'react';
import { RefreshControl, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { filtersToQuery } from '../../content/enumeration';
import { useCursorList } from '../../hooks/useCursorList';
import { useListFilters } from '../../hooks/useListFilters';
import { useResource } from '../../hooks/useResource';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import ChipGroup from '../ChipGroup';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import { FilterButton } from '../filters/FilterBar';

const t = strings.exerciseCatalog;
const keys = ['kind', 'program', 'difficulty', 'muscle', 'equipment', 'familyKey', 'nameLanguage', 'attireKey', 'levelKey', 'notationSystemKey'];
export const catalogFilters = (value) => Object.fromEntries(keys.filter((key) => value[key]).map((key) => [key, value[key]]));

function ExtraFilters({ value, onChange, config }) {
  const options = { kind: CatalogKinds.map((value) => ({ value, label: t.kinds[value] })), difficulty: CatalogDifficulties.map((value) => ({ value, label: t.difficulties[value] })) };
  for (const [key, group] of [['program', 'programs'], ['muscle', 'muscles'], ['equipment', 'equipment']]) options[key] = (config?.[group] || []).map((row) => ({ value: row.key, label: row.label }));
  for (const { key, group } of CatalogDescriptorFields) options[key] = catalogDescriptorOptions(config, value.program, group, value[key]).map((item) => ({ value: item.key, label: item.label }));
  options.nameLanguage = catalogDescriptorOptions(config, value.program, 'nameLanguages', value.nameLanguage).map((item) => ({ value: item.key, label: item.label }));
  return <View>{keys.filter((key) => !['familyKey', 'nameLanguage', 'attireKey', 'levelKey', 'notationSystemKey'].includes(key) || options[key].length).map((key) => <ChipGroup key={key} label={catalogDescriptorProfile(config, value.program)?.[CatalogDescriptorFields.find((field) => field.key === key)?.labelKey] || t[key]} value={value[key] || ''} options={[{ value: '', label: t.all }, ...options[key]]} onToggle={(next) => onChange({ ...value, [key]: next })} />)}</View>;
}

export default function CatalogHome() {
  const { api, user } = useAuth();
  const router = useRouter();
  const theme = useTheme();
  const [filters, setFilters] = useListFilters('exercise-catalog');
  const key = JSON.stringify(filters);
  const signedIn = Boolean(user);
  const config = useResource(() => api.getExerciseCatalogConfig(), 'exercise-catalog-config');
  const fetch = useCallback((continuationToken) => { const value = JSON.parse(key); return api[signedIn ? 'enumerateCatalogEntries' : 'enumeratePublicCatalogEntries']({ ...filtersToQuery(value), filters: catalogFilters(value), maxResults: 25, continuationToken }); }, [api, signedIn, key]);
  const list = useCursorList(fetch, { key: key + signedIn });
  return <FlashList data={list.items} keyExtractor={(item) => item.id} contentContainerStyle={{ padding: theme.spacing.lg }} refreshControl={<RefreshControl refreshing={list.status === 'loading'} onRefresh={list.reload} />} renderItem={({ item }) => <Card style={{ marginBottom: theme.spacing.md }}><Button label={item.name} variant="ghost" onPress={() => router.push('/catalog/' + encodeURIComponent(item.slug || item.id))} /><AppText color="textMuted">{[t.kinds[item.kind], t.difficulties[item.difficulty]].filter(Boolean).join(' · ')}</AppText>{item.summary ? <AppText>{item.summary}</AppText> : null}</Card>}
    ListHeaderComponent={<View style={{ gap: theme.spacing.md, marginBottom: theme.spacing.md }}><AppText variant="title" accessibilityRole="header">{t.title}</AppText><AppText>{t.summary}</AppText><FilterButton sheetOnly filters={filters} onChange={setFilters} searchLabel={t.search} extra={(value, onChange) => <ExtraFilters value={value} onChange={onChange} config={config.data} />} extraChips={(value) => keys.filter((key) => value[key]).map((key) => ({ key, text: t[key] + ': ' + value[key], remove: { ...value, [key]: '' } }))} />{config.error ? <ErrorState title={t.error} onRetry={config.reload} /> : null}</View>}
    ListEmptyComponent={list.status === 'ready' ? <AppText>{t.empty}</AppText> : null}
    ListFooterComponent={<View>{list.status === 'loading' || list.loadingMore ? <Loading label={t.loading} /> : null}{list.error || list.moreError ? <ErrorState title={t.error} onRetry={list.moreError ? list.loadMore : list.reload} /> : null}{list.hasMore && !list.loadingMore && !list.moreError ? <Button label={t.more} onPress={list.loadMore} variant="secondary" /> : null}</View>} />;
}
