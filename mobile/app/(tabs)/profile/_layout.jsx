import { Stack } from 'expo-router';
import HeaderActions from '../../../src/components/HeaderActions';
import strings from '../../../src/strings';
import { useTabStackOptions } from '../../../src/hooks/useStackOptions';

/** Profile tab stack: profile home and About this app. */
export default function ProfileLayout() {
  const screenOptions = useTabStackOptions();
  return (
    <Stack screenOptions={screenOptions}>
      <Stack.Screen name="index" options={{ title: strings.profile.title, headerRight: () => <HeaderActions /> }} />
      <Stack.Screen name="about" options={{ title: strings.about.title }} />
    </Stack>
  );
}
