import Button from './Button';
import strings from '../strings';
import { setColorSchemePreference, useTheme } from '../theme';

/** A page-level switch using the app's persisted appearance preference. */
export default function ThemeToggle({ testID = 'theme-toggle' }) {
  const theme = useTheme();
  return <Button label={theme.dark ? strings.appearance.useLight : strings.appearance.useDark} variant="ghost" onPress={() => setColorSchemePreference(theme.dark ? 'light' : 'dark')} testID={testID} />;
}
