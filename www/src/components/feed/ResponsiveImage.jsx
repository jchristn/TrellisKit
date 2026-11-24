import { useState } from 'react';
import { mediaUrl } from '../../utils/engagement.js';
import { refreshMediaGrant } from '../../utils/mediaGrant.js';

const ORDER = ['thumb', 'medium', 'large'];

/** `srcset` text for a variant format (`webpUrl` or `jpegUrl`). */
export function srcSetFor(variants, format) {
  return ORDER.filter((name) => variants[name] && variants[name][format])
    .map((name) => mediaUrl(variants[name][format]) + ' ' + variants[name].width + 'w')
    .join(', ');
}

/**
 * A processed image with WebP and JPEG variants chosen by the browser (`sizes`), intrinsic
 * dimensions to avoid layout shift, lazy loading, and alt text. A 403 (expired media grant)
 * renews the grant once and retries.
 * @param {{ media: object, sizes?: string, className?: string, prefer?: 'thumb'|'medium'|'large', alt?: string }} props
 */
function ResponsiveImage({ media, sizes = '(max-width: 760px) 100vw, 680px', className = '', prefer = 'medium', alt }) {
  const [attempt, setAttempt] = useState(0);
  const variants = (media.image && media.image.variants) || {};
  const fallback = variants[prefer] || variants.medium || variants.large || variants.thumb;
  if (!fallback) return null;
  const bust = attempt ? (fallback.jpegUrl.includes('?') ? '&' : '?') + 'g=' + attempt : '';
  const onError = () => {
    if (attempt > 0) return;
    refreshMediaGrant().then((ok) => { if (ok) setAttempt(1); });
  };
  return (
    <picture className={className}>
      <source type="image/webp" srcSet={srcSetFor(variants, 'webpUrl')} sizes={sizes} />
      <img
        key={attempt}
        src={mediaUrl(fallback.jpegUrl) + bust}
        srcSet={srcSetFor(variants, 'jpegUrl')}
        sizes={sizes}
        width={fallback.width}
        height={fallback.height}
        alt={alt !== undefined ? alt : (media.altText || '')}
        loading="lazy"
        decoding="async"
        onError={onError}
      />
    </picture>
  );
}

export default ResponsiveImage;
