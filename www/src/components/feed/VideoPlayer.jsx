import { useEffect, useRef, useState } from 'react';
import strings from '../../strings.js';
import Icon from '../Icon.jsx';
import { mediaUrl } from '../../utils/engagement.js';
import { refreshMediaGrant } from '../../utils/mediaGrant.js';

const HLS_TYPE = 'application/vnd.apple.mpegurl';

/** Whether the browser plays HLS natively (Safari, iOS, most Android browsers). */
export function canPlayHlsNatively(video) {
  return Boolean(video && typeof video.canPlayType === 'function' && video.canPlayType(HLS_TYPE));
}

/**
 * Attach an HLS or MP4 source to a <video>. Safari uses native HLS; other browsers load hls.js on
 * demand with `withCredentials` so the media grant cookies go with the playlist and every segment
 * (contract section 4.7). Returns a cleanup function.
 * @param {HTMLVideoElement} element
 * @param {{ hlsUrl?: string|null, mp4Url?: string|null }} source
 * @param {(fatal: boolean) => void} onError
 * @param {() => Promise<any>} [loadHls] dynamic import of hls.js (injectable for tests)
 */
export function attachVideoSource(element, { hlsUrl, mp4Url }, onError, loadHls = () => import('hls.js/light')) {
  const hls = mediaUrl(hlsUrl);
  const mp4 = mediaUrl(mp4Url);
  if (!hls) {
    if (mp4) element.src = mp4;
    return () => {};
  }
  if (canPlayHlsNatively(element)) {
    element.src = hls;
    return () => {};
  }
  let instance = null;
  let cancelled = false;
  let retried = false;
  loadHls().then((mod) => {
    const Hls = mod.default || mod;
    if (cancelled) return;
    if (!Hls.isSupported()) {
      if (mp4) element.src = mp4;
      else onError(true);
      return;
    }
    instance = new Hls({ xhrSetup: (xhr) => { xhr.withCredentials = true; } });
    instance.on(Hls.Events.ERROR, (_event, data) => {
      const status = data && data.response && data.response.code;
      if (status === 403 && !retried) {
        retried = true;
        refreshMediaGrant().then(() => { if (!cancelled && instance) instance.startLoad(); });
        return;
      }
      if (data && data.fatal) onError(true);
    });
    instance.loadSource(hls);
    instance.attachMedia(element);
  }, () => onError(true));
  return () => {
    cancelled = true;
    if (instance) instance.destroy();
  };
}

/**
 * Video with a poster frame. Nothing streams until the member presses play, so a feed full of
 * videos does not load playlists. Alt text is the accessible name and shows as a caption. The frame
 * has the video's own shape (width and height from the server, after rotation), and a portrait video
 * is narrowed on wide screens so it is never taller than the window allows.
 * @param {{ media: object, autoPlay?: boolean }} props
 */
function VideoPlayer({ media, autoPlay = false }) {
  const video = media.video;
  const [active, setActive] = useState(autoPlay);
  const [failed, setFailed] = useState(false);
  const ref = useRef(null);
  const poster = mediaUrl(video && video.posterUrl);
  const ratio = media.width && media.height ? media.width + ' / ' + media.height : '16 / 9';
  // Width over height as a number: the style sheet uses it to cap a tall (portrait) video's height on wide
  // screens by narrowing the frame, so the frame always has the video's own shape and never shows bars.
  const ratioNumber = media.width && media.height ? Number((media.width / media.height).toFixed(4)) : Number((16 / 9).toFixed(4));

  useEffect(() => {
    if (!active || !ref.current) return undefined;
    const element = ref.current;
    const cleanup = attachVideoSource(element, video || {}, () => setFailed(true));
    const play = element.play && element.play();
    if (play && typeof play.catch === 'function') play.catch(() => {});
    return cleanup;
  }, [active, video]);

  return (
    <figure className="media-video" style={{ '--video-ratio': ratio, '--video-ratio-number': ratioNumber }}>
      <div className="media-video-box">
      {active ? (
        <video
          ref={ref}
          className="media-video-element"
          controls
          playsInline
          poster={poster || undefined}
          aria-label={media.altText || strings.media.video}
          preload="metadata"
        />
      ) : (
        <button type="button" className="media-video-poster" onClick={() => setActive(true)} aria-label={strings.media.playVideo + (media.altText ? ': ' + media.altText : '')}>
          {poster ? <img src={poster} alt="" loading="lazy" decoding="async" /> : <span className="media-video-blank" />}
          <span className="media-play"><Icon name="play" size={28} /></span>
        </button>
      )}
      {failed && <span className="media-error" role="alert">{strings.media.videoError}</span>}
      </div>
      {media.altText && <figcaption className="media-caption">{media.altText}</figcaption>}
    </figure>
  );
}

export default VideoPlayer;
