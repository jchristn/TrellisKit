import { FlashList } from '@shopify/flash-list';
import { useRouter } from 'expo-router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshControl, View } from 'react-native';
import { useAuth } from '../../src/auth/AuthProvider';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import ChipGroup from '../../src/components/ChipGroup';
import ErrorState from '../../src/components/ErrorState';
import ListDetailLayout, { DetailPrompt } from '../../src/components/ListDetailLayout';
import Loading from '../../src/components/Loading';
import Screen from '../../src/components/Screen';
import SupportEmailCard from '../../src/components/support/SupportEmailCard';
import TicketDetail, { describeTicketError } from '../../src/components/support/TicketDetail';
import TicketRow, { statusText } from '../../src/components/support/TicketRow';
import { buildEnumerationQuery, filtersToQuery } from '../../src/content/enumeration';
import { onContentEvent } from '../../src/content/events';
import { useCursorList } from '../../src/hooks/useCursorList';
import { useLayout } from '../../src/hooks/useLayout';
import { useListFilters } from '../../src/hooks/useListFilters';
import { useProperty } from '../../src/property';
import strings from '../../src/strings';
import { supportEnabled, TicketStatuses } from '../../src/support/support';
import { useTheme } from '../../src/theme';

const t = strings.support;

/**
 * My support requests (`POST /tickets/enumerate`): status chips, search, label and tag filters,
 * load more, and New request. Phones open `/support/tickets/{id}`; tablets show the ticket beside
 * the list. With `features.support` off, the property's support email instead.
 */
export default function SupportScreen() {
  const property = useProperty();
  if (!supportEnabled(property)) {
    return (
      <Screen testID="screen-support">
        <SupportEmailCard />
      </Screen>
    );
  }
  return <TicketList />;
}

function TicketList() {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const { isWide } = useLayout();
  const [filters, setFilters] = useListFilters('tickets');
  const [statuses, setStatuses] = useState([]);
  const [selectedId, setSelectedId] = useState(null);
  const collectionFilters = useMemo(() => (statuses.length ? { status: statuses } : null), [statuses]);
  const fetchPage = useCallback((continuationToken) => api.enumerateTickets(buildEnumerationQuery({ continuationToken, maxResults: 25, filters, collectionFilters })), [api, filters, collectionFilters]);
  const list = useCursorList(fetchPage, { key: JSON.stringify(filtersToQuery(filters)) + JSON.stringify(collectionFilters) });
  const { prependItem, updateItem } = list;
  useEffect(() => onContentEvent('ticket', (ticket) => prependItem(ticket)), [prependItem]);
  useEffect(() => onContentEvent('ticketChanged', (ticket) => updateItem(ticket.id, (item) => ({ ...item, ...ticket, messages: undefined }))), [updateItem]);
  const open = useCallback((ticket) => (isWide ? setSelectedId(ticket.id) : router.push('/support/tickets/' + ticket.id)), [isWide, router]);

  const header = (
    <View style={{ gap: theme.spacing.sm }}>
      <AppText color="textMuted">{t.summary}</AppText>
      <Button label={t.newTicket} accessibilityHint={t.newTicketHint} onPress={() => router.push('/support/new')} testID="support-new" />
      <ChipGroup label={t.statusFilter} options={TicketStatuses.map((value) => ({ value, label: statusText(value) }))} values={statuses} onToggle={(value) => setStatuses((s) => (s.includes(value) ? s.filter((x) => x !== value) : s.concat(value)))} testID="support-status" />
      {list.status === 'error' ? <ErrorState title={t.loadFailedTitle} message={describeTicketError(list.error)} onRetry={list.reload} testID="support-error" /> : null}
    </View>
  );

  const content =
    list.status === 'loading' ? (
      <Loading label={strings.common.loading} testID="support-loading" />
    ) : (
      <FlashList
        testID="support-list"
        data={list.items}
        keyExtractor={(ticket) => ticket.id}
        extraData={selectedId}
        renderItem={({ item }) => <TicketRow ticket={item} onOpen={open} selected={item.id === selectedId} />}
        contentContainerStyle={{ padding: theme.spacing.lg }}
        ListEmptyComponent={
          list.status === 'ready' ? (
            <AppText color="textMuted" testID="support-empty">
              {t.empty}
            </AppText>
          ) : null
        }
        ListFooterComponent={list.hasMore ? <Button label={t.loadMore} variant="ghost" onPress={list.loadMore} disabled={list.loadingMore} testID="support-load-more" /> : null}
        onEndReached={list.loadMore}
        onEndReachedThreshold={0.5}
        refreshControl={<RefreshControl refreshing={list.refreshing} onRefresh={list.refresh} tintColor={theme.colors.primary} />}
      />
    );

  return (
    <ListDetailLayout
      testID="screen-support"
      filterProps={{ filters, onChange: setFilters, targetType: 'Ticket', testID: 'support-filters' }}
      header={header}
      list={content}
      detail={selectedId ? <TicketDetail key={selectedId} ticketId={selectedId} onTicketChange={(next) => list.updateItem(next.id, (item) => ({ ...item, ...next, messages: undefined }))} /> : <DetailPrompt text={t.selectTicket} testID="support-select" />}
    />
  );
}
