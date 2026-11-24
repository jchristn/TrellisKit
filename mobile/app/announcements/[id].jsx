import { useLocalSearchParams } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import AnnouncementDetail from '../../src/components/announcements/AnnouncementDetail';
import { useTheme } from '../../src/theme';

/** `/announcements/{id}`: one announcement. */
export default function AnnouncementScreen() {
  const theme = useTheme();
  const { id } = useLocalSearchParams();
  const announcementId = Array.isArray(id) ? id[0] : id;
  return (
    <SafeAreaView testID="screen-announcement" edges={['left', 'right', 'bottom']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <AnnouncementDetail key={announcementId} announcementId={announcementId} />
    </SafeAreaView>
  );
}
