import { Stack, useLocalSearchParams, useRouter } from 'expo-router';
import { collectEnumeration } from '@trelliskit/shared';
import { useCallback, useMemo, useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../../src/auth/AuthProvider';
import { isSystemAdmin } from '../../../src/auth/routing';
import AppText from '../../../src/components/AppText';
import Button from '../../../src/components/Button';
import ChipGroup from '../../../src/components/ChipGroup';
import ErrorState from '../../../src/components/ErrorState';
import Loading from '../../../src/components/Loading';
import Screen from '../../../src/components/Screen';
import { describeContentError } from '../../../src/content/errors';
import { useCursorList } from '../../../src/hooks/useCursorList';
import { useResource } from '../../../src/hooks/useResource';
import { useProperty } from '../../../src/property';
import strings from '../../../src/strings';
import { useTheme } from '../../../src/theme';

const t = strings.nativeMetrics;
const first = (value) => Array.isArray(value) ? value[0] : value;

/** Bucket boundaries are displayed in the metric calendar, not the device calendar. */
export function metricDate(value, timeZone, withTime = false) {
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return t.unavailable;
  const options = { timeZone: timeZone || 'UTC', year: 'numeric', month: 'short', day: 'numeric', ...(withTime ? { hour: 'numeric', minute: '2-digit' } : {}) };
  try { return new Intl.DateTimeFormat(undefined, options).format(date); } catch { return date.toISOString(); }
}

/** Missing, suppressed and unfinished observations never masquerade as zero. */
export function metricValue(row) {
  if (row.suppressed) return t.suppressed;
  if (row.complete === false) return t.partial;
  return typeof row.value === 'number' && Number.isFinite(row.value) ? row.value.toLocaleString(undefined, { maximumFractionDigits: 3 }) : t.unavailable;
}

/** Each chart keeps its own bounded observation cursor; paging never loses comparison periods. */
function NativeMetric({ api, chart, definition, range }) {
  const theme = useTheme();
  const [expanded, setExpanded] = useState(false);
  const [context, setContext] = useState(null);
  const onPage = useCallback((result) => setContext({ timeZone: result.timeZone || 'UTC', unit: result.unit || '', completeThroughUtc: result.completeThroughUtc }), []);
  const query = useMemo(() => ({ metric: chart.metric, from: range.from, to: range.to, bucket: definition.buckets?.includes('Day') ? 'Day' : definition.buckets?.[0] || 'Day', compare: true, ...(chart.groupBy ? { groupBy: chart.groupBy } : {}), percentile: chart.percentile || .95 }), [chart, definition, range]);
  const fetchPage = useCallback((continuationToken) => api.adminQueryMetrics({ maxResults: 50, ordering: 'CreatedDescending', ...(continuationToken ? { continuationToken } : {}), query }), [api, query]);
  const list = useCursorList(fetchPage, { key: JSON.stringify(query), onPage });
  const unit = context?.unit || definition.unit || '';
  const rows = list.status === 'ready' ? list.items : [];
  const current = rows.filter((row) => row.period !== 'Previous');
  const latest = current.slice().sort((a, b) => String(b.startUtc).localeCompare(String(a.startUtc)))[0];
  const points = current.slice().sort((a, b) => String(a.startUtc).localeCompare(String(b.startUtc))).slice(-14);
  const max = Math.max(1, ...points.map((row) => row.complete !== false && !row.suppressed && Number.isFinite(row.value) ? Math.abs(row.value) : 0));
  return <View testID={'metric-' + chart.id} style={{ borderWidth: 1, borderColor: theme.colors.border, borderRadius: 12, padding: theme.spacing.md, gap: theme.spacing.sm }}>
    <AppText variant="heading" accessibilityRole="header">{chart.title || definition.title || chart.metric}</AppText>
    {list.status === 'loading' ? <Loading label={strings.common.loading} /> : null}
    {list.status === 'error' ? <ErrorState title={t.loadFailed} message={describeContentError(list.error)} onRetry={list.reload} /> : null}
    {list.status === 'ready' && !rows.length ? <AppText>{t.empty}</AppText> : null}
    {context ? <AppText variant="caption">{t.timeZone(context.timeZone)}</AppText> : null}
    {context?.completeThroughUtc ? <AppText variant="caption">{t.completeThrough(metricDate(context.completeThroughUtc, context.timeZone, true))}</AppText> : null}
    {latest?.seriesKey && latest.seriesKey !== 'all' ? <AppText variant="label">{latest.seriesKey}</AppText> : null}
    {latest ? <AppText variant="title" testID={'metric-' + chart.id + '-latest'}>{metricValue(latest)} {unit}</AppText> : null}
    {points.length ? <View style={{ gap: theme.spacing.xs }}>
      {points.map((row, index) => <View key={row.id || index} accessibilityLabel={[row.seriesKey, row.startUtc, metricValue(row), unit].filter(Boolean).join(', ')} style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}>
        <AppText variant="caption" style={{ width: 90 }}>{metricDate(row.startUtc, context?.timeZone)}</AppText>
        <View style={{ flex: 1, height: 8, backgroundColor: theme.colors.border }}>
          {row.complete !== false && !row.suppressed && Number.isFinite(row.value) ? <View style={{ height: 8, width: Math.abs(row.value) / max * 100 + '%', backgroundColor: theme.colors.primary }} /> : null}
        </View>
        <AppText variant="caption">{metricValue(row)}</AppText>
      </View>)}
    </View> : null}
    {rows.length ? <Button label={expanded ? t.hideData : t.showData} variant="ghost" onPress={() => setExpanded(!expanded)} testID={'metric-' + chart.id + '-toggle'} /> : null}
    {expanded ? rows.map((row, index) => <View key={row.id || index} testID={'metric-observation-' + (row.id || index)} style={{ gap: theme.spacing.xxs, borderTopWidth: 1, borderColor: theme.colors.border, paddingTop: theme.spacing.sm }}>
      <AppText variant="label">{row.period === 'Previous' ? t.previous : t.current}{row.seriesKey ? ' · ' + row.seriesKey : ''}</AppText>
      <AppText variant="caption">{metricDate(row.startUtc, context?.timeZone, true)}</AppText>
      <AppText>{metricValue(row)} {unit}</AppText>
    </View>) : null}
    {list.moreError ? <ErrorState title={t.loadFailed} message={describeContentError(list.moreError)} onRetry={list.loadMore} /> : null}
    {list.hasMore ? <Button label={t.loadMore} onPress={list.loadMore} disabled={list.loadingMore} testID={'metric-' + chart.id + '-more'} /> : null}
  </View>;
}

