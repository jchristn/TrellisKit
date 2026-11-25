import { useRouter } from 'expo-router';
import { promotersFor, rankText } from '@trelliskit/shared';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { useResource } from '../../hooks/useResource';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import Screen from '../Screen';
import Section from '../Section';
import RankCard from './RankCard';
import RankProgressPanel from './RankProgressPanel';
import { shortDate, memberName, useRanksFeature } from './rankHooks';

/**
 * The member's "My rank" screen (`/rank`): effective rank per Rank field with the next rank's
 * requirements, recent history, attendance count, and who promoted the member (edited on the website).
 */
export function PromoterList({ entries, field, testID }) {
  const theme = useTheme();
  const t = strings.ranks.promoters;
  if (!entries.length) return <AppText color="textMuted" testID={testID}>{t.empty}</AppText>;
  return (
    <View style={{ gap: theme.spacing.sm }} testID={testID}>
      {entries.map((entry) => {
        const promoter = entry.promotedBy ? memberName(entry.promotedBy) : entry.promotedByName || '';
        return (
          <AppText key={entry.id}>
            {[entry.optionLabel || rankText(field, entry.optionKey, 0), promoter ? t.promoter + ': ' + promoter : '', entry.promotedUtc ? shortDate(entry.promotedUtc) : '', entry.verified ? t.verified : t.unverified].filter(Boolean).join(' · ')}
          </AppText>
        );
      })}
    </View>
  );
}

export default function MyRank() {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const { enabled, rankFields } = useRanksFeature();
  const t = strings.ranks;
  const rank = useResource(() => (enabled ? api.getMyRank() : Promise.resolve(null)), 'me-rank-' + enabled);
  const attendance = useResource(() => (enabled ? api.enumerateMyAttendance({ maxResults: 1 }) : Promise.resolve(null)), 'me-attendance-' + enabled);
  if (!enabled) return <Screen testID="my-rank"><ErrorState title={t.notAvailable} /></Screen>;
  const data = rank.data;
  const attendanceTotal = attendance.data && Number.isInteger(attendance.data.totalRecords) ? attendance.data.totalRecords : null;
  return (
    <Screen testID="my-rank">
      <View style={{ gap: theme.spacing.lg }}>
        <AppText variant="title" accessibilityRole="header">{t.title}</AppText>
        <AppText color="textMuted">{t.summary}</AppText>
        {rank.status === 'loading' ? <Loading label={t.loading} /> : null}
        {rank.status === 'error' || rank.status === 'gone' ? <ErrorState title={t.error} onRetry={rank.reload} testID="my-rank-error" /> : null}
        {data ? (
          <>
            {(data.fields || []).map((entry) => <RankCard key={entry.rank.fieldKey} rank={entry.rank} next={entry.next} showVerificationLabels={data.showVerificationLabels !== false} />)}
            <Button label={t.curriculaLink} variant="secondary" onPress={() => router.push('/curricula')} testID="my-rank-curricula" />
            <RankProgressPanel rank={data} attendanceTotal={attendanceTotal} />
            {rankFields.filter((field) => (data.fields || []).some((f) => f.rank.fieldKey === field.key)).map((field) => (
              <Section key={field.key} title={t.promoters.title + (field.label ? ' · ' + field.label : '')}>
                <PromoterList entries={promotersFor(data, field.key)} field={field} testID={'my-rank-promoters-' + field.key} />
                <AppText variant="caption" color="textMuted">{t.promoters.editOnWeb}</AppText>
              </Section>
            ))}
          </>
        ) : null}
      </View>
    </Screen>
  );
}
