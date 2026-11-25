import { eligibilityDisplay, verificationBadge } from '@trelliskit/shared';
import { View } from 'react-native';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import { Swatch } from '../profile/ProfileDisplay';
import ProgressBar from './ProgressBar';
import VerificationBadge from './VerificationBadge';
import { memberName, shortDate } from './rankHooks';

/**
 * "Your rank" card for one Rank field (Training Overview and Progress, My rank).
 * Props:
 * - `rank` EffectiveRankView from GET /me/rank `fields[].rank`.
 * - `next` RankNextView or null (`fields[].next`).
 * - `showVerificationLabels` boolean (default true).
 * - `onPress` optional; adds an "open" button with the field label (for example to `/rank`).
 * - `compact` boolean: hide the requirement checks (Overview).
 * - `testID` (default 'rank-card-{fieldKey}').
 */
export function EligibilityChecks({ next, testID }) {
  const theme = useTheme();
  const t = strings.ranks;
  const display = eligibilityDisplay(next);
  if (display.state === 'highest') return <AppText color="textMuted" testID={testID}>{t.highest}</AppText>;
  if (display.state === 'noRequirements') return <AppText color="textMuted" testID={testID}>{t.noRequirements}</AppText>;
  return (
    <View style={{ gap: theme.spacing.sm }} testID={testID}>
      <AppText>{display.state === 'eligible' ? t.eligible : t.inProgress}</AppText>
      {display.checks.map((check) => {
        const text = t.checks[check.key](check.current, check.required);
        return (
          <View key={check.key} style={{ gap: theme.spacing.xxs }}>
            <AppText variant="caption">{text + ' · ' + (check.met ? t.met : t.notMet)}</AppText>
            <ProgressBar value={check.current || 0} max={check.required} label={text} testID={testID ? testID + '-' + check.key : undefined} />
          </View>
        );
      })}
    </View>
  );
}

export default function RankCard({ rank, next = null, showVerificationLabels = true, onPress = null, compact = false, testID }) {
  const theme = useTheme();
  const t = strings.ranks;
  if (!rank) return null;
  const id = testID || 'rank-card-' + rank.fieldKey;
  const heading = rank.fieldLabel || t.title;
  const badge = verificationBadge(rank, showVerificationLabels);
  return (
    <Card style={{ gap: theme.spacing.sm }} testID={id}>
      <AppText variant="label" accessibilityRole="header">{heading}</AppText>
      {rank.optionKey ? (
        <View style={{ flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: theme.spacing.sm }}>
          <Swatch color={rank.color} />
          <AppText style={{ fontWeight: '700' }} testID={id + '-label'}>{rank.rankLabel || rank.optionLabel}</AppText>
          <VerificationBadge badge={badge} fieldLabel={heading} testID={id + '-badge'} />
        </View>
      ) : <AppText color="textMuted">{t.noRank}</AppText>}
      {rank.verified && rank.awardedUtc ? <AppText variant="caption" color="textMuted">{rank.awarder ? t.awardedBy(memberName(rank.awarder), shortDate(rank.awardedUtc)) : t.awardedOn(shortDate(rank.awardedUtc))}</AppText> : null}
      {next ? <AppText>{t.next + ': ' + (next.rankLabel || next.optionLabel)}</AppText> : null}
      {!compact ? <EligibilityChecks next={next} testID={id + '-checks'} /> : null}
      {onPress ? <Button label={t.title} variant="ghost" onPress={onPress} testID={id + '-open'} /> : null}
    </Card>
  );
}
