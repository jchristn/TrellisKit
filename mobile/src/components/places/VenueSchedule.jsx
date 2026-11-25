import { formatClockTime, venueScheduleDisplay, parseVenueDescription } from '@trelliskit/shared';
import { View } from 'react-native';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import MarkdownBody from '../feed/MarkdownBody';

/** The label of a row of opening hours: one day, a range of days, or every day. */
export function scheduleDayLabel(t, row) {
  if (row.from === 0 && row.to === 6) return t.scheduleEveryDay;
  return row.from === row.to ? t.scheduleDays[row.from] : t.scheduleDayRange(t.scheduleDays[row.from], t.scheduleDays[row.to]);
}

/** The hours of a row: a time range, or "Open 24 hours". */
export function scheduleTimeLabel(t, row) {
  return row.allDay ? t.scheduleOpen24 : t.scheduleTimeRange(formatClockTime(row.open), formatClockTime(row.close)) + (row.endsNextDay ? t.scheduleEditor.nextDaySuffix : '');
}

function Section({ label, children }) {
  const theme = useTheme();
  return (
    <View style={{ gap: theme.spacing.xs }}>
      <AppText variant="label">{label}</AppText>
      {children}
    </View>
  );
}

/**
 * A venue's schedule: opening hours as a small table (days on the left, hours on the right, with
 * days that share hours on one row), then the weekly sessions and the schedule notes as short paragraphs. Schedule
 * text in any other shape is shown as paragraphs.
 * @param {object} props
 * @param {string} props.text The venue's `scheduleText`.
 * @param {object} props.t Directory strings.
 * @param {boolean} [props.hoursOnly] Show only the hours table (the map preview).
 * @param {(entry: object, index: number) => React.ReactNode} [props.sessionAction] Extra controls for a
 *   session row; `index` is the zero-based position in `schedule.sessions` (attendance target `{venueId}:{index}`).
 */
export function VenueSchedule({ text, schedule, t, hoursOnly = false, testID, sessionAction = null }) {
  const theme = useTheme();
  const parsed = venueScheduleDisplay(schedule, text);
  const hours = parsed.hours;
  const notes = hoursOnly ? [] : parsed.notes;
  if (!hours.length && !notes.length && (hoursOnly || !parsed.sessions.length)) return null;
  return (
    <View style={{ gap: theme.spacing.md }} testID={testID}>
      {parsed.timeZone ? <AppText color="textMuted">{parsed.timeZone}</AppText> : null}
      {hours.length ? (
        <Section label={t.hours}>
          <View style={{ borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radii.md, backgroundColor: theme.colors.surface }}>
            {hours.map((row, index) => (
              <View
                key={index}
                accessible
                accessibilityLabel={scheduleDayLabel(t, row) + ', ' + scheduleTimeLabel(t, row)}
                style={{ flexDirection: 'row', justifyContent: 'space-between', gap: theme.spacing.md, paddingHorizontal: theme.spacing.md, paddingVertical: theme.spacing.sm, borderTopWidth: index ? 1 : 0, borderTopColor: theme.colors.border }}
              >
                <AppText style={{ fontWeight: '600' }}>{scheduleDayLabel(t, row)}</AppText>
                <AppText style={{ flexShrink: 1, textAlign: 'right' }}>{scheduleTimeLabel(t, row)}</AppText>
              </View>
            ))}
          </View>
        </Section>
      ) : null}
      {!hoursOnly && parsed.sessions.length ? <Section label={t.scheduleEditor.sessions}>
        {parsed.sessions.map((entry, i) => <View key={i} style={{ gap: theme.spacing.xs }}>
          <AppText>
            {entry.name}{' · '}{t.scheduleDays[entry.dayOfWeek]}{' · '}
            {scheduleTimeLabel(t, { open: entry.startTime, close: entry.endTime, endsNextDay: entry.endsNextDay })}
            {entry.instructor ? ' · ' + entry.instructor : ''}
          </AppText>
          {sessionAction && schedule && Array.isArray(schedule.sessions) ? sessionAction(entry, schedule.sessions.indexOf(entry)) : null}
        </View>)}
      </Section> : null}
      {notes.length ? (
        <Section label={t.schedule}>
          {notes.map((note, index) => (
            <AppText key={index}>{note}</AppText>
          ))}
        </Section>
      ) : null}
    </View>
  );
}

/**
 * A venue's description: pricing as its own short section, amenities as a wrapping row of small
 * tags, and any other text as Markdown.
 * @param {object} props
 * @param {string} props.text The venue's `description`.
 * @param {object} props.t Directory strings.
 */
export function VenueAbout({ text, schedule, t, testID }) {
  const theme = useTheme();
  const { pricing, amenities, body } = parseVenueDescription(text);
  if (!pricing && !amenities.length && !body) return null;
  return (
    <View style={{ gap: theme.spacing.md }} testID={testID}>
      {body ? <MarkdownBody body={body} headings={false} /> : null}
      {pricing ? (
        <Section label={t.pricing}>
          <AppText>{pricing}</AppText>
        </Section>
      ) : null}
      {amenities.length ? (
        <Section label={t.amenities}>
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.xs }}>
            {amenities.map((item, index) => (
              <View key={index} style={{ borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radii.md, backgroundColor: theme.colors.surface, paddingHorizontal: theme.spacing.sm, paddingVertical: theme.spacing.xs }}>
                <AppText variant="caption">{item}</AppText>
              </View>
            ))}
          </View>
        </Section>
      ) : null}
    </View>
  );
}
