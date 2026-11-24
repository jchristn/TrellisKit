import { formatRelativeTime } from '@trelliskit/shared';
import { FlashList } from '@shopify/flash-list';
import { useRouter } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, RefreshControl, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { notifications as notificationsApi } from '../src/api/community';
import { useAuth } from '../src/auth/AuthProvider';
import AppText from '../src/components/AppText';
import Avatar from '../src/components/Avatar';
import Button from '../src/components/Button';
import ChipGroup from '../src/components/ChipGroup';
import ErrorState from '../src/components/ErrorState';
import { FilterButton, FilterSidePanel } from '../src/components/filters/FilterBar';
import IconButton from '../src/components/IconButton';
import Loading from '../src/components/Loading';
import MemberGate from '../src/components/MemberGate';
import Notice from '../src/components/Notice';
import { filtersToQuery } from '../src/content/enumeration';
import { describeContentError } from '../src/content/errors';
import { useCursorList } from '../src/hooks/useCursorList';
import { useLayout } from '../src/hooks/useLayout';
import { useListFilters } from '../src/hooks/useListFilters';
import { openInAppBrowser } from '../src/links';
import { resolveNotificationPath, staffWebPath } from '../src/notifications/deepLinks';
import { useProperty, webUrl } from '../src/property';
import { useNotificationsState } from '../src/notifications/NotificationsProvider';
import strings from '../src/strings';
import { useTheme } from '../src/theme';

/** Notification center page size (contract 8.3 default). */
export const NotificationPageSize = 30;

function NotificationRow({ item, onOpen, onToggleRead }) {
  const theme = useTheme();
  const { api } = useAuth();
  const actor = item.actor;
  const unread = !item.isRead;
  const t = strings.notifications;
  const label = [unread ? t.unread : null, item.title, item.body, formatRelativeTime(item.updatedUtc || item.createdUtc)].filter(Boolean).join('. ');
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', borderBottomWidth: 1, borderBottomColor: theme.colors.border, backgroundColor: unread ? theme.colors.accentSoft : theme.colors.background }} testID={'notification-' + item.id}>
      <Pressable
        onPress={() => onOpen(item)}
        accessibilityRole="button"
        accessibilityLabel={label}
        accessibilityHint={resolveNotificationPath(item.path) || staffWebPath(item.path) ? t.rowHint : undefined}
        style={({ pressed }) => ({ flex: 1, flexDirection: 'row', gap: theme.spacing.md, padding: theme.spacing.md, minHeight: theme.layout.minTouchTarget, opacity: pressed ? 0.8 : 1 })}
        testID={'notification-' + item.id + '-open'}
      >
        <Avatar uri={actor && actor.avatarUrl ? api.url(actor.avatarUrl) : null} token={api.accessToken} name={actor ? actor.displayName : strings.appName} size={40} />
        <View style={{ flex: 1, gap: theme.spacing.xxs }}>
          <AppText variant="label">{item.title}</AppText>
          {item.body ? <AppText color="textMuted">{item.body}</AppText> : null}
          <AppText variant="caption" color="textMuted">
            {formatRelativeTime(item.updatedUtc || item.createdUtc)}
          </AppText>
        </View>
        {unread ? <View testID={'notification-' + item.id + '-unread'} style={{ width: 10, height: 10, borderRadius: 5, backgroundColor: theme.colors.primary, alignSelf: 'center' }} /> : null}
      </Pressable>
      <IconButton icon={unread ? 'mail-open-outline' : 'mail-unread-outline'} label={unread ? t.markRead : t.markUnread} onPress={() => onToggleRead(item)} color="textMuted" testID={'notification-' + item.id + '-toggle'} />
    </View>
  );
}

/**
 * Notification center (contract 8.3): newest first, POST /notifications/enumerate paged by continuationToken, search, label and tag filters, All and Unread filters,
 * Mark all as read, per-row read and unread, pull to refresh. Opening a row marks it read, then
 * follows its `path` through Expo Router; unknown paths (types from a later phase) stay here.
 */
