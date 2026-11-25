import { Pressable, ScrollView, View } from 'react-native';
import { useTheme } from '../../theme';
import AppText from '../AppText';

/**
 * A row of tabs (the Training sections, the Library segments). Each option is a 44 point `tab`
 * with its selected state; the row scrolls sideways when the labels outgrow the width (large
 * text, narrow phones).
 * @param {{ label: string, options: { value: string, label: string }[], value: string|null, onChange: (value: string) => void, testID?: string }} props
 */
export default function SectionTabs({ label, options, value, onChange, testID }) {
  const theme = useTheme();
  const { colors, spacing, radii, layout } = theme;
  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={{ gap: spacing.sm }} testID={testID}>
      <View accessibilityRole="tablist" accessibilityLabel={label} style={{ flexDirection: 'row', gap: spacing.sm }}>
        {options.map((option) => {
          const selected = option.value === value;
          return (
            <Pressable
              key={option.value}
              testID={testID ? testID + '-' + option.value : undefined}
              accessibilityRole="tab"
              accessibilityLabel={option.label}
              accessibilityState={{ selected }}
              onPress={() => (selected ? null : onChange(option.value))}
              style={({ pressed }) => ({
                minHeight: layout.minTouchTarget,
                minWidth: layout.minTouchTarget,
                justifyContent: 'center',
                paddingHorizontal: spacing.md,
                borderRadius: radii.pill,
                borderWidth: 1,
                borderColor: selected ? colors.primary : colors.border,
                backgroundColor: selected ? colors.primary : colors.surface,
                opacity: pressed ? 0.85 : 1
              })}
            >
              <AppText color={selected ? 'onPrimary' : 'text'} style={{ fontWeight: selected ? '700' : '500' }}>{option.label}</AppText>
            </Pressable>
          );
        })}
      </View>
    </ScrollView>
  );
}
