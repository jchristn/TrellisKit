import { brandPalette, BrandSchemes, palettePresets, resolvePalettePreset } from '@trelliskit/shared';
import { useCallback, useEffect, useState } from 'react';
import { Pressable, StyleSheet, useWindowDimensions, View } from 'react-native';
import { useAuth } from '../../src/auth/AuthProvider';
import AppText from '../../src/components/AppText';
import ChipGroup from '../../src/components/ChipGroup';
import ErrorState from '../../src/components/ErrorState';
import Loading from '../../src/components/Loading';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import { useProperty } from '../../src/property';
import strings from '../../src/strings';
import { ColorSchemeModes, neutralDarkColors, neutralLightColors, setColorSchemePreference, setPalettePreference, useColorSchemePreference, usePalettePreference, useTheme } from '../../src/theme';

const t = strings.appearance;
const DEFAULT = '';
const PAGE = Object.freeze({ light: neutralLightColors.background, dark: neutralDarkColors.background });

/** Light and dark preview of a palette: primary button, accent, and soft accent chip on each page background. */
export function PaletteSwatches({ palette, testID }) {
  const theme = useTheme();
  return (
    <View style={[styles.swatches, { borderColor: theme.colors.border, borderRadius: theme.radii.md }]} testID={testID} accessible={false} importantForAccessibility="no-hide-descendants">
      {BrandSchemes.map((scheme) => {
        const set = (palette && palette[scheme]) || {};
        return (
          <View key={scheme} style={[styles.swatch, { backgroundColor: PAGE[scheme], padding: theme.spacing.sm, gap: theme.spacing.sm }]}>
            <View style={[styles.button, { backgroundColor: set.primary, borderRadius: theme.radii.sm }]}>
              <AppText variant="caption" style={{ color: set.onPrimary, fontWeight: '700' }}>Aa</AppText>
            </View>
            <View style={[styles.dot, { backgroundColor: set.accent }]} />
            <View style={[styles.chip, { backgroundColor: set.accentSoft, borderRadius: theme.radii.pill }]}>
              <AppText variant="caption" style={{ color: set.onAccentSoft, fontWeight: '600' }}>Aa</AppText>
            </View>
          </View>
        );
      })}
    </View>
  );
}

function Option({ id, name, body, palette, selected, onSelect, disabled, wide }) {
  const theme = useTheme();
  return (
    <Pressable
      accessibilityRole="radio"
      accessibilityState={{ checked: selected, disabled }}
      accessibilityLabel={name}
      accessibilityHint={t.optionHint}
      disabled={disabled}
      onPress={() => onSelect(id)}
      testID={'appearance-' + (id || 'default')}
      style={({ pressed }) => [
        styles.option,
        {
          width: wide ? '48%' : '100%',
          minHeight: theme.layout.minTouchTarget,
          padding: theme.spacing.md,
          gap: theme.spacing.sm,
          borderRadius: theme.radii.lg,
          backgroundColor: theme.colors.surfaceRaised,
          borderColor: selected ? theme.colors.primary : theme.colors.border,
          borderWidth: selected ? 2 : 1,
          opacity: pressed ? 0.85 : 1
        }
      ]}
    >
      <View style={[styles.head, { gap: theme.spacing.sm }]}>
        <View style={[styles.radio, { borderColor: selected ? theme.colors.primary : theme.colors.textMuted }]}>
          {selected ? <View style={[styles.radioDot, { backgroundColor: theme.colors.primary }]} /> : null}
        </View>
        <AppText variant="label" style={styles.flex}>
          {name}
        </AppText>
        {selected ? (
          <AppText variant="caption" color="primary">
            {t.selected}
          </AppText>
        ) : null}
      </View>
      <PaletteSwatches palette={palette} />
      {body ? (
        <AppText variant="caption" color="textMuted">
          {body}
        </AppText>
      ) : null}
    </Pressable>
  );
}

