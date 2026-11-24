import { formatRelativeTime } from '@trelliskit/shared';
import { useCallback } from 'react';
import { View } from 'react-native';
import { comments as commentsApi, feed as feedApi } from '../../api/community';
import { useAuth } from '../../auth/AuthProvider';
import { describeContentError } from '../../content/errors';
import { activeFilterCount, filtersToQuery } from '../../content/enumeration';
import { useCursorList } from '../../hooks/useCursorList';
import { useListFilters } from '../../hooks/useListFilters';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import Loading from '../Loading';
import Sheet from '../Sheet';
import { InlineFilters } from '../filters/FilterBar';
import MarkdownBody from './MarkdownBody';

/**
 * Edit history of a post, comment, or forum thread (Phase 2 5.5 and 7.6, Phase 3 4.1), visible to everyone who can see the
 * item: the current version, then each stored revision newest first, filterable by the labels and
 * tags each revision carried (kept for the session).
 * @param {object} props
 * @param {'FeedPost'|'Comment'|'Thread'} props.targetType
 * @param {object} props.current The post or comment as it is now.
 */
export default function EditHistorySheet({ targetType, current, onClose }) {
  const theme = useTheme();
  const { api } = useAuth();
  const isPost = targetType === 'FeedPost' || targetType === 'Thread';
  const [filters, setFilters] = useListFilters('edit-history');
  const fetchPage = useCallback(
    (continuationToken) => {
      if (targetType === 'Thread') return api.enumerateThreadRevisions(current.id, { continuationToken, maxResults: 20, ...filtersToQuery(filters) });
      return targetType === 'FeedPost' ? feedApi.revisions(api, current.id, { continuationToken, filters }) : commentsApi.revisions(api, current.id, { continuationToken, filters });
    },
    [api, current.id, targetType, filters]
  );
  const list = useCursorList(fetchPage, { key: current.id + ':' + JSON.stringify(filters) });
  const filtered = activeFilterCount(filters) > 0;

  const version = (item, heading, key) => (
    <Card key={key} testID={'history-' + key}>
      <View style={{ gap: theme.spacing.xxs }}>
        <AppText variant="label">{heading}</AppText>
        <AppText variant="caption" color="textMuted">
          {[item.createdUtc || item.editedUtc ? formatRelativeTime(item.createdUtc || item.editedUtc) : null, item.editedBy ? strings.history.editedBy(item.editedBy.displayName) : null].filter(Boolean).join(' . ')}
        </AppText>
      </View>
      {isPost && item.title ? <AppText variant="heading">{item.title}</AppText> : null}
      <MarkdownBody body={item.body || ''} mentions={item.mentions} headings={isPost} />
      {item.attachmentSummary ? (
        <AppText variant="caption" color="textMuted">
          {item.attachmentSummary}
        </AppText>
      ) : null}
    </Card>
  );

  return (
    <Sheet visible title={strings.history.title} onClose={onClose} testID="edit-history">
      <InlineFilters filters={filters} onChange={setFilters} targetType={targetType + 'Revision'} searchLabel={strings.filters.searchHistory} testID="edit-history-filters" />
      {filtered ? null : version({ ...current, createdUtc: current.editedUtc || current.createdUtc, editedBy: null }, strings.history.current, 'current')}
      {list.status === 'loading' ? <Loading label={strings.common.loading} /> : null}
      {list.status === 'error' ? <AppText color="danger">{describeContentError(list.error) || strings.history.loadFailed}</AppText> : null}
      {list.status === 'ready' && !list.items.length ? <AppText color="textMuted">{filtered ? strings.history.noMatches : strings.history.empty}</AppText> : null}
      {list.items.map((rev) => version(rev, strings.history.revision(rev.revision), rev.id))}
      {list.hasMore ? <Button label={strings.history.loadMore} variant="secondary" onPress={list.loadMore} disabled={list.loadingMore} /> : null}
    </Sheet>
  );
}
