import { formatDuration } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { ScrollView, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { formatBytes } from '../../content/phase4';
import { removeDownload, useDownloads } from '../../library/downloads';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import Loading from '../Loading';
import Notice from '../Notice';

const t = strings.library.downloads;

function formatDate(iso) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', year: 'numeric' }).format(d);
}

/**
 * Downloads on this device (docs/PHASE4_CONTRACT.md section 5.9): storage used, each lesson with
 * its license expiry, Play and Remove, the paused notice when the kill switch is on, and the honest
 * note about where files live and when they are removed. The list is local; it is bounded by the
 * server's per-member license limit.
 */
export default function DownloadsList({ testID = 'downloads' }) {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const downloads = useDownloads();
  if (!downloads.loaded) return <Loading label={strings.library.loading} testID={testID + '-loading'} />;
  const total = downloads.items.reduce((sum, d) => sum + (Number(d.sizeBytes) || 0), 0);
  return (
    <ScrollView contentContainerStyle={{ padding: theme.spacing.lg, gap: theme.spacing.md }} testID={testID}>
      {downloads.paused ? (
        <Notice tone="warning" testID={testID + '-paused'}>
          {t.paused}
        </Notice>
      ) : null}
      <AppText color="textMuted" testID={testID + '-storage'}>
        {t.storage(formatBytes(total))}
      </AppText>
      {downloads.items.length ? (
        downloads.items.map((entry) => (
          <Card key={entry.id} testID={'download-' + entry.id}>
            <AppText variant="heading">{entry.title}</AppText>
            <AppText color="textMuted">{[entry.seriesTitle, entry.durationSeconds ? formatDuration(entry.durationSeconds) : null, formatBytes(entry.sizeBytes)].filter(Boolean).join(' · ')}</AppText>
            <AppText color="textMuted" testID={'download-' + entry.id + '-expires'}>
              {t.expires(formatDate(entry.licenseExpiresUtc))}
            </AppText>
            <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
              <Button label={strings.library.lessonTitle} variant="secondary" onPress={() => router.push('/lessons/' + entry.lessonId)} style={{ alignSelf: 'auto' }} testID={'download-' + entry.id + '-open'} />
              <Button label={t.remove} variant="ghost" onPress={() => removeDownload(api, entry)} style={{ alignSelf: 'auto' }} testID={'download-' + entry.id + '-remove'} />
            </View>
          </Card>
        ))
      ) : (
        <Card testID={testID + '-empty'}>
          <AppText>{t.empty}</AppText>
        </Card>
      )}
      <AppText variant="caption" color="textMuted">
        {t.honestNote}
      </AppText>
    </ScrollView>
  );
}
