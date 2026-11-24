import { render, screen } from '@testing-library/react-native';
import { Text } from 'react-native';
import { useLayout } from '../src/hooks/useLayout';
import SplitView from '../src/components/SplitView';
import WelcomeScreen from '../app/(onboarding)/welcome';
import { AuthProvider } from '../src/auth/AuthProvider';
import { installFakeServer, resetEnvironment } from '../test-utils/fakeServer';

jest.mock('../src/hooks/useLayout', () => ({ ...jest.requireActual('../src/hooks/useLayout'), useLayout: jest.fn(() => ({ width: 390, height: 800, isWide: false })) }));
jest.mock('expo-router', () => ({ ...jest.requireActual('expo-router'), useRouter: () => ({ push: jest.fn(), replace: jest.fn() }) }));

function mockWidth(width) {
  const { isWideWidth } = jest.requireActual('../src/hooks/useLayout');
  useLayout.mockReturnValue({ width, height: 1024, isWide: isWideWidth(width) });
}

describe('SplitView', () => {
  test('phones show only the list pane', async () => {
    mockWidth(390);
    await render(<SplitView list={<Text>List</Text>} detail={<Text>Detail</Text>} />);
    expect(screen.getByText('List')).toBeOnTheScreen();
    expect(screen.queryByText('Detail')).toBeNull();
  });

  test('tablets show list and detail side by side', async () => {
    mockWidth(1024);
    await render(<SplitView list={<Text>List</Text>} detail={<Text>Detail</Text>} />);
    expect(screen.getByTestId('split-list-pane')).toBeOnTheScreen();
    expect(screen.getByTestId('split-detail-pane')).toBeOnTheScreen();
    expect(screen.getByText('Detail')).toBeOnTheScreen();
  });
});

describe('Welcome', () => {
  beforeEach(() => {
    resetEnvironment();
    installFakeServer();
  });
  afterEach(() => delete global.fetch);

  test('offers Get started and sign-in, with no price and no development bypass', async () => {
    await render(
      <AuthProvider>
        <WelcomeScreen />
      </AuthProvider>
    );
    expect(screen.getByRole('button', { name: 'Get started' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'I already have an account' })).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Preview the app' })).toBeNull();
    expect(screen.queryByText(/\$/)).toBeNull();
  });
});

describe('KeyboardAvoider', () => {
  const { default: KeyboardAvoider, keyboardAvoidingProps } = require('../src/components/KeyboardAvoider');

  test('iOS pads with the header offset; Android (edge to edge, no window resize) pads from the measured position', () => {
    expect(keyboardAvoidingProps('ios', 96, 120)).toEqual({ behavior: 'padding', keyboardVerticalOffset: 96 });
    expect(keyboardAvoidingProps('android', 96, 120)).toEqual({ behavior: 'padding', keyboardVerticalOffset: 120 });
    expect(keyboardAvoidingProps('android', 96, undefined)).toEqual({ behavior: 'padding', keyboardVerticalOffset: 0 });
  });

  test('renders its children on both platforms', async () => {
    for (const os of ['ios', 'android']) {
      await render(
        <KeyboardAvoider os={os} iosOffset={96} style={{ flex: 1 }} testID={'avoider-' + os}>
          <Text>{'child ' + os}</Text>
        </KeyboardAvoider>
      );
      expect(screen.getByTestId('avoider-' + os)).toBeOnTheScreen();
      expect(screen.getByText('child ' + os)).toBeOnTheScreen();
    }
  });
});

describe('KeyboardDoneBar', () => {
  const { act, fireEvent } = require('@testing-library/react-native');
  const { Keyboard } = require('react-native');
  const { default: KeyboardDoneBar, keyboardInset } = require('../src/components/KeyboardDoneBar');

  test('the inset is the docked keyboard height, and zero for a floating or hidden keyboard', () => {
    expect(keyboardInset({ screenY: 508, height: 336 }, 844)).toBe(336);
    expect(keyboardInset({ screenY: 300, height: 300 }, 1024)).toBe(0);
    expect(keyboardInset({ screenY: 844, height: 336 }, 844)).toBe(0);
    expect(keyboardInset(null, 844)).toBe(0);
  });

  test('iOS shows Done while the keyboard is open, and Done dismisses it', async () => {
    const listeners = {};
    const add = jest.spyOn(Keyboard, 'addListener').mockImplementation((name, fn) => {
      listeners[name] = fn;
      return { remove: jest.fn() };
    });
    const dismiss = jest.spyOn(Keyboard, 'dismiss').mockImplementation(() => {});
    await render(<KeyboardDoneBar os="ios" />);
    expect(screen.queryByTestId('keyboard-done')).toBeNull();
    await act(async () => listeners.keyboardWillShow({ duration: 0, endCoordinates: { screenY: 508, height: 336 } }));
    fireEvent.press(screen.getByTestId('keyboard-done'));
    expect(dismiss).toHaveBeenCalled();
    await act(async () => listeners.keyboardWillHide({ duration: 0 }));
    expect(screen.queryByTestId('keyboard-done')).toBeNull();
    add.mockRestore();
    dismiss.mockRestore();
  });

  test('Android renders nothing and listens to nothing', async () => {
    const add = jest.spyOn(Keyboard, 'addListener');
    await render(<KeyboardDoneBar os="android" />);
    expect(add).not.toHaveBeenCalled();
    expect(screen.queryByTestId('keyboard-done-bar')).toBeNull();
    add.mockRestore();
  });
});
