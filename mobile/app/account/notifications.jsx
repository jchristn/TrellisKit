import { useRouter } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import { AppState, Linking, Switch, View } from 'react-native';
import { collectEnumeration, isDigestEmailCategory } from '@trelliskit/shared';
import { notifications as notificationsApi } from '../../src/api/community';
import { useAuth } from '../../src/auth/AuthProvider';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import Card from '../../src/components/Card';
import CheckRow from '../../src/components/CheckRow';
import ErrorState from '../../src/components/ErrorState';
import IconButton from '../../src/components/IconButton';
import Loading from '../../src/components/Loading';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import Section from '../../src/components/Section';
import { describeContentError } from '../../src/content/errors';
import { deviceTimeZone, displayTime, orderedCategories, parseTime, stepTime, toggleCategory } from '../../src/notifications/preferences';
import { enablePushNotifications, getPushPermission, requestPushPermission } from '../../src/notifications/push';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

const CHANNELS = ['inApp', 'push', 'email'];

/** A `HH:mm` control with earlier and later buttons (30 minute steps), adjustable for screen readers. */
function TimeStepper({ label, value, onChange, disabled, testID }) {
  const theme = useTheme();
  const t = strings.notificationSettings;
  const text = displayTime(value);
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}>
      <AppText variant="label" style={{ width: 64 }} importantForAccessibility="no" accessibilityElementsHidden>
        {label}
      </AppText>
      <IconButton icon="remove-circle-outline" label={t.earlier(label)} onPress={() => onChange(stepTime(value, -1))} disabled={disabled} testID={testID + '-earlier'} />
      <View
        accessible
        accessibilityRole="adjustable"
        accessibilityLabel={label}
        accessibilityValue={{ text }}
        accessibilityActions={[{ name: 'increment' }, { name: 'decrement' }]}
        onAccessibilityAction={(e) => onChange(stepTime(value, e.nativeEvent.actionName === 'increment' ? 1 : -1))}
        style={{ minWidth: 96, alignItems: 'center' }}
      >
        <AppText variant="heading" testID={testID + '-value'}>
          {text}
        </AppText>
      </View>
      <IconButton icon="add-circle-outline" label={t.later(label)} onPress={() => onChange(stepTime(value, 1))} disabled={disabled} testID={testID + '-later'} />
    </View>
  );
}

/**
 * Notification settings (contract 8.5, 8.6, 8.10): per category in-app, push, and email toggles
 * (all on by default; locked cells say so), Pause all push notifications, the notification
 * hours window (default 7:00 to 22:00 in the device time zone), and this device's permission
 * state with a way to turn it on. Phase 5 adds the Messages, Ideas, and Announcements categories
 * (docs/PHASE5_CONTRACT.md section 9.2) when the server lists them; Messages links to the
 * messaging settings (push preview and the notification hours exemption).
 */
