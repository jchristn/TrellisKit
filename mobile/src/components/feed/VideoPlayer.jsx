import { useVideoPlayer, VideoView } from 'expo-video';
import { useEffect, useMemo, useRef } from 'react';
import { useMedia } from '../../media/MediaProvider';

/**
 * Video playback with expo-video. Plays `video.hlsUrl` (HLS; the content type is set so iOS
 * reads the tracks) or, in local passthrough mode, `video.mp4Url`. The media grant cookies go in
 * the source headers, which expo-video applies to the playlist and every segment request. When
 * the player errors (a 403 from an expired grant), the grant is refreshed once and the source
 * replaced.
 * @param {object} props
 * @param {object} props.media Ready video Media.
 */
export default function VideoPlayer({ media, style, testID }) {
  const mediaContext = useMedia();
  const video = (media && media.video) || {};
  const url = video.hlsUrl || video.mp4Url || null;
  const isHls = Boolean(video.hlsUrl);
  const resolved = mediaContext.source(url);
  const retried = useRef(false);
  const uri = resolved ? resolved.uri : null;
  // Key on the header values so the source only changes when a header does (a new grant).
  const headerKey = resolved ? JSON.stringify(resolved.headers || {}) : '';
  const source = useMemo(() => {
    if (!uri) return null;
    const headers = headerKey ? JSON.parse(headerKey) : {};
    return { uri, headers, ...(isHls ? { contentType: 'hls' } : null) };
  }, [uri, headerKey, isHls]);
  const player = useVideoPlayer(source, (p) => {
    p.loop = false;
  });

  useEffect(() => {
    if (!player || typeof player.addListener !== 'function') return undefined;
    const sub = player.addListener('statusChange', (event) => {
      if (event && event.status === 'error' && !retried.current) {
        retried.current = true;
        mediaContext.refreshAfterForbidden();
      }
    });
    return () => sub && sub.remove && sub.remove();
  }, [player, mediaContext]);

  useEffect(() => {
    if (retried.current && player && source && typeof player.replaceAsync === 'function') player.replaceAsync(source).catch(() => {});
  }, [player, source]);

  if (!source) return null;
  return <VideoView testID={testID} player={player} style={style} nativeControls contentFit="contain" allowsPictureInPicture={false} accessibilityLabel={media.altText || undefined} />;
}
