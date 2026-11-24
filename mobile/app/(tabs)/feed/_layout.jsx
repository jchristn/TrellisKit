import { Stack } from 'expo-router';
import HeaderActions from '../../../src/components/HeaderActions';
import strings from '../../../src/strings';
import { useTabStackOptions } from '../../../src/hooks/useStackOptions';

/** Feed tab stack: the feed and a post with its comments (`/feed/{id}`, `?comment=` for a permalink). */
export default function FeedLayout() {
  const screenOptions = useTabStackOptions();
  return (
    <Stack screenOptions={screenOptions}>
      <Stack.Screen name="index" options={{ title: strings.feed.title, headerRight: () => <HeaderActions /> }} />
      <Stack.Screen name="[id]" options={{ title: strings.feed.postTitle }} />
    </Stack>
  );
}
