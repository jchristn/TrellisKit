import { formatDuration, isLockedItem, levelLabels, libraryConfig, lockedRequirement, progressPercent } from '@trelliskit/shared';
import { File, Paths } from 'expo-file-system';
import { useRouter } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeFeatureError, documentUrl, featureOn } from '../../content/phase4';
import { useIsOffline } from '../../hooks/useNetworkStatus';
import { useResource } from '../../hooks/useResource';
import { downloadLesson, findDownload, onDownloadsChange, removeDownload } from '../../library/downloads';
import { openInAppBrowser } from '../../links';
import { useMedia } from '../../media/MediaProvider';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import { AccessPromptCard, useAccessPrompt } from '../AccessPrompt';
import AppText from '../AppText';
import Button from '../Button';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import MarkdownBody from '../feed/MarkdownBody';
import PostDetail from '../feed/PostDetail';
import ReactionBar from '../feed/ReactionBar';
import ReportSheet, { Phase4ReportReasons } from '../feed/ReportSheet';
import { ProgressBar } from './LibraryCards';
import LessonPlayer from './LessonPlayer';

const t = strings.library;

function Block({ label, children, testID }) {
  const theme = useTheme();
  return (
    <View style={{ gap: theme.spacing.xs }} testID={testID}>
      <AppText variant="label">{label}</AppText>
      {children}
    </View>
  );
}

/** Open a Document resource: fetched with the grant cookies into the cache, then previewed (QuickLook or the system viewer). */
async function openDocument(mediaContext, media, title) {
  const url = documentUrl(media);
  const resolved = url ? mediaContext.source(url) : null;
  if (!resolved) return;
  try {
    const name = (media.id || 'document') + (media.contentType === 'text/vtt' ? '.vtt' : '.pdf');
    const file = await File.downloadFileAsync(resolved.uri, new File(Paths.cache, name), { headers: resolved.headers, idempotent: true });
    if (file && typeof file.preview === 'function') await file.preview({ title });
    else await openInAppBrowser(resolved.uri);
  } catch {
    await openInAppBrowser(resolved.uri);
  }
}

/** Download, downloading, or downloaded (with Remove) for one lesson (docs/PHASE4_CONTRACT.md section 5.9). */
export function DownloadButton({ lesson, testID = 'lesson-download' }) {
  const theme = useTheme();
  const { api } = useAuth();
  const prompt = useAccessPrompt();
  const [entry, setEntry] = useState(undefined);
  const [progress, setProgress] = useState(null);
  const [error, setError] = useState(null);
  useEffect(() => {
    let active = true;
    const load = () => findDownload(lesson.id).then((found) => active && setEntry(found));
    load();
    const off = onDownloadsChange(load);
    return () => {
      active = false;
      off();
    };
  }, [lesson.id]);

  async function start() {
    setError(null);
    setProgress(0);
    try {
      await downloadLesson(api, lesson, { onProgress: setProgress });
    } catch (e) {
      if (!prompt.handle(e)) setError(e && e.code === 'FeatureDisabled' ? t.downloads.paused : e && e.status ? describeFeatureError(e, 'library') : t.downloads.failed);
    } finally {
      setProgress(null);
    }
  }

  if (entry === undefined) return null;
  return (
    <View style={{ gap: theme.spacing.xs }}>
      {entry ? (
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm, alignItems: 'center' }}>
          <AppText color="success" style={{ fontWeight: '700' }} testID={testID + '-done'}>
            {t.downloads.downloaded}
          </AppText>
          <Button label={t.downloads.remove} variant="ghost" onPress={() => removeDownload(api, entry)} style={{ alignSelf: 'auto' }} testID={testID + '-remove'} />
        </View>
      ) : progress !== null ? (
        <AppText color="textMuted" testID={testID + '-progress'}>
          {t.downloads.downloading(Math.round(progress * 100))}
        </AppText>
      ) : (
        <Button label={t.downloads.download} accessibilityHint={t.downloads.downloadHint} variant="secondary" onPress={start} style={{ alignSelf: 'auto' }} testID={testID} />
      )}
      {error ? (
        <AppText color="danger" accessibilityRole="alert" testID={testID + '-error'}>
          {error}
        </AppText>
      ) : null}
    </View>
  );
}

/**
 * Everything under the player: title, series, levels, progress with Mark as complete, chapters
 * (tap to jump), resources (documents open in the system viewer, links in the in-app browser,
 * references as text), the article body, previous and next lessons, Download (when allowed),
 * reactions, and report.
 */
