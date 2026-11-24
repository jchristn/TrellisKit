import { FlashList } from '@shopify/flash-list';
import { useRouter } from 'expo-router';
import { useCallback, useState } from 'react';
import { RefreshControl, View } from 'react-native';
import { BannerSlot } from '../../appConfig';
import { useAuth } from '../../auth/AuthProvider';
import { useLayout } from '../../hooks/useLayout';
import { useListFilters } from '../../hooks/useListFilters';
import { describeMessagingError } from '../../messaging/format';
import { useMessaging } from '../../messaging/MessagingProvider';
import { useConversationList } from '../../messaging/useConversationList';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import ChipGroup from '../ChipGroup';
import ErrorState from '../ErrorState';
import IconButton from '../IconButton';
import ListDetailLayout, { DetailPrompt } from '../ListDetailLayout';
import Loading from '../Loading';
import ConversationRow from './ConversationRow';
import ConversationView from './ConversationView';

const FOLDERS = ['Inbox', 'Requests', 'Archived'];
const t = strings.messaging;

/**
 * The conversation list (contract section 19): Inbox, Requests, and Archived with the request
 * count, search by name, label and tag filters on my own conversation metadata (bottom sheet on
 * phones, side panel on tablets), load more, pull to refresh, and live updates from the socket.
 * Phones open `/messages/{id}`; tablets show the conversation beside the list in SplitView.
 * @param {{ initialFolder?: string }} props
 */
export default function ConversationsScreen({ initialFolder = 'Inbox' }) {
  const theme = useTheme();
  const router = useRouter();
  const { api, user } = useAuth();
  const { isWide } = useLayout();
  const { unread } = useMessaging();
  const [folder, setFolder] = useState(FOLDERS.includes(initialFolder) ? initialFolder : 'Inbox');
  const [filters, setFilters] = useListFilters('messages');
  const [selectedId, setSelectedId] = useState(null);
  const list = useConversationList({ api, folder, filters, currentUserId: user ? user.id : null, activeConversationId: isWide ? selectedId : null });

  const open = useCallback(
    (conversation) => {
      if (isWide) setSelectedId(conversation.id);
      else router.push('/messages/' + conversation.id);
    },
    [isWide, router]
  );

  const folderOptions = FOLDERS.map((value) => ({ value, label: value === 'Requests' && unread.requests ? t.folderCount(t.folders[value], unread.requests) : t.folders[value] }));
  const header = (
    <View style={{ gap: theme.spacing.sm }}>
      <BannerSlot placement="Messages" />
      <AppText color="textMuted">{strings.messages.summary}</AppText>
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}>
        <View style={{ flex: 1 }}>
          <ChipGroup
            label={t.folderLabel}
            hideLabel
            options={folderOptions}
            value={folder}
            onToggle={(next) => {
              setFolder(next);
              setSelectedId(null);
            }}
            testID="messages-folder"
          />
        </View>
        <IconButton icon="search-outline" label={t.search} accessibilityHint={t.searchHint} onPress={() => router.push('/messages/search')} testID="messages-search" />
        <IconButton icon="settings-outline" label={t.settings} accessibilityHint={t.settingsHint} onPress={() => router.push('/account/messaging')} testID="messages-settings" />
      </View>
      {list.status === 'error' ? <ErrorState title={t.loadFailedTitle} message={describeMessagingError(list.error)} onRetry={list.reload} testID="messages-error" /> : null}
    </View>
  );

  const content =
    list.status === 'loading' ? (
      <Loading label={strings.common.loading} testID="messages-loading" />
    ) : (
      <FlashList
        testID="messages-list"
        data={list.items}
        keyExtractor={(c) => c.id}
        extraData={selectedId}
        renderItem={({ item }) => <ConversationRow conversation={item} onOpen={open} selected={item.id === selectedId} />}
        contentContainerStyle={{ padding: theme.spacing.lg }}
        ListEmptyComponent={
          list.status === 'ready' ? (
            <AppText color="textMuted" testID="messages-empty">
              {t.empty[folder]}
            </AppText>
          ) : null
        }
        ListFooterComponent={list.hasMore ? <Button label={t.loadMore} variant="ghost" onPress={list.loadMore} disabled={list.loadingMore} testID="messages-load-more" /> : null}
        onEndReached={list.loadMore}
        onEndReachedThreshold={0.5}
        refreshControl={<RefreshControl refreshing={list.refreshing} onRefresh={list.refresh} tintColor={theme.colors.primary} />}
      />
    );

  return (
    <ListDetailLayout
      testID="screen-messages"
      filterProps={{ filters, onChange: setFilters, targetType: 'Conversation', searchLabel: t.searchPeople, testID: 'messages-filters' }}
      header={header}
      list={content}
      detail={
        selectedId ? (
          <ConversationView
            key={selectedId}
            conversationId={selectedId}
            onConversationChange={list.upsert}
            onRead={list.markRead}
            onClosed={(id) => {
              list.remove(id);
              setSelectedId(null);
            }}
          />
        ) : (
          <DetailPrompt text={t.selectConversation} testID="messages-select" />
        )
      }
    />
  );
}
