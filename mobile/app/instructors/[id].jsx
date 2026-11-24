import { useLocalSearchParams } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import InstructorDetail from '../../src/components/places/InstructorDetail';
import { useTheme } from '../../src/theme';

/** An instructor (`/instructors/{id}`). */
export default function Screen() {
  const theme = useTheme();
  const { id } = useLocalSearchParams();
  const value = Array.isArray(id) ? id[0] : id;
  return (
    <SafeAreaView testID="screen-instructor" edges={['left', 'right']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <InstructorDetail key={value} instructorId={value} />
    </SafeAreaView>
  );
}
