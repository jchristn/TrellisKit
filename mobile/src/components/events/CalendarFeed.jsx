import { useState } from 'react';
import { Linking, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeFeatureError } from '../../content/phase4';
import { useResource } from '../../hooks/useResource';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import ErrorState from '../ErrorState';
import Loading from '../Loading';

const t = strings.events.calendar;

/** The `webcal:` form of a feed URL, which calendar apps open as a subscription. */
export function webcalUrl(url) {
  return String(url || '').replace(/^https?:\/\//i, 'webcal://');
}

/**
 * The member's private calendar feed (docs/PHASE4_CONTRACT.md section 4.5): the URL (created on
 * first open), Subscribe (opens the calendar app), Get a new link (rotate; the old one stops
 * working), and Turn off the feed.
 */
export default function CalendarFeed({ testID = 'calendar-feed' }) {
  const theme = useTheme();
  const { api } = useAuth();
  const [off, setOff] = useState(false);
  const [note, setNote] = useState(null);
  const [busy, setBusy] = useState(false);
  const feed = useResource(() => (off ? Promise.resolve(null) : api.getCalendarFeed()), String(off));

  async function run(fn, message) {
    setBusy(true);
    setNote(null);
    try {
      const next = await fn();
      if (next && next.url) feed.setData(next);
      setNote(message);
    } catch (e) {
      setNote(describeFeatureError(e, 'events'));
    } finally {
      setBusy(false);
    }
  }

  if (feed.status === 'loading') return <Loading label={strings.events.loading} testID={testID + '-loading'} />;
  if (feed.status !== 'ready') return <ErrorState title={t.feedOff} message={describeFeatureError(feed.error, 'events')} onRetry={feed.reload} testID={testID + '-error'} />;
  return (
    <View style={{ gap: theme.spacing.lg }} testID={testID}>
      <AppText>{t.feedBody}</AppText>
      {feed.data && feed.data.url ? (
        <Card>
          <AppText selectable testID={testID + '-url'}>
            {feed.data.url}
          </AppText>
          <Button label={t.feedSubscribe} onPress={() => Linking.openURL(webcalUrl(feed.data.url)).catch(() => {})} testID={testID + '-subscribe'} />
          <Button label={t.feedRotate} variant="secondary" disabled={busy} onPress={() => run(() => api.rotateCalendarFeed(), t.feedRotated)} testID={testID + '-rotate'} />
          <Button
            label={t.feedDelete}
            variant="ghost"
            disabled={busy}
            onPress={() =>
              run(async () => {
                await api.deleteCalendarFeed();
                setOff(true);
                return null;
              }, t.feedDeleted)
            }
            testID={testID + '-delete'}
          />
        </Card>
      ) : (
        <Button label={t.feedEnable} onPress={() => setOff(false)} testID={testID + '-enable'} />
      )}
      {note ? (
        <AppText color="textMuted" accessibilityRole="alert" testID={testID + '-note'}>
          {note}
        </AppText>
      ) : null}
    </View>
  );
}
