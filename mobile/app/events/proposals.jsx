import { SafeAreaView } from 'react-native-safe-area-context';
import MyProposals from '../../src/components/events/MyProposals';
import { useTheme } from '../../src/theme';

/** My event proposals (`/events/proposals`). */
export default function Page() {
  const theme = useTheme();
  return (
    <SafeAreaView testID="screen-proposals" edges={['left', 'right']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <MyProposals />
    </SafeAreaView>
  );
}
