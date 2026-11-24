import { useLocalSearchParams } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import IdeaDetail from '../../src/components/ideas/IdeaDetail';
import { emitContentEvent } from '../../src/content/events';
import { useTheme } from '../../src/theme';

/** `/ideas/{id}` (the `IdeaStatusChanged` notification path): one idea with its comments. */
export default function IdeaScreen() {
  const theme = useTheme();
  const { id } = useLocalSearchParams();
  const ideaId = Array.isArray(id) ? id[0] : id;
  return (
    <SafeAreaView testID="screen-idea" edges={['left', 'right', 'bottom']} style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <IdeaDetail key={ideaId} ideaId={ideaId} onIdeaChange={(idea) => emitContentEvent('ideaChanged', idea)} />
    </SafeAreaView>
  );
}
