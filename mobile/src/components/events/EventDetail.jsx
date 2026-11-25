import { categoryOf, formatAddress, formatEventShort, formatEventTime, isLockedItem, isRecurring, joinUrlOf, lockedRequirement } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useEffect, useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeFeatureError, deviceTimeZone, imageUrl } from '../../content/phase4';
import { useResource } from '../../hooks/useResource';
import { openAppLink } from '../../links';
import { hasAddOnlyAccess, openInDeviceCalendar, saveToDeviceCalendar, storedCalendarEventId } from '../../native/calendar';
import { useProperty } from '../../property';
import { webUrl } from '../../property/property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import { AccessPromptCard } from '../AccessPrompt';
import AppText from '../AppText';
import Button from '../Button';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import MarkdownBody from '../feed/MarkdownBody';
import MediaImage from '../feed/MediaImage';
import PostDetail from '../feed/PostDetail';
import ReactionBar from '../feed/ReactionBar';
import ReportSheet, { Phase4ReportReasons } from '../feed/ReportSheet';
import { directionsUrl } from '../places/VenueDetail';
import EventCard from './EventCard';
import AttendeesSheet from './AttendeesSheet';
import RsvpControl from './RsvpControl';
import { CheckInControls } from '../ranks/CheckIn';

const t = strings.events;


function Block({ label, children, testID }) {
  const theme = useTheme();
  return (
    <View style={{ gap: theme.spacing.xs }} testID={testID}>
      <AppText variant="label">{label}</AppText>
      {children}
    </View>
  );
}

/** Other dates of a repeating event (`POST /events/{id}/occurrences/enumerate`, bounded). */
function OtherDates({ event }) {
  const router = useRouter();
  const { api } = useAuth();
  const [items, setItems] = useState(null);
  useEffect(() => {
    let active = true;
    api
      .enumerateEventOccurrences(event.id, { maxResults: 5, filters: { when: 'Upcoming' } })
      .then((page) => active && setItems(((page && page.objects) || []).filter((o) => o.id !== event.id)))
      .catch(() => active && setItems([]));
    return () => {
      active = false;
    };
  }, [api, event.id]);
  if (items === null) return null;
  return (
    <Block label={t.recurrence.otherDates} testID="event-other-dates">
      {items.length ? items.map((o) => <EventCard key={o.id} event={o} onPress={() => router.push('/events/' + o.id)} testID={'occurrence-' + o.id} />) : <AppText color="textMuted">{t.recurrence.noOtherDates}</AppText>}
    </Block>
  );
}

/**
 * The top of an event screen (docs/PHASE4_CONTRACT.md sections 2.5, 4.3 to 4.6, and resolved
 * decision 11): category, title, cancellation, the time in the event's zone with the viewer's
 * zone when it differs, the repeat rule and other dates, location with directions, the online
 * join link (only when the server returned it), capacity, the RSVP control, add to the device
 * calendar (updated in place later), hosts, who is going, details, reactions, and report.
 */
