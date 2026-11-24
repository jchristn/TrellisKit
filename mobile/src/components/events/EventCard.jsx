import { categoryOf, formatEventShort, isLockedItem, isRecurring, localityLine, rsvpState } from '@trelliskit/shared';
import { Pressable, View } from 'react-native';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import { LockBadge } from '../AccessPrompt';
import AppText from '../AppText';

const t = strings.events;

/** The caller's RSVP line for a card ("You are going."), or null. */
export function rsvpLine(event) {
  const state = rsvpState(event);
  if (state.status === 'Going') return t.rsvp.youAreGoing;
  if (state.status === 'Interested') return t.rsvp.youAreInterested;
  if (state.status === 'Waitlisted') return t.rsvp.youAreWaitlisted(state.position);
  return null;
}

/**
 * An EventSummary card (docs/PHASE4_CONTRACT.md section 2.5): category color and label, title,
 * start in the event's zone, place or Online, spots left or Full, the caller's RSVP, canceled and
 * repeating markers, and the lock for a locked stub.
 */
export default function EventCard({ event, onPress, selected = false, testID }) {
  const theme = useTheme();
  const property = useProperty();
  const id = testID || 'event-' + event.id;
  const category = categoryOf(property, event);
  const state = rsvpState(event);
  const location = event.location || {};
  const place = event.format === 'Online' ? t.online : (location.venue && location.venue.name) || location.name || localityLine(location.address);
  const when = formatEventShort(event);
  const mine = rsvpLine(event);
  const capacity = state.canceled ? t.canceled : state.full ? t.full : state.spotsLeft !== null ? t.spotsLeft(state.spotsLeft) : null;
  return (
    <Pressable
      onPress={() => onPress(event)}
      accessibilityRole="button"
      accessibilityLabel={[event.title, when, place, capacity, mine].filter(Boolean).join(', ')}
      accessibilityState={{ selected }}
      testID={id}
      style={({ pressed }) => ({
        flexDirection: 'row',
        gap: theme.spacing.md,
        padding: theme.spacing.md,
        borderRadius: theme.radii.lg,
        borderWidth: 1,
        borderColor: selected ? theme.colors.primary : theme.colors.border,
        backgroundColor: theme.colors.surface,
        opacity: pressed ? 0.85 : 1
      })}
    >
      <View style={{ width: 4, borderRadius: 2, backgroundColor: (category && category.color) || theme.colors.border }} />
      <View style={{ flex: 1, gap: 2 }}>
        {category ? (
          <AppText variant="caption" color="textMuted">
            {category.label}
          </AppText>
        ) : null}
        <AppText variant="heading" style={state.canceled ? { textDecorationLine: 'line-through' } : null}>
          {event.title}
        </AppText>
        <AppText testID={id + '-when'}>{when}</AppText>
        {place ? <AppText color="textMuted">{place}</AppText> : null}
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
          {capacity ? (
            <AppText variant="caption" color={state.canceled ? 'danger' : 'textMuted'} testID={id + '-capacity'}>
              {capacity}
            </AppText>
          ) : null}
          {isRecurring(event) && event.recurrence.summary ? (
            <AppText variant="caption" color="textMuted">
              {event.recurrence.summary}
            </AppText>
          ) : null}
          {mine ? (
            <AppText variant="caption" color="success" style={{ fontWeight: '700' }} testID={id + '-mine'}>
              {mine}
            </AppText>
          ) : null}
        </View>
        {isLockedItem(event) ? <LockBadge testID={id + '-locked'} /> : null}
      </View>
    </Pressable>
  );
}
