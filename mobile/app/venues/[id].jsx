import { useLocalSearchParams } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import VenueDetail from '../../src/components/places/VenueDetail';
import { useTheme } from '../../src/theme';

/** A venue (`/venues/{id}`; `?review={reviewId}` from a review notification shows that review first). */
export default function Screen() {
  const theme = useTheme();
  const { id, review } = useLocalSearchParams();
  const value = Array.isArray(id) ? id[0] : id;
  const focusReviewId = (Array.isArray(review) ? review[0] : review) || null;
  return (
    <SafeAreaView testID="screen-venue" edges={['left', 'right']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <VenueDetail key={value} venueId={value} focusReviewId={focusReviewId} />
    </SafeAreaView>
  );
}
