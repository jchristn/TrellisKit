import { formatDuration, formatTotalDuration, isLockedItem, levelLabels, progressPercent } from '@trelliskit/shared';
import { Pressable, View } from 'react-native';
import { imageUrl } from '../../content/phase4';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import { LockBadge } from '../AccessPrompt';
import AppText from '../AppText';
import MediaImage from '../feed/MediaImage';

const t = strings.library;

/** A thin progress bar (0 to 100). */
export function ProgressBar({ percent, testID }) {
  const theme = useTheme();
  const value = Math.max(0, Math.min(100, Math.round(Number(percent) || 0)));
  return (
    <View accessible accessibilityRole="progressbar" accessibilityLabel={t.progressPercent(value)} accessibilityValue={{ min: 0, max: 100, now: value }} style={{ height: 4, borderRadius: 2, backgroundColor: theme.colors.border, overflow: 'hidden' }} testID={testID}>
      <View style={{ width: value + '%', height: 4, backgroundColor: theme.colors.primary }} />
    </View>
  );
}

function rowStyle(theme, selected) {
  return ({ pressed }) => ({
    flexDirection: 'row',
    gap: theme.spacing.md,
    padding: theme.spacing.md,
    borderRadius: theme.radii.lg,
    borderWidth: 1,
    borderColor: selected ? theme.colors.primary : theme.colors.border,
    backgroundColor: theme.colors.surface,
    opacity: pressed ? 0.85 : 1
  });
}

/** A SeriesSummary row: cover, title, levels, lesson count and length, the caller's progress, and the lock. */
export function SeriesCard({ series, onPress, selected = false, testID }) {
  const theme = useTheme();
  const property = useProperty();
  const id = testID || 'series-' + series.id;
  const levels = levelLabels(property, series.levels).join(', ');
  const progress = series.myProgress;
  const meta = [t.lessonCount(series.lessonCount || 0), series.totalDurationSeconds ? formatTotalDuration(series.totalDurationSeconds) : null, levels || null].filter(Boolean).join(' · ');
  return (
    <Pressable onPress={() => onPress(series)} accessibilityRole="button" accessibilityLabel={[series.title, meta, progress ? t.seriesProgress(progress.completedLessons || 0, series.lessonCount || 0) : null].filter(Boolean).join(', ')} accessibilityState={{ selected }} style={rowStyle(theme, selected)} testID={id}>
      {imageUrl(series.cover, 'thumb') ? <MediaImage url={imageUrl(series.cover, 'thumb')} blurhash={series.cover.blurhash} alt={null} style={{ width: 72, height: 48, borderRadius: theme.radii.md }} /> : null}
      <View style={{ flex: 1, gap: 2 }}>
        <AppText variant="heading">
          {series.title}
        </AppText>
        <AppText variant="caption" color="textMuted">
          {meta}
        </AppText>
        {progress && progress.percent ? <ProgressBar percent={progress.percent} testID={id + '-progress'} /> : null}
        {isLockedItem(series) ? <LockBadge testID={id + '-locked'} /> : null}
      </View>
    </Pressable>
  );
}

/** A LessonSummary row: poster, position and title, length, completed or percent watched, and the lock. */
export function LessonRow({ lesson, progress, onPress, selected = false, testID }) {
  const theme = useTheme();
  const id = testID || 'lesson-' + lesson.id;
  const p = progress || lesson.myProgress || null;
  const percent = progressPercent(p);
  const poster = lesson.video && lesson.video.posterThumbUrl;
  const length = lesson.durationSeconds ? formatDuration(lesson.durationSeconds) : lesson.kind === 'Article' ? t.kinds.Article : null;
  const state = p && p.completed ? t.completed : percent ? t.progressPercent(percent) : null;
  return (
    <Pressable onPress={() => onPress(lesson)} accessibilityRole="button" accessibilityLabel={[lesson.title, length, state].filter(Boolean).join(', ')} accessibilityState={{ selected }} style={rowStyle(theme, selected)} testID={id}>
      {poster ? <MediaImage url={poster} blurhash={lesson.video.blurhash} alt={null} style={{ width: 96, height: 54, borderRadius: theme.radii.md }} /> : null}
      <View style={{ flex: 1, gap: 2 }}>
        <AppText style={{ fontWeight: '600' }}>
          {lesson.position ? lesson.position + '. ' : ''}
          {lesson.title}
        </AppText>
        {length ? (
          <AppText variant="caption" color="textMuted">
            {length}
          </AppText>
        ) : null}
        {p && p.completed ? (
          <AppText variant="caption" color="success" style={{ fontWeight: '700' }} testID={id + '-completed'}>
            {t.completed}
          </AppText>
        ) : percent ? (
          <ProgressBar percent={percent} testID={id + '-progress'} />
        ) : null}
        {isLockedItem(lesson) ? <LockBadge testID={id + '-locked'} /> : null}
      </View>
    </Pressable>
  );
}
