import { FlashList } from '@shopify/flash-list';
import { useRouter } from 'expo-router';
import { useCallback, useMemo } from 'react';
import { RefreshControl, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useAuth } from '../../src/auth/AuthProvider';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import ErrorState from '../../src/components/ErrorState';
import { FilterButton, FilterSidePanel } from '../../src/components/filters/FilterBar';
import Loading from '../../src/components/Loading';
import MemberRow from '../../src/components/profile/MemberRow';
import ProfileFilterControls from '../../src/components/profile/ProfileFilterControls';
import { buildEnumerationQuery, filtersToQuery } from '../../src/content/enumeration';
import { describeContentError } from '../../src/content/errors';
import { useCursorList } from '../../src/hooks/useCursorList';
import { useLayout } from '../../src/hooks/useLayout';
import { useListFilters } from '../../src/hooks/useListFilters';
import { directoryChips, directoryCollectionFilters, DirectoryOrderings, DirectoryPageSize } from '../../src/profile/directory';
import { useProfileFields } from '../../src/profile/fields';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';
import FeatureGate from '../../src/components/FeatureGate';

/**
 * Member directory (POST /members/directory/enumerate): search, ordering, label and tag filters,
 * and profile filters for the property's filterable fields, in a side panel on tablets and a
 * bottom sheet on phones. Active filters show as removable chips with a count and Clear all.
 * Pages load by continuation token as the list scrolls.
 */
function MemberDirectoryScreenContent() {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const { isWide } = useLayout();
  const fields = useProfileFields();
  const [filters, setFilters] = useListFilters('directory');
  const orderings = DirectoryOrderings.map((value) => ({ value, label: strings.filters.orderings[value] }));
  const collectionFilters = useMemo(() => directoryCollectionFilters(filters, fields), [filters, fields]);
  const fetchPage = useCallback(
    (continuationToken) => api.enumerateMemberDirectory(buildEnumerationQuery({ continuationToken, maxResults: DirectoryPageSize, filters, collectionFilters })),
    [api, filters, collectionFilters]
  );
  const list = useCursorList(fetchPage, { key: JSON.stringify(filtersToQuery(filters)) + JSON.stringify(collectionFilters || {}) });
  const extraChips = useCallback((f) => directoryChips(f, fields), [fields]);
  const extra = useCallback((draft, setDraft) => <ProfileFilterControls fields={fields} value={draft.profile || {}} onChange={(profile) => setDraft((d) => ({ ...d, profile }))} testID="directory-profile-filters" />, [fields]);
  const open = useCallback((member) => router.push('/members/' + member.id), [router]);
  const t = strings.directory;
  const filterProps = { filters, onChange: setFilters, targetType: 'User', orderings, searchLabel: t.search, extra, testID: 'directory-filters' };

  const header = (
    <View style={{ gap: theme.spacing.md, paddingBottom: theme.spacing.sm }}>
      <FilterButton {...filterProps} extraChips={extraChips} />
      {list.status === 'error' ? <ErrorState title={t.loadFailedTitle} message={describeContentError(list.error)} onRetry={list.reload} testID="directory-error" /> : null}
      {list.status === 'ready' && !list.items.length ? (
        <AppText color="textMuted" testID="directory-empty">
          {t.empty}
        </AppText>
      ) : null}
    </View>
  );

  return (
    <SafeAreaView testID="screen-directory" edges={['left', 'right', 'bottom']} style={{ flex: 1, flexDirection: isWide ? 'row' : 'column', backgroundColor: theme.colors.background }}>
      {isWide ? <FilterSidePanel {...filterProps} /> : null}
      <View style={{ flex: 1 }}>
        {list.status === 'loading' ? (
          <View style={{ padding: isWide ? theme.spacing.xl : theme.spacing.lg }}>
            {header}
            <Loading label={strings.common.loading} testID="directory-loading" />
          </View>
        ) : (
          <FlashList
            testID="directory-list"
            data={list.items}
            keyExtractor={(m) => m.id}
            renderItem={({ item }) => <MemberRow member={item} fields={fields} onOpen={open} />}
            ListHeaderComponent={header}
            contentContainerStyle={{ padding: isWide ? theme.spacing.xl : theme.spacing.lg }}
            ListFooterComponent={list.hasMore ? <Button label={t.loadMore} variant="ghost" onPress={list.loadMore} disabled={list.loadingMore} testID="directory-load-more" /> : null}
            onEndReached={list.loadMore}
            onEndReachedThreshold={0.5}
            refreshControl={<RefreshControl refreshing={list.refreshing} onRefresh={list.refresh} tintColor={theme.colors.primary} />}
          />
        )}
      </View>
    </SafeAreaView>
  );
}

/** Gated by the access model: the `directory` feature (ACCESS_MODELS_CONTRACT.md section 8.7). */
export default function MemberDirectoryScreen() {
  return (
    <FeatureGate feature="directory" allowGuest={false}>
      <MemberDirectoryScreenContent />
    </FeatureGate>
  );
}
