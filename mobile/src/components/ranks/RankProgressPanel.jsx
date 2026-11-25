import { View } from 'react-native';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Section from '../Section';
import ProgressBar from './ProgressBar';
import { memberName, shortDate } from './rankHooks';

/**
 * Rank progress for the Training Progress section: rank history, attendance count, and
 * curriculum completion bars.
 * Props: `rank` (GET /me/rank response or null), `attendanceTotal` (number or null to hide),
 * `curricula` (CurriculumView[] or null to hide), `onOpenCurriculum(id)` optional, `testID`.
 */
export function RankHistoryList({ history = [], total = null, testID }) {
  const theme = useTheme();
  const t = strings.ranks;
  if (!history.length) return <AppText color="textMuted" testID={testID}>{t.historyEmpty}</AppText>;
  return (
    <View style={{ gap: theme.spacing.sm }} testID={testID}>
      {history.map((row) => (
        <View key={row.id} style={{ gap: theme.spacing.xxs }}>
          <AppText>{t.historyRow(t.actions[row.action] || row.action, row.rankLabel || row.optionLabel || row.optionKey, shortDate(row.createdUtc))}</AppText>
          {row.actor ? <AppText variant="caption" color="textMuted">{memberName(row.actor)}</AppText> : null}
          {row.note || row.reason ? <AppText variant="caption" color="textMuted">{row.note || row.reason}</AppText> : null}
        </View>
      ))}
      {total !== null && total > history.length ? <AppText variant="caption" color="textMuted">{t.historyMore(history.length, total)}</AppText> : null}
    </View>
  );
}

export default function RankProgressPanel({ rank = null, attendanceTotal = null, curricula = null, onOpenCurriculum = null, testID = 'rank-progress' }) {
  const theme = useTheme();
  const t = strings.ranks;
  return (
    <View style={{ gap: theme.spacing.lg }} testID={testID}>
      {rank ? <Section title={t.historyTitle}><RankHistoryList history={rank.history || []} total={rank.historyTotal ?? null} testID={testID + '-history'} /></Section> : null}
      {attendanceTotal !== null && attendanceTotal !== undefined ? <Section title={t.attendanceTitle}><AppText testID={testID + '-attendance'}>{t.attendanceCount(attendanceTotal)}</AppText></Section> : null}
      {Array.isArray(curricula) ? (
        <Section title={t.curriculaTitle}>
          {!curricula.length ? <AppText color="textMuted">{t.curriculaEmpty}</AppText> : curricula.map((c) => {
            const done = (c.progress && c.progress.completed) || 0;
            const total = (c.progress && c.progress.total) ?? c.itemCount ?? 0;
            const text = t.curriculumProgress(done, total);
            return (
              <View key={c.id} style={{ gap: theme.spacing.xxs }}>
                <AppText onPress={onOpenCurriculum ? () => onOpenCurriculum(c.id) : undefined} accessibilityRole={onOpenCurriculum ? 'link' : undefined} color={onOpenCurriculum ? 'primary' : 'text'}>{c.name}</AppText>
                <AppText variant="caption" color="textMuted">{text}</AppText>
                <ProgressBar value={done} max={total} label={c.name + ': ' + text} />
              </View>
            );
          })}
        </Section>
      ) : null}
    </View>
  );
}
