import Ionicons from '@expo/vector-icons/Ionicons';
import { useEffect, useState } from 'react';
import { View } from 'react-native';
import { getAppleAuthentication, isAppleSignInAvailable } from '../../native/appleAuth';
import { nativeSocialAvailability, visibleProviders } from '../../native/social';
import strings from '../../strings';
import { useTheme } from '../../theme';
import Button from '../Button';

/** Track whether Sign in with Apple can be shown on this device. */
export function useAppleAvailable() {
  const [available, setAvailable] = useState(false);
  useEffect(() => {
    let active = true;
    isAppleSignInAvailable().then((value) => {
      if (active) setAvailable(Boolean(value));
    });
    return () => {
      active = false;
    };
  }, []);
  return available;
}

/**
 * Apple, Google, and Facebook buttons. Each appears only when GET /auth/providers enables it and
 * the native module is in this build; unavailable providers render nothing.
 * @param {object} props
 * @param {object} props.providers
 * @param {(provider: 'apple'|'google'|'facebook') => void} props.onPress
 * @param {boolean} [props.disabled]
 */
export default function SocialButtons({ providers, onPress, disabled }) {
  const theme = useTheme();
  const appleAvailable = useAppleAvailable();
  const visible = visibleProviders(providers, { appleAvailable, ...nativeSocialAvailability() });
  const apple = visible.apple ? getAppleAuthentication() : null;

  if (!visible.apple && !visible.google && !visible.facebook) return null;

  return (
    <View style={{ gap: theme.spacing.md }} testID="social-buttons">
      {apple && apple.AppleAuthenticationButton ? (
        <apple.AppleAuthenticationButton
          testID="sign-in-apple"
          buttonType={apple.AppleAuthenticationButtonType.CONTINUE}
          buttonStyle={theme.dark ? apple.AppleAuthenticationButtonStyle.WHITE : apple.AppleAuthenticationButtonStyle.BLACK}
          cornerRadius={theme.radii.md}
          style={{ width: '100%', height: 48 }}
          onPress={() => (disabled ? undefined : onPress('apple'))}
        />
      ) : null}
      {visible.google ? (
        <Button
          testID="sign-in-google"
          label={strings.signIn.google}
          variant="secondary"
          disabled={disabled}
          onPress={() => onPress('google')}
          icon={<Ionicons name="logo-google" size={20} color={theme.colors.onSecondary} />}
        />
      ) : null}
      {visible.facebook ? (
        <Button
          testID="sign-in-facebook"
          label={strings.signIn.facebook}
          variant="secondary"
          disabled={disabled}
          onPress={() => onPress('facebook')}
          icon={<Ionicons name="logo-facebook" size={20} color={theme.colors.onSecondary} />}
        />
      ) : null}
    </View>
  );
}