export function LessonInfo({ lesson, onChange, onSeek, testID = 'lesson' }) {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { api, user } = useAuth();
  const mediaContext = useMedia();
  const prompt = useAccessPrompt();
  const [error, setError] = useState(null);
  const [reporting, setReporting] = useState(false);
  const viewer = lesson.viewer || {};
  const progress = lesson.myProgress;
  const config = libraryConfig(property);
  const levels = levelLabels(property, lesson.levels).join(', ');

  async function run(fn) {
    setError(null);
    try {
      const next = await fn();
      onChange({ ...lesson, myProgress: next && next.lessonId ? next : null });
    } catch (e) {
      if (!prompt.handle(e)) setError(describeFeatureError(e, 'library'));
    }
  }

  return (
    <View style={{ gap: theme.spacing.lg }} testID={testID + '-info'}>
      <View style={{ gap: theme.spacing.xs }}>
        <AppText variant="title" accessibilityRole="header" testID={testID + '-title'}>
          {lesson.title}
        </AppText>
        {lesson.series ? <Button label={lesson.series.title} variant="ghost" onPress={() => router.push('/series/' + lesson.series.id)} style={{ alignSelf: 'auto' }} testID={testID + '-series'} /> : null}
        <AppText color="textMuted">{[lesson.durationSeconds ? formatDuration(lesson.durationSeconds) : null, levels || null].filter(Boolean).join(' · ')}</AppText>
        {lesson.summary ? <AppText>{lesson.summary}</AppText> : null}
      </View>
      {user ? (
        <View style={{ gap: theme.spacing.sm }}>
          {progress ? (
            <>
              <AppText color={progress.completed ? 'success' : 'textMuted'} testID={testID + '-progress'}>
                {progress.completed ? t.completed : t.progressPercent(progressPercent(progress))}
              </AppText>
              {!progress.completed ? <ProgressBar percent={progressPercent(progress)} /> : null}
            </>
          ) : null}
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
            {progress && progress.completed ? (
              <Button label={t.markIncomplete} variant="ghost" onPress={() => run(() => api.uncompleteLesson(lesson.id))} style={{ alignSelf: 'auto' }} testID={testID + '-uncomplete'} />
            ) : (
              <Button label={t.markComplete} variant="secondary" onPress={() => run(() => api.completeLesson(lesson.id))} style={{ alignSelf: 'auto' }} testID={testID + '-complete'} />
            )}
            {progress ? <Button label={t.resetProgress} variant="ghost" onPress={() => run(async () => (await api.resetLessonProgress(lesson.id), null))} style={{ alignSelf: 'auto' }} testID={testID + '-reset'} /> : null}
          </View>
          {error ? (
            <AppText color="danger" accessibilityRole="alert" testID={testID + '-error'}>
              {error}
            </AppText>
          ) : null}
        </View>
      ) : null}
      {config.downloadsEnabled && viewer.canDownload && lesson.kind === 'Video' ? <DownloadButton lesson={lesson} /> : null}
      {lesson.markers && lesson.markers.length ? (
        <Block label={t.chapters} testID={testID + '-chapters'}>
          {lesson.markers.map((m) => (
            <Button key={m.id} label={t.chapterLabel(formatDuration(m.seconds), m.label)} accessibilityLabel={t.jumpTo(m.label)} variant="ghost" onPress={() => onSeek && onSeek(m.seconds)} style={{ alignSelf: 'auto' }} testID={testID + '-chapter-' + m.id} />
          ))}
        </Block>
      ) : null}
      {lesson.resources && lesson.resources.length ? (
        <Block label={t.resources} testID={testID + '-resources'}>
          {lesson.resources.map((r) =>
            r.kind === 'Reference' ? (
              <View key={r.id} testID={'resource-' + r.id}>
                <AppText style={{ fontWeight: '600' }}>{r.title}</AppText>
                {r.text ? <AppText color="textMuted">{r.text}</AppText> : null}
              </View>
            ) : (
              <Button key={r.id} label={r.title + ' (' + (t.resourceKinds[r.kind] || r.kind) + ')'} accessibilityLabel={t.openResource(r.title)} variant="ghost" onPress={() => (r.kind === 'Link' && r.url ? openInAppBrowser(r.url) : openDocument(mediaContext, r.media, r.title))} style={{ alignSelf: 'auto' }} testID={'resource-' + r.id} />
            )
          )}
        </Block>
      ) : null}
      {lesson.description ? <MarkdownBody body={lesson.description} /> : null}
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
        {lesson.previous ? <Button label={t.previous} accessibilityLabel={t.previous + ', ' + lesson.previous.title} variant="secondary" onPress={() => router.replace('/lessons/' + lesson.previous.id)} style={{ alignSelf: 'auto' }} testID={testID + '-previous'} /> : null}
        {lesson.next ? <Button label={t.next} accessibilityLabel={t.next + ', ' + lesson.next.title} onPress={() => router.replace('/lessons/' + lesson.next.id)} style={{ alignSelf: 'auto' }} testID={testID + '-next'} /> : null}
      </View>
      <ReactionBar target={lesson.commentTarget || { type: 'Lesson', id: lesson.id }} fields={lesson} onChange={(fields) => onChange({ ...lesson, ...fields })} canReact={viewer.canReact !== false} trailing={viewer.canReport ? <Button label={strings.places.report} variant="ghost" onPress={() => setReporting(true)} style={{ alignSelf: 'auto' }} testID={testID + '-report'} /> : null} testID={testID + '-reactions'} />
      {reporting ? <ReportSheet target={{ targetType: 'Lesson', targetId: lesson.id }} author={null} reasons={Phase4ReportReasons} onClose={() => setReporting(false)} /> : null}
    </View>
  );
}

