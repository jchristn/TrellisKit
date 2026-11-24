import { formatEventShort, rsvpState } from '@trelliskit/shared';
import { useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeFeatureError } from '../../content/phase4';
import strings from '../../strings';
import { useTheme } from '../../theme';
import { AccessPromptCard, useAccessPrompt } from '../AccessPrompt';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import CheckRow from '../CheckRow';

const t = strings.events;

/**
 * The RSVP control (docs/PHASE4_CONTRACT.md section 4.3): Going (or Join the waitlist on a full
 * event with a waitlist), Interested, and Not going, the caller's status and waitlist position,
 * check-in, "hide me from the attendee list", and the window. Every write returns the Event, which
 * replaces the screen's copy. Each occurrence of a repeating event has its own RSVP.
 */
export default function RsvpControl({ event, onChange, testID = 'rsvp' }) {
  const theme = useTheme();
  const { api, user } = useAuth();
  const prompt = useAccessPrompt();
  const state = rsvpState(event);
  const [hide, setHide] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  if (state.canceled || state.past) return null;
  if (!user || (event.viewer && event.viewer.canRsvp === false && event.viewer.requires)) {
    return <AccessPromptCard required={(event.viewer && event.viewer.requires) || 'SignIn'} feature="events" testID={testID + '-access'} />;
  }

  async function run(fn) {
    setBusy(true);
    setError(null);
    try {
      const next = await fn();
      if (next && next.id) onChange(next);
    } catch (e) {
      if (!prompt.handle(e)) setError(describeFeatureError(e, 'events'));
    } finally {
      setBusy(false);
    }
  }

  const going = () => run(() => api.rsvpEvent(event.id, { status: 'Going', hideFromAttendeeLists: hide }));
  const interested = () => run(() => api.rsvpEvent(event.id, { status: 'Interested', hideFromAttendeeLists: hide }));
  const cancel = () => run(() => api.cancelRsvp(event.id));
  let status = null;
  if (state.status === 'Going') status = t.rsvp.youAreGoing;
  else if (state.status === 'Interested') status = t.rsvp.youAreInterested;
  else if (state.status === 'Waitlisted') status = t.rsvp.youAreWaitlisted(state.position);

  return (
    <Card testID={testID}>
      <AppText variant="heading" accessibilityRole="header">
        {t.rsvp.title}
      </AppText>
      {status ? (
        <AppText color="success" style={{ fontWeight: '700' }} testID={testID + '-status'}>
          {status}
        </AppText>
      ) : null}
      {event.myRsvp && event.myRsvp.checkedInUtc ? <AppText color="textMuted">{t.rsvp.checkedIn}</AppText> : null}
      {state.closed ? (
        <AppText color="textMuted" testID={testID + '-closed'}>
          {t.rsvp.closed}
        </AppText>
      ) : (
        <>
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
            {state.canGoing && state.status !== 'Going' && state.status !== 'Waitlisted' ? <Button label={state.joinsWaitlist ? t.rsvp.joinWaitlist : t.rsvp.going} onPress={going} disabled={busy} style={{ alignSelf: 'auto' }} testID={testID + '-going'} /> : null}
            {state.canInterested && state.status !== 'Interested' ? <Button label={t.rsvp.interested} variant="secondary" onPress={interested} disabled={busy} style={{ alignSelf: 'auto' }} testID={testID + '-interested'} /> : null}
            {state.canCancel ? <Button label={t.rsvp.notGoing} variant="ghost" onPress={cancel} disabled={busy} style={{ alignSelf: 'auto' }} testID={testID + '-cancel'} /> : null}
          </View>
          {!state.status ? <CheckRow label={t.rsvp.hideMe} value={hide} onValueChange={setHide} testID={testID + '-hide'} /> : null}
          {event.rsvpClosesUtc ? <AppText color="textMuted">{t.rsvp.closesAt(formatEventShort({ startUtc: event.rsvpClosesUtc, timeZone: event.timeZone }))}</AppText> : null}
        </>
      )}
      {error ? (
        <AppText color="danger" accessibilityRole="alert" testID={testID + '-error'}>
          {error}
        </AppText>
      ) : null}
    </Card>
  );
}
