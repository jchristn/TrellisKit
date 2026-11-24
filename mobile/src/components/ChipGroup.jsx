import { Pressable, View } from 'react-native';
import { useTheme } from '../theme';
import AppText from './AppText';

/**
 * Row of selectable chips. Single select passes `value`; multi select passes `values`.
 * @param {object} props
 * @param {string} props.label Group label.
 * @param {{ value: any, label: string, color?: string|null }[]} props.options Options; a color shows as a swatch before the label.
 * @param {any} [props.value] Selected value (single select).
 * @param {any[]} [props.values] Selected values (multi select).
 * @param {(value: any) => void} props.onToggle Called with the pressed option's value.
 * @param {boolean} [props.hideLabel] Hide the visible label (a wrapper shows it); it stays the group's accessibility label.
 */
export default function ChipGroup({ label, options, value, values, onToggle, error, hideLabel = false, testID }) {
  const theme = useTheme();
  const { colors, spacing, radii, layout } = theme;
  const multi = Array.isArray(values);
  return (
    <View style={{ gap: spacing.xs }} testID={testID}>
      {hideLabel ? null : <AppText variant="label">{label}</AppText>}
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm }} accessibilityRole={multi ? undefined : 'radiogroup'} accessibilityLabel={label}>
        {options.map((option) => {
          const selected = multi ? values.includes(option.value) : value === option.value;
          return (
            <Pressable
              key={String(option.value)}
              testID={testID ? testID + '-' + option.value : undefined}
              onPress={() => onToggle(option.value)}
              accessibilityRole={multi ? 'checkbox' : 'radio'}
              accessibilityLabel={option.label}
              accessibilityState={multi ? { checked: selected } : { selected }}
              style={({ pressed }) => ({
                minHeight: layout.minTouchTarget,
                justifyContent: 'center',
                paddingHorizontal: spacing.md,
                borderRadius: radii.pill,
                borderWidth: 1,
                borderColor: selected ? colors.primary : colors.border,
                backgroundColor: selected ? colors.primary : colors.surface,
                opacity: pressed ? 0.85 : 1
              })}
            >
              <View style={{ flexDirection: 'row', alignItems: 'center', gap: spacing.xs }}>
                {option.color ? (
                  <View
                    testID={testID ? testID + '-' + option.value + '-swatch' : undefined}
                    style={{ width: 14, height: 14, borderRadius: radii.pill, backgroundColor: option.color, borderWidth: 1, borderColor: selected ? colors.onPrimary : colors.border }}
                  />
                ) : null}
                <AppText color={selected ? 'onPrimary' : 'text'}>{option.label}</AppText>
              </View>
            </Pressable>
          );
        })}
      </View>
      {error ? (
        <AppText variant="caption" color="danger" accessibilityRole="alert">
          {error}
        </AppText>
      ) : null}
    </View>
  );
}
