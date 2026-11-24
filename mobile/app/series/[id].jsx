import { useLocalSearchParams } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import SeriesDetail from '../../src/components/library/SeriesDetail';
import { useTheme } from '../../src/theme';

/** A series (`/series/{id}`). */
export default function Screen() {
  const theme = useTheme();
  const { id } = useLocalSearchParams();
  const value = Array.isArray(id) ? id[0] : id;
  return (
    <SafeAreaView testID="screen-series" edges={['left', 'right']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <SeriesDetail key={value} seriesId={value} />
    </SafeAreaView>
  );
}