export default function NotificationsScreen() {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { api } = useAuth();
  const { isWide } = useLayout();
  const { setUnreadCount, refreshCount } = useNotificationsState();
  const [filter, setFilter] = useState('all');
  const [filters, setFilters] = useListFilters('notifications');
  const [message, setMessage] = useState(null);
  const fetchPage = useCallback((continuationToken) => notificationsApi.list(api, { continuationToken, maxResults: NotificationPageSize, unreadOnly: filter === 'unread', filters }), [api, filter, filters]);
  const onPage = useCallback(
    (page, { first }) => {
      if (!first) return;
      if (page && typeof page.unreadCount === 'number') setUnreadCount(page.unreadCount);
      else refreshCount();
    },
    [setUnreadCount, refreshCount]
  );
  const list = useCursorList(fetchPage, { key: filter + JSON.stringify(filtersToQuery(filters)), onPage });
  const orderings = ['CreatedDescending', 'CreatedAscending'].map((value) => ({ value, label: strings.filters.orderings[value] }));
  const { updateItem, setItems } = list;
  const t = strings.notifications;

  const setRead = useCallback(
    async (item, read) => {
      updateItem(item.id, (n) => ({ ...n, isRead: read, readUtc: read ? new Date().toISOString() : null }));
      try {
        const result = read ? await notificationsApi.markRead(api, [item.id]) : await notificationsApi.markUnread(api, [item.id]);
        if (result && typeof result.unreadCount === 'number') setUnreadCount(result.unreadCount);
      } catch (e) {
        updateItem(item.id, (n) => ({ ...n, isRead: !read }));
        setMessage(describeContentError(e));
      }
    },
    [api, updateItem, setUnreadCount]
  );

  const open = useCallback(
    async (item) => {
      setMessage(null);
      if (!item.isRead) await setRead(item, true);
      const path = resolveNotificationPath(item.path);
      if (path) {
        router.push(path);
        return;
      }
      // Staff notifications (tickets, queues) open the website's admin console in the in-app browser.
      const staff = staffWebPath(item.path);
      const url = staff ? webUrl(property, staff) : null;
      if (url) await openInAppBrowser(url);
    },
    [router, setRead, property]
  );

  async function markAll() {
    setMessage(null);
    try {
      const result = await notificationsApi.markAllRead(api);
      setItems((items) => (filter === 'unread' ? [] : items.map((n) => ({ ...n, isRead: true }))));
      if (result && typeof result.unreadCount === 'number') setUnreadCount(result.unreadCount);
      else refreshCount();
    } catch (e) {
      setMessage(describeContentError(e));
    }
  }

  const header = (
    <View style={{ gap: theme.spacing.md, padding: isWide ? theme.spacing.xl : theme.spacing.lg }}>
      <ChipGroup label={t.filterLabel} options={[{ value: 'all', label: t.all }, { value: 'unread', label: t.unreadOnly }]} value={filter} onToggle={setFilter} testID="notifications-filter" />
      <FilterButton filters={filters} onChange={setFilters} targetType="Notification" orderings={orderings} searchLabel={strings.filters.searchNotifications} testID="notifications-filters" />
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
        <Button label={t.markAllRead} variant="secondary" onPress={markAll} style={{ alignSelf: 'auto' }} testID="notifications-mark-all" />
        <Button label={t.settings} accessibilityHint={t.settingsHint} variant="ghost" onPress={() => router.push('/account/notifications')} style={{ alignSelf: 'auto' }} testID="notifications-settings" />
      </View>
      <Notice tone="error">{message}</Notice>
      {list.status === 'error' ? <ErrorState title={t.loadFailedTitle} message={describeContentError(list.error)} onRetry={list.reload} testID="notifications-error" /> : null}
      {list.status === 'ready' && !list.items.length ? (
        <View testID="notifications-empty" style={{ gap: theme.spacing.xs }}>
          <AppText variant="heading">{t.emptyTitle}</AppText>
          <AppText color="textMuted">{t.emptyBody}</AppText>
        </View>
      ) : null}
    </View>
  );

  return (
    <MemberGate>
      <SafeAreaView testID="screen-notifications" edges={['left', 'right', 'bottom']} style={{ flex: 1, flexDirection: isWide ? 'row' : 'column', backgroundColor: theme.colors.background }}>
        {isWide ? <FilterSidePanel filters={filters} onChange={setFilters} targetType="Notification" orderings={orderings} searchLabel={strings.filters.searchNotifications} testID="notifications-filters" /> : null}
        <View style={{ flex: 1 }}>
        {list.status === 'loading' ? (
          <>
            {header}
            <Loading label={strings.common.loading} testID="notifications-loading" />
          </>
        ) : (
          <FlashList
            testID="notifications-list"
            data={list.items}
            keyExtractor={(n) => n.id}
            renderItem={({ item }) => <NotificationRow item={item} onOpen={open} onToggleRead={(n) => setRead(n, !n.isRead)} />}
            ListHeaderComponent={header}
            ListFooterComponent={list.hasMore ? <Button label={t.loadMore} variant="ghost" onPress={list.loadMore} disabled={list.loadingMore} testID="notifications-load-more" /> : null}
            onEndReached={list.loadMore}
            onEndReachedThreshold={0.5}
            refreshControl={<RefreshControl refreshing={list.refreshing} onRefresh={list.refresh} tintColor={theme.colors.primary} />}
          />
        )}
        </View>
      </SafeAreaView>
    </MemberGate>
  );
}
