import { useMemo } from 'react';
import { useWindowDimensions, View } from 'react-native';
import strings from '../strings';
import { useTheme } from '../theme';
import AppText from './AppText';
import SelectField from './SelectField';

/**
 * Parse month, day, and year text into a calendar date.
 * @param {{ month: string, day: string, year: string }} parts
 * @returns {{ date: Date, iso: string }|null} Local date and its YYYY-MM-DD form, or null when invalid.
 */
export function parseDateParts({ month, day, year }) {
  const m = Number(month);
  const d = Number(day);
  const y = Number(year);
  if (!/^\d{1,2}$/.test(String(month)) || !/^\d{1,2}$/.test(String(day)) || !/^\d{4}$/.test(String(year))) return null;
  if (y < 1900 || m < 1 || m > 12 || d < 1 || d > 31) return null;
  const date = new Date(y, m - 1, d);
  if (date.getFullYear() !== y || date.getMonth() !== m - 1 || date.getDate() !== d) return null;
  const iso = String(y) + '-' + String(m).padStart(2, '0') + '-' + String(d).padStart(2, '0');
  return { date, iso };
}

const MONTH_NAMES = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const MONTHS = MONTH_NAMES.map((name, i) => ({ value: String(i + 1), label: name }));
const DAYS = Array.from({ length: 31 }, (_, i) => ({ value: String(i + 1), label: String(i + 1) }));
/** Years from this year back to 1900, newest first. */
function yearOptions() {
  const now = new Date().getFullYear();
  return Array.from({ length: now - 1900 + 1 }, (_, i) => ({ value: String(now - i), label: String(now - i) }));
}

/**
 * Date entry (for example date of birth) as three pickers: month, day, and year. Each opens a
 * sheet; the year list is searchable. The value stays `{ month, day, year }` text (month 1 to 12),
 * so parseDateParts validates it as before.
 */
export default function DateField({ label, hint, value, onChange, error, testID }) {
  const theme = useTheme();
  // Picker widths follow the text size, so at the largest sizes they wrap onto their own lines
  // instead of clipping their contents.
  const { fontScale } = useWindowDimensions();
  const basis = (n) => ({ flexGrow: n, flexBasis: Math.round(88 * n * Math.max(1, fontScale || 1)) });
  const years = useMemo(() => yearOptions(), []);
  const set = (key) => (next) => onChange({ ...value, [key]: next });
  const part = (key) => String(Number(value[key]) || '') || '';
  return (
    <View style={{ gap: theme.spacing.xs }}>
      <AppText variant="label">{label}</AppText>
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
        <View style={basis(1.6)} testID={testID + '-month-wrap'}>
          <SelectField label={label + ', ' + strings.dateField.month} showLabel={false} placeholder={strings.dateField.month} options={MONTHS} value={part('month')} onChange={set('month')} required testID={testID + '-month'} />
        </View>
        <View style={basis(1)} testID={testID + '-day-wrap'}>
          <SelectField label={label + ', ' + strings.dateField.day} showLabel={false} placeholder={strings.dateField.day} options={DAYS} value={part('day')} onChange={set('day')} required testID={testID + '-day'} />
        </View>
        <View style={basis(1.2)} testID={testID + '-year-wrap'}>
          <SelectField label={label + ', ' + strings.dateField.year} showLabel={false} placeholder={strings.dateField.year} options={years} value={String(value.year || '')} onChange={set('year')} required searchPlaceholder={strings.dateField.year} testID={testID + '-year'} />
        </View>
      </View>
      {hint && !error ? (
        <AppText variant="caption" color="textMuted">
          {hint}
        </AppText>
      ) : null}
      {error ? (
        <AppText variant="caption" color="danger" accessibilityRole="alert" testID={testID + '-error'}>
          {error}
        </AppText>
      ) : null}
    </View>
  );
}
