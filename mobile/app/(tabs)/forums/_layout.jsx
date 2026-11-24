import { Stack, useRouter } from 'expo-router';
import { View } from 'react-native';
import { ServerCapability, hasServerCapability } from '@trelliskit/shared';
import { CapabilityGate } from '../../../src/components/ComingSoon';
import HeaderActions from '../../../src/components/HeaderActions';
import IconButton from '../../../src/components/IconButton';
import strings from '../../../src/strings';
import { useTabStackOptions } from '../../../src/hooks/useStackOptions';
import { useAuth } from '../../../src/auth/AuthProvider';
import { useProperty } from '../../../src/property';

/** Search and Following, the Forums tab's header menu (docs/PHASE3_CONTRACT.md section 19). */
function ForumsHeaderActions() {
  const router = useRouter();
  const { user } = useAuth();
  // The global search entry shows only when the server reports search (features.search can be false).
  const searchOn = hasServerCapability(useProperty(), ServerCapability.Search);
  if (!user) return <HeaderActions />;
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center' }}>
      {searchOn ? <IconButton icon="search-outline" label={strings.forums.search} accessibilityHint={strings.forums.searchHint} onPress={() => router.push('/forums/search')} testID="forums-header-search" /> : null}
      <IconButton icon="bookmark-outline" label={strings.forums.following} accessibilityHint={strings.forums.followingHint} onPress={() => router.push('/forums/following')} testID="forums-header-following" />
      <HeaderActions />
    </View>
  );
}

/**
 * Forums tab stack: the forum list (three panes on tablets), a forum's threads, Following, and
 * Search. Until the server reports `features.forums` the tab shows a "Coming soon" screen and no
 * forum route mounts (so nothing calls the forum API).
 */
export default function ForumsLayout() {
  const screenOptions = useTabStackOptions();
  return (
    <CapabilityGate capability={ServerCapability.Forums} title={strings.forums.title} summary={strings.forums.summary} items={strings.forums.comingSoonItems} edges={['top', 'left', 'right']} testID="screen-forums-coming-soon">
      <Stack screenOptions={screenOptions}>
      <Stack.Screen name="index" options={{ title: strings.forums.title, headerRight: () => <ForumsHeaderActions /> }} />
      <Stack.Screen name="[id]" options={{ title: strings.forums.title, headerRight: () => <ForumsHeaderActions /> }} />
      <Stack.Screen name="following" options={{ title: strings.forums.followingScreen.title }} />
      <Stack.Screen name="search" options={{ title: strings.forums.searchScreen.title }} />
      </Stack>
    </CapabilityGate>
  );
}