function MetricsContent({ api, params }) {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const pageId = first(params.pageId) || 'health';
  const [days, setDays] = useState('7');
  const [now, setNow] = useState(() => new Date().toISOString());
  const range = useMemo(() => ({ from: new Date(Date.parse(now) - Number(days) * 86400000).toISOString(), to: now }), [days, now]);
  const { data, status, error, reload } = useResource(async () => {
    const [pages, definitions] = await Promise.all([
      collectEnumeration((query) => api.adminMetricPages(query), { maxResults: 50 }, { maxPages: 10 }),
      collectEnumeration((query) => api.adminMetricRegistry(query), { maxResults: 50 }, { maxPages: 10 })
    ]);
    return { pages: pages.objects, definitions: definitions.objects };
  }, 'catalog');
  const page = data?.pages.find((item) => item.id === pageId);
  const byKey = new Map((data?.definitions || []).map((item) => [item.key, item]));
  const charts = (page?.charts || []).filter((chart) => byKey.has(chart.metric) && property.features?.[chart.feature || byKey.get(chart.metric).feature] !== false);
  const title = first(params.notificationTitle);
  const body = first(params.notificationBody);
  return <Screen testID="screen-native-metrics" contentStyle={{ gap: theme.spacing.lg }}>
    <Stack.Screen options={{ title: page?.title || t.title }} />
    {title ? <AppText variant="heading" accessibilityRole="header">{title.slice(0, 1000)}</AppText> : null}
    {body ? <AppText>{body.slice(0, 8000)}</AppText> : null}
    {status === 'loading' ? <Loading label={strings.common.loading} /> : null}
    {status === 'error' ? <ErrorState testID="native-metrics-error" title={error?.status === 403 ? t.deniedTitle : t.loadFailed} message={error?.status === 403 ? t.deniedBody : describeContentError(error)} onRetry={reload} /> : null}
    {data && status === 'ready' ? <>
      <AppText variant="title" accessibilityRole="header">{page?.title || t.title}</AppText>
      {page?.question ? <AppText>{page.question}</AppText> : null}
      <ChipGroup label={t.pageLabel} options={data.pages.map((item) => ({ value: item.id, label: item.title }))} value={pageId} onToggle={(id) => router.setParams({ pageId: id })} testID="metrics-pages" />
      {!page ? <AppText>{t.unknownPage}</AppText> : null}
      <ChipGroup label={t.rangeLabel} options={[{ value: '7', label: t.week }, { value: '30', label: t.month }]} value={days} onToggle={setDays} testID="metrics-range" />
      <Button label={t.refresh} variant="secondary" onPress={() => { setNow(new Date().toISOString()); reload(); }} testID="metrics-refresh" />
      {charts.map((chart) => <NativeMetric key={pageId + '/' + chart.id} api={api} chart={chart} definition={byKey.get(chart.metric)} range={range} />)}
    </> : null}
  </Screen>;
}

/** Native property-neutral metrics honor the same SystemAdmin authorization as the API. */
export default function NativeMetricsScreen() {
  const { api, session } = useAuth();
  const params = useLocalSearchParams();
  if (!isSystemAdmin(session.user)) return <Screen testID="native-metrics-denied"><ErrorState title={t.deniedTitle} message={t.deniedBody} /></Screen>;
  return <MetricsContent api={api} params={params} />;
}
