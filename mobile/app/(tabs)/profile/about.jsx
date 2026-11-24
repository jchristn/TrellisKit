import { ActivityIndicator, View } from 'react-native';
import AppText from '../../../src/components/AppText';
import Button from '../../../src/components/Button';
import Card from '../../../src/components/Card';
import Screen from '../../../src/components/Screen';
import { getAppVersion, resolveApiUrl } from '../../../src/config';
import { useServerHealth } from '../../../src/hooks/useServerHealth';
import strings, { formatUptime } from '../../../src/strings';
import { useTheme } from '../../../src/theme';

const STATUS_COLORS = { Healthy: 'success', Degraded: 'warning', Unhealthy: 'danger' };

function Row({ label, value, valueColor = 'text', testID }) {
  const theme = useTheme();
  return (
    <View accessible accessibilityLabel={label + ': ' + value} style={{ flexDirection: 'row', flexWrap: 'wrap', justifyContent: 'space-between', gap: theme.spacing.sm }}>
      <AppText color="textMuted">{label}</AppText>
      <AppText testID={testID} color={valueColor} style={{ fontWeight: '600', flexShrink: 1, textAlign: 'right' }}>
        {value}
      </AppText>
    </View>
  );
}

/** About this app: app version, configured server, and a live GET /api/v1/health check. */
export default function AboutScreen() {
  const theme = useTheme();
  const { loading, data, error, retry } = useServerHealth();
  const unreachable = error && !error.status;

  let health;
  if (loading) {
    health = (
      <View accessible accessibilityLabel={strings.about.checking} style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}>
        <ActivityIndicator color={theme.colors.primary} />
        <AppText color="textMuted">{strings.about.checking}</AppText>
      </View>
    );
  } else if (error) {
    health = (
      <View style={{ gap: theme.spacing.md }} testID="health-error">
        <AppText variant="heading" color="danger" accessibilityRole="alert">
          {unreachable ? strings.about.unreachableTitle : strings.about.errorTitle}
        </AppText>
        <AppText color="textMuted">{unreachable ? strings.about.unreachableBody : error.message}</AppText>
        <Button label={strings.common.retry} accessibilityHint={strings.about.retryHint} onPress={retry} />
      </View>
    );
  } else {
    const status = data && data.status;
    health = (
      <View style={{ gap: theme.spacing.md }} testID="health-result">
        <Row label={strings.about.status} value={strings.about.statusLabels[status] || String(status)} valueColor={STATUS_COLORS[status] || 'text'} testID="health-status" />
        <Row label={strings.about.version} value={String(data.version)} testID="health-version" />
        <Row label={strings.about.database} value={String(data.database)} testID="health-database" />
        <Row label={strings.about.uptime} value={formatUptime(data.uptimeSeconds)} testID="health-uptime" />
        <Button label={strings.common.retry} accessibilityHint={strings.about.retryHint} variant="secondary" onPress={retry} />
      </View>
    );
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.xl }}>
      <View style={{ gap: theme.spacing.sm }}>
        <AppText variant="heading" accessibilityRole="header">
          {strings.about.appSection}
        </AppText>
        <Card>
          <Row label={strings.about.appVersion} value={getAppVersion()} testID="app-version" />
          <Row label={strings.about.apiUrl} value={resolveApiUrl()} testID="api-url" />
        </Card>
      </View>
      <View style={{ gap: theme.spacing.sm }}>
        <AppText variant="heading" accessibilityRole="header">
          {strings.about.serverSection}
        </AppText>
        <Card>{health}</Card>
      </View>
    </Screen>
  );
}
