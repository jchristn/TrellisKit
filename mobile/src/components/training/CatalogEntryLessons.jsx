import { useRouter } from 'expo-router';
import { useCallback } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { useCursorList } from '../../hooks/useCursorList';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import { LessonRow } from '../library/LibraryCards';

const t = strings.training.links;

/**
 * "Lessons that teach this" for a catalog entry: `POST /catalog/entries/{id}/lessons/enumerate`
 * for members (locked stubs where gated) and `GET /public/catalog/entries/{id}/lessons` signed
 * out. A lesson opens its own screen, so Back returns here. Nothing renders while the list is
 * empty or the links are unavailable.
 * @param {{ entryId: string, testID?: string }} props
 */
export default function CatalogEntryLessons({ entryId, testID = 'catalog-lessons' }) {
  const { api, user } = useAuth();
  const router = useRouter();
  const theme = useTheme();
  const signedIn = Boolean(user);
  const enabled = Boolean(api && entryId && typeof (signedIn ? api.listCatalogEntryLessons : api.listPublicCatalogEntryLessons) === 'function');
  const fetchPage = useCallback(
    (continuationToken) => (signedIn ? api.listCatalogEntryLessons(entryId, { maxResults: 25, continuationToken }) : api.listPublicCatalogEntryLessons(entryId)),
    [api, entryId, signedIn]
  );
  const list = useCursorList(fetchPage, { key: entryId + ':' + signedIn, enabled });
  if (!enabled || !list.items.length) return null;
  return (
    <Card testID={testID}>
      <AppText variant="heading" accessibilityRole="header">
        {t.lessonsTeaching}
      </AppText>
      <View style={{ gap: theme.spacing.sm }}>
        {list.items.map((lesson) => (
          <LessonRow key={lesson.id} lesson={lesson} onPress={() => router.push('/lessons/' + encodeURIComponent(lesson.id))} testID={testID + '-' + lesson.id} />
        ))}
      </View>
      {signedIn && list.hasMore && !list.loadingMore ? <Button label={t.more} variant="secondary" onPress={list.loadMore} /> : null}
    </Card>
  );
}
