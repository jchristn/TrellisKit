import { SafeAreaView } from 'react-native-safe-area-context';
import MySubmissions from '../../src/components/places/MySubmissions';
import { useTheme } from '../../src/theme';

/** My directory submissions (`/directory/submissions`, the `SubmissionDecision` path). */
export default function Page() {
  const theme = useTheme();
  return (
    <SafeAreaView testID="screen-submissions" edges={['left', 'right']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <MySubmissions />
    </SafeAreaView>
  );
}