export function EventHeader({ event, onChange, testID = 'event' }) {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const [calendarNote, setCalendarNote] = useState(null);
  const [inCalendar, setInCalendar] = useState(false);
  // Add-only calendar access (iOS 17 and later) cannot update the copy added before: it shows "Added" and opens the calendar.
  const [addOnly, setAddOnly] = useState(false);
  const [reporting, setReporting] = useState(false);
  const [attendeesOpen, setAttendeesOpen] = useState(false);
  useEffect(() => {
    let active = true;
    storedCalendarEventId(event.id).then(async (id) => {
      if (!active) return;
      setInCalendar(Boolean(id));
      if (id) {
        const only = await hasAddOnlyAccess();
        if (active) setAddOnly(only);
      }
    });
    return () => {
      active = false;
    };
  }, [event.id]);

  const category = categoryOf(property, event);
  const time = formatEventTime(event, deviceTimeZone());
  const location = event.location || {};
  const address = location.address ? formatAddress(location.address, location.addressVisibility) : '';
  const canceled = event.status === 'Canceled' || Boolean(event.canceledUtc);
  const join = joinUrlOf(event);
  const viewer = event.viewer || {};
  const eventUrl = webUrl(property, '/app/events/' + event.id);

  async function addToCalendar() {
    if (inCalendar && addOnly) {
      if (!(await openInDeviceCalendar(event))) setCalendarNote(t.calendar.openFailed);
      return;
    }
    const result = await saveToDeviceCalendar(event, { url: eventUrl });
    if (result === 'added' || result === 'updated' || result === 'alreadyAdded') setInCalendar(true);
    if (result === 'alreadyAdded' || result === 'added') setAddOnly(await hasAddOnlyAccess());
    setCalendarNote(
      result === 'added' ? t.calendar.added : result === 'updated' ? t.calendar.updated : result === 'alreadyAdded' ? t.calendar.alreadyAdded : result === 'denied' ? t.calendar.denied : t.calendar.failed
    );
  }

  return (
    <View style={{ gap: theme.spacing.lg }} testID={testID + '-header'}>
      {imageUrl(event.cover) ? <MediaImage url={imageUrl(event.cover)} blurhash={event.cover.blurhash} alt={event.cover.altText || null} style={{ width: '100%', aspectRatio: 16 / 9, borderRadius: theme.radii.lg }} /> : null}
      <View style={{ gap: theme.spacing.xs }}>
        {category ? <AppText color="textMuted">{category.label}</AppText> : null}
        <AppText variant="title" accessibilityRole="header" testID={testID + '-title'}>
          {event.title}
        </AppText>
        {canceled ? (
          <AppText color="danger" style={{ fontWeight: '700' }} testID={testID + '-canceled'}>
            {event.cancelReason ? t.canceledReason(event.cancelReason) : t.canceled}
          </AppText>
        ) : null}
        <AppText testID={testID + '-time'}>{time.primary}</AppText>
        {time.secondary ? (
          <AppText color="textMuted" testID={testID + '-viewer-time'}>
            {t.yourTime(time.secondary)}
          </AppText>
        ) : null}
        {event.summary ? <AppText>{event.summary}</AppText> : null}
      </View>
      {isRecurring(event) ? (
        <Block label={t.recurrence.repeats} testID={testID + '-recurrence'}>
          {event.recurrence.summary ? <AppText>{event.recurrence.summary}</AppText> : null}
          {event.recurrence.isException ? <AppText color="textMuted">{t.recurrence.exception}</AppText> : null}
          <AppText color="textMuted">{t.recurrence.rsvpNote}</AppText>
        </Block>
      ) : null}
      {event.format !== 'Online' && (location.venue || location.name || address) ? (
        <Block label={t.location} testID={testID + '-location'}>
          {location.venue ? <Button label={location.venue.name} variant="ghost" onPress={() => router.push('/venues/' + location.venue.id)} style={{ alignSelf: 'auto' }} testID={testID + '-venue'} /> : location.name ? <AppText>{location.name}</AppText> : null}
          {address ? <AppText>{address}</AppText> : null}
          {location.venue || location.geo ? (
            <Button
              label={strings.places.directions}
              variant="ghost"
              onPress={() => {
                const url = directionsUrl({ name: location.venue ? location.venue.name : location.name, geo: location.geo || (location.venue && location.venue.geo), address: location.address, addressVisibility: location.addressVisibility });
                if (url) openAppLink(url).catch(() => {});
              }}
              style={{ alignSelf: 'auto' }}
            />
          ) : null}
        </Block>
      ) : null}
      {event.format !== 'InPerson' && event.online ? (
        <Block label={t.online} testID={testID + '-online'}>
          {event.online.platformLabel ? <AppText>{event.online.platformLabel}</AppText> : null}
          {join ? (
            <Button label={t.joinLink} accessibilityHint={t.joinHint} onPress={() => openAppLink(join).catch(() => {})} style={{ alignSelf: 'auto' }} testID={testID + '-join'} />
          ) : event.myRsvp && event.myRsvp.status === 'Going' && event.online.joinUrlAvailableUtc ? (
            <AppText color="textMuted">{t.joinLater(formatEventShort({ startUtc: event.online.joinUrlAvailableUtc, timeZone: event.timeZone }))}</AppText>
          ) : (
            <AppText color="textMuted">{t.joinGoingOnly}</AppText>
          )}
        </Block>
      ) : null}
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.md }}>
        <AppText color="textMuted">{t.going(event.goingCount || 0)}</AppText>
        {event.interestedCount ? <AppText color="textMuted">{t.interested(event.interestedCount)}</AppText> : null}
        {event.spotsLeft !== null && event.spotsLeft !== undefined ? <AppText color="textMuted">{event.spotsLeft > 0 ? t.spotsLeft(event.spotsLeft) : t.full}</AppText> : null}
      </View>
      <RsvpControl event={event} onChange={onChange} />
      {!canceled ? <CheckInControls targetType="Event" targetId={event.id} title={event.title} testID={testID + '-check-in'} /> : null}
      {!canceled ? (
        <View style={{ gap: theme.spacing.xs }}>
          <Button
            label={inCalendar ? (addOnly ? t.calendar.inCalendar : t.calendar.update) : t.calendar.add}
            accessibilityHint={inCalendar && addOnly ? t.calendar.openHint : t.calendar.addHint}
            variant="secondary"
            onPress={addToCalendar}
            style={{ alignSelf: 'auto' }}
            testID={testID + '-calendar'}
          />
          {calendarNote ? (
            <AppText color="textMuted" accessibilityRole="alert" testID={testID + '-calendar-note'}>
              {calendarNote}
            </AppText>
          ) : null}
        </View>
      ) : null}
      {isRecurring(event) ? <OtherDates event={event} /> : null}
      {event.hosts && event.hosts.length ? (
        <Block label={t.hosts}>
          {event.hosts.map((host) => (
            <Button key={host.id} label={host.displayName} variant="ghost" onPress={() => router.push('/members/' + host.id)} style={{ alignSelf: 'auto' }} />
          ))}
        </Block>
      ) : null}
      {event.attendeePreview && event.attendeePreview.objects && event.attendeePreview.objects.length ? (
        <Block label={t.attendees} testID={testID + '-attendees'}>
          <AppText>{event.attendeePreview.objects.map((u) => u.displayName).join(', ')}</AppText>
          {event.attendeePreview.totalRecords > event.attendeePreview.objects.length ? <Button label={t.seeAllAttendees(event.attendeePreview.totalRecords)} variant="ghost" onPress={() => setAttendeesOpen(true)} style={{ alignSelf: 'auto' }} testID={testID + '-all-attendees'} /> : null}
        </Block>
      ) : null}
      {event.description ? <MarkdownBody body={event.description} /> : null}
      {event.externalUrl ? <Button label={t.details} accessibilityHint={t.detailsHint} variant="ghost" onPress={() => openAppLink(event.externalUrl)} style={{ alignSelf: 'auto' }} /> : null}
      <ReactionBar target={event.commentTarget || { type: 'Event', id: event.id }} fields={event} onChange={(fields) => onChange({ ...event, ...fields })} canReact={viewer.canReact !== false} trailing={viewer.canReport ? <Button label={strings.places.report} variant="ghost" onPress={() => setReporting(true)} style={{ alignSelf: 'auto' }} testID={testID + '-report'} /> : null} testID={testID + '-reactions'} />
      {attendeesOpen ? <AttendeesSheet eventId={event.id} onClose={() => setAttendeesOpen(false)} /> : null}
      {reporting ? <ReportSheet target={{ targetType: 'Event', targetId: event.id }} author={null} reasons={Phase4ReportReasons} onClose={() => setReporting(false)} /> : null}
    </View>
  );
}

