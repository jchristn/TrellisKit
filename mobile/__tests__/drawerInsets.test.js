import { fireEvent, screen } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { Pressable, Text } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useIsOffline } from '../src/hooks/useNetworkStatus';
import { useAppNavigation } from '../src/navigation/AppNavigation';
import { useNewVersionHint } from '../src/updates/NewVersionHint';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';

// Physical notched-phone measurements remain nonzero even after an in-app banner consumes
// the navigator's top inset. Native providers normally get these measurements from the OS.
jest.mock('react-native-safe-area-context', () => {
  const actual = jest.requireActual('react-native-safe-area-context');
  const metrics = { frame: { x: 0, y: 0, width: 390, height: 844 }, insets: { top: 47, bottom: 34, left: 0, right: 0 } };
  return { ...actual, initialWindowMetrics: metrics, SafeAreaProvider: (props) => <actual.SafeAreaProvider {...props} initialMetrics={metrics} /> };
});
jest.mock('../src/hooks/useNetworkStatus', () => ({ ...jest.requireActual('../src/hooks/useNetworkStatus'), useIsOffline: jest.fn(() => false) }));
jest.mock('../src/updates/NewVersionHint', () => ({ ...jest.requireActual('../src/updates/NewVersionHint'), useNewVersionHint: jest.fn(() => ({ visible: false })) }));

function mockNavigationProbe() {
  const { top } = useSafeAreaInsets();
  const navigation = useAppNavigation();
  return <>
    <Text testID="content-top-inset">{String(top)}</Text>
    <Pressable testID="open-drawer-probe" onPress={() => navigation.open()}><Text>Open menu</Text></Pressable>
  </>;
}

// The probe replaces the Profile screen. (renderRouter overrides normalize route keys without their
// extension, which expo-router's special-file filter misses, so +native-intent would read as a route.)
jest.mock('../app/(tabs)/profile/index', () => ({ __esModule: true, default: function ProfileProbe() { return mockNavigationProbe(); } }));

beforeEach(() => { resetEnvironment(); installFakeServer(signedInAs(makeUser())); });
afterEach(() => { delete global.fetch; jest.clearAllMocks(); });

test.each(['offline', 'new-version'])('%s banner consumes only the navigator inset, never the fullscreen drawer inset', async (banner) => {
  useIsOffline.mockReturnValue(banner === 'offline');
  useNewVersionHint.mockReturnValue({ visible: banner === 'new-version', dismiss: jest.fn(), storeUrl: null });
  await renderRouter(path.resolve(__dirname, '../app'), { initialUrl: '/profile' });
  expect(await screen.findByTestId('content-top-inset')).toHaveTextContent('0');
  expect(screen.getByTestId(banner === 'offline' ? 'offline-banner' : 'new-version-hint')).toBeOnTheScreen();
  await fireEvent.press(screen.getByTestId('open-drawer-probe'));
  expect(await screen.findByTestId('navigation-drawer-panel')).toHaveStyle({ paddingTop: 47, paddingBottom: 34 });
});
