import { formatAddress, isLockedItem, lockedRequirement, programLabels, venueHasPin } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import { Linking, Platform, ScrollView, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeFeatureError, imageUrl, useDirectoryStrings } from '../../content/phase4';
import { useResource } from '../../hooks/useResource';
import { openAppLink } from '../../links';
import { useProperty } from '../../property';
import { useTheme } from '../../theme';
import { AccessPromptCard } from '../AccessPrompt';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import MediaImage from '../feed/MediaImage';
import ReportSheet, { DirectoryReportReasons } from '../feed/ReportSheet';
import EventCard from '../events/EventCard';
import InstructorRow from './InstructorRow';
import { StarDisplay } from './StarRating';
import VenueMedia from './VenueMedia';
import VenueReviews from './VenueReviews';
import { VenueAbout, VenueSchedule } from './VenueSchedule';

/** A maps app URL for a venue (the maps app URL scheme on iOS, a geo URI elsewhere). */
export function directionsUrl(venue) {
  const geo = venue.geo;
  const label = encodeURIComponent(venue.name || '');
  if (geo && venueHasPin(venue)) return Platform.OS === 'ios' ? 'maps:?ll=' + geo.lat + ',' + geo.lng + '&q=' + label : 'geo:' + geo.lat + ',' + geo.lng + '?q=' + geo.lat + ',' + geo.lng + '(' + label + ')';
  const address = encodeURIComponent(formatAddress(venue.address, venue.addressVisibility));
  return address ? (Platform.OS === 'ios' ? 'maps:?q=' + address : 'geo:0,0?q=' + address) : null;
}

function Row({ label, children }) {
  const theme = useTheme();
  return (
    <View style={{ gap: theme.spacing.xs }}>
      <AppText variant="label">{label}</AppText>
      {children}
    </View>
  );
}

/**
 * A venue (docs/PHASE4_CONTRACT.md section 2.2): photos, name, verified, the address as its
 * visibility allows with directions, contact, schedule, programs, visitor policy, links, the
 * bounded instructor preview (paged in place), and upcoming events at the venue. The `viewer`
 * object drives Suggest an edit, Report closed, and Report. The Media section (staff photos, then
 * member photos and videos) and the Reviews section (sections 3.12 and 3.13) follow the events.
 * `focusReviewId` (a notification's `?review=`) shows that review first. A locked stub shows the
 * membership prompt and nothing else.
 */
