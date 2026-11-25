import { catalogFeatureEnabled, formatDuration } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { useCursorList } from '../../hooks/useCursorList';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';

const t = strings.training.links;

/**
 * "Techniques in this lesson" (`POST /lessons/{id}/catalog-entries/enumerate`): each technique
 * opens its catalog entry, and when the link names where it appears in the video, a second
 * button plays from there (`onSeek`, when the lesson has a player). Signed-in members only, while
 * the catalog is on; nothing renders when the lesson has no linked techniques.
 * @param {{ lessonId: string, onSeek?: ((seconds: number) => void)|null, testID?: string }} props
 */
export default function LessonTechniques({ lessonId, onSeek = null, testID = 'lesson-techniques' }) {
  const { api, user } = useAuth();
  const property = useProperty();
  const router = useRouter();
  const theme = useTheme();
  const enabled = Boolean(user && api && catalogFeatureEnabled(property) && typeof api.listLessonCatalogEntries === 'function');
  const fetchPage = useCallback((continuationToken) => api.listLessonCatalogEntries(lessonId, { maxResults: 25, continuationToken }), [api, lessonId]);
  const list = useCursorList(fetchPage, { key: lessonId, enabled });
  if (!enabled || !list.items.length) return null;
  const items = list.items.slice().sort((a, b) => (a.position ?? 0) - (b.position ?? 0));
  return (
    <View style={{ gap: theme.spacing.xs }} testID={testID}>
      <AppText variant="label" accessibilityRole="header">
        {t.techniquesInLesson}
      </AppText>
      {items.map((entry) => {
        const name = entry.name || entry.slug || entry.id;
        const seconds = Number.isFinite(entry.startSeconds) && entry.startSeconds >= 0 ? entry.startSeconds : null;
        return (
          <View key={entry.linkId || entry.id} style={{ flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: theme.spacing.xs }}>
            <Button label={name} variant="ghost" onPress={() => router.push('/catalog/' + encodeURIComponent(entry.slug || entry.id))} style={{ alignSelf: 'auto' }} testID={testID + '-' + entry.id} />
            {seconds !== null && onSeek ? (
              <Button label={t.atTime(formatDuration(seconds))} accessibilityLabel={t.seekTo(formatDuration(seconds), name)} variant="ghost" onPress={() => onSeek(seconds)} style={{ alignSelf: 'auto' }} testID={testID + '-seek-' + entry.id} />
            ) : null}
          </View>
        );
      })}
      {list.hasMore && !list.loadingMore ? <Button label={t.more} variant="secondary" onPress={list.loadMore} style={{ alignSelf: 'auto' }} /> : null}
    </View>
  );
}
