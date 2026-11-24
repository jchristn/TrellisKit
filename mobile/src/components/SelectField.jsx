import Ionicons from '@expo/vector-icons/Ionicons';
import { useMemo, useState } from 'react';
import { Pressable, View } from 'react-native';
import strings from '../strings';
import { useTheme } from '../theme';
import AppText from './AppText';
import Button from './Button';
import Sheet from './Sheet';
import TextField from './TextField';
import { Swatch } from './profile/ProfileDisplay';

const t = () => strings.profileFields;

/** Lists longer than this get a search box in the sheet. */
export const SELECT_SEARCH_THRESHOLD = 12;

/**
 * A dropdown: a field-styled button showing the chosen option that opens a sheet listing the
 * options (with a search box for long lists, such as countries and time zones). The mobile
 * counterpart of a web `<select>`. Options are `{ value, label, color? }`.
 * @param {object} props
 * @param {string} props.label Field label (shown above the button and as the sheet title).
 * @param {Array<{ value: string, label: string, color?: string }>} props.options
 * @param {string} props.value Selected value, or '' for none.
 * @param {(value: string) => void} props.onChange
 * @param {boolean} [props.required] When false, the sheet offers Clear.
 * @param {boolean} [props.showLabel] Show the label above the button (default true).
 * @param {string} [props.hint] Helper text under the button.
 * @param {string} [props.error] Field error under the button.
 * @param {string} [props.placeholder] Text when nothing is chosen.
 * @param {string} [props.searchPlaceholder]
 * @param {boolean} [props.disabled]
 * @param {string} [props.testID]
 */
export default function SelectField({ label, options, value, onChange, required = false, showLabel = true, hint, error, placeholder, searchPlaceholder, disabled = false, testID }) {
  const theme = useTheme();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const selected = options.find((o) => o.value === value);
  const searchable = options.length > SELECT_SEARCH_THRESHOLD;
  const shown = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return options;
    return options.filter((o) => o.label.toLowerCase().includes(q) || String(o.value).toLowerCase() === q);
  }, [options, query]);
  const close = () => {
    setOpen(false);
    setQuery('');
  };
  const choose = (next) => {
    close();
    onChange(next);
  };
  return (
    <View style={{ gap: theme.spacing.xs }}>
      {showLabel ? (
        <AppText variant="caption" style={{ fontWeight: '600' }} color="textMuted">
          {label}
        </AppText>
      ) : null}
      <Pressable
        onPress={() => setOpen(true)}
        disabled={disabled}
        accessibilityRole="button"
        accessibilityLabel={label + ', ' + (selected ? selected.label : placeholder || t().choose)}
        accessibilityState={{ disabled }}
        testID={testID}
        style={{ minHeight: theme.layout.minTouchTarget, flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm, paddingHorizontal: theme.spacing.md, borderWidth: 1, borderColor: error ? theme.colors.danger : theme.colors.border, borderRadius: theme.radii.md, backgroundColor: theme.colors.surface, opacity: disabled ? 0.6 : 1 }}
      >
        {selected && selected.color ? <Swatch color={selected.color} /> : null}
        <AppText color={selected ? 'text' : 'textMuted'} style={{ flex: 1 }}>
          {selected ? selected.label : placeholder || t().choose}
        </AppText>
        <Ionicons name="chevron-down" size={18} color={theme.colors.textMuted} />
      </Pressable>
      {hint && !error ? (
        <AppText variant="caption" color="textMuted">
          {hint}
        </AppText>
      ) : null}
      {error ? (
        <AppText variant="caption" color="danger" accessibilityRole="alert">
          {error}
        </AppText>
      ) : null}
      <Sheet visible={open} title={t().chooseTitle(label)} onClose={close} testID={testID + '-sheet'}>
        {searchable ? <TextField label={searchPlaceholder || t().searchOptions} hideLabel compact value={query} onChangeText={setQuery} autoCorrect={false} placeholder={searchPlaceholder || t().searchOptions} testID={testID + '-search'} /> : null}
        {shown.length === 0 ? (
          <AppText variant="caption" color="textMuted">
            {t().noMatches}
          </AppText>
        ) : null}
        {shown.map((o) => (
          <Pressable
            key={o.value}
            onPress={() => choose(o.value)}
            accessibilityRole="radio"
            accessibilityState={{ selected: o.value === value }}
            accessibilityLabel={o.label}
            testID={testID + '-option-' + o.value}
            style={{ minHeight: theme.layout.minTouchTarget, flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}
          >
            {o.color ? <Swatch color={o.color} /> : null}
            <AppText style={{ flex: 1 }} color={o.value === value ? 'primary' : 'text'}>
              {o.label}
            </AppText>
            {o.value === value ? <Ionicons name="checkmark" size={18} color={theme.colors.primary} /> : null}
          </Pressable>
        ))}
        {!required && value ? <Button label={t().clear} variant="ghost" onPress={() => choose('')} testID={testID + '-clear'} /> : null}
      </Sheet>
    </View>
  );
}
