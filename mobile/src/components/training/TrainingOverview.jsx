import Ionicons from '@expo/vector-icons/Ionicons';
import { formatEventShort, libraryConfig } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { useEffect, useState } from 'react';
import { Pressable, RefreshControl, ScrollView, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { featureOn } from '../../content/phase4';
import { useLayout } from '../../hooks/useLayout';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import { libraryOpen, saveTargetPath, segmentHref, upNextPath } from '../../training/hub';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import { LessonRow, SeriesCard } from '../library/LibraryCards';
import { ContinueRail } from '../library/LibraryHome';
import { CurriculumCard, RankCard } from '../ranks';

const t = strings.training;
const o = t.overview;

function CardTitle({ icon, children }) {
  const theme = useTheme();
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm }}>
      <Ionicons name={icon} size={20} color={theme.colors.primary} />
      <AppText variant="heading" accessibilityRole="header" style={{ flex: 1 }}>
        {children}
      </AppText>
    </View>
  );
}

function ListRow({ title, subtitle, onPress, disabled = false, testID }) {
  const theme = useTheme();
  return (
    <Pressable
      onPress={disabled ? undefined : onPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityLabel={[title, subtitle].filter(Boolean).join(', ')}
      accessibilityState={{ disabled }}
      testID={testID}
      style={({ pressed }) => ({ minHeight: theme.layout.minTouchTarget, flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm, paddingVertical: theme.spacing.xs, opacity: disabled ? 0.6 : pressed ? 0.7 : 1 })}
    >
      <View style={{ flex: 1 }}>
        <AppText style={{ fontWeight: '600' }}>{title}</AppText>
        {subtitle ? (
          <AppText variant="caption" color="textMuted">
            {subtitle}
          </AppText>
        ) : null}
      </View>
      {disabled ? null : <Ionicons name="chevron-forward" size={18} color={theme.colors.textMuted} />}
    </Pressable>
  );
}

/** Real featured courses (the first series in library order); nothing renders when there are none. */
export function FeaturedCourses({ testID = 'training-featured' }) {
  const { api } = useAuth();
  const router = useRouter();
  const theme = useTheme();
  const [items, setItems] = useState([]);
  useEffect(() => {
    let active = true;
    Promise.resolve(api && api.enumerateSeries ? api.enumerateSeries({ maxResults: 3, ordering: 'SortOrderAscending' }) : null)
      .then((page) => active && setItems(((page && page.objects) || []).filter(Boolean).slice(0, 3)))
      .catch(() => active && setItems([]));
    return () => {
      active = false;
    };
  }, [api]);
  if (!items.length) return null;
  return (
    <View style={{ gap: theme.spacing.sm }} testID={testID}>
      <AppText variant="label">{o.featured}</AppText>
      {items.map((series) => (
        <SeriesCard key={series.id} series={series} onPress={() => router.push('/series/' + encodeURIComponent(series.id))} testID={testID + '-' + series.id} />
      ))}
    </View>
  );
}

function UpNextCard({ page }) {
  const router = useRouter();
  return (
    <Card testID="training-up-next">
      <CardTitle icon="calendar-outline">{o.upNext}</CardTitle>
      {page.objects.map((item) => {
        const path = upNextPath(item);
        let when = '';
        try {
          when = formatEventShort(item);
        } catch {
          when = '';
        }
        return <ListRow key={(item.kind || 'Event') + ':' + item.id} title={item.title} subtitle={[when, item.locationSummary].filter(Boolean).join(' · ')} disabled={!path} onPress={() => router.push(path)} testID={'training-up-next-' + item.id} />;
      })}
    </Card>
  );
}

function ContinueCard({ page }) {
  const router = useRouter();
  return (
    <Card testID="training-continue">
      <ContinueRail items={page.objects} title={o.continueTraining} onOpenLesson={(lesson) => router.push('/lessons/' + encodeURIComponent(lesson.id))} testID="training-continue-rail" />
    </Card>
  );
}

function RankCards({ rank }) {
  const router = useRouter();
  const theme = useTheme();
  return (
    <View style={{ gap: theme.spacing.md }} testID="training-rank">
      {rank.entries.map((entry) => (
        <RankCard key={entry.rank.fieldKey || 'rank'} rank={entry.rank} next={entry.next} compact showVerificationLabels={rank.showVerificationLabels} onPress={() => router.push('/rank')} testID={'training-rank-' + (entry.rank.fieldKey || 'rank')} />
      ))}
      {rank.curricula.map((curriculum) => (
        <CurriculumCard key={curriculum.id} curriculum={curriculum} onPress={() => router.push('/training/curricula/' + encodeURIComponent(curriculum.id))} testID={'training-curriculum-' + curriculum.id} />
      ))}
    </View>
  );
}

function CoachCard() {
  const router = useRouter();
  return (
    <Card testID="training-coach">
      <CardTitle icon="sparkles-outline">{o.coachTitle}</CardTitle>
      <AppText color="textMuted">{o.coachBody}</AppText>
      <Button label={o.coachOpen} onPress={() => router.push('/assistants')} testID="training-coach-open" />
    </Card>
  );
}

function RecentSavesCard({ page }) {
  const router = useRouter();
  return (
    <Card testID="training-recent-saves">
      <CardTitle icon="bookmark-outline">{o.recentlySaved}</CardTitle>
      {page.objects.map((save) => {
        const available = Boolean(save.target && save.target.available);
        const path = available ? saveTargetPath(save.targetType, save.targetId) : null;
        const title = available ? save.target.title || t.saved.type[save.targetType] || save.targetType : t.saved.unavailable;
        return <ListRow key={save.id || save.targetType + ':' + save.targetId} title={title} subtitle={t.saved.type[save.targetType] || null} disabled={!path} onPress={() => router.push(path)} testID={'training-save-' + (save.id || save.targetId)} />;
      })}
      <Button label={o.seeAllSaved} variant="secondary" onPress={() => router.replace(segmentHref('saved'))} testID="training-see-saved" />
    </Card>
  );
}

function NewForYouCard({ page }) {
  const router = useRouter();
  const theme = useTheme();
  return (
    <Card testID="training-new">
      <CardTitle icon="sparkles-outline">{o.newForYou}</CardTitle>
      <View style={{ gap: theme.spacing.sm }}>
        {page.objects.map((lesson) => (
          <LessonRow key={lesson.id} lesson={lesson} onPress={() => router.push('/lessons/' + encodeURIComponent(lesson.id))} testID={'training-new-' + lesson.id} />
        ))}
      </View>
    </Card>
  );
}

function DownloadsCard() {
  const router = useRouter();
  return (
    <Card testID="training-downloads">
      <CardTitle icon="download-outline">{o.downloadsOffline}</CardTitle>
      <AppText color="textMuted">{o.downloadsOfflineBody}</AppText>
      <Button label={t.downloads} variant="secondary" onPress={() => router.push('/library/downloads')} testID="training-downloads-open" />
    </Card>
  );
}

function FirstRunCard() {
  const router = useRouter();
  const theme = useTheme();
  const property = useProperty();
  const { user } = useAuth();
  const courses = libraryOpen(property, user);
  const places = featureOn(property, 'directory');
  const events = featureOn(property, 'events');
  return (
    <Card testID="training-first-run">
      <CardTitle icon="flag-outline">{o.firstRunTitle}</CardTitle>
      <AppText color="textMuted">{o.firstRunBody}</AppText>
      {courses ? <FeaturedCourses testID="training-first-run-featured" /> : null}
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
        {courses ? <Button label={o.startCourse} onPress={() => router.replace(segmentHref('courses'))} style={{ alignSelf: 'auto' }} testID="training-start-course" /> : null}
        {places ? <Button label={o.findPlaces} variant="secondary" onPress={() => router.push('/map?section=places')} style={{ alignSelf: 'auto' }} testID="training-find-places" /> : null}
        {events ? <Button label={o.findEvents} variant="secondary" onPress={() => router.push('/map?section=events')} style={{ alignSelf: 'auto' }} testID="training-find-events" /> : null}
      </View>
    </Card>
  );
}

/**
 * The cards of an overview, in display order (plan section 3.1): Up next, Continue training, Your
 * rank and curriculum, Ask your coach, Recently saved, and New for you, each only with real data.
 * Offline, a Downloads card leads to the lessons on this device.
 * @param {object} overview Normalized overview.
 * @param {{ offline?: boolean, downloads?: boolean }} [context]
 * @returns {{ key: string, node: React.ReactNode }[]}
 */
export function overviewCards(overview, { offline = false, downloads = false } = {}) {
  const cards = [];
  if (overview.upNext.objects.length) cards.push({ key: 'upNext', node: <UpNextCard page={overview.upNext} /> });
  if (overview.continue.objects.length) cards.push({ key: 'continue', node: <ContinueCard page={overview.continue} /> });
  if (offline && downloads) cards.push({ key: 'downloads', node: <DownloadsCard /> });
  if (overview.rank) cards.push({ key: 'rank', node: <RankCards rank={overview.rank} /> });
  if (overview.coach.available) cards.push({ key: 'coach', node: <CoachCard /> });
  if (overview.recentSaves.objects.length) cards.push({ key: 'recentSaves', node: <RecentSavesCard page={overview.recentSaves} /> });
  if (overview.newForYou.objects.length) cards.push({ key: 'newForYou', node: <NewForYouCard page={overview.newForYou} /> });
  return cards;
}

/**
 * Training Overview: the cards with real data, or the first-run card (real featured courses and
 * the places or events entry) when the member has nothing yet. Tablets (768 points and wider) lay
 * the cards out in two columns.
 * @param {{ overview: object, offline?: boolean, refreshing?: boolean, onRefresh?: () => void }} props
 */
export default function TrainingOverview({ overview, offline = false, refreshing = false, onRefresh }) {
  const theme = useTheme();
  const property = useProperty();
  const { user } = useAuth();
  const { isWide } = useLayout();
  const downloads = Boolean(user && libraryConfig(property).downloadsEnabled);
  const cards = overviewCards(overview, { offline, downloads });
  const showFirstRun = overview.firstRun || !cards.length;
  const all = [...(showFirstRun ? [{ key: 'firstRun', node: <FirstRunCard /> }] : []), ...cards];
  const columns = isWide ? [all.filter((_, i) => i % 2 === 0), all.filter((_, i) => i % 2 === 1)] : [all];
  return (
    <ScrollView
      testID="training-overview"
      contentContainerStyle={{ padding: theme.spacing.lg, gap: theme.spacing.lg, flexDirection: isWide ? 'row' : 'column', alignItems: 'flex-start' }}
      refreshControl={onRefresh ? <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={theme.colors.primary} /> : undefined}
    >
      {columns.map((column, index) => (
        <View key={index} style={{ flex: isWide ? 1 : undefined, alignSelf: 'stretch', gap: theme.spacing.lg }} testID={isWide ? 'training-overview-column-' + index : undefined}>
          {column.map((card) => (
            <View key={card.key}>{card.node}</View>
          ))}
        </View>
      ))}
    </ScrollView>
  );
}
