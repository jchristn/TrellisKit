import { FlashList } from '@shopify/flash-list';
import { formatEventShort } from '@trelliskit/shared';
import { useCallback, useState } from 'react';
import { RefreshControl, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { filtersToQuery } from '../../content/enumeration';
import { describeFeatureError } from '../../content/phase4';
import { useCursorList } from '../../hooks/useCursorList';
import { useListFilters } from '../../hooks/useListFilters';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import { FilterButton } from '../filters/FilterBar';

const t = strings.events.proposals;

/** The member's event proposals (`POST /me/events/proposals/enumerate`) with status, the review note, and Withdraw. */
export default function MyProposals({ testID = 'my-proposals' }) {
  const theme = useTheme();
  const { api } = useAuth();
  const [filters, setFilters] = useListFilters('my-proposals', 'CreatedDescending');
  const [error, setError] = useState(null);
  const fetchPage = useCallback((continuationToken) => api.enumerateMyEventProposals({ ...filtersToQuery(filters), continuationToken, maxResults: 25 }), [api, filters]);
  const list = useCursorList(fetchPage, { key: JSON.stringify(filtersToQuery(filters)) });

  async function withdraw(item) {
    setError(null);
    try {
      const next = await api.withdrawEventProposal(item.id);
      list.updateItem(item.id, () => (next && next.id ? next : { ...item, status: 'Withdrawn' }));
    } catch (e) {
      setError(describeFeatureError(e, 'events'));
    }
  }

  return (
    <FlashList
      testID={testID}
      data={list.status === 'ready' ? list.items : []}
      keyExtractor={(item) => item.id}
      renderItem={({ item }) => (
        <Card style={{ marginBottom: theme.spacing.sm }} testID={'proposal-' + item.id}>
          <AppText variant="heading">{item.title}</AppText>
          <AppText>{formatEventShort(item)}</AppText>
          <AppText color="textMuted" testID={'proposal-' + item.id + '-status'}>
            {t.statuses[item.status] || item.status}
          </AppText>
          {item.reviewNote ? <AppText color="textMuted">{t.reviewNote(item.reviewNote)}</AppText> : null}
          {item.status === 'Proposed' ? <Button label={t.withdraw} variant="ghost" onPress={() => withdraw(item)} style={{ alignSelf: 'auto' }} testID={'proposal-' + item.id + '-withdraw'} /> : null}
        </Card>
      )}
      ListHeaderComponent={
        <View style={{ gap: theme.spacing.md, paddingBottom: theme.spacing.md }}>
          <FilterButton filters={filters} onChange={setFilters} targetType="Event" sheetOnly testID={testID + '-filters'} />
          {error ? (
            <AppText color="danger" accessibilityRole="alert">
              {error}
            </AppText>
          ) : null}
          {list.status === 'loading' ? <Loading label={strings.events.loading} /> : null}
          {list.status === 'error' ? <ErrorState title={strings.events.loadFailedTitle} message={describeFeatureError(list.error, 'events')} onRetry={list.reload} /> : null}
          {list.status === 'ready' && !list.items.length ? (
            <Card testID={testID + '-empty'}>
              <AppText>{t.empty}</AppText>
            </Card>
          ) : null}
        </View>
      }
      onEndReached={list.loadMore}
      onEndReachedThreshold={0.5}
      refreshControl={<RefreshControl refreshing={list.refreshing} onRefresh={list.refresh} tintColor={theme.colors.primary} />}
      contentContainerStyle={{ padding: theme.spacing.lg }}
    />
  );
}
