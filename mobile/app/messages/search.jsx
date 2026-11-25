import { FlashList } from '@shopify/flash-list';
import { headlineParts } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, View } from 'react-native';
import { useAuth } from '../../src/auth/AuthProvider';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import ErrorState from '../../src/components/ErrorState';
import ListDetailLayout from '../../src/components/ListDetailLayout';
import Loading from '../../src/components/Loading';
import TextField from '../../src/components/TextField';
import { buildEnumerationQuery, filtersToQuery } from '../../src/content/enumeration';
import { useCursorList } from '../../src/hooks/useCursorList';
import { useListFilters } from '../../src/hooks/useListFilters';
import { describeMessagingError } from '../../src/messaging/format';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

const t = strings.messaging;
const MinSearch = 2;

function ResultRow({ hit, onOpen }) {
  const theme = useTheme();
  const name = (hit.otherMember && hit.otherMember.displayName) || t.deletedMember;
  // The server wraps matches in `**`; render them highlighted instead of showing the markers.
  const parts = hit.headline ? headlineParts(hit.headline) : [{ text: (hit.message && hit.message.body) || '', match: false }];
  const text = parts.map((p) => p.text).join('');
  return (
    <Pressable onPress={() => onOpen(hit)} accessibilityRole="button" accessibilityLabel={t.searchResultLabel(name, text)} testID={'message-hit-' + (hit.message ? hit.message.id : hit.conversationId)} style={{ paddingVertical: theme.spacing.md, gap: theme.spacing.xxs, borderBottomWidth: 1, borderBottomColor: theme.colors.border }}>
      <AppText variant="label">{name}</AppText>
      <AppText color="textMuted" testID={'message-hit-text-' + (hit.message ? hit.message.id : hit.conversationId)}>
        {parts.map((part, i) =>
          part.match ? (
            <AppText key={i} color="onAccentSoft" style={{ fontWeight: '700', backgroundColor: theme.colors.accentSoft }}>
              {part.text}
            </AppText>
          ) : (
            part.text
          )
        )}
      </AppText>
    </Pressable>
  );
}

/**
 * Search inside my direct messages (as-built note B, `POST /messages/search/enumerate`): 2 to 200
 * characters, label and tag filters on my own message metadata, load more by continuation token.
 * A result opens its conversation.
 */
export default function MessageSearchScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const [filters, setFilters] = useListFilters('message-search');
  const [text, setText] = useState(filters.search || '');
  const term = (filters.search || '').trim();
  const enabled = term.length >= MinSearch;
  // Hits carry no id of their own; the message id keys them in the list.
  const fetchPage = useCallback(
    async (continuationToken) => {
      const result = await api.messaging.searchMessages(buildEnumerationQuery({ continuationToken, maxResults: 25, filters }));
      return result && Array.isArray(result.objects) ? { ...result, objects: result.objects.map((hit) => ({ id: hit.message ? hit.message.id : hit.conversationId, ...hit })) } : result;
    },
    [api, filters]
  );
  const list = useCursorList(fetchPage, { key: JSON.stringify(filtersToQuery(filters)), enabled });
  const open = useCallback((hit) => router.push('/messages/' + hit.conversationId), [router]);

  const header = (
    <View style={{ gap: theme.spacing.sm }}>
      <TextField label={t.searchField} value={text} onChangeText={setText} returnKeyType="search" maxLength={200} onSubmitEditing={() => setFilters({ ...filters, search: text })} testID="message-search-field" />
      <Button label={t.search} variant="secondary" onPress={() => setFilters({ ...filters, search: text })} testID="message-search-submit" />
      {!enabled ? (
        <AppText color="textMuted" testID="message-search-minimum">
          {t.searchMinimum}
        </AppText>
      ) : null}
      {enabled && list.status === 'error' ? <ErrorState title={t.searchFailedTitle} message={describeMessagingError(list.error)} onRetry={list.reload} testID="message-search-error" /> : null}
    </View>
  );

  const content = !enabled ? null : list.status === 'loading' ? (
    <Loading label={strings.common.loading} testID="message-search-loading" />
  ) : (
    <FlashList
      testID="message-search-results"
      data={list.items}
      keyExtractor={(hit) => hit.id}
      renderItem={({ item }) => <ResultRow hit={item} onOpen={open} />}
      contentContainerStyle={{ padding: theme.spacing.lg }}
      ListEmptyComponent={
        list.status === 'ready' ? (
          <AppText color="textMuted" testID="message-search-empty">
            {t.searchEmpty}
          </AppText>
        ) : null
      }
      ListFooterComponent={list.hasMore ? <Button label={t.searchLoadMore} variant="ghost" onPress={list.loadMore} disabled={list.loadingMore} testID="message-search-load-more" /> : null}
      onEndReached={list.loadMore}
      onEndReachedThreshold={0.5}
    />
  );

  return <ListDetailLayout testID="screen-message-search" filterProps={{ filters, onChange: (next) => { setFilters(next); setText(next.search || ''); }, targetType: 'Message', searchLabel: t.searchField, testID: 'message-search-filters' }} header={header} list={content} detail={null} />;
}
