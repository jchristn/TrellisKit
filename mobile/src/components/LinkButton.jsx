import { Pressable } from 'react-native';
import { useTheme } from '../theme';
import AppText from './AppText';

/** Inline text link with a 44 point touch target. */
export default function LinkButton({ label, onPress, accessibilityHint, testID, role = 'button' }) {
  const theme = useTheme();
  return (
    <Pressable
      testID={testID}
      onPress={onPress}
      accessibilityRole={role}
      accessibilityLabel={label}
      accessibilityHint={accessibilityHint}
      hitSlop={8}
      style={({ pressed }) => ({ minHeight: theme.layout.minTouchTarget, justifyContent: 'center', opacity: pressed ? 0.6 : 1 })}
    >
      <AppText color="primary" style={{ textDecorationLine: 'underline' }}>
        {label}
      </AppText>
    </Pressable>
  );
}
