import { formatAddress, isLockedItem, programLabels } from '@trelliskit/shared';
import { Linking, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeFeatureError } from '../../content/phase4';
import { useResource } from '../../hooks/useResource';
import { openInAppBrowser } from '../../links';
import { useProperty } from '../../property';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Loading from '../Loading';
import Sheet from '../Sheet';
import { StarDisplay } from './StarRating';
import { VenueSchedule } from './VenueSchedule';

function PreviewBody({ venueId, t, onOpen, testID }) {
  const theme = useTheme();
  const property = useProperty();
  const { api } = useAuth();
  const resource = useResource(() => api.getVenue(venueId), venueId);
  const venue = resource.data;
  if (resource.status === 'loading') {
    return (
      <View style={{ minHeight: 120 }}>
        <Loading label={t.loading} testID={testID + '-loading'} />
      </View>
    );
  }
  if (!venue) {
    return (
      <View style={{ gap: theme.spacing.md }}>
        <AppText color="danger" testID={testID + '-error'}>
          {resource.status === 'gone' ? t.venueGoneBody : describeFeatureError(resource.error, 'places', t)}
        </AppText>
        {resource.status === 'gone' ? null : <Button label={t.previewRetry} variant="secondary" onPress={() => resource.reload()} testID={testID + '-retry'} />}
      </View>
    );
  }
  const locked = isLockedItem(venue);
  const address = formatAddress(venue.address, venue.addressVisibility, { includeCountry: false });
  const programs = programLabels(property, venue.programs).join(', ');
  const policy = venue.visitorPolicy && venue.visitorPolicy !== 'Unknown' ? t.visitorPolicy[venue.visitorPolicy] || null : null;
  return (
    <View style={{ gap: theme.spacing.md }} testID={testID + '-body'}>
      <View style={{ gap: theme.spacing.xs }}>
        <AppText variant="heading" accessibilityRole="header" testID={testID + '-name'}>
          {venue.name}
        </AppText>
        {venue.isVerified ? (
          <AppText variant="caption" color="success">
            {t.verified}
          </AppText>
        ) : null}
        {venue.status === 'Closed' ? <AppText color="danger">{t.closed}</AppText> : null}
        {venue.ratingCount > 0 ? <StarDisplay value={venue.ratingAverage} count={venue.ratingCount} /> : null}
        {programs ? <AppText color="textMuted">{programs}</AppText> : null}
      </View>
      {venue.summary ? <AppText>{venue.summary}</AppText> : null}
      {address ? <AppText>{address}</AppText> : null}
      {policy ? <AppText color="textMuted">{policy}</AppText> : null}
      {!locked && venue.scheduleText ? <VenueSchedule text={venue.scheduleText} t={t} hoursOnly /> : null}
      {!locked && (venue.phone || venue.websiteUrl) ? (
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
          {venue.phone ? <Button label={venue.phone} variant="secondary" onPress={() => Linking.openURL('tel:' + venue.phone.replace(/[^+\d]/g, '')).catch(() => {})} style={{ alignSelf: 'auto' }} testID={testID + '-phone'} /> : null}
          {venue.websiteUrl ? <Button label={t.website} variant="secondary" onPress={() => openInAppBrowser(venue.websiteUrl)} style={{ alignSelf: 'auto' }} testID={testID + '-website'} /> : null}
        </View>
      ) : null}
      <Button label={t.previewOpen} onPress={() => onOpen(venue)} testID={testID + '-open'} />
    </View>
  );
}

/**
 * The preview that opens when a place is tapped on the map: a sheet with the essentials (what it
 * is, where, opening hours, visitors, phone and web page) and a button to the full page. The venue
 * is read when the sheet opens.
 * @param {object} props
 * @param {string|null} props.venueId The tapped venue, or null when closed.
 * @param {object} props.t Directory strings.
 * @param {(venue: object) => void} props.onOpen Open the full page.
 * @param {() => void} props.onClose
 */
export default function VenuePreviewSheet({ venueId, t, onOpen, onClose, testID = 'venue-preview' }) {
  return (
    <Sheet visible={Boolean(venueId)} title={t.previewTitle} onClose={onClose} testID={testID}>
      {venueId ? <PreviewBody key={venueId} venueId={venueId} t={t} onOpen={onOpen} testID={testID} /> : null}
    </Sheet>
  );
}
