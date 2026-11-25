import { View } from 'react-native';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';

/**
 * The "Verified" or "Self-reported" pill next to a Rank field value.
 * Props: `badge` ('verified' | 'selfReported' | null; null renders nothing), `fieldLabel` (for the
 * accessibility label), `testID`.
 */
export default function VerificationBadge({ badge, fieldLabel = '', testID }) {
  const theme = useTheme();
  if (badge !== 'verified' && badge !== 'selfReported') return null;
  const verified = badge === 'verified';
  const t = strings.ranks;
  return (
    <View
      accessible
      accessibilityLabel={verified ? t.verifiedLabel(fieldLabel) : t.selfReportedLabel(fieldLabel)}
      testID={testID}
      style={{ backgroundColor: verified ? theme.colors.accentSoft : theme.colors.surface, borderColor: theme.colors.border, borderWidth: verified ? 0 : 1, borderRadius: theme.radii.pill, paddingHorizontal: theme.spacing.sm, paddingVertical: theme.spacing.xxs }}
    >
      <AppText variant="caption" color={verified ? 'onAccentSoft' : 'textMuted'} style={{ fontWeight: '700' }}>{verified ? t.verified : t.selfReported}</AppText>
    </View>
  );
}
