import { activeMarker, createProgressReporter, formatDuration, libraryConfig, resumePosition } from '@trelliskit/shared';
import { useVideoPlayer, VideoView } from 'expo-video';
import { forwardRef, useCallback, useEffect, useImperativeHandle, useMemo, useRef, useState } from 'react';
import { AppState, Pressable, StyleSheet, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { documentUrl } from '../../content/phase4';
import { sendProgress } from '../../library/progress';
import { cueTextAt, parseVtt } from '../../library/vtt';
import { useMedia } from '../../media/MediaProvider';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import ChipGroup from '../ChipGroup';

const t = strings.library;

/** The expo-video source for a lesson: a downloaded file, else the HLS stream (or MP4 in local mode) with the grant cookies. */
export function lessonSource(lesson, mediaContext, localUri) {
  if (localUri) return { uri: localUri };
  const video = (lesson && lesson.video && (lesson.video.video || lesson.video)) || {};
  const url = video.hlsUrl || video.mp4Url || null;
  const resolved = mediaContext.source(url);
  if (!resolved) return null;
  return { uri: resolved.uri, headers: resolved.headers, ...(video.hlsUrl ? { contentType: 'hls' } : null) };
}

/**
 * The lesson video (docs/PHASE4_CONTRACT.md sections 5.3 to 5.5): expo-video with the media
 * grant cookies in the source headers (refreshed once after a playback error), resume from the
 * saved position, chapter ticks on a bar under the video, WebVTT captions shown over the video
 * with a language picker, and progress reported through the shared reporter (every
 * `progressIntervalSeconds` while playing, on pause, seek, end, app background, and exit; only
 * real playback counts). Reports that cannot be sent are queued on the device.
 * `ref.seek(seconds)` jumps to a chapter.
 */
const LessonPlayer = forwardRef(function LessonPlayer({ lesson, localUri = null, offline = false, onProgress, testID = 'lesson-player' }, ref) {
  const theme = useTheme();
  const property = useProperty();
  const { api, user } = useAuth();
  // A signed-out guest may watch a Public sample (anonymous browsing) but has no progress to record.
  const signedIn = Boolean(user);
  const mediaContext = useMedia();
  const config = libraryConfig(property);
  const duration = lesson.durationSeconds || 0;
  const startAt = resumePosition(lesson.myProgress, duration, config.resumeTailSeconds);
  const [position, setPosition] = useState(startAt);
  const [captionLanguage, setCaptionLanguage] = useState(null);
  const [cues, setCues] = useState([]);
  const [captionError, setCaptionError] = useState(false);
  const retried = useRef(false);
  const offlineRef = useRef(offline);
  const onProgressRef = useRef(onProgress);
  useEffect(() => {
    offlineRef.current = offline;
    onProgressRef.current = onProgress;
  });

  const source = lessonSource(lesson, mediaContext, localUri);
  const sourceKey = source ? source.uri + JSON.stringify(source.headers || {}) : '';
  // Key on the URI and header values so a new object with the same values does not reload the player.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const stableSource = useMemo(() => source, [sourceKey]);
  const player = useVideoPlayer(stableSource, (p) => {
    p.loop = false;
    p.timeUpdateEventInterval = 1;
    if (startAt > 0) p.currentTime = startAt;
  });

  const reporter = useMemo(
    () =>
      createProgressReporter({
        intervalSeconds: config.progressIntervalSeconds,
        durationSeconds: duration || null,
        send: (body) =>
          signedIn
            ? sendProgress(api, lesson.id, body, { offline: offlineRef.current }).then((result) => {
                if (result.progress && onProgressRef.current) onProgressRef.current(result.progress);
              })
            : Promise.resolve()
      }),
    [api, lesson.id, config.progressIntervalSeconds, duration, signedIn]
  );

  useEffect(() => {
    if (!player || typeof player.addListener !== 'function') return undefined;
    const subs = [
      player.addListener('playingChange', (e) => {
        const now = Number(player.currentTime) || 0;
        if (e && e.isPlaying) reporter.play(now);
        else reporter.pause(now);
      }),
      player.addListener('timeUpdate', (e) => {
        const now = Number(e && e.currentTime) || 0;
        setPosition(now);
        reporter.timeUpdate(now);
      }),
      player.addListener('playToEnd', () => reporter.ended(duration || Number(player.currentTime) || 0)),
      player.addListener('statusChange', (e) => {
        if (e && e.status === 'error' && !retried.current && !localUri) {
          retried.current = true;
          mediaContext.refreshAfterForbidden();
        }
      })
    ];
    return () => subs.forEach((s) => s && s.remove && s.remove());
  }, [player, reporter, duration, localUri, mediaContext]);

  // A refreshed grant changes the headers: replace the source once after an error.
  useEffect(() => {
    if (retried.current && player && stableSource && typeof player.replaceAsync === 'function') player.replaceAsync(stableSource).catch(() => {});
  }, [player, stableSource]);

  useEffect(() => {
    const sub = AppState.addEventListener('change', (state) => {
      if (state !== 'active') reporter.flush();
    });
    return () => {
      sub.remove();
      reporter.close();
    };
  }, [reporter]);

  const seek = useCallback(
    (seconds) => {
      const s = Math.max(0, Number(seconds) || 0);
      if (player) player.currentTime = s;
      setPosition(s);
      reporter.seeked(s);
    },
    [player, reporter]
  );
  useImperativeHandle(ref, () => ({ seek }), [seek]);

  const captions = lesson.captions || [];
  const caption = captions.find((c) => c.language === captionLanguage) || null;
  const captionUrl = caption ? documentUrl(caption.media) : null;
  useEffect(() => {
    if (!captionUrl) {
      setCues([]);
      return undefined;
    }
    let active = true;
    const resolved = mediaContext.source(captionUrl);
    if (!resolved) return undefined;
    globalThis
      .fetch(resolved.uri, { headers: resolved.headers })
      .then((r) => (r && r.ok ? r.text() : Promise.reject(new Error('caption'))))
      .then((text) => {
        if (!active) return;
        setCues(parseVtt(text));
        setCaptionError(false);
      })
      .catch(() => {
        if (!active) return;
        setCues([]);
        setCaptionError(true);
      });
    return () => {
      active = false;
    };
    // mediaContext.source changes with the grant; the URL is what matters here.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [captionUrl]);

  const markers = lesson.markers || [];
  const current = activeMarker(markers, position);
  const captionText = cueTextAt(cues, position);

  if (!stableSource) {
    return (
      <View style={[styles.video, { backgroundColor: '#000', alignItems: 'center', justifyContent: 'center' }]} testID={testID + '-unavailable'}>
        <AppText style={{ color: '#FFFFFF' }}>{t.videoProcessing}</AppText>
      </View>
    );
  }

  return (
    <View testID={testID}>
      <View style={styles.video}>
        <VideoView testID={testID + '-video'} player={player} style={StyleSheet.absoluteFill} nativeControls contentFit="contain" allowsPictureInPicture={false} />
        {captionText ? (
          <View pointerEvents="none" style={styles.captionBox} testID={testID + '-caption'}>
            <AppText style={styles.captionText}>{captionText}</AppText>
          </View>
        ) : null}
      </View>
      {markers.length && duration > 0 ? (
        <View style={{ paddingHorizontal: theme.spacing.lg, paddingTop: theme.spacing.sm, gap: theme.spacing.xs }}>
          <View style={[styles.track, { backgroundColor: theme.colors.border }]} testID={testID + '-markers'}>
            {markers.map((m) => (
              <Pressable
                key={m.id}
                onPress={() => seek(m.seconds)}
                accessibilityRole="button"
                accessibilityLabel={t.jumpTo(m.label) + ', ' + formatDuration(m.seconds)}
                hitSlop={12}
                style={[styles.tick, { left: Math.min(100, (100 * m.seconds) / duration) + '%', backgroundColor: theme.colors.primary }]}
                testID={testID + '-tick-' + m.id}
              />
            ))}
          </View>
          {current ? (
            <AppText variant="caption" color="textMuted" testID={testID + '-chapter'}>
              {t.chapterLabel(formatDuration(current.seconds), current.label)}
            </AppText>
          ) : null}
        </View>
      ) : null}
      {captions.length ? (
        <View style={{ paddingHorizontal: theme.spacing.lg, paddingTop: theme.spacing.sm }}>
          <ChipGroup label={t.captions} options={[{ value: null, label: t.captionsOff }].concat(captions.map((c) => ({ value: c.language, label: c.label || c.language })))} value={captionLanguage} onToggle={setCaptionLanguage} testID={testID + '-captions'} />
          {captionError ? (
            <AppText color="danger" variant="caption">
              {t.captionsFailed}
            </AppText>
          ) : null}
        </View>
      ) : null}
    </View>
  );
});

export default LessonPlayer;

const styles = StyleSheet.create({
  video: { width: '100%', aspectRatio: 16 / 9, backgroundColor: '#000' },
  captionBox: { position: 'absolute', left: 12, right: 12, bottom: 36, alignItems: 'center' },
  captionText: { color: '#FFFFFF', backgroundColor: 'rgba(0,0,0,0.7)', paddingHorizontal: 8, paddingVertical: 4, borderRadius: 4, textAlign: 'center' },
  track: { height: 6, borderRadius: 3, marginVertical: 8 },
  tick: { position: 'absolute', top: -4, width: 6, height: 14, borderRadius: 2, marginLeft: -3 }
});
