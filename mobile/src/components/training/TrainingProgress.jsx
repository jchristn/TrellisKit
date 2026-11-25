import { ranksFeatureEnabled } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { ScrollView } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { featureOn } from '../../content/phase4';
import { useResource } from '../../hooks/useResource';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Card from '../Card';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import Section from '../Section';
import { RankProgressPanel } from '../ranks';

const t = strings.training.progress;

const total = (page) => (page && Number.isInteger(page.totalRecords) ? page.totalRecords : null);

/**
 * Training Progress: completed lessons and, while ranks are on, rank history, attendance count,
 * and curriculum completion (the ranks components). Each part loads on its own, so one failing
 * request leaves the others in place.
 */
export default function TrainingProgress() {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { api } = useAuth();
  const library = featureOn(property, 'library');
  const ranks = ranksFeatureEnabled(property);
  const completed = useResource(() => (library ? api.enumerateMyLessonProgress({ maxResults: 1, filters: { completed: true } }) : Promise.resolve(null)), 'progress-completed-' + library);
  const rank = useResource(() => (ranks ? api.getMyRank().catch(() => null) : Promise.resolve(null)), 'progress-rank-' + ranks);
  const attendance = useResource(() => (ranks ? api.enumerateMyAttendance({ maxResults: 1 }).catch(() => null) : Promise.resolve(null)), 'progress-attendance-' + ranks);
  const curricula = useResource(() => (ranks ? api.enumerateCurricula({ maxResults: 25 }).catch(() => null) : Promise.resolve(null)), 'progress-curricula-' + ranks);
  const count = total(completed.data);
  return (
    <ScrollView contentContainerStyle={{ padding: theme.spacing.lg, gap: theme.spacing.lg }} testID="training-progress">
      {library ? (
        <Card>
          <Section title={t.completedLessons} testID="training-progress-completed">
            {completed.status === 'loading' ? <Loading label={t.title} /> : null}
            {completed.status === 'error' ? <ErrorState title={t.loadFailed} onRetry={completed.reload} /> : null}
            {completed.status === 'ready' ? <AppText testID="training-progress-completed-count">{count ? t.completedCount(count) : t.completedEmpty}</AppText> : null}
          </Section>
        </Card>
      ) : null}
      {ranks ? (
        <Card>
          {rank.status === 'loading' ? <Loading label={t.title} /> : (
            <RankProgressPanel
              rank={rank.data || null}
              attendanceTotal={total(attendance.data)}
              curricula={curricula.data && Array.isArray(curricula.data.objects) ? curricula.data.objects.filter(Boolean) : null}
              onOpenCurriculum={(id) => router.push('/training/curricula/' + encodeURIComponent(id))}
              testID="training-rank-progress"
            />
          )}
        </Card>
      ) : null}
    </ScrollView>
  );
}
