import { SafeAreaView } from 'react-native-safe-area-context';
import DownloadsList from '../../../src/components/library/DownloadsList';
import { useTheme } from '../../../src/theme';

/** Downloads on this device (`/library/downloads`). */
export default function DownloadsScreen() {
  const theme = useTheme();
  return (
    <SafeAreaView testID="screen-downloads" edges={['left', 'right']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <DownloadsList />
    </SafeAreaView>
  );
}