/**
 * Account, Appearance (docs/TRELLISKIT.md section 2.1): the member picks light or dark mode for
 * this device (or to match the device, the default), and the property palette (the
 * default) or one of the presets the community allows (`GET /api/v1/property` `brand.presets`,
 * cached with its ETag). The choice is saved to the account with `PUT /me/appearance` and applies
 * at once in light and dark through useTheme (no rebuild); a preset the administrator removes falls
 * back to the property palette. Phones show one column; tablets two. The logo and icon never change.
 */
export default function AppearanceScreen() {
  const theme = useTheme();
  const { width } = useWindowDimensions();
  const wide = width >= theme.layout.wideBreakpoint;
  const { api } = useAuth();
  const property = useProperty();
  const local = usePalettePreference();
  const mode = useColorSchemePreference();
  const [status, setStatus] = useState('loading');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const view = await api.getAppearance();
      if (view && typeof view === 'object') setPalettePreference(view.palettePreset || null);
      setStatus('ready');
    } catch {
      setStatus('error');
    }
  }, [api]);

  useEffect(() => {
    // load sets state after its request resolves.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    load();
  }, [load]);

  const presets = palettePresets(property);
  const applied = resolvePalettePreset(property, local);
  const selected = applied ? applied.id : DEFAULT;

  async function choose(presetId) {
    if (busy || presetId === selected) return;
    const before = local;
    setError(null);
    setPalettePreference(presetId || null);
    setBusy(true);
    try {
      const view = await api.updateAppearance(presetId || null);
      setPalettePreference(view && typeof view === 'object' ? view.palettePreset || null : presetId || null);
    } catch {
      setPalettePreference(before);
      setError(t.saveFailed);
    } finally {
      setBusy(false);
    }
  }

  if (status === 'loading') return <Loading label={strings.common.loading} testID="appearance-loading" />;
  if (status === 'error') {
    return (
      <Screen testID="screen-appearance">
        <ErrorState title={t.loadFailedTitle} onRetry={load} testID="appearance-error" />
      </Screen>
    );
  }

  const defaultName = t.propertyDefault;
  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-appearance">
      <AppText color="textMuted">{t.body}</AppText>
      <ChipGroup label={t.mode} options={ColorSchemeModes.map((value) => ({ value, label: t.modes[value] }))} value={mode} onToggle={setColorSchemePreference} testID="appearance-mode" />
      <AppText variant="label">{t.palette}</AppText>
      <Notice tone="error" testID="appearance-error-notice">
        {error}
      </Notice>
      {local && !applied ? (
        <Notice tone="info" testID="appearance-disallowed">
          {t.disallowed}
        </Notice>
      ) : null}
      <View accessibilityRole="radiogroup" style={[styles.grid, { gap: theme.spacing.md }]}>
        <Option id={DEFAULT} name={defaultName} body={t.propertyDefaultBody} palette={brandPalette(property)} selected={selected === DEFAULT} onSelect={choose} disabled={busy} wide={wide} />
        {presets.map((preset) => (
          <Option key={preset.id} id={preset.id} name={preset.name} body={preset.description} palette={preset} selected={selected === preset.id} onSelect={choose} disabled={busy} wide={wide} />
        ))}
      </View>
      {presets.length === 0 ? <AppText color="textMuted">{t.noPresets}</AppText> : null}
    </Screen>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  grid: { flexDirection: 'row', flexWrap: 'wrap', justifyContent: 'space-between' },
  option: {},
  head: { flexDirection: 'row', alignItems: 'center' },
  radio: { width: 22, height: 22, borderRadius: 11, borderWidth: 2, alignItems: 'center', justifyContent: 'center' },
  radioDot: { width: 10, height: 10, borderRadius: 5 },
  swatches: { flexDirection: 'row', borderWidth: 1, overflow: 'hidden' },
  swatch: { flex: 1, flexDirection: 'row', alignItems: 'center', minHeight: 44 },
  button: { minWidth: 36, height: 24, paddingHorizontal: 6, alignItems: 'center', justifyContent: 'center' },
  dot: { width: 16, height: 16, borderRadius: 8 },
  chip: { height: 22, paddingHorizontal: 8, alignItems: 'center', justifyContent: 'center' }
});