const loadEvent = (api, id) => api.getEvent(id);

/**
 * An event with its comments: the header above, then the Phase 2 comment tree against the
 * event's `commentTarget` (new comments are closed on canceled events). A locked stub shows the
 * membership prompt and loads nothing else.
 */
export default function EventDetail({ eventId, commentId = null, onEventChange, testID = 'event' }) {
  const theme = useTheme();
  const { api } = useAuth();
  const resource = useResource(() => api.getEvent(eventId), eventId);
  if (resource.status === 'loading') return <Loading label={t.loading} testID={testID + '-loading'} />;
  if (resource.status === 'gone') return <ErrorState title={t.goneTitle} message={t.goneBody} testID={testID + '-gone'} />;
  if (resource.status === 'error') return <ErrorState title={t.loadFailedTitle} message={describeFeatureError(resource.error, 'events')} onRetry={resource.reload} testID={testID + '-error'} />;
  const event = resource.data;
  if (isLockedItem(event)) {
    return (
      <View style={{ padding: theme.spacing.lg, gap: theme.spacing.md }} testID={testID + '-locked'}>
        <AppText variant="title" accessibilityRole="header">
          {event.title}
        </AppText>
        <AppText>{formatEventShort(event)}</AppText>
        <AccessPromptCard required={lockedRequirement(event)} feature="events" body={t.lockedBody} testID={testID + '-locked-prompt'} />
      </View>
    );
  }
  return (
    <PostDetail
      key={event.id}
      postId={event.id}
      initialPost={event}
      commentId={commentId}
      loadItem={loadEvent}
      onPostChange={(next) => {
        resource.setData(next);
        if (onEventChange) onEventChange(next);
      }}
      renderCard={({ item, onChange }) => <EventHeader event={item} onChange={onChange} testID={testID} />}
      canComment={(item) => item.commentsEnabled !== false && item.status !== 'Canceled' && (!item.viewer || item.viewer.canComment !== false)}
      goneText={{ title: t.goneTitle, body: t.goneBody }}
      accessFeature="events"
      testID={testID + '-detail'}
    />
  );
}
