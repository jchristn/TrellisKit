import { useLocalSearchParams } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import LessonDetail from '../../src/components/library/LessonDetail';
import { useTheme } from '../../src/theme';

/** A lesson with its player (`/lessons/{id}`). */
export default function Screen() {
  const theme = useTheme();
  const { id, comment } = useLocalSearchParams();
  const value = Array.isArray(id) ? id[0] : id;
  const commentId = (Array.isArray(comment) ? comment[0] : comment) || null;
  return (
    <SafeAreaView testID="screen-lesson" edges={['left', 'right']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <LessonDetail key={value} lessonId={value} commentId={commentId} />
    </SafeAreaView>
  );
}
