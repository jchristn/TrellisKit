import { DmPolicy, propertyFeatureEnabled, PushPreview } from '@trelliskit/shared';
import { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../../src/auth/AuthProvider';
import AppText from '../../src/components/AppText';
import CheckRow from '../../src/components/CheckRow';
import ChipGroup from '../../src/components/ChipGroup';
import ErrorState from '../../src/components/ErrorState';
import Loading from '../../src/components/Loading';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import Section from '../../src/components/Section';
import { describeMessagingError } from '../../src/messaging/format';
import { useProperty } from '../../src/property';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

const t = strings.messaging;

/**
 * The messaging setting that exempts message pushes from the notification hours (resolved
 * decision 12). The contract names the toggle but not its field; this is the field the app sends
 * with `PUT /me/messaging/settings`.
 */
export const ExemptHoursField = 'exemptFromNotificationHours';

/**
 * Messaging settings (`GET`, `PUT /me/messaging/settings`, contract section 1.2): who can message
 * me (Everyone, People I know, Nobody), read receipts and typing indicators (reciprocal), the push
 * preview (Sender only, Sender and text, Hidden), and whether message pushes ignore the
 * notification hours. Each change saves at once and rolls back on failure. Under Account, so it
 * needs a session but not a membership.
 */
export default function MessagingSettingsScreen() {
  const theme = useTheme();
  const { api } = useAuth();
  const property = useProperty();
  const [settings, setSettings] = useState(null);
  const [status, setStatus] = useState('loading');
  const [error, setError] = useState(null);
  const [saveError, setSaveError] = useState(null);
  const enabled = propertyFeatureEnabled(property, 'messaging');

  const load = useCallback(async () => {
    setError(null);
    try {
      setSettings(await api.getMessagingSettings());
      setStatus('ready');
    } catch (e) {
      setError(e);
      setStatus('error');
    }
  }, [api]);

  useEffect(() => {
    if (!enabled) return;
    // load sets state after its request resolves.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    load();
  }, [enabled, load]);

  async function save(patch) {
    const previous = settings;
    setSettings({ ...settings, ...patch });
    setSaveError(null);
    try {
      const result = await api.updateMessagingSettings(patch);
      if (result && typeof result === 'object') setSettings((s) => ({ ...s, ...result }));
    } catch (e) {
      setSettings(previous);
      setSaveError(e && e.code === 'ValidationFailed' ? describeMessagingError(e) : t.saveFailed);
    }
  }

  if (!enabled) {
    return (
      <Screen testID="screen-messaging-settings">
        <ErrorState title={t.featureOffTitle} message={t.featureOffBody} />
      </Screen>
    );
  }
  if (status === 'loading') return <Loading label={strings.common.loading} testID="messaging-settings-loading" />;
  if (status === 'error' || !settings) {
    return (
      <Screen testID="screen-messaging-settings">
        <ErrorState title={t.settingsLoadFailedTitle} message={describeMessagingError(error)} onRetry={load} testID="messaging-settings-error" />
      </Screen>
    );
  }

  const policy = settings.dmPolicy || DmPolicy.Everyone;
  const preview = settings.pushPreview || PushPreview.SenderOnly;
  return (
    <Screen contentStyle={{ gap: theme.spacing.xl }} testID="screen-messaging-settings">
      <AppText color="textMuted">{t.settingsBody}</AppText>
      <Notice tone="error" testID="messaging-settings-save-error">
        {saveError}
      </Notice>
      <Section title={t.dmPolicyLabel} testID="messaging-dm-policy-section">
        <ChipGroup label={t.dmPolicyLabel} hideLabel options={Object.values(DmPolicy).map((value) => ({ value, label: t.dmPolicies[value].label }))} value={policy} onToggle={(dmPolicy) => dmPolicy !== policy && save({ dmPolicy })} testID="messaging-dm-policy" />
        <AppText variant="caption" color="textMuted" testID="messaging-dm-policy-hint">
          {t.dmPolicies[policy] ? t.dmPolicies[policy].hint : ''}
        </AppText>
      </Section>
      <Section title={t.settingsTitle}>
        <CheckRow label={t.readReceipts} value={settings.readReceipts !== false} onValueChange={(readReceipts) => save({ readReceipts })} testID="messaging-read-receipts">
          <AppText variant="caption" color="textMuted">
            {t.readReceiptsHint}
          </AppText>
        </CheckRow>
        <CheckRow label={t.typingIndicators} value={settings.typingIndicators !== false} onValueChange={(typingIndicators) => save({ typingIndicators })} testID="messaging-typing">
          <AppText variant="caption" color="textMuted">
            {t.typingIndicatorsHint}
          </AppText>
        </CheckRow>
        <CheckRow label={t.exemptHours} value={Boolean(settings[ExemptHoursField])} onValueChange={(value) => save({ [ExemptHoursField]: value })} testID="messaging-exempt-hours">
          <AppText variant="caption" color="textMuted">
            {t.exemptHoursHint}
          </AppText>
        </CheckRow>
      </Section>
      <Section title={t.pushPreviewLabel} testID="messaging-push-preview-section">
        <ChipGroup label={t.pushPreviewLabel} hideLabel options={Object.values(PushPreview).map((value) => ({ value, label: t.pushPreviews[value].label }))} value={preview} onToggle={(pushPreview) => pushPreview !== preview && save({ pushPreview })} testID="messaging-push-preview" />
        <AppText variant="caption" color="textMuted">
          {t.pushPreviews[preview] ? t.pushPreviews[preview].hint : ''}
        </AppText>
      </Section>
      <AppText variant="caption" color="textMuted" testID="messaging-privacy">
        {t.privacyNote}
      </AppText>
    </Screen>
  );
}
