import Ionicons from '@expo/vector-icons/Ionicons';
import { aiAvailable, aiPlacement, catalogAccess, showsLockBadge, signedOutBrowsable } from '@trelliskit/shared';
import { Redirect } from 'expo-router';
import { Tabs } from 'expo-router/js-tabs';
import { GlobalBannerFrame } from '../../src/appConfig';
import HeaderActions from '../../src/components/HeaderActions';
import Loading from '../../src/components/Loading';
import { useAuth } from '../../src/auth/AuthProvider';
import { canEnterTabs, isGuest, nextRoute } from '../../src/auth/routing';
import { featureOn } from '../../src/content/phase4';
import { useProperty } from '../../src/property';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

const TABS = [
  { name: 'feed', title: strings.tabs.feed, icon: 'home', headerShown: false, access: 'feed' },
  { name: 'forums', title: strings.tabs.forums, icon: 'chatbox-ellipses', headerShown: false, access: 'forums' },
  { name: 'library', title: strings.tabs.library, icon: 'library', headerShown: false, access: 'library', catalog: true },
  { name: 'map', title: strings.tabs.map, icon: 'map', headerShown: false, access: 'directory', catalog: true },
  { name: 'ai', title: strings.assistants.tab, icon: 'sparkles', promotable: true, access: 'assistants' },
  { name: 'profile', title: strings.tabs.profile, icon: 'person-circle', headerShown: false, guest: true }
];

/**
 * Member tab layout: Feed, Forums, Library, Map, Profile, plus Assistants when the server reports `features.ai`
 * and `features.aiPlacement === 'Tab'` (otherwise the screen stays reachable from Profile but has no
 * tab button). Gated: requires a signed-in, age-attested account past the paywall step when
 * `Me.membershipRequired` (SystemAdmin also passes), or a signed-out guest when the property
 * allows anonymous browsing (guests see only the sections open to them). Gated features in
 * PartiallyGated show a lock icon for non-members. Everyone else is sent to the step they still need.
 */
export default function TabsLayout() {
  const theme = useTheme();
  const { session } = useAuth();
  const property = useProperty();
  const assistantsTab = aiAvailable(property) && aiPlacement(property) === 'Tab';
  if (session.status === 'loading') return <Loading label={strings.launch.restoring} />;
  if (!canEnterTabs(session, property)) return <Redirect href={nextRoute(session)} />;
  const guest = isGuest(session, property);
  // Guests see only the sections open to anonymous browsing (and Profile, which offers sign in).
  const guestBrowsable = (tab) => (tab.catalog ? catalogAccess(property, null, tab.access, 'read').allowed : signedOutBrowsable(property, tab.access));
  const hiddenForGuest = (tab) => guest && !tab.guest && (tab.signedInOnly || !tab.access || !guestBrowsable(tab));
  // Phase 4 catalogs: Library hides with `features.library: false`; Map shows while the directory or events is on.
  const switchedOff = (tab) => (tab.name === 'library' ? !featureOn(property, 'library') : tab.name === 'map' ? !featureOn(property, 'directory') && !featureOn(property, 'events') : false);
  const title = (tab) => (tab.name === 'map' && !featureOn(property, 'directory') ? strings.tabs.events : tab.title);
  // Gated features (PartiallyGated, non-members) keep their tab with a lock icon and label.
  const locked = (tab) => !guest && tab.access && showsLockBadge(property, session.user, tab.name === 'map' && !featureOn(property, 'directory') ? 'events' : tab.access);
  return (
    <GlobalBannerFrame>
    <Tabs
      screenOptions={{
        headerStyle: { backgroundColor: theme.colors.tabBar },
        headerTintColor: theme.colors.text,
        headerRight: () => <HeaderActions />,
        tabBarActiveTintColor: theme.colors.primary,
        tabBarInactiveTintColor: theme.colors.tabInactive,
        tabBarStyle: { backgroundColor: theme.colors.tabBar, borderTopColor: theme.colors.border },
        sceneStyle: { backgroundColor: theme.colors.background }
      }}
    >
      {TABS.map((tab) => (
        <Tabs.Screen
          key={tab.name}
          name={tab.name}
          options={{
            title: title(tab),
            ...((tab.promotable && !assistantsTab) || hiddenForGuest(tab) || switchedOff(tab) ? { href: null } : {}),
            headerShown: tab.headerShown !== false,
            tabBarAccessibilityLabel: locked(tab) ? strings.access.lockedLabel(title(tab)) : title(tab),
            tabBarButtonTestID: 'tab-' + tab.name,
            tabBarIcon: ({ color, size, focused }) => <Ionicons name={locked(tab) ? (focused ? 'lock-closed' : 'lock-closed-outline') : focused ? tab.icon : tab.icon + '-outline'} size={size} color={color} />
          }}
        />
      ))}
    </Tabs>
    </GlobalBannerFrame>
  );
}
