import { FlashList } from '@shopify/flash-list';
import { IdeaOrdering } from '@trelliskit/shared';
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
import IdeaDetail, { describeIdeaError } from '../../src/components/ideas/IdeaDetail';
import IdeaRow from '../../src/components/ideas/IdeaRow';
import { buildEnumerationQuery, filtersToQuery } from '../../src/content/enumeration';
import { onContentEvent } from '../../src/content/events';
import { useCursorList } from '../../src/hooks/useCursorList';
import { useLayout } from '../../src/hooks/useLayout';
import { useListFilters } from '../../src/hooks/useListFilters';
import { configuredStatuses, ideaCollectionFilters, IdeaOrderings, IdeaPageSize, membersSubmitIdeas } from '../../src/ideas/ideas';
import { useProperty } from '../../src/property';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

const t = strings.ideas;

/**
 * The ideas board (`POST /ideas/enumerate`, contract section 7.1): orderings (Top, Trending,
 * Newest, Recently updated), status filter chips, open ideas only and my votes, search, label and
 * tag filters, and load more. Members suggest ideas directly when the property allows it, else
 * through a support request. Phones open `/ideas/{id}`; tablets show the idea beside the board.
 */
export default function IdeasScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const property = useProperty();
  const { isWide } = useLayout();
  const [filters, setFilters] = useListFilters('ideas', IdeaOrdering.Top);
  const [statuses, setStatuses] = useState(() => configuredStatuses(property));
  const [selectedStatuses, setSelectedStatuses] = useState([]);
  const [flags, setFlags] = useState([]);
  const [selectedId, setSelectedId] = useState(null);
  const collectionFilters = useMemo(() => ideaCollectionFilters({ statuses: selectedStatuses, openOnly: flags.includes('open'), votedByMe: flags.includes('voted') }), [selectedStatuses, flags]);
  const fetchPage = useCallback((continuationToken) => api.enumerateIdeas(buildEnumerationQuery({ continuationToken, maxResults: IdeaPageSize, filters, collectionFilters })), [api, filters, collectionFilters]);
  const list = useCursorList(fetchPage, { key: JSON.stringify(filtersToQuery(filters)) + JSON.stringify(collectionFilters) });
  const { updateItem, prependItem } = list;

  useEffect(() => {
    let live = true;
    api
      .enumerateIdeaStatuses({ maxResults: 50 })
      .then((result) => {
        const objects = result && Array.isArray(result.objects) ? result.objects.filter((s) => !s.isRetired) : [];
        if (live && objects.length) setStatuses(objects);
      })
      .catch(() => {});
    return () => {
      live = false;
    };
  }, [api]);

  useEffect(() => onContentEvent('idea', (idea) => prependItem(idea)), [prependItem]);
  useEffect(() => onContentEvent('ideaChanged', (idea) => updateItem(idea.id, (item) => ({ ...item, ...idea }))), [updateItem]);

  const open = useCallback((idea) => (isWide ? setSelectedId(idea.id) : router.push('/ideas/' + idea.id)), [isWide, router]);
  const toggle = (setter) => (value) => setter((current) => (current.includes(value) ? current.filter((x) => x !== value) : current.concat(value)));
  const direct = membersSubmitIdeas(property);

  const header = (
    <View style={{ gap: theme.spacing.sm }}>
      <AppText color="textMuted">{t.summary}</AppText>
      <Button label={t.suggest} accessibilityHint={t.suggestHint} variant="secondary" onPress={() => router.push(direct ? '/ideas/new' : '/support/new?type=Enhancement')} testID="ideas-suggest" />
      <ChipGroup label={t.statusFilter} options={statuses.map((s) => ({ value: s.key, label: s.label || s.key, color: s.color || null }))} values={selectedStatuses} onToggle={toggle(setSelectedStatuses)} testID="ideas-status" />
      <ChipGroup
        label={t.statusFilter}
        hideLabel
        options={[
          { value: 'open', label: t.openOnly },
          { value: 'voted', label: t.votedByMe }
        ]}
        values={flags}
        onToggle={toggle(setFlags)}
        testID="ideas-flags"
      />
      {list.status === 'error' ? <ErrorState title={t.loadFailedTitle} message={describeIdeaError(list.error)} onRetry={list.reload} testID="ideas-error" /> : null}
    </View>
  );

  const content =
    list.status === 'loading' ? (
      <Loading label={strings.common.loading} testID="ideas-loading" />
    ) : (
      <FlashList
        testID="ideas-list"
        data={list.items}
        keyExtractor={(idea) => idea.id}
        extraData={selectedId}
        renderItem={({ item }) => <IdeaRow idea={item} onOpen={open} selected={item.id === selectedId} />}
        contentContainerStyle={{ padding: theme.spacing.lg }}
        ListEmptyComponent={
          list.status === 'ready' ? (
            <AppText color="textMuted" testID="ideas-empty">
              {t.empty}
            </AppText>
          ) : null
        }
        ListFooterComponent={list.hasMore ? <Button label={t.loadMore} variant="ghost" onPress={list.loadMore} disabled={list.loadingMore} testID="ideas-load-more" /> : null}
        onEndReached={list.loadMore}
        onEndReachedThreshold={0.5}
        refreshControl={<RefreshControl refreshing={list.refreshing} onRefresh={list.refresh} tintColor={theme.colors.primary} />}
      />
    );

  return (
    <ListDetailLayout
      testID="screen-ideas"
      filterProps={{ filters, onChange: setFilters, targetType: 'Idea', orderings: IdeaOrderings.map((value) => ({ value, label: t.orderings[value] })), testID: 'ideas-filters' }}
      header={header}
      list={content}
      detail={selectedId ? <IdeaDetail key={selectedId} ideaId={selectedId} initialIdea={list.items.find((i) => i.id === selectedId) || null} onIdeaChange={(idea) => updateItem(idea.id, (item) => ({ ...item, ...idea }))} /> : <DetailPrompt text={t.selectIdea} testID="ideas-select" />}
    />
  );
}
