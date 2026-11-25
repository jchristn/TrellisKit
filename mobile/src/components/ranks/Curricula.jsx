import { catalogFeatureEnabled, groupCurriculumItems } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { useCursorList } from '../../hooks/useCursorList';
import { useResource } from '../../hooks/useResource';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import Screen from '../Screen';
import CurriculumCard from './CurriculumCard';
import ProgressBar from './ProgressBar';
import { useRanksFeature } from './rankHooks';

/**
 * Member curricula: `CurriculaList` (`/curricula`) and `CurriculumDetail` (`/curricula/{id}`,
 * props `curriculumId`) with items grouped by rank and self-completion where allowed.
 */
export function curriculumItemRoute(item, property) {
  if (!item || !item.targetId || item.targetAvailable === false) return null;
  const id = encodeURIComponent(item.targetId);
  if (item.itemType === 'Lesson') return '/lessons/' + id;
  if (item.itemType === 'Series') return '/series/' + id;
  if (item.itemType === 'CatalogEntry' && catalogFeatureEnabled(property)) return '/catalog/' + id;
  return null;
}

export function CurriculaList() {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const { enabled } = useRanksFeature();
  const t = strings.ranks.curricula;
  const fetchPage = useCallback((cursor) => api.enumerateCurricula({ maxResults: 25, continuationToken: cursor }), [api]);
  const list = useCursorList(fetchPage, { enabled });
  if (!enabled) return <Screen testID="curricula"><ErrorState title={strings.ranks.notAvailable} /></Screen>;
  return (
    <Screen testID="curricula">
      <View style={{ gap: theme.spacing.md }}>
        <AppText variant="title" accessibilityRole="header">{t.title}</AppText>
        {list.status === 'loading' ? <Loading label={t.loading} /> : null}
        {list.status === 'error' ? <ErrorState title={t.error} onRetry={list.reload} /> : null}
        {list.status === 'ready' && !list.items.length ? <AppText color="textMuted">{t.empty}</AppText> : null}
        {list.items.map((c) => <CurriculumCard key={c.id} curriculum={c} onPress={() => router.push('/curricula/' + encodeURIComponent(c.id))} />)}
        {list.hasMore && !list.loadingMore ? <Button label={t.more} variant="secondary" onPress={list.loadMore} /> : null}
      </View>
    </Screen>
  );
}

function ItemRow({ item, api, curriculumId, property, onChange }) {
  const theme = useTheme();
  const router = useRouter();
  const t = strings.ranks.curricula;
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const title = item.title || item.customText || item.targetId || '';
  const route = curriculumItemRoute(item, property);
  const done = ['SelfComplete', 'Completed', 'Verified'].includes(item.state);
  const canToggle = item.selfCompletable && item.state !== 'Verified';
  const toggle = async () => {
    setBusy(true);
    setError('');
    try {
      const next = item.state === 'SelfComplete' ? await api.uncompleteCurriculumItem(curriculumId, item.id) : await api.completeCurriculumItem(curriculumId, item.id);
      onChange(next && next.id ? next : { ...item, state: item.state === 'SelfComplete' ? 'Incomplete' : 'SelfComplete' });
    } catch (err) {
      setError((err && err.code && strings.ranks.errors[err.code]) || strings.ranks.errors.generic);
    } finally {
      setBusy(false);
    }
  };
  return (
    <View style={{ gap: theme.spacing.xxs }} testID={'curriculum-item-' + item.id}>
      {route ? <Button label={title} variant="ghost" accessibilityLabel={t.open(title)} onPress={() => router.push(route)} /> : <AppText style={{ fontWeight: '600' }}>{title}</AppText>}
      <AppText variant="caption" color="textMuted">{[t.types[item.itemType] || item.itemType, item.required ? t.required : t.optional, t.states[item.state] || item.state].join(' · ')}</AppText>
      {item.targetAvailable === false ? <AppText variant="caption" color="textMuted">{t.unavailable}</AppText> : null}
      {!item.selfCompletable && !done ? <AppText variant="caption" color="textMuted">{t.autoComplete}</AppText> : null}
      {item.selfCompletable && item.required && item.state === 'SelfComplete' ? <AppText variant="caption" color="textMuted">{t.waitingVerification}</AppText> : null}
      {canToggle ? <Button label={item.state === 'SelfComplete' ? t.undo : t.complete} variant="secondary" disabled={busy} accessibilityLabel={item.state === 'SelfComplete' ? t.undoLabel(title) : t.completeLabel(title)} onPress={toggle} testID={'curriculum-item-' + item.id + '-toggle'} /> : null}
      {error ? <AppText accessibilityRole="alert" color="danger">{error}</AppText> : null}
    </View>
  );
}

export function CurriculumDetail({ curriculumId }) {
  const theme = useTheme();
  const { api } = useAuth();
  const property = useProperty();
  const { enabled, rankFields } = useRanksFeature();
  const t = strings.ranks.curricula;
  const resource = useResource(() => (enabled ? api.getCurriculum(curriculumId) : Promise.resolve(null)), curriculumId + enabled);
  const curriculum = resource.data;
  const field = useMemo(() => (curriculum && rankFields.find((f) => f.key === curriculum.fieldKey)) || rankFields[0] || null, [curriculum, rankFields]);
  const groups = useMemo(() => groupCurriculumItems(field, (curriculum && curriculum.items) || []), [field, curriculum]);
  if (!enabled) return <Screen testID="curriculum"><ErrorState title={strings.ranks.notAvailable} /></Screen>;
  const replace = (item) => resource.setData((current) => ({ ...current, items: (current.items || []).map((i) => (i.id === item.id ? { ...i, ...item } : i)) }));
  const items = (curriculum && curriculum.items) || [];
  const done = items.filter((i) => i.satisfied).length;
  return (
    <Screen testID="curriculum">
      <View style={{ gap: theme.spacing.lg }}>
        {resource.status === 'loading' ? <Loading label={t.loading} /> : null}
        {resource.status === 'error' || resource.status === 'gone' ? <ErrorState title={t.error} onRetry={resource.reload} /> : null}
        {curriculum ? (
          <>
            <AppText variant="title" accessibilityRole="header">{curriculum.name}</AppText>
            {curriculum.description ? <AppText color="textMuted">{curriculum.description}</AppText> : null}
            <AppText>{strings.ranks.curriculumProgress(done, items.length)}</AppText>
            <ProgressBar value={done} max={items.length} label={curriculum.name + ': ' + strings.ranks.curriculumProgress(done, items.length)} testID="curriculum-progress" />
            {groups.map((group) => (
              <Card key={group.optionKey || 'none'} style={{ gap: theme.spacing.md }} testID={'curriculum-group-' + (group.optionKey || 'none')}>
                <AppText variant="heading" accessibilityRole="header">{group.label || t.ungrouped}</AppText>
                <AppText variant="caption" color="textMuted">{strings.ranks.curriculumProgress(group.completed, group.total)}</AppText>
                <ProgressBar value={group.completed} max={group.total} label={(group.label || t.ungrouped) + ': ' + strings.ranks.curriculumProgress(group.completed, group.total)} />
                {group.items.map((item) => <ItemRow key={item.id} item={item} api={api} curriculumId={curriculum.id} property={property} onChange={replace} />)}
              </Card>
            ))}
          </>
        ) : null}
      </View>
    </Screen>
  );
}
