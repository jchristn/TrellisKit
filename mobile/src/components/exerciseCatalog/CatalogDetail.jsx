import { CatalogApparatusFields, CatalogDescriptorFields, catalogDescriptorOptions, catalogDescriptorProfile } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useCallback } from 'react';
import { ScrollView, View } from 'react-native';
import { openAppLink } from '../../links';
import { useAuth } from '../../auth/AuthProvider';
import { useCursorList } from '../../hooks/useCursorList';
import { useResource } from '../../hooks/useResource';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import { SaveRow } from '../SaveButton';
import CatalogEntryLessons from '../training/CatalogEntryLessons';

const t = strings.exerciseCatalog;
const safeUrl = (value) => { try { const url = new URL(value); return ['http:', 'https:'].includes(url.protocol) ? url.href : null; } catch { return null; } };
const label = (config, group, value) => config?.[group]?.find((item) => item.key === value)?.label || value;

function Relationships({ api, entryId, signedIn }) {
  const router = useRouter();
  const list = useCursorList(useCallback((continuationToken) => api[signedIn ? 'enumerateCatalogRelationships' : 'enumeratePublicCatalogRelationships'](entryId, { maxResults: 25, continuationToken }), [api, entryId, signedIn]), { key: entryId + signedIn });
  return <Card><AppText variant="heading">{t.related}</AppText>{list.items.map((edge) => <View key={edge.id}><Button label={edge.targetName || edge.targetId} variant="ghost" onPress={() => router.push('/catalog/' + encodeURIComponent(edge.targetSlug || edge.targetId))} /><AppText>{t.relationshipKinds[edge.kind] || edge.kind}</AppText>{edge.note ? <AppText>{edge.note}</AppText> : null}</View>)}{list.status === 'loading' || list.loadingMore ? <Loading label={t.loading} /> : null}{list.status === 'ready' && !list.items.length ? <AppText>{t.noRelated}</AppText> : null}{list.error || list.moreError ? <ErrorState title={t.error} onRetry={list.moreError ? list.loadMore : list.reload} /> : null}{list.hasMore && !list.loadingMore && !list.moreError ? <Button label={t.more} onPress={list.loadMore} /> : null}</Card>;
}

export default function CatalogDetail({ entryId }) {
  const { api, user } = useAuth();
  const theme = useTheme();
  const router = useRouter();
  const signedIn = Boolean(user);
  const resource = useResource(() => api[signedIn ? 'getCatalogEntry' : 'getPublicCatalogEntry'](entryId), entryId + signedIn);
  const config = useResource(() => api.getExerciseCatalogConfig(), 'exercise-catalog-config');
  if (resource.status === 'loading') return <Loading label={t.loading} />;
  if (resource.status !== 'ready') return <ErrorState title={t.error} onRetry={resource.reload} />;
  const entry = resource.data;
  const groups = ['aliases', 'primaryMuscles', 'secondaryMuscles', 'equipment', 'instructions', 'cues', 'commonErrors', 'regressions', 'progressions', 'substitutions', 'contraindications'];
  return <ScrollView contentContainerStyle={{ padding: theme.spacing.lg, gap: theme.spacing.md }}><SaveRow targetType="CatalogEntry" targetId={entry.id} title={entry.name} testID="catalog-save"><AppText variant="title" accessibilityRole="header">{entry.name}</AppText></SaveRow><AppText color="textMuted">{[t.kinds[entry.kind], t.difficulties[entry.difficulty], label(config.data, 'programs', entry.program)].filter(Boolean).join(' · ')}</AppText>{entry.summary ? <AppText>{entry.summary}</AppText> : null}
    {['movementPattern', 'plane', 'laterality', 'techniqueType', 'fromPosition', 'toPosition'].filter((key) => entry[key]).map((key) => <View key={key}><AppText variant="label">{t.fields[key]}</AppText><AppText>{entry[key]}</AppText></View>)}
    {CatalogDescriptorFields.filter(({ key }) => entry.modalityDetails?.[key]).map(({ key, group, labelKey }) => <Card key={key}><AppText variant="heading">{catalogDescriptorProfile(config.data, entry.program)?.[labelKey] || t[key]}</AppText><AppText>{catalogDescriptorOptions(config.data, entry.program, group, entry.modalityDetails[key]).find((item) => item.key === entry.modalityDetails[key])?.label}</AppText>{key === 'levelKey' ? <AppText color="textMuted">{t.levelHint}</AppText> : null}{key === 'notationSystemKey' && entry.modalityDetails.notationText ? <AppText>{entry.modalityDetails.notationText}</AppText> : null}</Card>)}
    {entry.modalityDetails?.localizedNames?.length ? <Card><AppText variant="heading">{t.localizedNames}</AppText>{entry.modalityDetails.localizedNames.map((name, index) => <View key={index}><AppText>{name.text}</AppText><AppText color="textMuted">{catalogDescriptorOptions(config.data, entry.program, 'nameLanguages', name.languageTag).find((item) => item.key === name.languageTag)?.label}</AppText></View>)}</Card> : null}
    {CatalogApparatusFields.some((key) => entry.apparatusSetup?.[key]) ? <Card><AppText variant="heading">{t.apparatus.title}</AppText>{CatalogApparatusFields.filter((key) => entry.apparatusSetup[key]).map((key) => <View key={key}><AppText variant="label">{t.apparatus[key]}</AppText><AppText>{key === 'apparatusKey' ? label(config.data, 'equipment', entry.apparatusSetup[key]) : entry.apparatusSetup[key]}</AppText></View>)}{entry.apparatusSetup.springSetupText ? <AppText color="textMuted">{t.apparatus.springHint}</AppText> : null}</Card> : null}
    {groups.filter((key) => entry[key]?.length).map((key) => <Card key={key}><AppText variant="heading">{t.fields[key]}</AppText>{entry[key].map((value, index) => <AppText key={index}>{key === 'instructions' ? (index + 1) + '. ' : '• '}{label(config.data, ['primaryMuscles', 'secondaryMuscles'].includes(key) ? 'muscles' : key, value)}</AppText>)}</Card>)}
    {entry.sources?.length ? <Card><AppText variant="heading">{t.sources}</AppText>{entry.sources.map((source, index) => <View key={index}>{safeUrl(source.url) ? <Button label={source.title || source.url} variant="ghost" onPress={() => openAppLink(safeUrl(source.url), { router })} /> : <AppText>{source.title}</AppText>}{source.license ? <AppText>{source.license}</AppText> : null}{source.attribution ? <AppText>{source.attribution}</AppText> : null}</View>)}</Card> : null}
    <CatalogEntryLessons entryId={entry.id} />
    <Relationships api={api} entryId={entry.id} signedIn={signedIn} />
  </ScrollView>;
}
