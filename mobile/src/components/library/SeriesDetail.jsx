import { FlashList } from '@shopify/flash-list';
import { formatTotalDuration, isLockedItem, levelLabels, lockedRequirement } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback, useState } from 'react';
import { RefreshControl, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeFeatureError, imageUrl } from '../../content/phase4';
import { useCursorList } from '../../hooks/useCursorList';
import { useResource } from '../../hooks/useResource';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import { AccessPromptCard, useAccessPrompt } from '../AccessPrompt';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import MarkdownBody from '../feed/MarkdownBody';
import MediaImage from '../feed/MediaImage';
import { LessonRow, ProgressBar } from './LibraryCards';
import { SaveRow } from '../SaveButton';

const t = strings.library;

/**
 * A series (docs/PHASE4_CONTRACT.md sections 2.6, 5.1, and 5.7): cover, title, levels, length,
 * the caller's progress, Follow, the description, and its lessons in order (paged). A series the
 * caller cannot open shows the membership prompt and lists only the lessons they may open
 * (sample lessons), as locked stubs otherwise.
 */
export default function SeriesDetail({ seriesId, onOpenLesson, testID = 'series' }) {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { api, user } = useAuth();
  const prompt = useAccessPrompt();
  const resource = useResource(() => api.getSeries(seriesId), seriesId);
  const [followError, setFollowError] = useState(null);
  const fetchLessons = useCallback((continuationToken) => api.enumerateSeriesLessons(seriesId, { continuationToken, maxResults: 50, ordering: 'PositionAscending' }), [api, seriesId]);
  const lessons = useCursorList(fetchLessons, { key: seriesId, enabled: resource.status === 'ready' });
  const open = onOpenLesson || ((lesson) => router.push('/lessons/' + lesson.id));
  const series = resource.data;

  async function follow() {
    setFollowError(null);
    try {
      const result = series.isFollowed ? await api.unfollowSeries(series.id) : await api.followSeries(series.id);
      resource.setData({ ...series, isFollowed: result && typeof result.isFollowed === 'boolean' ? result.isFollowed : !series.isFollowed });
    } catch (e) {
      if (!prompt.handle(e)) setFollowError(describeFeatureError(e, 'library'));
    }
  }

  if (resource.status === 'loading') return <Loading label={t.loading} testID={testID + '-loading'} />;
  if (resource.status === 'gone') return <ErrorState title={t.seriesGoneTitle} message={t.seriesGoneBody} testID={testID + '-gone'} />;
  if (resource.status === 'error') return <ErrorState title={t.loadFailedTitle} message={describeFeatureError(resource.error, 'library')} onRetry={resource.reload} testID={testID + '-error'} />;

  const locked = isLockedItem(series);
  const viewer = series.viewer || {};
  const progress = series.myProgress;
  const levels = levelLabels(property, series.levels).join(', ');
  const header = (
    <View style={{ gap: theme.spacing.md, paddingBottom: theme.spacing.md }}>
      {imageUrl(series.cover) ? <MediaImage url={imageUrl(series.cover)} blurhash={series.cover.blurhash} alt={series.cover.altText || null} style={{ width: '100%', aspectRatio: 16 / 9, borderRadius: theme.radii.lg }} /> : null}
      <SaveRow targetType="Series" targetId={series.id} title={series.title} testID={testID + '-save'}>
        <AppText variant="title" accessibilityRole="header" testID={testID + '-title'}>
          {series.title}
        </AppText>
      </SaveRow>
      <AppText color="textMuted">{[t.lessonCount(series.lessonCount || 0), series.totalDurationSeconds ? formatTotalDuration(series.totalDurationSeconds) : null, levels || null].filter(Boolean).join(' · ')}</AppText>
      {series.summary ? <AppText>{series.summary}</AppText> : null}
      {progress && !locked ? (
        <View style={{ gap: theme.spacing.xs }}>
          <AppText color="textMuted" testID={testID + '-progress'}>
            {t.seriesProgress(progress.completedLessons || 0, series.lessonCount || 0)}
          </AppText>
          <ProgressBar percent={progress.percent} />
        </View>
      ) : null}
      {locked ? <AccessPromptCard required={lockedRequirement(series)} feature="library" body={t.lockedSeries} testID={testID + '-locked'} /> : null}
      {user && !locked && viewer.canFollow !== false ? <Button label={series.isFollowed ? t.unfollow : t.follow} accessibilityHint={series.isFollowed ? t.unfollowHint : t.followHint} variant={series.isFollowed ? 'secondary' : 'primary'} onPress={follow} style={{ alignSelf: 'auto' }} testID={testID + '-follow'} /> : null}
      {followError ? (
        <AppText color="danger" accessibilityRole="alert">
          {followError}
        </AppText>
      ) : null}
      {series.description && !locked ? <MarkdownBody body={series.description} /> : null}
      {lessons.status === 'loading' ? <Loading label={t.loading} /> : null}
      {lessons.status === 'error' ? <ErrorState title={t.loadFailedTitle} message={describeFeatureError(lessons.error, 'library')} onRetry={lessons.reload} /> : null}
      {lessons.status === 'ready' && !lessons.items.length ? (
        <Card testID={testID + '-empty'}>
          <AppText>{t.seriesEmpty}</AppText>
        </Card>
      ) : null}
    </View>
  );

  return (
    <FlashList
      testID={testID}
      data={lessons.status === 'ready' ? lessons.items : []}
      keyExtractor={(lesson) => lesson.id}
      renderItem={({ item }) => (
        <View style={{ paddingBottom: theme.spacing.sm }}>
          <LessonRow lesson={item} onPress={open} />
        </View>
      )}
      ListHeaderComponent={header}
      ListFooterComponent={lessons.loadingMore ? <Loading label={t.loadMore} /> : null}
      onEndReached={lessons.loadMore}
      onEndReachedThreshold={0.5}
      refreshControl={<RefreshControl refreshing={lessons.refreshing} onRefresh={lessons.refresh} tintColor={theme.colors.primary} />}
      contentContainerStyle={{ padding: theme.spacing.lg }}
    />
  );
}
