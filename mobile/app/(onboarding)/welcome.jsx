import { useRouter } from 'expo-router';
import { View, Image } from 'react-native';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import { useAuth } from '../../src/auth/AuthProvider';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';
import { useProperty } from '../../src/property';
import { accessOf, purchasesEnabled } from '@trelliskit/shared';

/** Welcome screen for signed-out visitors. Shows a notice after sign-out or account closure. The logo is the active property's generated mobile/logo.png (see metro.config.js). */
const logo = require('tk-property/logo.png');

export default function WelcomeScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { notice, browseAsGuest } = useAuth();
  const property = useProperty();
  const sells = purchasesEnabled(property);
  const browsable = accessOf(property).anonymousBrowsing;
  const { spacing } = theme;
  const noticeText = notice ? strings.welcome.notices[notice] : null;

  return (
    <Screen edges={['top', 'bottom', 'left', 'right']} contentStyle={{ flexGrow: 1, justifyContent: 'center', gap: spacing.xxl }}>
      <View style={{ alignItems: 'center', gap: spacing.lg }}>
        <Image
          source={logo}
          accessible
          accessibilityRole="image"
          accessibilityLabel={strings.welcome.logoLabel}
          style={{ width: 168, height: 168 }}
          resizeMode="contain"
          testID="welcome-logo"
        />
        <AppText variant="title" accessibilityRole="header" style={{ textAlign: 'center' }}>
          {strings.appName}
        </AppText>
        <AppText color="textMuted" style={{ textAlign: 'center' }}>
          {strings.welcome.tagline}
        </AppText>
        <AppText variant="caption" color="textMuted" style={{ textAlign: 'center' }}>
          {sells ? strings.welcome.membershipNote : strings.welcome.ageNote}
        </AppText>
      </View>

      <Notice testID="welcome-notice">{noticeText}</Notice>

      <View style={{ gap: spacing.md }}>
        <Button label={strings.welcome.getStarted} accessibilityHint={strings.welcome.getStartedHint} onPress={() => router.push('/sign-in')} />
        <Button label={strings.welcome.haveAccount} accessibilityHint={strings.welcome.haveAccountHint} variant="secondary" onPress={() => router.push('/sign-in')} />
        {browsable ? <Button label={strings.access.browse} accessibilityHint={strings.access.browseHint} variant="ghost" onPress={() => { browseAsGuest(); router.replace('/feed'); }} testID="welcome-browse" /> : null}
      </View>
    </Screen>
  );
}
