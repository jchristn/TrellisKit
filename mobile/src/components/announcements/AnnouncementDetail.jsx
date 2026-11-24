import { formatRelativeTime } from '@trelliskit/shared';
import { useCallback, useEffect, useState } from 'react';
import { ScrollView, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeContentError } from '../../content/errors';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import MarkdownBody from '../feed/MarkdownBody';

const t = strings.announcements;

/** One announcement (`GET /announcements/{id}`, contract section 8.2): title, sent time, and the Markdown body. */
export default function AnnouncementDetail({ announcementId, initial = null, testID = 'announcement' }) {
  const theme = useTheme();
  const { api } = useAuth();
  const [item, setItem] = useState(initial && initial.body !== undefined ? initial : null);
  const [status, setStatus] = useState(item ? 'ready' : 'loading');
  const [error, setError] = useState(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setItem(await api.getAnnouncement(announcementId));
      setStatus('ready');
    } catch (e) {
      setError(e);
      setStatus((s) => (s === 'ready' ? 'ready' : e && e.status === 404 ? 'gone' : 'error'));
    }
  }, [api, announcementId]);

  useEffect(() => {
    load();
  }, [load]);

  if (status === 'loading') return <Loading label={strings.common.loading} testID={testID + '-loading'} />;
  if (status !== 'ready' || !item) {
    return (
      <View style={{ padding: theme.spacing.lg }} testID={testID + '-error'}>
        <ErrorState title={status === 'gone' ? t.goneTitle : t.loadFailedTitle} message={status === 'gone' ? null : describeContentError(error)} onRetry={status === 'gone' ? undefined : load} />
      </View>
    );
  }
  return (
    <ScrollView contentContainerStyle={{ padding: theme.spacing.lg, gap: theme.spacing.md }} testID={testID}>
      <AppText variant="title" accessibilityRole="header" testID={testID + '-title'}>
        {item.title}
      </AppText>
      {item.sentUtc ? (
        <AppText variant="caption" color="textMuted">
          {formatRelativeTime(item.sentUtc)}
        </AppText>
      ) : null}
      <MarkdownBody body={item.body || ''} testID={testID + '-body'} />
    </ScrollView>
  );
}
