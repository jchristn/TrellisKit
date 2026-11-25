import { View } from 'react-native';
import { useTheme } from '../../theme';

/**
 * A progress bar drawn with plain views (no native drawing module).
 * Props: `value` (done), `max` (total; 0 draws an empty bar), `label` (accessibility label),
 * `height` (default 8), `testID`.
 */
export default function ProgressBar({ value = 0, max = 0, label, height = 8, testID }) {
  const theme = useTheme();
  const total = Math.max(0, Number(max) || 0);
  const done = Math.min(Math.max(0, Number(value) || 0), total);
  const percent = total ? Math.round((done / total) * 100) : 0;
  return (
    <View
      accessible
      accessibilityRole="progressbar"
      accessibilityLabel={label}
      accessibilityValue={{ min: 0, max: total, now: done, text: percent + '%' }}
      testID={testID}
      style={{ height, borderRadius: height / 2, backgroundColor: theme.colors.surface, borderColor: theme.colors.border, borderWidth: 1, overflow: 'hidden' }}
    >
      <View style={{ width: percent + '%', height: '100%', backgroundColor: theme.colors.primary }} />
    </View>
  );
}
