import { FlashList } from '@shopify/flash-list';
import { useCallback, useState } from 'react';
import { RefreshControl, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { filtersToQuery } from '../../content/enumeration';
import { describeFeatureError, useDirectoryStrings } from '../../content/phase4';
import { useCursorList } from '../../hooks/useCursorList';
import { useListFilters } from '../../hooks/useListFilters';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import ChipGroup from '../ChipGroup';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import { FilterButton } from '../filters/FilterBar';

const STATUSES = ['Pending', 'Approved', 'Rejected', 'Withdrawn'];

/**
 * The member's own directory submissions (`POST /me/directory/submissions/enumerate`) with status
 * chips, label and tag filters, the moderator's note, and Withdraw for pending ones.
 */
export default function MySubmissions({ testID = 'my-submissions' }) {
  const theme = useTheme();
  const { api } = useAuth();
  const t = useDirectoryStrings();
  const [filters, setFilters] = useListFilters('my-submissions', 'CreatedDescending');
  const [status, setStatus] = useState(null);
  const [error, setError] = useState(null);
  const fetchPage = useCallback((continuationToken) => api.enumerateMySubmissions({ ...filtersToQuery(filters), continuationToken, maxResults: 25, filters: status ? { status: [status] } : undefined }), [api, filters, status]);
  const list = useCursorList(fetchPage, { key: JSON.stringify(filtersToQuery(filters)) + status });

  async function withdraw(item) {
    setError(null);
    try {
      const next = await api.withdrawSubmission(item.id);
      list.updateItem(item.id, () => (next && next.id ? next : { ...item, status: 'Withdrawn' }));
    } catch (e) {
      setError(describeFeatureError(e, 'places', t));
    }
  }

  const header = (
    <View style={{ gap: theme.spacing.md, paddingBottom: theme.spacing.md }}>
      <FilterButton filters={filters} onChange={setFilters} targetType="DirectorySubmission" sheetOnly testID={testID + '-filters'} />
      <ChipGroup label={t.submissions.title} hideLabel options={STATUSES.map((value) => ({ value, label: t.submissions.statuses[value] }))} value={status} onToggle={(value) => setStatus((s) => (s === value ? null : value))} testID={testID + '-status'} />
      {error ? (
        <AppText color="danger" accessibilityRole="alert">
          {error}
        </AppText>
      ) : null}
      {list.status === 'loading' ? <Loading label={t.loading} /> : null}
      {list.status === 'error' ? <ErrorState title={t.loadFailedTitle} message={describeFeatureError(list.error, 'places', t)} onRetry={list.reload} /> : null}
      {list.status === 'ready' && !list.items.length ? (
        <Card testID={testID + '-empty'}>
          <AppText>{t.submissions.empty}</AppText>
        </Card>
      ) : null}
    </View>
  );

  return (
    <FlashList
      testID={testID}
      data={list.status === 'ready' ? list.items : []}
      keyExtractor={(item) => item.id}
      renderItem={({ item }) => (
        <Card style={{ marginBottom: theme.spacing.sm }} testID={'submission-' + item.id}>
          <AppText variant="heading">{t.submissions.types[item.type] || item.type}</AppText>
          {item.target && item.target.name ? <AppText>{item.target.name}</AppText> : null}
          <AppText color="textMuted" testID={'submission-' + item.id + '-status'}>
            {t.submissions.statuses[item.status] || item.status}
          </AppText>
          {item.reviewNote ? <AppText color="textMuted">{t.submissions.reviewNote(item.reviewNote)}</AppText> : null}
          {item.status === 'Pending' ? <Button label={t.submissions.withdraw} variant="ghost" onPress={() => withdraw(item)} style={{ alignSelf: 'auto' }} testID={'submission-' + item.id + '-withdraw'} /> : null}
        </Card>
      )}
      ListHeaderComponent={header}
      onEndReached={list.loadMore}
      onEndReachedThreshold={0.5}
      refreshControl={<RefreshControl refreshing={list.refreshing} onRefresh={list.refresh} tintColor={theme.colors.primary} />}
      contentContainerStyle={{ padding: theme.spacing.lg }}
    />
  );
}
