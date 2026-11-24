import { useCallback } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeFeatureError } from '../../content/phase4';
import { useCursorList } from '../../hooks/useCursorList';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import Sheet from '../Sheet';

/** Who is going (`POST /events/{id}/attendees/enumerate`, paged), shown only when `viewer.canSeeAttendees`. */
export default function AttendeesSheet({ eventId, onClose, testID = 'attendees' }) {
  const theme = useTheme();
  const { api } = useAuth();
  const fetchPage = useCallback((continuationToken) => api.enumerateAttendees(eventId, { continuationToken, maxResults: 50 }), [api, eventId]);
  const list = useCursorList(fetchPage, { key: eventId });
  const t = strings.events;
  return (
    <Sheet visible title={t.attendees} onClose={onClose} testID={testID}>
      <View style={{ gap: theme.spacing.sm }}>
        {list.status === 'loading' ? <Loading label={t.loading} /> : null}
        {list.status === 'error' ? <ErrorState title={t.loadFailedTitle} message={describeFeatureError(list.error, 'events')} onRetry={list.reload} /> : null}
        {list.items.map((row, i) => (
          <AppText key={(row.user && row.user.id) || i} testID={testID + '-' + ((row.user && row.user.id) || i)}>
            {row.user ? row.user.displayName : ''}
          </AppText>
        ))}
        {list.hasMore ? <Button label={t.loadMore} variant="secondary" onPress={list.loadMore} testID={testID + '-more'} /> : null}
      </View>
    </Sheet>
  );
}
