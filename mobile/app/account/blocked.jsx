import { useCallback, useState } from 'react';
import { View } from 'react-native';
import { safety } from '../../src/api/community';
import { useAuth } from '../../src/auth/AuthProvider';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import Card from '../../src/components/Card';
import ErrorState from '../../src/components/ErrorState';
import AuthorRow from '../../src/components/feed/AuthorRow';
import { FilterButton, FilterSidePanel } from '../../src/components/filters/FilterBar';
import Loading from '../../src/components/Loading';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import { filtersToQuery } from '../../src/content/enumeration';
import { describeContentError } from '../../src/content/errors';
import { useCursorList } from '../../src/hooks/useCursorList';
import { useLayout } from '../../src/hooks/useLayout';
import { useListFilters } from '../../src/hooks/useListFilters';
import { notifyBlocksChanged } from '../../src/messaging/blockEvents';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

/**
 * Blocked members (POST /me/blocks/enumerate, contract 9.2, with search and label and tag filters) with Unblock. Explains that staff cannot be
 * blocked, so they never appear here.
 */
export default function BlockedMembersScreen() {
  const theme = useTheme();
  const { api } = useAuth();
  const [message, setMessage] = useState(null);
  const [busyId, setBusyId] = useState(null);
  const { isWide } = useLayout();
  const [filters, setFilters] = useListFilters('blocked');
  const fetchPage = useCallback((continuationToken) => safety.listBlocks(api, { continuationToken, filters }), [api, filters]);
  const list = useCursorList(fetchPage, { key: JSON.stringify(filtersToQuery(filters)) });
  // The blocks collection orders only by block time (REST_API.md, Blocks).
  const orderings = ['CreatedDescending', 'CreatedAscending'].map((value) => ({ value, label: strings.filters.orderings[value] }));
  const t = strings.blocked;

  async function unblock(entry) {
    setMessage(null);
    setBusyId(entry.user.id);
    try {
      await safety.unblock(api, entry.user.id);
      notifyBlocksChanged(entry.user.id, false);
      list.setItems((items) => items.filter((i) => i.user.id !== entry.user.id));
      setMessage({ tone: 'success', text: strings.block.unblocked(entry.user.handle || entry.user.displayName) });
    } catch (e) {
      setMessage({ tone: 'error', text: describeContentError(e) });
    } finally {
      setBusyId(null);
    }
  }

  const content = (
    <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-blocked">
      <AppText color="textMuted">{t.body}</AppText>
      <FilterButton filters={filters} onChange={setFilters} targetType="User" orderings={orderings} searchLabel={strings.filters.searchBlocked} testID="blocked-filters" />
      <Card testID="blocked-staff-note">
        <AppText color="textMuted">{t.staffNote}</AppText>
      </Card>
      <Notice tone={message ? message.tone : 'info'} testID="blocked-message">
        {message ? message.text : null}
      </Notice>
      {list.status === 'loading' ? <Loading label={strings.common.loading} /> : null}
      {list.status === 'error' ? <ErrorState title={t.loadFailedTitle} message={describeContentError(list.error)} onRetry={list.reload} testID="blocked-error" /> : null}
      {list.status === 'ready' && !list.items.length ? (
        <AppText color="textMuted" testID="blocked-empty">
          {t.empty}
        </AppText>
      ) : null}
      {list.items.map((entry) => (
        <Card key={entry.user.id} testID={'blocked-' + entry.user.id}>
          <AuthorRow author={entry.user} size={40} />
          <Button label={t.unblock} accessibilityLabel={t.unblockLabel(entry.user.displayName)} variant="secondary" onPress={() => unblock(entry)} disabled={busyId === entry.user.id} testID={'blocked-' + entry.user.id + '-unblock'} />
        </Card>
      ))}
      {list.hasMore ? (
        <View>
          <Button label={t.loadMore} variant="ghost" onPress={list.loadMore} disabled={list.loadingMore} testID="blocked-load-more" />
        </View>
      ) : null}
    </Screen>
  );
  if (!isWide) return content;
  return (
    <View style={{ flex: 1, flexDirection: 'row' }}>
      <FilterSidePanel filters={filters} onChange={setFilters} targetType="User" orderings={orderings} searchLabel={strings.filters.searchBlocked} testID="blocked-filters" />
      <View style={{ flex: 1 }}>{content}</View>
    </View>
  );
}
