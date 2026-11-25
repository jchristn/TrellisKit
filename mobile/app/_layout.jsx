import { Stack, ThemeProvider } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { View } from 'react-native';
import { SafeAreaInsetsContext, SafeAreaProvider, useSafeAreaInsets } from 'react-native-safe-area-context';
import { AppContentProvider } from '../src/appConfig';
import { AuthProvider } from '../src/auth/AuthProvider';
import { AccessPromptProvider } from '../src/components/AccessPrompt';
import { PurchaseProvider } from '../src/billing/PurchaseProvider';
import DocumentsGate from '../src/components/DocumentsGate';
import KeyboardDoneBar from '../src/components/KeyboardDoneBar';
import OfflineBanner from '../src/components/OfflineBanner';
import { useIsOffline } from '../src/hooks/useNetworkStatus';
import { useStackOptions } from '../src/hooks/useStackOptions';
import { MediaProvider } from '../src/media/MediaProvider';
import { MessagingProvider } from '../src/messaging/MessagingProvider';
import { NotificationsProvider } from '../src/notifications/NotificationsProvider';
import PushRegistration from '../src/notifications/PushRegistration';
import LibrarySync from '../src/library/LibrarySync';
import { AppNavigationProvider, drawerHeaderOptions, useAppNavigation } from '../src/navigation/AppNavigation';
import { PropertyProvider } from '../src/property';
import strings from '../src/strings';
import { toNavigationTheme, useTheme } from '../src/theme';
import { NewVersionHint, UpdateRequiredGate, UpdatesProvider, useNewVersionHint } from '../src/updates';

/**
 * The offline banner and the "new version available" hint sit above the navigator. The first one
 * shown takes the top safe area, so the navigator below gets a zero top inset (no double gap).
 */
function AppFrame({ children }) {
  const insets = useSafeAreaInsets();
  const offline = useIsOffline();
  const hint = useNewVersionHint();
  const theme = useTheme();
  const topTaken = offline || hint.visible;
  return (
    <View style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <StatusBar style={offline === theme.dark ? 'dark' : 'light'} />
      <OfflineBanner offline={offline} topInset={insets.top} />
      <NewVersionHint hint={hint} topInset={offline ? 0 : insets.top} />
      <SafeAreaInsetsContext.Provider value={topTaken ? { ...insets, top: 0 } : insets}>{children}</SafeAreaInsetsContext.Provider>
      <KeyboardDoneBar />
    </View>
  );
}

/** Native stack headers read the drawer context supplied outside AppFrame's inset adjustment. */
function AppStack() {
  const screenOptions = useStackOptions();
  const menuOptions = drawerHeaderOptions(useAppNavigation());
  return (
    <Stack screenOptions={screenOptions}>
      <Stack.Screen name="index" options={{ headerShown: false }} />
      <Stack.Screen name="(onboarding)" options={{ title: strings.common.back, headerShown: false }} />
      <Stack.Screen name="(setup)" options={{ title: strings.common.back, headerShown: false, gestureEnabled: false }} />
      <Stack.Screen name="(tabs)" options={{ title: strings.common.back, headerShown: false, gestureEnabled: false }} />
      <Stack.Screen name="admin" options={{ headerShown: false }} />
      <Stack.Screen name="notification-detail" options={{ title: strings.nativeMetrics.detailTitle }} />
      <Stack.Screen name="external-content" options={{ title: strings.links.title }} />
      <Stack.Screen name="account" options={{ headerShown: false }} />
      <Stack.Screen name="documents" options={{ title: strings.common.back, headerShown: false }} />
      <Stack.Screen name="messages" options={{ title: strings.common.back, headerShown: false }} />
      <Stack.Screen name="support" options={{ title: strings.common.back, headerShown: false }} />
      <Stack.Screen name="ideas" options={{ title: strings.common.back, headerShown: false }} />
      <Stack.Screen name="announcements" options={{ title: strings.common.back, headerShown: false }} />
      <Stack.Screen name="notifications" options={{ title: strings.notifications.title, ...menuOptions }} />
      <Stack.Screen name="compose" options={{ title: strings.composer.newTitle, presentation: 'modal' }} />
      <Stack.Screen name="members" options={{ headerShown: false }} />
      <Stack.Screen name="threads" options={{ headerShown: false }} />
      <Stack.Screen name="assistants" options={{ headerShown: false }} />
      <Stack.Screen name="venues" options={{ headerShown: false }} />
      <Stack.Screen name="instructors" options={{ headerShown: false }} />
      <Stack.Screen name="directory" options={{ headerShown: false }} />
      <Stack.Screen name="events" options={{ headerShown: false }} />
      <Stack.Screen name="series" options={{ headerShown: false }} />
      <Stack.Screen name="lessons" options={{ headerShown: false }} />
      <Stack.Screen name="training" options={{ headerShown: false }} />
      <Stack.Screen name="compose-thread" options={{ title: strings.forums.composer.newTitle, presentation: 'modal' }} />
    </Stack>
  );
}

/**
 * Root navigator: property, session, purchases, push, media grant, and notification providers;
 * onboarding, setup, tabs, account, header screens, the post composer, member profiles, messages, support, ideas, and announcements. MessagingProvider owns the realtime client of a signed-in session. PropertyProvider fetches GET /api/v1/property at launch and falls
 * back to the bundled copy. UpdatesProvider runs live updates (check, background download,
 * critical prompt) and UpdateRequiredGate blocks binaries below mobile.minimumBinaryVersion.
 * AppContentProvider keeps the content manifest (flags, kill switches, remote strings, banners,
 * assets). DocumentsGate sends a member with pending required documents to the documents step.
 */
export default function RootLayout() {
  const theme = useTheme();
  return (
    <SafeAreaProvider>
      <PropertyProvider>
      <UpdatesProvider>
      <UpdateRequiredGate>
      <AuthProvider>
      <AppContentProvider>
        <PurchaseProvider>
          <AccessPromptProvider>
          <PushRegistration />
          <DocumentsGate />
          <LibrarySync />
          <MediaProvider>
          <NotificationsProvider>
          <MessagingProvider>
          <ThemeProvider value={toNavigationTheme(theme)}>
            <AppNavigationProvider>
              <AppFrame>
                <AppStack />
              </AppFrame>
            </AppNavigationProvider>
          </ThemeProvider>
          </MessagingProvider>
          </NotificationsProvider>
          </MediaProvider>
          </AccessPromptProvider>
        </PurchaseProvider>
      </AppContentProvider>
      </AuthProvider>
      </UpdateRequiredGate>
      </UpdatesProvider>
      </PropertyProvider>
    </SafeAreaProvider>
  );
}
