import { FlashList } from '@shopify/flash-list';
import { ServerCapability, formatRelativeTime, searchEventInfo, snippetSegments } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useAuth } from '../../../src/auth/AuthProvider';
import AppText from '../../../src/components/AppText';
import Button from '../../../src/components/Button';
import Card from '../../../src/components/Card';
import ChipGroup from '../../../src/components/ChipGroup';
import { CapabilityGate } from '../../../src/components/ComingSoon';
import ErrorState from '../../../src/components/ErrorState';
import Loading from '../../../src/components/Loading';
import TextField from '../../../src/components/TextField';
import { FilterButton } from '../../../src/components/filters/FilterBar';
import { filtersToQuery } from '../../../src/content/enumeration';
import { describeContentError } from '../../../src/content/errors';
import { useCursorList } from '../../../src/hooks/useCursorList';
import { useListFilters } from '../../../src/hooks/useListFilters';
import { resolveNotificationPath } from '../../../src/notifications/deepLinks';
import strings from '../../../src/strings';
import { useTheme } from '../../../src/theme';
import FeatureGate from '../../../src/components/FeatureGate';

const s = strings.forums.searchScreen;
/** Content types offered as filters (resolved decision 11); other types still render. */
export const SearchTypes = Object.freeze(['FeedPost', 'Thread', 'Comment', 'User']);

/**
 * Global search (resolved decision 11): one search box across every content type, with type,
 * label, and tag filters. Results are an EnumerationResult from `POST /search/enumerate` with
 * highlighted snippets; a result opens its app path when the app knows it.
 */
/** Event results: the date (the next one for a recurring series), the rule, and a Canceled mark. */
function SearchEventLine({ hit }) {
  const ev = searchEventInfo(hit);
  if (!ev) return null;
  const parts = [
    ev.recurring && !ev.past && !ev.canceled ? s.nextDate(ev.when) : ev.when,
    ev.recurrenceSummary,
    ev.occurrenceCount > 1 ? s.occurrences(ev.occurrenceCount) : null
  ].filter(Boolean);
  return (
    <View testID={'search-hit-' + hit.id + '-event'}>
      {ev.canceled ? (
        <AppText variant="caption" color="danger" style={{ fontWeight: '700' }}>
          {s.canceled}
        </AppText>
      ) : null}
      <AppText variant="caption" color="textMuted">
        {parts.join(' · ')}
      </AppText>
    </View>
  );
}

/** The search route; "Coming soon" until the server reports `features.search`. */
function SearchRouteContent() {
  return (
    <CapabilityGate capability={ServerCapability.Search} title={s.title} summary={strings.forums.searchComingSoonSummary} testID="screen-search-coming-soon">
      <SearchScreen />
    </CapabilityGate>
  );
}

function SearchScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const [text, setText] = useState('');
  const [term, setTerm] = useState('');
  const [types, setTypes] = useState([]);
  const [filters, setFilters] = useListFilters('search');
  const fetchPage = useCallback(
    (continuationToken) => api.searchContent({ ...filtersToQuery({ ...filters, search: term }), ordering: undefined, continuationToken, maxResults: 20, filters: types.length ? { types } : {} }),
    [api, filters, term, types]
  );
  const list = useCursorList(fetchPage, { enabled: Boolean(term), key: term + types.join(',') + JSON.stringify(filtersToQuery(filters)) });

  const header = (
    <View style={{ gap: theme.spacing.md, paddingBottom: theme.spacing.md }}>
      <TextField label={s.label} hint={s.hint} value={text} onChangeText={setText} onSubmitEditing={() => setTerm(text.trim())} returnKeyType="search" maxLength={200} testID="search-input" />
      <Button label={s.submit} onPress={() => setTerm(text.trim())} testID="search-submit" />
      <ChipGroup label={s.typesLabel} options={SearchTypes.map((value) => ({ value, label: s.types[value] }))} values={types} onToggle={(value) => setTypes((c) => (c.includes(value) ? c.filter((x) => x !== value) : c.concat(value)))} testID="search-types" />
      <FilterButton filters={filters} onChange={setFilters} sheetOnly testID="search-filters" />
      {!term ? <AppText color="textMuted">{s.prompt}</AppText> : null}
      {term && list.status === 'error' ? <ErrorState title={s.failedTitle} message={describeContentError(list.error)} onRetry={list.reload} testID="search-error" /> : null}
      {term && list.status === 'ready' && !list.items.length ? (
        <Card testID="search-empty">
          <AppText>{s.empty}</AppText>
        </Card>
      ) : null}
    </View>
  );

  return (
    <SafeAreaView testID="screen-search" edges={['left', 'right']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      {term && list.status === 'loading' ? (
        <View style={{ padding: theme.spacing.lg }}>
          {header}
          <Loading label={strings.common.loading} />
        </View>
      ) : (
        <FlashList
          testID="search-results"
          data={term ? list.items : []}
          keyExtractor={(hit) => hit.type + ':' + hit.id}
          renderItem={({ item }) => {
            const path = resolveNotificationPath(item.path);
            return (
              <Pressable onPress={path ? () => router.push(path) : undefined} disabled={!path} accessibilityRole={path ? 'button' : undefined} testID={'search-hit-' + item.id} style={{ paddingBottom: theme.spacing.sm }}>
                <Card>
                  <AppText variant="caption" color="primary" style={{ fontWeight: '700' }}>
                    {s.types[item.type] || item.type}
                  </AppText>
                  <AppText variant="label">{item.title || (item.author && item.author.displayName) || s.untitled}</AppText>
                  <SearchEventLine hit={item} />
                  {item.snippet && item.snippet.text ? (
                    <AppText color="textMuted" testID={'search-hit-' + item.id + '-snippet'}>
                      {snippetSegments(item.snippet.text, item.snippet.highlights).map((seg, i) => (
                        <Text key={i} style={seg.highlight ? { fontWeight: '700', color: theme.colors.text } : null}>
                          {seg.text}
                        </Text>
                      ))}
                    </AppText>
                  ) : null}
                  {item.createdUtc ? (
                    <AppText variant="caption" color="textMuted">
                      {formatRelativeTime(item.createdUtc)}
                    </AppText>
                  ) : null}
                </Card>
              </Pressable>
            );
          }}
          ListHeaderComponent={header}
          onEndReached={term ? list.loadMore : undefined}
          onEndReachedThreshold={0.5}
          contentContainerStyle={{ padding: theme.spacing.lg }}
        />
      )}
    </SafeAreaView>
  );
}

/**
 * Gated by the access model (ACCESS_MODELS_CONTRACT.md section 8.7): the `search` feature, gated
 * independently of the forums. Search is never open to signed-out guests.
 */
export default function SearchRoute() {
  return (
    <FeatureGate feature="search" allowGuest={false}>
      <SearchRouteContent />
    </FeatureGate>
  );
}
