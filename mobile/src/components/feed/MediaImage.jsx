import { Image } from 'expo-image';
import { useState } from 'react';
import { useMedia } from '../../media/MediaProvider';

/**
 * A members-only image (media variant, poster, or link preview image) through expo-image, with
 * the media grant cookies as request headers. When it fails to load (for example a 403 after
 * the grant expired) the grant is refreshed once and the image retried.
 * @param {object} props
 * @param {string} props.url Absolute or root-relative URL from the payload.
 * @param {string|null} [props.blurhash] Placeholder.
 * @param {string|null} [props.alt] Accessible description; decorative when null.
 */
export default function MediaImage({ url, blurhash, alt, style, contentFit = 'cover', testID }) {
  const mediaContext = useMedia();
  const [retried, setRetried] = useState(false);
  const source = mediaContext.source(url);
  if (!source) return null;
  return (
    <Image
      key={retried ? 'retry-' + mediaContext.version : 'first'}
      testID={testID}
      source={source}
      placeholder={blurhash ? { blurhash } : undefined}
      contentFit={contentFit}
      transition={150}
      style={style}
      accessible={Boolean(alt)}
      accessibilityLabel={alt || undefined}
      accessibilityIgnoresInvertColors
      onError={() => {
        if (retried) return;
        setRetried(true);
        mediaContext.refreshAfterForbidden();
      }}
    />
  );
}
