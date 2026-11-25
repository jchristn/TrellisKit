import { formatEventShort, ranksFeatureEnabled } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback } from 'react';
import { ScrollView, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { featureOn } from '../../content/phase4';
import { useCursorList } from '../../hooks/useCursorList';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import Section from '../Section';
import { shortDate } from '../ranks/rankHooks';

const t = strings.training.schedule;

function when(event) {
  try {
    return formatEventShort(event);
  } catch {
    return '';
  }
}

/** Upcoming events the member is going to (`POST /me/events/enumerate`, Going and upcoming). */
function UpcomingEvents() {
  const { api } = useAuth();
  const router = useRouter();
  const theme = useTheme();
  const fetchPage = useCallback((continuationToken) => api.enumerateMyEvents({ maxResults: 25, continuationToken, filters: { rsvpStatus: 'Going', upcoming: true } }), [api]);
  const list = useCursorList(fetchPage, { key: 'upcoming' });
  return (
    <Section title={t.upcoming} testID="training-schedule-upcoming">
      {list.status === 'loading' ? <Loading label={t.title} /> : null}
      {list.status === 'error' ? <ErrorState title={t.loadFailed} onRetry={list.reload} /> : null}
      {list.status === 'ready' && !list.items.length ? (
        <View style={{ gap: theme.spacing.sm }}>
          <AppText color="textMuted">{t.empty}</AppText>
          <Button label={t.browseEvents} variant="secondary" onPress={() => router.push('/map?section=events')} style={{ alignSelf: 'auto' }} testID="training-schedule-browse" />
        </View>
      ) : null}
      {list.items.map((event) => (
        <Button key={event.id} label={event.title} accessibilityLabel={[event.title, when(event)].filter(Boolean).join(', ')} variant="ghost" onPress={() => router.push('/events/' + encodeURIComponent(event.id))} style={{ alignSelf: 'auto' }} testID={'training-schedule-event-' + event.id} />
      ))}
      {list.hasMore && !list.loadingMore ? <Button label={t.loadMore} variant="secondary" onPress={list.loadMore} /> : null}
    </Section>
  );
}

/** The member's recent check-ins (`POST /me/attendance/enumerate`), shown while ranks are on. */
function CheckIns() {
  const { api } = useAuth();
  const fetchPage = useCallback((continuationToken) => api.enumerateMyAttendance({ maxResults: 25, continuationToken }), [api]);
  const list = useCursorList(fetchPage, { key: 'attendance' });
  return (
    <Section title={t.checkIns} testID="training-schedule-check-ins">
      {list.status === 'loading' ? <Loading label={t.checkIns} /> : null}
      {list.status === 'error' ? <ErrorState title={t.loadFailed} onRetry={list.reload} /> : null}
      {list.status === 'ready' && !list.items.length ? <AppText color="textMuted">{t.checkInsEmpty}</AppText> : null}
      {list.items.map((row) => (
        <AppText key={row.id}>{[row.title || row.targetTitle || null, shortDate(row.checkedInUtc || row.createdUtc)].filter(Boolean).join(' · ')}</AppText>
      ))}
      {list.hasMore && !list.loadingMore ? <Button label={t.loadMore} variant="secondary" onPress={list.loadMore} /> : null}
    </Section>
  );
}

/** Training Schedule: upcoming events the member is going to and, with ranks on, recent check-ins. */
export default function TrainingSchedule() {
  const theme = useTheme();
  const property = useProperty();
  return (
    <ScrollView contentContainerStyle={{ padding: theme.spacing.lg, gap: theme.spacing.lg }} testID="training-schedule">
      {featureOn(property, 'events') ? <Card><UpcomingEvents /></Card> : null}
      {ranksFeatureEnabled(property) ? <Card><CheckIns /></Card> : null}
    </ScrollView>
  );
}
