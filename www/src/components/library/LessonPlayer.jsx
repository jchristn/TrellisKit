import { useEffect, useMemo, useRef, useState } from 'react';
import { activeMarker, createProgressReporter, formatDuration, resumePosition } from '@trelliskit/shared';
import strings from '../../strings.js';
import { attachVideoSource } from '../feed/VideoPlayer.jsx';
import { mediaUrl } from '../../utils/engagement.js';
import { mediaFileUrl } from '../../utils/phase4.js';
import { Banner } from '../States.jsx';

const t = strings.library;
export const PLAYBACK_RATES = Object.freeze([0.5, 0.75, 1, 1.25, 1.5, 1.75, 2]);

/** The playable source of a lesson's video Media: `{ hlsUrl, mp4Url, posterUrl, status }`. */
export function lessonVideoSource(lesson) {
  const media = lesson && lesson.video;
  if (!media) return null;
  const v = media.video || media;
  return { hlsUrl: v.hlsUrl || null, mp4Url: v.mp4Url || null, posterUrl: v.posterUrl || media.posterUrl || null, status: media.status || v.status || 'Ready', durationSeconds: v.durationSeconds || media.durationSeconds || lesson.durationSeconds || 0 };
}

/**
 * Keyboard shortcuts of the lesson player: returns the action for a key event, or null.
 * Space and K play or pause, J and L skip 10 seconds, arrows skip 5, M mutes, F toggles full
 * screen, and Shift with `>` or `<` changes the speed.
 * @param {{ key: string, shiftKey?: boolean }} e
 */
export function playerShortcut(e) {
  const key = e.key;
  if (key === ' ' || key === 'k' || key === 'K') return { kind: 'toggle' };
  if (key === 'j' || key === 'J') return { kind: 'seekBy', seconds: -10 };
  if (key === 'l' || key === 'L') return { kind: 'seekBy', seconds: 10 };
  if (key === 'ArrowLeft') return { kind: 'seekBy', seconds: -5 };
  if (key === 'ArrowRight') return { kind: 'seekBy', seconds: 5 };
  if (key === 'm' || key === 'M') return { kind: 'mute' };
  if (key === 'f' || key === 'F') return { kind: 'fullscreen' };
  if (key === '>' || (e.shiftKey && key === '.')) return { kind: 'rate', step: 1 };
  if (key === '<' || (e.shiftKey && key === ',')) return { kind: 'rate', step: -1 };
  return null;
}

/**
 * The lesson player (docs/PHASE4_CONTRACT.md section 5.3 and 5.5): hls.js with credentials (Safari
 * plays natively), resume from the stored position (from 0 inside the resume tail), chapter markers
 * on the scrubber and as a list, WebVTT captions, playback speed, keyboard shortcuts, and a
 * progress reporter that sends `PUT /lessons/{id}/progress` every `intervalSeconds` while playing and
 * on pause, seek end, end, tab hide, and leaving the page.
 *
 * @param {{ lesson: object, client: object, intervalSeconds?: number, resumeTailSeconds?: number, onProgress?: (progress: object) => void, reportProgress?: boolean }} props
 */
