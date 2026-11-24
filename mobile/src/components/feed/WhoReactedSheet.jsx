import { useCallback, useState } from 'react';
import { View } from 'react-native';
import { reactions as reactionsApi } from '../../api/community';
import { useAuth } from '../../auth/AuthProvider';
import { activeFilterCount } from '../../content/enumeration';
import { describeContentError } from '../../content/errors';
import { useCursorList } from '../../hooks/useCursorList';
import { useListFilters } from '../../hooks/useListFilters';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import ChipGroup from '../ChipGroup';
import Loading from '../Loading';
import Sheet from '../Sheet';
import { InlineFilters } from '../filters/FilterBar';
import AuthorRow from './AuthorRow';

/**
 * Who reacted (POST {target}/reactions/enumerate with the `key` filter, contract 6.3): newest first, filterable by
 * reaction and by label and tag (kept for the session), paged with "Load more". Members the viewer
 * blocked are already left out by the server.
 */
export default function WhoReactedSheet({ target, initialKey, set, onClose }) {
  const theme = useTheme();
  const { api } = useAuth();
  const [key, setKey] = useState(initialKey || null);
  const [filters, setFilters] = useListFilters('who-reacted');
  const fetchPage = useCallback((continuationToken) => reactionsApi.list(api, target, { key: key || undefined, continuationToken, filters }), [api, target, key, filters]);
  const list = useCursorList(fetchPage, { key: (key || 'all') + ':' + JSON.stringify(filters) });
  const emojiOf = (k) => {
    const found = set.items.find((r) => r.key === k);
    return found ? found.emoji : '';
  };
  const options = [{ value: null, label: strings.reactions.all }].concat(set.items.map((r) => ({ value: r.key, label: r.emoji + ' ' + r.label })));

  return (
    <Sheet visible title={strings.reactions.whoTitle} onClose={onClose} testID="who-reacted">
      <ChipGroup label={strings.reactions.whoTitle} options={options} value={key} onToggle={setKey} testID="who-reacted-filter" />
      <InlineFilters filters={filters} onChange={setFilters} targetType="Reaction" searchLabel={strings.filters.searchPeople} testID="who-reacted-filters" />
      {list.status === 'loading' ? <Loading label={strings.common.loading} /> : null}
      {list.status === 'error' ? <AppText color="danger">{describeContentError(list.error) || strings.reactions.loadFailed}</AppText> : null}
      {list.status === 'ready' && !list.items.length ? <AppText color="textMuted">{activeFilterCount(filters) ? strings.reactions.noMatches : strings.reactions.none}</AppText> : null}
      {list.items.map((item) => (
        <View key={(item.user && item.user.id) + ':' + item.key + ':' + item.createdUtc} style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}>
          <View style={{ flex: 1 }}>
            <AuthorRow author={item.user} size={32} onNavigate={onClose} />
          </View>
          <AppText accessibilityLabel={(set.items.find((r) => r.key === item.key) || { label: item.key }).label}>{emojiOf(item.key)}</AppText>
        </View>
      ))}
      {list.hasMore ? <Button label={strings.feed.loadMore} variant="secondary" onPress={list.loadMore} disabled={list.loadingMore} /> : null}
    </Sheet>
  );
}
