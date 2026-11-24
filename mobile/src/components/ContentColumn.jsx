import { View } from 'react-native';
import { useLayout } from '../hooks/useLayout';
import { useTheme } from '../theme';

/**
 * Centers content in a readable column. Full width on phones; capped at
 * layout.maxContentWidth on tablets and other wide windows.
 */
export default function ContentColumn({ children, style, maxWidth }) {
  const theme = useTheme();
  const { isWide } = useLayout();
  return (
    <View
      style={[
        {
          width: '100%',
          alignSelf: 'center',
          maxWidth: isWide ? maxWidth || theme.layout.maxContentWidth : undefined,
          paddingHorizontal: isWide ? theme.spacing.xl : theme.spacing.lg
        },
        style
      ]}
    >
      {children}
    </View>
  );
}