function LessonPlayer({ lesson, client, intervalSeconds = 15, resumeTailSeconds = 10, onProgress, reportProgress = true }) {
  const ref = useRef(null);
  const reporterRef = useRef(null);
  const [failed, setFailed] = useState(false);
  const [rate, setRate] = useState(1);
  const [position, setPosition] = useState(0);
  const [resumedFrom, setResumedFrom] = useState(null);
  const source = useMemo(() => lessonVideoSource(lesson), [lesson]);
  const duration = (source && source.durationSeconds) || lesson.durationSeconds || 0;
  const markers = useMemo(() => (lesson.markers || []).slice().sort((a, b) => a.seconds - b.seconds), [lesson.markers]);
  const start = useMemo(() => resumePosition(lesson.myProgress, duration, resumeTailSeconds), [lesson.myProgress, duration, resumeTailSeconds]);
  const progressRef = useRef(onProgress);
  progressRef.current = onProgress;
  const ready = source && source.status === 'Ready' && (source.hlsUrl || source.mp4Url);

  useEffect(() => {
    if (!reportProgress || !ready) return undefined;
    const reporter = createProgressReporter({
      intervalSeconds,
      durationSeconds: duration,
      send: (body) => client.putLessonProgress(lesson.id, body).then((p) => { if (progressRef.current && p) progressRef.current(p); })
    });
    reporterRef.current = reporter;
    const element = ref.current;
    const onHide = () => {
      if (document.visibilityState === 'hidden') reporter.flush();
    };
    document.addEventListener('visibilitychange', onHide);
    window.addEventListener('pagehide', reporter.flush);
    return () => {
      document.removeEventListener('visibilitychange', onHide);
      window.removeEventListener('pagehide', reporter.flush);
      if (element && !element.paused) reporter.pause(element.currentTime || 0);
      reporter.close();
      reporterRef.current = null;
    };
  }, [client, lesson.id, intervalSeconds, duration, reportProgress, ready]);

  useEffect(() => {
    const element = ref.current;
    if (!element || !ready) return undefined;
    setFailed(false);
    return attachVideoSource(element, { hlsUrl: source.hlsUrl, mp4Url: source.mp4Url }, () => setFailed(true));
  }, [source, ready]);

  if (!source) return <Banner tone="info">{t.noVideo}</Banner>;
  if (!ready) return <Banner tone="info" icon="clock">{t.processing}</Banner>;

  const video = () => ref.current;
  const seek = (seconds) => {
    const el = video();
    if (!el) return;
    const target = Math.max(0, Math.min(duration || Infinity, seconds));
    el.currentTime = target;
    setPosition(target);
  };
  const changeRate = (value) => {
    setRate(value);
    if (video()) video().playbackRate = value;
    if (reporterRef.current) reporterRef.current.setRate(value);
  };
  const onKeyDown = (e) => {
    const action = playerShortcut(e);
    const el = video();
    if (!action || !el) return;
    if (e.target && ['INPUT', 'SELECT', 'TEXTAREA', 'BUTTON'].includes(e.target.tagName) && e.target !== el) return;
    e.preventDefault();
    if (action.kind === 'toggle') {
      if (el.paused) {
        const p = el.play && el.play();
        if (p && p.catch) p.catch(() => {});
      } else el.pause();
    }
    if (action.kind === 'seekBy') seek((el.currentTime || 0) + action.seconds);
    if (action.kind === 'mute') el.muted = !el.muted;
    if (action.kind === 'fullscreen') {
      if (document.fullscreenElement) document.exitFullscreen && document.exitFullscreen();
      else if (el.requestFullscreen) el.requestFullscreen().catch(() => {});
    }
    if (action.kind === 'rate') {
      const index = PLAYBACK_RATES.indexOf(rate);
      changeRate(PLAYBACK_RATES[Math.max(0, Math.min(PLAYBACK_RATES.length - 1, (index < 0 ? 2 : index) + action.step))]);
    }
  };
  const current = activeMarker(markers, position);

  return (
    <div className="p4-player-wrap">
      {/* The container takes the keyboard shortcuts; the video keeps its native controls. */}
      <div className="p4-player" onKeyDown={onKeyDown}>
        <video
          ref={ref}
          controls
          playsInline
          preload="metadata"
          crossOrigin="use-credentials"
          poster={mediaUrl(source.posterUrl) || undefined}
          aria-label={lesson.title}
          data-testid="lesson-video"
          onLoadedMetadata={() => {
            const el = video();
            if (el && start > 0) {
              el.currentTime = start;
              setResumedFrom(start);
            }
            if (el) el.playbackRate = rate;
            if (reporterRef.current && el && el.duration) reporterRef.current.setDuration(el.duration);
          }}
          onPlay={() => { if (reporterRef.current) reporterRef.current.play(video().currentTime || 0); }}
          onPause={() => { if (reporterRef.current) reporterRef.current.pause(video().currentTime || 0); }}
          onSeeked={() => { if (reporterRef.current) reporterRef.current.seeked(video().currentTime || 0); }}
          onEnded={() => { if (reporterRef.current) reporterRef.current.ended(video().currentTime || duration); }}
          onTimeUpdate={() => {
            const at = video().currentTime || 0;
            setPosition(at);
            if (reporterRef.current) reporterRef.current.timeUpdate(at);
          }}
          onError={() => setFailed(true)}
        >
          {(lesson.captions || []).map((caption, i) => {
            const src = mediaFileUrl(caption.media);
            return src ? <track key={caption.language + i} kind="captions" src={src} srcLang={caption.language} label={caption.label || caption.language} default={i === 0} /> : null;
          })}
        </video>
      </div>
      {markers.length > 0 && duration > 0 && (
        <div className="p4-scrub-markers" aria-hidden="true">
          {markers.map((m) => <button key={m.id || m.seconds} type="button" tabIndex={-1} className="p4-scrub-marker" style={{ left: (100 * m.seconds) / duration + '%' }} title={m.label} onClick={() => seek(m.seconds)} />)}
        </div>
      )}
      <div className="p4-player-bar">
        <span className="muted small" aria-live="polite">{resumedFrom ? t.resumeFrom(formatDuration(resumedFrom)) : ''}{resumedFrom ? ' ' : ''}{resumedFrom && <button type="button" className="link-button small" onClick={() => { seek(0); setResumedFrom(null); }}>{t.startOver}</button>}</span>
        <label className="inline-field">
          <span className="label-text">{t.speed}</span>
          <select className="select-compact" value={rate} onChange={(e) => changeRate(Number(e.target.value))}>
            {PLAYBACK_RATES.map((r) => <option key={r} value={r}>{t.speedValue(r)}</option>)}
          </select>
        </label>
      </div>
      {failed && <Banner tone="danger" icon="alert">{t.playerError}</Banner>}
      {markers.length > 0 && (
        <section className="panel" aria-labelledby="lesson-chapters">
          <h2 id="lesson-chapters" className="section-title">{t.chapters}</h2>
          <div className="p4-markers">
            {markers.map((m) => (
              <button key={m.id || m.seconds} type="button" className={'p4-marker' + (current && current.seconds === m.seconds ? ' active' : '')} onClick={() => seek(m.seconds)} aria-label={t.jumpTo(m.label, formatDuration(m.seconds))}>
                <span className="p4-marker-time">{formatDuration(m.seconds)}</span><span>{m.label}</span>
              </button>
            ))}
          </div>
        </section>
      )}
      <details className="muted small"><summary>{t.shortcutsTitle}</summary><p>{t.shortcuts}</p></details>
    </div>
  );
}

export default LessonPlayer;
