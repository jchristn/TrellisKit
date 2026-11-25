import { emptyVenueSchedule } from '@trelliskit/shared';
import { View } from 'react-native';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import CheckRow from '../CheckRow';
import ChipGroup from '../ChipGroup';
import TextField from '../TextField';

/** Weekly local-time opening hours and classes, submitted through the directory review queue. */
export default function VenueScheduleEditor({ value, onChange, legacyText = '', t, invalid = false }) {
  const theme = useTheme();
  const s = t.scheduleEditor;
  if (!value) return <Button label={s.enable} variant="secondary" onPress={() => onChange({ ...emptyVenueSchedule(), notes: legacyText })} />;
  const set = patch => onChange({ ...value, ...patch });
  return <View style={{ gap: theme.spacing.md }}>
    <AppText variant="heading">{s.title}</AppText>
    <AppText color="textMuted">{s.hint}</AppText>
    <TextField label={s.timeZone} value={value.timeZone || ''} onChangeText={timeZone => set({ timeZone })} placeholder="Pacific/Honolulu" autoCapitalize="none" />
    <AppText variant="caption">{s.timeZoneHint}</AppText>
    {[['openingHours', s.hours, s.addHours, 28], ['classes', s.classes, s.addClass, 100]].map(([kind, title, add, limit]) => <View key={kind} style={{ gap: theme.spacing.md }}>
      <AppText variant="label">{title}</AppText>
      {(value[kind] || []).map((row, i) => {
        const change = patch => set({ [kind]: value[kind].map((r, j) => i === j ? { ...r, ...patch } : r) });
        return <View key={i} style={{ gap: theme.spacing.sm }}>
          <ChipGroup label={s.day} options={t.scheduleDays.map((label, value) => ({ label, value }))} value={row.dayOfWeek} onToggle={dayOfWeek => change({ dayOfWeek })} />
          <TextField label={s.start} value={row.startTime} placeholder="HH:mm" maxLength={5} onChangeText={startTime => change({ startTime })} />
          <TextField label={s.end} value={row.endTime} placeholder="HH:mm" maxLength={5} onChangeText={endTime => change({ endTime })} />
          <CheckRow label={s.nextDay} value={row.endsNextDay} onValueChange={endsNextDay => change({ endsNextDay })} />
          {kind === 'classes' ? <>
            <TextField label={s.name} value={row.name || ''} maxLength={120} onChangeText={name => change({ name })} />
            <TextField label={s.instructor} value={row.instructor || ''} maxLength={120} onChangeText={instructor => change({ instructor })} />
          </> : null}
          <Button label={s.removeEntry} variant="ghost" onPress={() => set({ [kind]: value[kind].filter((_, j) => j !== i) })} />
        </View>;
      })}
      {(value[kind] || []).length < limit ? <Button label={add} variant="secondary" onPress={() => set({ [kind]: [...(value[kind] || []), { dayOfWeek: 0, startTime: '', endTime: '', endsNextDay: false, ...(kind === 'classes' ? { name: '', instructor: '' } : {}) }] })} /> : null}
    </View>)}
    <TextField label={s.notes} value={value.notes || ''} multiline maxLength={1000} onChangeText={notes => set({ notes })} />
    {invalid ? <AppText color="danger" accessibilityRole="alert">{s.invalid}</AppText> : null}
    <Button label={s.remove} variant="ghost" onPress={() => onChange(null)} />
  </View>;
}
