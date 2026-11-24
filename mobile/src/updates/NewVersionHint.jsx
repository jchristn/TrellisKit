import * as SecureStore from 'expo-secure-store';
import { useCallback, useEffect, useState } from 'react';
import { Platform, View } from 'react-native';
import AppText from '../components/AppText';
import IconButton from '../components/IconButton';
import LinkButton from '../components/LinkButton';
import { openExternal } from '../links';
import { useProperty } from '../property';
import strings from '../strings';
import { useTheme } from '../theme';
import { getBinaryVersion, isBinaryBelowLatest, isBinaryBelowMinimum, latestBinaryVersionFor, storeUrlFor } from './minimumVersion';

/**
 * "A new version is available" hint (docs/LIVE_UPDATES_CONTRACT.md decision 17): shown when this
 * binary is below the property's optional `mobile.latestBinaryVersion` for the platform. Unlike
 * the "Update required" screen it never blocks: the member can open the store or dismiss it, and a
 * dismissal is remembered for that version on this device (a later version shows it again).
 */

/** Secure-store key holding the latest version the member dismissed. */
export const DismissedVersionKey = 'tk.updates.latestHintDismissed';

/**
 * State of the hint.
 * @param {object} [options]
 * @param {string} [options.os]
 * @param {string} [options.binaryVersion]
 * @returns {{ visible: boolean, version: string|null, storeUrl: string|null, dismiss: () => void }}
 */
export function useNewVersionHint({ os = Platform.OS, binaryVersion } = {}) {
  const property = useProperty();
  const version = binaryVersion || getBinaryVersion();
  const latest = latestBinaryVersionFor(property, os);
  const eligible = Boolean(latest) && isBinaryBelowLatest(property, { os, binaryVersion: version }) && !isBinaryBelowMinimum(property, { os, binaryVersion: version });
  const [dismissed, setDismissed] = useState(undefined);

  useEffect(() => {
    let active = true;
    if (!eligible) return undefined;
    SecureStore.getItemAsync(DismissedVersionKey)
      .catch(() => null)
      .then((value) => {
        if (active) setDismissed(value || null);
      });
    return () => {
      active = false;
    };
  }, [eligible]);

  const dismiss = useCallback(() => {
    setDismissed(latest);
    SecureStore.setItemAsync(DismissedVersionKey, latest || '').catch(() => {});
  }, [latest]);

  // Hidden until the dismissal is read, so a dismissed hint never flashes.
  const visible = eligible && dismissed !== undefined && dismissed !== latest;
  return { visible, version: latest, storeUrl: storeUrlFor(property, os), dismiss };
}

/**
 * The hint bar. Render it where the app shows top-of-screen notices.
 * @param {object} props
 * @param {{ visible: boolean, storeUrl: string|null, dismiss: () => void }} props.hint From useNewVersionHint.
 * @param {number} [props.topInset]
 */
export function NewVersionHint({ hint, topInset = 0 }) {
  const theme = useTheme();
  if (!hint || !hint.visible) return null;
  return (
    <View
      testID="new-version-hint"
      accessibilityLiveRegion="polite"
      style={{
        paddingTop: topInset + theme.spacing.xs,
        paddingBottom: theme.spacing.xs,
        paddingLeft: theme.spacing.lg,
        paddingRight: theme.spacing.xs,
        backgroundColor: theme.colors.surface,
        borderBottomWidth: 1,
        borderBottomColor: theme.colors.border,
        flexDirection: 'row',
        alignItems: 'center',
        gap: theme.spacing.sm
      }}
    >
      <AppText style={{ flex: 1 }}>{strings.updates.newVersionMessage}</AppText>
      {hint.storeUrl ? <LinkButton testID="new-version-hint-store" label={strings.updates.newVersionOpenStore} accessibilityHint={strings.updates.openStoreHint} onPress={() => openExternal(hint.storeUrl).catch(() => {})} /> : null}
      <IconButton testID="new-version-hint-dismiss" icon="close" label={strings.updates.newVersionDismiss} onPress={hint.dismiss} />
    </View>
  );
}
