import strings from '../../strings.js';
import Icon from '../Icon.jsx';
import { mediaUrl } from '../../utils/engagement.js';

function hostOf(url) {
  try {
    return new URL(url).host;
  } catch {
    return '';
  }
}

/**
 * Link preview card (contract section 5.9). The image comes from the media host (the server
 * re-encodes it), so members never load third-party images. `onRemove` adds a remove control
 * (composers).
 * @param {{ preview: object, onRemove?: () => void }} props
 */
function LinkPreviewCard({ preview, onRemove }) {
  if (!preview || !preview.url || !/^https?:\/\//i.test(preview.url)) return null;
  const href = preview.canonicalUrl && /^https?:\/\//i.test(preview.canonicalUrl) ? preview.canonicalUrl : preview.url;
  const image = mediaUrl(preview.imageUrl);
  const host = hostOf(href);
  return (
    <div className="link-preview">
      <a className="link-preview-body" href={href} target="_blank" rel="noopener noreferrer nofollow" aria-label={strings.feed.linkPreviewLabel(preview.title || preview.siteName || host)}>
        {image && <img className="link-preview-image" src={image} alt="" width={preview.imageWidth || undefined} height={preview.imageHeight || undefined} loading="lazy" decoding="async" />}
        <span className="link-preview-text">
          <span className="link-preview-site muted small">{preview.siteName || host}</span>
          {preview.title && <strong className="link-preview-title">{preview.title}</strong>}
          {preview.description && <span className="link-preview-description small">{preview.description}</span>}
        </span>
      </a>
      {onRemove && (
        <button type="button" className="icon-button link-preview-remove" onClick={onRemove} aria-label={strings.composer.removePreview} title={strings.composer.removePreview}>
          <Icon name="close" size={14} />
        </button>
      )}
    </div>
  );
}

export default LinkPreviewCard;