export default function NotificationSettingsScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api, user } = useAuth();
  const [prefs, setPrefs] = useState(null);
  const [status, setStatus] = useState('loading');
  const [error, setError] = useState(null);
  const [saveError, setSaveError] = useState(null);
  const [permission, setPermission] = useState(null);
  const t = strings.notificationSettings;

  const checkPermission = useCallback(async () => {
    setPermission(await getPushPermission());
  }, []);

  const load = useCallback(async () => {
    setError(null);
    try {
      let doc = await collectEnumeration((q) => notificationsApi.getPreferences(api, q)).then((page) => ({ ...page, categories: page.objects }));
      const zone = deviceTimeZone();
      if (zone && doc && doc.notificationHours && doc.notificationHours.timeZone !== zone) {
        try {
          doc = (await notificationsApi.updatePreferences(api, { notificationHours: { timeZone: zone } })) || doc;
        } catch {
          // Keep the server's zone; the next foreground tries again.
        }
      }
      setPrefs(doc);
      setStatus('ready');
    } catch (e) {
      setError(e);
      setStatus('error');
    }
  }, [api]);

  useEffect(() => {
    // Both read external state and set it when they resolve.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    load();
    checkPermission();
    const sub = AppState.addEventListener('change', (state) => {
      if (state === 'active') checkPermission();
    });
    return () => sub.remove();
  }, [load, checkPermission]);

  /** Apply locally, PUT the change, and replace with the server's document (or roll back). */
  async function save(next, body) {
    const previous = prefs;
    setPrefs(next);
    setSaveError(null);
    try {
      const result = await notificationsApi.updatePreferences(api, body);
      if (result && result.categories) setPrefs(result);
    } catch (e) {
      setPrefs(previous);
      setSaveError(e && e.code === 'ValidationFailed' ? describeContentError(e) : t.saveFailed);
    }
  }

  async function turnOnDevice() {
    if (permission === 'denied') {
      Linking.openSettings().catch(() => {});
      return;
    }
    const result = await requestPushPermission();
    if (result === 'granted') await enablePushNotifications(api, { userId: user ? user.id : '' }).catch(() => {});
    setPermission(result === 'granted' ? 'granted' : await getPushPermission());
  }

  function toggle(category, channel, value) {
    const { prefs: next, change } = toggleCategory(prefs, category, channel, value);
    save(next, { categories: [change] });
    if (channel === 'push' && value && permission && permission !== 'granted' && permission !== 'unavailable') turnOnDevice();
  }

  function setHours(patch) {
    const hours = { ...prefs.notificationHours, ...patch };
    save({ ...prefs, notificationHours: hours }, { notificationHours: patch });
  }

  if (status === 'loading') return <Loading label={strings.common.loading} testID="notification-settings-loading" />;
  if (status === 'error' || !prefs) {
    return (
      <Screen testID="screen-notification-settings">
        <ErrorState title={t.loadFailedTitle} message={describeContentError(error)} onRetry={load} retryHint={t.retryHint} testID="notification-settings-error" />
      </Screen>
    );
  }

  const hours = prefs.notificationHours || { enabled: true, start: '07:00', end: '22:00', timeZone: deviceTimeZone() };
  const sameTime = hours.enabled && parseTime(hours.start) === parseTime(hours.end);
  const deviceOff = permission && permission !== 'granted';

  return (
    <Screen contentStyle={{ gap: theme.spacing.xl }} testID="screen-notification-settings">
      <AppText color="textMuted">{t.body}</AppText>

      {deviceOff ? (
        <Card testID="notification-permission-banner" style={{ borderColor: theme.colors.warning }}>
          <AppText variant="heading" accessibilityRole="header">
            {t.permissionOffTitle}
          </AppText>
          <AppText color="textMuted">{permission === 'unavailable' ? t.permissionUnavailable : t.permissionOffBody}</AppText>
          {permission !== 'unavailable' ? <Button label={permission === 'denied' ? t.openSettings : t.turnOn} onPress={turnOnDevice} testID="notification-permission-turn-on" /> : null}
        </Card>
      ) : null}

      <Notice tone="error" testID="notification-settings-save-error">
        {saveError}
      </Notice>

      <Section title={strings.notificationSettings.title}>
        <CheckRow label={t.pushPaused} value={Boolean(prefs.pushPaused)} onValueChange={(v) => save({ ...prefs, pushPaused: v }, { pushPaused: v })} testID="notification-push-paused">
          <AppText variant="caption" color="textMuted">
            {t.pushPausedHint}
          </AppText>
        </CheckRow>
        {typeof prefs.autoFollowOnComment === 'boolean' ? (
          <CheckRow label={t.autoFollow} value={prefs.autoFollowOnComment} onValueChange={(v) => save({ ...prefs, autoFollowOnComment: v }, { autoFollowOnComment: v })} testID="notification-auto-follow">
            <AppText variant="caption" color="textMuted">
              {t.autoFollowHint}
            </AppText>
          </CheckRow>
        ) : null}
      </Section>

      {orderedCategories(prefs).map((cat) => {
        const meta = t.categories[cat.category] || { label: cat.category, description: t.otherCategory.description };
        const editable = cat.editable || {};
        return (
          <Section key={cat.category} title={meta.label} testID={'category-' + cat.category}>
            <AppText color="textMuted">{meta.description}</AppText>
            {isDigestEmailCategory(cat.category) && editable.email ? <AppText variant="caption" color="textMuted" testID={'digest-note-' + cat.category}>{t.digestNote}</AppText> : null}
            {CHANNELS.map((channel) => {
              // Following and Forums email is an opt-in digest (PHASE3_CONTRACT.md section 9.3).
              const digest = channel === 'email' && isDigestEmailCategory(cat.category);
              const label = t.channels[channel] + (digest ? ' (' + t.digest + ')' : '');
              const locked = !editable[channel];
              const value = Boolean(cat[channel]);
              const pausedPush = channel === 'push' && prefs.pushPaused;
              return (
                <View key={channel} style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.md, minHeight: theme.layout.minTouchTarget }}>
                  <AppText style={{ flex: 1 }} importantForAccessibility="no" accessibilityElementsHidden>
                    {label + (locked && value ? ' (' + t.locked + ')' : '')}
                  </AppText>
                  <Switch
                    testID={'pref-' + cat.category + '-' + channel}
                    value={value}
                    disabled={locked}
                    onValueChange={(v) => toggle(cat.category, channel, v)}
                    accessibilityLabel={t.channelLabel(meta.label, label)}
                    accessibilityHint={locked && value ? t.locked : pausedPush ? t.pushPaused : undefined}
                    trackColor={{ true: theme.colors.primary, false: theme.colors.border }}
                    style={{ minHeight: theme.layout.minTouchTarget }}
                  />
                </View>
              );
            })}
            {cat.category === 'Messages' ? <Button label={t.messagingSettings} accessibilityHint={t.messagingSettingsHint} variant="secondary" onPress={() => router.push('/account/messaging')} testID="notification-messaging-settings" /> : null}
          </Section>
        );
      })}

      <Section title={t.hoursTitle} testID="notification-hours">
        <AppText color="textMuted">{t.hoursBody}</AppText>
        <CheckRow label={t.hoursEnabled} value={Boolean(hours.enabled)} onValueChange={(v) => setHours({ enabled: v })} testID="notification-hours-enabled" />
        {hours.enabled ? (
          <>
            <TimeStepper label={t.start} value={hours.start} onChange={(start) => setHours({ start })} testID="notification-hours-start" />
            <TimeStepper label={t.end} value={hours.end} onChange={(end) => setHours({ end })} testID="notification-hours-end" />
            {sameTime ? <AppText color="textMuted">{t.sameTime}</AppText> : null}
          </>
        ) : (
          <AppText color="textMuted">{t.allDay}</AppText>
        )}
        {hours.timeZone ? (
          <AppText variant="caption" color="textMuted" testID="notification-hours-zone">
            {t.timeZone(hours.timeZone)}
          </AppText>
        ) : null}
      </Section>
    </Screen>
  );
}
