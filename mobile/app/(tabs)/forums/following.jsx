import { FlashList } from '@shopify/flash-list';
import { useRouter } from 'expo-router';
import { useCallback, useState } from 'react';
import { RefreshControl, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useAuth } from '../../../src/auth/AuthProvider';
import AppText from '../../../src/components/AppText';
import Card from '../../../src/components/Card';
import ChipGroup from '../../../src/components/ChipGroup';
import ErrorState from '../../../src/components/ErrorState';
import Loading from '../../../src/components/Loading';
import { FilterButton } from '../../../src/components/filters/FilterBar';
import ForumRow from '../../../src/components/forums/ForumRow';
import ThreadCard from '../../../src/components/forums/ThreadCard';
import { filtersToQuery } from '../../../src/content/enumeration';
import { describeContentError } from '../../../src/content/errors';
import { useCursorList } from '../../../src/hooks/useCursorList';
import { useListFilters } from '../../../src/hooks/useListFilters';
import strings from '../../../src/strings';
import { useTheme } from '../../../src/theme';
import FeatureGate from '../../../src/components/FeatureGate';

const f = strings.forums.followingScreen;
const TABS = ['Thread', 'Forum'];

/** The forums and threads the member follows (`POST /me/follows/enumerate`, docs/PHASE3_CONTRACT.md section 9.2). */
function FollowingScreenContent() {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const [type, setType] = useState('Thread');
  const [filters, setFilters] = useListFilters('forum-follows');
  const fetchPage = useCallback((continuationToken) => api.enumerateMyFollows({ ...filtersToQuery(filters), ordering: undefined, continuationToken, maxResults: 20, filters: { targetType: type } }), [api, filters, type]);
  const list = useCursorList(fetchPage, { key: type + JSON.stringify(filtersToQuery(filters)), filter: (row) => Boolean(row && row.target) });

  const header = (
    <View style={{ gap: theme.spacing.md, paddingBottom: theme.spacing.md }}>
      <ChipGroup label={f.title} options={TABS.map((value) => ({ value, label: f.tabs[value] }))} value={type} onToggle={setType} testID="following-tabs" />
      <FilterButton filters={filters} onChange={setFilters} targetType={type === 'Forum' ? 'ForumFollow' : 'ThreadFollow'} sheetOnly testID="following-filters" />
      {list.status === 'error' ? <ErrorState title={f.loadFailedTitle} message={describeContentError(list.error)} onRetry={list.reload} testID="following-error" /> : null}
      {list.status === 'ready' && !list.items.length ? (
        <Card testID="following-empty">
          <AppText>{f.empty[type]}</AppText>
        </Card>
      ) : null}
    </View>
  );

  return (
    <SafeAreaView testID="screen-following" edges={['left', 'right']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      {list.status === 'loading' ? (
        <Loading label={strings.common.loading} />
      ) : (
        <FlashList
          testID="following-list"
          data={list.items}
          keyExtractor={(row) => row.id}
          renderItem={({ item }) => (
            <View style={{ paddingBottom: theme.spacing.sm }}>
              {item.targetType === 'Forum' ? <ForumRow forum={item.target} onPress={() => router.push('/forums/' + item.target.id)} /> : <ThreadCard thread={item.target} showForum onPress={() => router.push('/threads/' + item.target.id)} />}
            </View>
          )}
          ListHeaderComponent={header}
          onEndReached={list.loadMore}
          onEndReachedThreshold={0.5}
          refreshControl={<RefreshControl refreshing={list.refreshing} onRefresh={list.refresh} tintColor={theme.colors.primary} />}
          contentContainerStyle={{ padding: theme.spacing.lg }}
        />
      )}
    </SafeAreaView>
  );
}

/** Gated by the access model: the `forums` feature (ACCESS_MODELS_CONTRACT.md section 8.7). */
export default function FollowingScreen() {
  return (
    <FeatureGate feature="forums" allowGuest={false}>
      <FollowingScreenContent />
    </FeatureGate>
  );
}