const loadLesson = (api, id) => api.getLesson(id);

/**
 * A lesson screen: the player stays at the top (it does not scroll away) and the info and the
 * Phase 2 comment tree scroll below it. A locked lesson shows the membership prompt; a lesson
 * downloaded on this device plays from the file, also offline.
 */
export default function LessonDetail({ lessonId, commentId = null, testID = 'lesson' }) {
  const theme = useTheme();
  const property = useProperty();
  const { api } = useAuth();
  const offline = useIsOffline();
  const playerRef = useRef(null);
  const resource = useResource(() => api.getLesson(lessonId), lessonId);
  const [localUri, setLocalUri] = useState(null);
  useEffect(() => {
    let active = true;
    findDownload(lessonId).then((entry) => active && setLocalUri(entry ? entry.fileUri : null));
    return () => {
      active = false;
    };
  }, [lessonId]);

  if (resource.status === 'loading') return <Loading label={t.loading} testID={testID + '-loading'} />;
  if (resource.status === 'gone') return <ErrorState title={t.lessonGoneTitle} message={t.lessonGoneBody} testID={testID + '-gone'} />;
  if (resource.status === 'error') return <ErrorState title={t.loadFailedTitle} message={describeFeatureError(resource.error, 'library')} onRetry={resource.reload} testID={testID + '-error'} />;
  const lesson = resource.data;
  if (isLockedItem(lesson)) {
    return (
      <View style={{ padding: theme.spacing.lg, gap: theme.spacing.md }} testID={testID + '-locked'}>
        <AppText variant="title" accessibilityRole="header">
          {lesson.title}
        </AppText>
        <AccessPromptCard required={lockedRequirement(lesson)} feature="library" body={t.lockedLesson} testID={testID + '-locked-prompt'} />
      </View>
    );
  }
  const playable = lesson.kind === 'Video' && (lesson.viewer ? lesson.viewer.canPlay !== false : true) && featureOn(property, 'library');
  const setProgress = (progress) => resource.setData((current) => (current ? { ...current, myProgress: progress } : current));
  return (
    <View style={{ flex: 1 }} testID={testID}>
      {playable ? <LessonPlayer key={'player:' + lesson.id + ':' + (localUri || '')} ref={playerRef} lesson={lesson} localUri={localUri} offline={offline} onProgress={setProgress} /> : null}
      {offline ? (
        <AppText color="textMuted" style={{ paddingHorizontal: theme.spacing.lg, paddingTop: theme.spacing.sm }} testID={testID + '-offline'}>
          {t.offlineQueued}
        </AppText>
      ) : null}
      <PostDetail
        key={'detail:' + lesson.id}
        postId={lesson.id}
        initialPost={lesson}
        commentId={commentId}
        loadItem={loadLesson}
        onPostChange={(next) => resource.setData(next)}
        renderCard={({ item, onChange }) => <LessonInfo lesson={{ ...item, myProgress: lesson.myProgress }} onChange={onChange} onSeek={(s) => playerRef.current && playerRef.current.seek(s)} testID={testID} />}
        canComment={(item) => item.commentsEnabled !== false && (!item.viewer || item.viewer.canComment !== false)}
        goneText={{ title: t.lessonGoneTitle, body: t.lessonGoneBody }}
        accessFeature="library"
        testID={testID + '-detail'}
      />
    </View>
  );
}
