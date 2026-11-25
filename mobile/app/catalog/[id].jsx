import { useLocalSearchParams } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import CatalogDetail from '../../src/components/exerciseCatalog/CatalogDetail';
import { useTheme } from '../../src/theme';

export default function Screen() {
  const { id } = useLocalSearchParams();
  const value = Array.isArray(id) ? id[0] : id;
  const theme = useTheme();
  return <SafeAreaView edges={['left', 'right']} style={{ flex: 1, backgroundColor: theme.colors.background }}><CatalogDetail key={value} entryId={value} /></SafeAreaView>;
}
