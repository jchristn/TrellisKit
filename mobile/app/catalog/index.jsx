import { SafeAreaView } from 'react-native-safe-area-context';
import CatalogHome from '../../src/components/exerciseCatalog/CatalogHome';
import { useTheme } from '../../src/theme';

export default function Screen() {
  const theme = useTheme();
  return <SafeAreaView edges={['left', 'right']} style={{ flex: 1, backgroundColor: theme.colors.background }}><CatalogHome /></SafeAreaView>;
}
