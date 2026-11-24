import { formatAddress, formatDistance, formatRating, isLockedItem, programLabels } from '@trelliskit/shared';
import { Pressable, View } from 'react-native';
import { imageUrl } from '../../content/phase4';
import { useProperty } from '../../property';
import { useTheme } from '../../theme';
import { LockBadge } from '../AccessPrompt';
import AppText from '../AppText';
import MediaImage from '../feed/MediaImage';
import { StarDisplay } from './StarRating';

/**
 * A VenueSummary row (docs/PHASE4_CONTRACT.md section 2.2): cover, name, verified and closed
 * markers, the address as its visibility allows, distance when the list was near a position,
 * programs, the instructor count, and the average rating with the review count when rated. A locked stub shows only the name with the lock.
 */
export default function VenueCard({ venue, onPress, terms, selected = false, testID }) {
  const theme = useTheme();
  const property = useProperty();
  const locked = isLockedItem(venue);
  const id = testID || 'venue-' + venue.id;
  const address = formatAddress(venue.address, venue.addressVisibility, { includeCountry: false });
  const programs = programLabels(property, venue.programs).join(', ');
  const rated = !locked && Number(venue.ratingCount) > 0 && formatRating(venue.ratingAverage, venue.ratingCount) !== null;
  const ratingText = rated && terms.rating ? terms.rating.summary(formatRating(venue.ratingAverage, venue.ratingCount), venue.ratingCount) : null;
  const parts = [venue.name, venue.isVerified ? terms.verified : null, venue.status === 'Closed' ? terms.closed : null, ratingText, address, venue.distanceKm !== undefined && venue.distanceKm !== null ? formatDistance(venue.distanceKm) : null].filter(Boolean);
  return (
    <Pressable
      onPress={() => onPress(venue)}
      accessibilityRole="button"
      accessibilityLabel={parts.join(', ')}
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
      {imageUrl(venue.coverPhoto, 'thumb') ? <MediaImage url={imageUrl(venue.coverPhoto, 'thumb')} blurhash={venue.coverPhoto.blurhash} alt={null} style={{ width: 64, height: 64, borderRadius: theme.radii.md }} /> : null}
      <View style={{ flex: 1, gap: 2 }}>
        <AppText variant="heading">
          {venue.name}
        </AppText>
        {locked ? <LockBadge testID={id + '-locked'} /> : null}
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
          {venue.isVerified ? (
            <AppText variant="caption" color="success" style={{ fontWeight: '700' }} testID={id + '-verified'}>
              {terms.verified}
            </AppText>
          ) : null}
          {venue.status === 'Closed' ? (
            <AppText variant="caption" color="danger" style={{ fontWeight: '700' }}>
              {terms.closed}
            </AppText>
          ) : null}
          {venue.distanceKm !== undefined && venue.distanceKm !== null ? (
            <AppText variant="caption" color="textMuted" testID={id + '-distance'}>
              {terms.distance(formatDistance(venue.distanceKm))}
            </AppText>
          ) : null}
        </View>
        {rated ? <StarDisplay value={venue.ratingAverage} count={venue.ratingCount} size={14} testID={id + '-rating'} /> : null}
        {address ? <AppText color="textMuted">{address}</AppText> : null}
        {programs ? (
          <AppText variant="caption" color="textMuted">
            {programs}
          </AppText>
        ) : null}
        {venue.instructorCount ? (
          <AppText variant="caption" color="textMuted">
            {terms.instructorCount(venue.instructorCount)}
          </AppText>
        ) : null}
      </View>
    </Pressable>
  );
}
