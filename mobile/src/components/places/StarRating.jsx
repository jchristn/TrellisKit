import Ionicons from '@expo/vector-icons/Ionicons';
import { formatRating } from '@trelliskit/shared';
import { Pressable, View } from 'react-native';
import { useDirectoryStrings } from '../../content/phase4';
import { useTheme } from '../../theme';
import AppText from '../AppText';

const STARS = [1, 2, 3, 4, 5];

/** Icon name of star `n` for a rating rounded to the nearest half. */
export function starIcon(n, value) {
  const rounded = Math.round((Number(value) || 0) * 2) / 2;
  if (rounded >= n) return 'star';
  if (rounded >= n - 0.5) return 'star-half';
  return 'star-outline';
}

/**
 * Read-only stars (half stars allowed), optionally with the average and the review count. The
 * group is one accessible element ("Rated 4.5 out of 5, 12 reviews"); the icons are decorative.
 */
export function StarDisplay({ value, count, size = 16, showText = true, testID }) {
  const theme = useTheme();
  const t = useDirectoryStrings();
  const average = formatRating(value, count);
  const label = average !== null && count !== undefined && count !== null ? t.rating.summary(average, count) : average !== null ? t.rating.outOfFive(average) : t.rating.outOfFive(Number(value) || 0);
  return (
    <View accessible accessibilityRole="text" accessibilityLabel={label} testID={testID} style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.xs }}>
      <View style={{ flexDirection: 'row' }} importantForAccessibility="no-hide-descendants" accessibilityElementsHidden>
        {STARS.map((n) => (
          <Ionicons key={n} name={starIcon(n, value)} size={size} color={theme.colors.warning} />
        ))}
      </View>
      {showText && average !== null ? (
        <AppText variant="caption" color="textMuted" importantForAccessibility="no" accessibilityElementsHidden>
          {count !== undefined && count !== null ? average + ' (' + count + ')' : average}
        </AppText>
      ) : null}
    </View>
  );
}

/**
 * Star rating input: a radio group of five star buttons ("1 star" to "5 stars"), each with a
 * 44 point target and `selected` on the chosen value.
 */
export function StarInput({ value, onChange, label, error, disabled = false, testID = 'star-input' }) {
  const theme = useTheme();
  const t = useDirectoryStrings();
  return (
    <View style={{ gap: theme.spacing.xs }}>
      {label ? (
        <AppText variant="label" accessibilityElementsHidden importantForAccessibility="no">
          {label}
        </AppText>
      ) : null}
      <View accessibilityRole="radiogroup" accessibilityLabel={label} style={{ flexDirection: 'row' }} testID={testID}>
        {STARS.map((n) => {
          const selected = value === n;
          return (
            <Pressable
              key={n}
              testID={testID + '-' + n}
              onPress={disabled ? undefined : () => onChange(n)}
              disabled={disabled}
              accessibilityRole="radio"
              accessibilityLabel={t.rating.stars(n)}
              accessibilityState={{ selected, checked: selected, disabled }}
              hitSlop={2}
              style={({ pressed }) => ({ minWidth: theme.layout.minTouchTarget, minHeight: theme.layout.minTouchTarget, alignItems: 'center', justifyContent: 'center', opacity: pressed ? 0.6 : 1 })}
            >
              <Ionicons name={(Number(value) || 0) >= n ? 'star' : 'star-outline'} size={30} color={theme.colors.warning} />
            </Pressable>
          );
        })}
      </View>
      {error ? (
        <AppText variant="caption" color="danger" accessibilityRole="alert" testID={testID + '-error'}>
          {error}
        </AppText>
      ) : null}
    </View>
  );
}
