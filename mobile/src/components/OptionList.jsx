import { Pressable, View } from 'react-native';
import { useTheme } from '../theme';
import AppText from './AppText';

/**
 * A list of options, one per row, with a check box (several may be chosen) or a radio mark (one
 * may be chosen) before each label. Used where the choices are answers to a question (profile
 * fields), which read better as a list than as a cloud of chips. Single select passes `value`;
 * multi select passes `values`.
 * @param {object} props
 * @param {string} props.label Group label.
 * @param {{ value: any, label: string, color?: string|null }[]} props.options Options; a color shows as a swatch after the mark.
 * @param {any} [props.value] Selected value (single select).
 * @param {any[]} [props.values] Selected values (multi select).
 * @param {(value: any) => void} props.onToggle Called with the pressed option's value.
 * @param {boolean} [props.hideLabel] Hide the visible label (a wrapper shows it); it stays the group's accessibility label.
 */
export default function OptionList({ label, options, value, values, onToggle, error, hideLabel = false, testID }) {
  const theme = useTheme();
  const { colors, spacing, radii, layout } = theme;
  const multi = Array.isArray(values);
  return (
    <View style={{ gap: spacing.xs }} testID={testID}>
      {hideLabel ? null : <AppText variant="label">{label}</AppText>}
      <View style={{ borderWidth: 1, borderColor: colors.border, borderRadius: radii.md, backgroundColor: colors.surface, overflow: 'hidden' }} accessibilityRole={multi ? undefined : 'radiogroup'} accessibilityLabel={label}>
        {options.map((option, index) => {
          const selected = multi ? values.includes(option.value) : value === option.value;
          return (
            <Pressable
              key={String(option.value)}
              testID={testID ? testID + '-' + option.value : undefined}
              onPress={() => onToggle(option.value)}
              accessibilityRole={multi ? 'checkbox' : 'radio'}
              accessibilityLabel={option.label}
              accessibilityState={{ checked: selected }}
              style={({ pressed }) => ({
                flexDirection: 'row',
                alignItems: 'center',
                gap: spacing.md,
                minHeight: layout.minTouchTarget,
                paddingHorizontal: spacing.md,
                paddingVertical: spacing.sm,
                borderTopWidth: index ? 1 : 0,
                borderTopColor: colors.border,
                opacity: pressed ? 0.7 : 1
              })}
            >
              <View
                style={{
                  width: 22,
                  height: 22,
                  borderRadius: multi ? radii.sm : 11,
                  borderWidth: 2,
                  borderColor: selected ? colors.primary : colors.textMuted,
                  backgroundColor: selected && multi ? colors.primary : 'transparent',
                  alignItems: 'center',
                  justifyContent: 'center'
                }}
              >
                {selected && multi ? <AppText style={{ color: colors.onPrimary, fontSize: 14, lineHeight: 16, fontWeight: '700' }}>{'\u2713'}</AppText> : null}
                {selected && !multi ? <View style={{ width: 10, height: 10, borderRadius: 5, backgroundColor: colors.primary }} /> : null}
              </View>
              {option.color ? <View testID={testID ? testID + '-' + option.value + '-swatch' : undefined} style={{ width: 14, height: 14, borderRadius: 7, backgroundColor: option.color, borderWidth: 1, borderColor: colors.border }} /> : null}
              <AppText style={{ flex: 1 }}>{option.label}</AppText>
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
