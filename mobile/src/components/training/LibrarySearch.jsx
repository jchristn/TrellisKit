import { useRouter } from 'expo-router';
import { useEffect, useState } from 'react';
import { ScrollView, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import { LessonRow, SeriesCard } from '../library/LibraryCards';

const t = strings.training.library;

/** Shortest and longest unified search query (characters). */
export const LibrarySearchMinLength = 2;
export const LibrarySearchMaxLength = 100;

/**
 * The `POST /library/search` body for a query: courses and lessons always, techniques when the
 * Techniques segment is available. Null when the query is too short.
 * @param {string} query
 * @param {{ techniques?: boolean, maxResultsPerKind?: number }} [options]
 * @returns {object|null}
 */
export function librarySearchBody(query, { techniques = false, maxResultsPerKind = 10 } = {}) {
  const text = String(query || '').trim().slice(0, LibrarySearchMaxLength);
  if (text.length < LibrarySearchMinLength) return null;
  return { query: text, kinds: techniques ? ['Series', 'Lesson', 'CatalogEntry'] : ['Series', 'Lesson'], maxResultsPerKind };
}

const objectsOf = (page) => ((page && Array.isArray(page.objects) && page.objects) || []).filter(Boolean);

/**
 * Unified Library search results: Courses, Lessons, and Techniques from one request, each group
 * shown only when it has hits. The query is sent 300 ms after typing stops.
 * @param {{ query: string, techniques?: boolean, testID?: string }} props
 */
export default function LibrarySearch({ query, techniques = false, testID = 'library-search' }) {
  const { api } = useAuth();
  const router = useRouter();
  const theme = useTheme();
  const [state, setState] = useState({ status: 'idle', result: null });
  const [attempt, setAttempt] = useState(0);
  const body = librarySearchBody(query, { techniques });
  const key = body ? JSON.stringify(body) : '';
  useEffect(() => {
    if (!key) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setState({ status: 'idle', result: null });
      return undefined;
    }
    let active = true;
    setState((current) => ({ status: 'loading', result: current.result }));
    const timer = setTimeout(() => {
      api.searchLibrary(JSON.parse(key)).then(
        (result) => active && setState({ status: 'ready', result }),
        () => active && setState({ status: 'error', result: null })
      );
    }, 300);
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [api, key, attempt]);

  if (!body) {
    return (
      <View style={{ padding: theme.spacing.lg }} testID={testID + '-short'}>
        <AppText color="textMuted">{t.searchTooShort}</AppText>
      </View>
    );
  }
  const series = objectsOf(state.result && state.result.series);
  const lessons = objectsOf(state.result && state.result.lessons);
  const entries = techniques ? objectsOf(state.result && state.result.techniques) : [];
  const empty = state.status === 'ready' && !series.length && !lessons.length && !entries.length;
  return (
    <ScrollView contentContainerStyle={{ padding: theme.spacing.lg, gap: theme.spacing.md }} keyboardShouldPersistTaps="handled" testID={testID}>
      {state.status === 'loading' ? <Loading label={t.searching} /> : null}
      {state.status === 'error' ? <ErrorState title={t.searchFailed} onRetry={() => setAttempt((n) => n + 1)} testID={testID + '-error'} /> : null}
      {empty ? (
        <AppText color="textMuted" testID={testID + '-empty'}>
          {t.noResults}
        </AppText>
      ) : null}
      {series.length ? (
        <View style={{ gap: theme.spacing.sm }} testID={testID + '-series'}>
          <AppText variant="heading" accessibilityRole="header">
            {t.resultsSeries}
          </AppText>
          {series.map((item) => (
            <SeriesCard key={item.id} series={item} onPress={() => router.push('/series/' + encodeURIComponent(item.id))} testID={testID + '-series-' + item.id} />
          ))}
        </View>
      ) : null}
      {lessons.length ? (
        <View style={{ gap: theme.spacing.sm }} testID={testID + '-lessons'}>
          <AppText variant="heading" accessibilityRole="header">
            {t.resultsLessons}
          </AppText>
          {lessons.map((lesson) => (
            <LessonRow key={lesson.id} lesson={lesson} onPress={() => router.push('/lessons/' + encodeURIComponent(lesson.id))} testID={testID + '-lesson-' + lesson.id} />
          ))}
        </View>
      ) : null}
      {entries.length ? (
        <View style={{ gap: theme.spacing.sm }} testID={testID + '-techniques'}>
          <AppText variant="heading" accessibilityRole="header">
            {t.resultsTechniques}
          </AppText>
          {entries.map((entry) => (
            <Button key={entry.id} label={entry.name || entry.slug || entry.id} variant="ghost" onPress={() => router.push('/catalog/' + encodeURIComponent(entry.slug || entry.id))} style={{ alignSelf: 'auto' }} testID={testID + '-technique-' + entry.id} />
          ))}
        </View>
      ) : null}
    </ScrollView>
  );
}
