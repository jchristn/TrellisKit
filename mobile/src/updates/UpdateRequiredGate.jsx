import { Platform, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import AppText from '../components/AppText';
import Button from '../components/Button';
import ContentColumn from '../components/ContentColumn';
import { openExternal } from '../links';
import { useProperty } from '../property';
import strings from '../strings';
import { useTheme } from '../theme';
import { getBinaryVersion, isBinaryBelowMinimum, storeUrlFor } from './minimumVersion';

/**
 * Blocking "Update required" screen, shown instead of the whole app when this binary is older
 * than the property's mobile.minimumBinaryVersion for the platform.
 */
export function UpdateRequiredScreen({ storeUrl, os = Platform.OS }) {
  const theme = useTheme();
  const label = os === 'android' ? strings.updates.openPlayStore : strings.updates.openAppStore;
  return (
    <SafeAreaView testID="update-required" style={{ flex: 1, backgroundColor: theme.colors.background, justifyContent: 'center' }}>
      <ContentColumn>
        <View style={{ gap: theme.spacing.lg, paddingVertical: theme.spacing.xl }}>
          <AppText variant="title" accessibilityRole="header">
            {strings.updates.requiredTitle}
          </AppText>
          <AppText>{strings.updates.requiredMessage}</AppText>
          {storeUrl ? (
            <Button testID="update-required-store" label={label} accessibilityHint={strings.updates.openStoreHint} onPress={() => openExternal(storeUrl).catch(() => {})} />
          ) : (
            <AppText color="textMuted">{strings.updates.noStoreLink}</AppText>
          )}
        </View>
      </ContentColumn>
    </SafeAreaView>
  );
}

/**
 * Render children, or the blocking screen when the binary is below the minimum.
 * @param {object} props
 * @param {string} [props.os] Platform override (tests).
 * @param {string} [props.binaryVersion] Binary version override (tests).
 */
export function UpdateRequiredGate({ children, os = Platform.OS, binaryVersion }) {
  const property = useProperty();
  const version = binaryVersion || getBinaryVersion();
  if (isBinaryBelowMinimum(property, { os, binaryVersion: version })) return <UpdateRequiredScreen os={os} storeUrl={storeUrlFor(property, os)} />;
  return children;
}
