import { View } from 'react-native';
import { useTheme } from '../theme';

/** Themed surface with a border and padding. */
export default function Card({ children, style, ...rest }) {
  const theme = useTheme();
  return (
    <View
      style={[
        {
          backgroundColor: theme.colors.surface,
          borderColor: theme.colors.border,
          borderWidth: 1,
          borderRadius: theme.radii.lg,
          padding: theme.spacing.lg,
          gap: theme.spacing.md
        },
        style
      ]}
      {...rest}
    >
      {children}
    </View>
  );
}