export default function VenueDetail({ venueId, focusReviewId = null, testID = 'venue' }) {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { api } = useAuth();
  const t = useDirectoryStrings();
  const resource = useResource(() => api.getVenue(venueId), venueId);
  const [moreInstructors, setMoreInstructors] = useState(null);
  const [instructorToken, setInstructorToken] = useState(undefined);
  const [events, setEvents] = useState(null);
  const [reporting, setReporting] = useState(false);
  const venue = resource.data;

  const loadInstructors = useCallback(async () => {
    const page = await api.enumerateVenueInstructors(venue.id, { maxResults: 25, continuationToken: instructorToken || undefined });
    setMoreInstructors((current) => (current || []).concat((page && page.objects) || []));
    setInstructorToken(page && !page.endOfResults && page.continuationToken ? page.continuationToken : null);
  }, [api, venue, instructorToken]);

  const eventsFor = venue && !isLockedItem(venue) ? venue.id : null;
  useEffect(() => {
    if (!eventsFor) return undefined;
    let active = true;
    api
      .enumerateEvents({ maxResults: 5, filters: { venueId: eventsFor, when: 'Upcoming' } })
      .then((page) => active && setEvents((page && page.objects) || []))
      .catch(() => active && setEvents([]));
    return () => {
      active = false;
    };
  }, [api, eventsFor]);

  if (resource.status === 'loading') return <Loading label={t.loading} testID={testID + '-loading'} />;
  if (resource.status === 'gone') return <ErrorState title={t.venueGoneTitle} message={t.venueGoneBody} testID={testID + '-gone'} />;
  if (resource.status === 'error') return <ErrorState title={t.loadFailedTitle} message={describeFeatureError(resource.error, 'places', t)} onRetry={resource.reload} testID={testID + '-error'} />;

  if (isLockedItem(venue)) {
    return (
      <View style={{ padding: theme.spacing.lg, gap: theme.spacing.md }} testID={testID + '-locked'}>
        <AppText variant="title" accessibilityRole="header">
          {venue.name}
        </AppText>
        <AccessPromptCard required={lockedRequirement(venue)} feature="directory" body={t.lockedVenue} testID={testID + '-locked-prompt'} />
      </View>
    );
  }

  const viewer = venue.viewer || {};
  const address = formatAddress(venue.address, venue.addressVisibility);
  const directions = directionsUrl(venue);
  const programs = programLabels(property, venue.programs);
  const preview = (venue.instructors && venue.instructors.objects) || [];
  const instructorTotal = (venue.instructors && venue.instructors.totalRecords) || preview.length;
  const instructors = moreInstructors || preview;
  const canLoadMoreInstructors = moreInstructors ? Boolean(instructorToken) : instructorTotal > preview.length;

  return (
    <ScrollView contentContainerStyle={{ padding: theme.spacing.lg, gap: theme.spacing.lg }} testID={testID}>
      {venue.photos && venue.photos.length && imageUrl(venue.photos[0]) ? <MediaImage url={imageUrl(venue.photos[0])} blurhash={venue.photos[0].blurhash} alt={venue.photos[0].altText || null} style={{ width: '100%', aspectRatio: 16 / 9, borderRadius: theme.radii.lg }} /> : null}
      <View style={{ gap: theme.spacing.xs }}>
        <AppText variant="title" accessibilityRole="header" testID={testID + '-name'}>
          {venue.name}
        </AppText>
        {venue.isVerified ? (
          <AppText color="success" style={{ fontWeight: '700' }} testID={testID + '-verified'}>
            {t.verified}
          </AppText>
        ) : null}
        {venue.status === 'Closed' ? <AppText color="danger">{t.closed}</AppText> : null}
        {venue.ratingCount > 0 ? <StarDisplay value={venue.ratingAverage} count={venue.ratingCount} testID={testID + '-rating'} /> : null}
        {venue.affiliation ? <AppText color="textMuted">{venue.affiliation}</AppText> : null}
        {venue.summary ? <AppText>{venue.summary}</AppText> : null}
      </View>
      {address ? (
        <Row label={t.address}>
          <AppText testID={testID + '-address'}>{address}</AppText>
          {venue.addressVisibility === 'Approximate' ? <AppText color="textMuted">{t.approximate}</AppText> : null}
          {venue.addressVisibility === 'LocalityOnly' ? <AppText color="textMuted">{t.localityOnly}</AppText> : null}
          {directions ? <Button label={t.directions} accessibilityHint={t.directionsHint} variant="secondary" onPress={() => openAppLink(directions).catch(() => {})} style={{ alignSelf: 'auto' }} testID={testID + '-directions'} /> : null}
        </Row>
      ) : null}
      {venue.phone || venue.email || venue.websiteUrl ? (
        <Row label={t.contact}>
          {venue.phone ? <Button label={venue.phone} variant="ghost" onPress={() => Linking.openURL('tel:' + venue.phone.replace(/[^+\d]/g, '')).catch(() => {})} style={{ alignSelf: 'auto' }} testID={testID + '-phone'} /> : null}
          {venue.email ? <Button label={venue.email} variant="ghost" onPress={() => Linking.openURL('mailto:' + venue.email).catch(() => {})} style={{ alignSelf: 'auto' }} testID={testID + '-email'} /> : null}
          {venue.websiteUrl ? <Button label={t.website} variant="ghost" onPress={() => openAppLink(venue.websiteUrl)} style={{ alignSelf: 'auto' }} testID={testID + '-website'} /> : null}
        </Row>
      ) : null}
      {(venue.schedule || venue.scheduleText) ? <VenueSchedule schedule={venue.schedule} text={venue.scheduleText} t={t} testID={testID + '-schedule'} /> : null}
      {programs.length ? (
        <Row label={t.programs}>
          <AppText testID={testID + '-programs'}>{programs.join(', ')}</AppText>
        </Row>
      ) : null}
      {venue.visitorPolicy ? (
        <Row label={t.visitorPolicyLabel}>
          <AppText>{t.visitorPolicy[venue.visitorPolicy] || venue.visitorPolicy}</AppText>
          {venue.visitorPolicyNote ? <AppText color="textMuted">{venue.visitorPolicyNote}</AppText> : null}
        </Row>
      ) : null}
      {venue.description ? <VenueAbout text={venue.description} t={t} testID={testID + '-about'} /> : null}
      {venue.socialLinks && venue.socialLinks.length ? (
        <Row label={t.links}>
          {venue.socialLinks.map((link) => (
            <Button key={link.url} label={link.kind} variant="ghost" onPress={() => openAppLink(link.url)} style={{ alignSelf: 'auto' }} />
          ))}
        </Row>
      ) : null}
      <Row label={t.instructors}>
        {instructors.length ? instructors.map((instructor) => <InstructorRow key={instructor.id} instructor={instructor} onPress={() => router.push('/instructors/' + instructor.id)} />) : <AppText color="textMuted">{t.noInstructors}</AppText>}
        {canLoadMoreInstructors ? <Button label={moreInstructors ? t.loadMore : t.seeAllInstructors(instructorTotal)} variant="ghost" onPress={() => loadInstructors().catch(() => {})} style={{ alignSelf: 'auto' }} testID={testID + '-more-instructors'} /> : null}
      </Row>
      {events && events.length ? (
        <Row label={t.events}>
          {events.map((event) => (
            <EventCard key={event.id} event={event} onPress={() => router.push('/events/' + event.id)} />
          ))}
        </Row>
      ) : null}
      <VenueMedia venue={venue} testID={testID + '-media'} />
      {venue.reviews ? <VenueReviews venue={venue} focusReviewId={focusReviewId} onChanged={() => resource.reload({ quiet: true })} testID={testID + '-reviews'} /> : null}
      <Card>
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
          {viewer.canSuggestEdit ? <Button label={t.suggestEdit} variant="secondary" onPress={() => router.push({ pathname: '/directory/submit', params: { type: 'EditVenue', targetId: venue.id } })} style={{ alignSelf: 'auto' }} testID={testID + '-suggest'} /> : null}
          {viewer.canReportClosed && venue.status !== 'Closed' ? <Button label={t.reportClosed} variant="ghost" onPress={() => router.push({ pathname: '/directory/submit', params: { type: 'ReportClosed', targetId: venue.id } })} style={{ alignSelf: 'auto' }} testID={testID + '-report-closed'} /> : null}
          {viewer.canReport ? <Button label={t.report} variant="ghost" onPress={() => setReporting(true)} style={{ alignSelf: 'auto' }} testID={testID + '-report'} /> : null}
        </View>
      </Card>
      {reporting ? <ReportSheet reasons={DirectoryReportReasons} target={{ targetType: 'Venue', targetId: venue.id }} author={null} onClose={() => setReporting(false)} /> : null}
    </ScrollView>
  );
}
