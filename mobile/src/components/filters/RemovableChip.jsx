import Ionicons from '@expo/vector-icons/Ionicons';
import { Pressable } from 'react-native';
import { useTheme } from '../../theme';
import AppText from '../AppText';

/** A chip with a remove action (active filters, chosen labels). */
export default function RemovableChip({ text, removeLabel, onRemove, testID }) {
  const theme = useTheme();
  return (
    <Pressable
      onPress={onRemove}
      accessibilityRole="button"
      accessibilityLabel={removeLabel}
      hitSlop={4}
      testID={testID}
      style={({ pressed }) => ({ minHeight: 36, flexDirection: 'row', alignItems: 'center', gap: theme.spacing.xs, paddingHorizontal: theme.spacing.md, borderRadius: theme.radii.pill, borderWidth: 1, borderColor: theme.colors.primary, backgroundColor: theme.colors.accentSoft, opacity: pressed ? 0.8 : 1 })}
    >
      <AppText variant="caption" color="onAccentSoft">{text}</AppText>
      <Ionicons name="close" size={14} color={theme.colors.onAccentSoft} />
    </Pressable>
  );
}
