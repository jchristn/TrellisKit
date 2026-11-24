import { Text } from 'react-native';
import { useTheme } from '../theme';

/**
 * Themed text. Variants map to the type scale; color is a theme color key.
 * Text always scales with the system font size setting.
 */
export default function AppText({ variant = 'body', color = 'text', style, children, ...rest }) {
  const theme = useTheme();
  return (
    <Text style={[theme.typography[variant], { color: theme.colors[color] || color }, style]} {...rest}>
      {children}
    </Text>
  );
}
