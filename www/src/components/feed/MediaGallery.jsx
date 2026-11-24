import { useState } from 'react';
import strings from '../../strings.js';
import Modal from '../Modal.jsx';
import Icon from '../Icon.jsx';
import ResponsiveImage from './ResponsiveImage.jsx';
import VideoPlayer from './VideoPlayer.jsx';

const MAX_TILES = 4;

function PendingTile({ media }) {
  const failed = media.status === 'Failed';
  const reason = failed && media.failureReason ? strings.composer.failureReasons[media.failureReason] : '';
  return (
    <div className={'media-pending' + (failed ? ' media-failed' : '')} role="status">
      {failed ? <Icon name="alert" size={20} /> : <span className="spinner" aria-hidden="true" />}
      <span>{failed ? strings.media.failed : (media.kind === 'Video' ? strings.composer.upload.processing : strings.media.processing)}</span>
      {reason && <span className="small">{reason}</span>}
    </div>
  );
}

function ImageViewer({ images, index, onClose, onIndex }) {
  const media = images[index];
  const variants = (media.image && media.image.variants) || {};
  const large = variants.large || variants.medium || variants.thumb;
  return (
    <Modal open size="xl" onClose={onClose} title={strings.media.viewerTitle(index + 1, images.length)}>
      <figure className="image-viewer">
        <ResponsiveImage media={media} prefer="large" sizes="(max-width: 1200px) 100vw, 1200px" className="image-viewer-picture" />
        {media.altText && <figcaption className="media-caption">{media.altText}</figcaption>}
      </figure>
      {images.length > 1 && (
        <div className="image-viewer-nav">
          <button type="button" className="button button-secondary button-sm" onClick={() => onIndex((index - 1 + images.length) % images.length)}>
            <Icon name="chevronLeft" size={14} /><span>{strings.media.previous}</span>
          </button>
          <span className="muted small">{index + 1} / {images.length}</span>
          <button type="button" className="button button-secondary button-sm" onClick={() => onIndex((index + 1) % images.length)}>
            <span>{strings.media.next}</span><Icon name="chevronRight" size={14} />
          </button>
        </div>
      )}
      {large && <span className="visually-hidden">{large.width} x {large.height}</span>}
    </Modal>
  );
}

/**
 * Attachments of a post or comment in display order: one large tile, or a grid of up to four
 * with a "+N" overlay. Images open a viewer with their alt text as the caption; videos play in
 * place; Processing and Failed media show their state.
 * @param {{ items: object[], compact?: boolean }} props
 */
function MediaGallery({ items, compact = false }) {
  const [viewer, setViewer] = useState(null);
  const list = (items || []).filter(Boolean);
  if (!list.length) return null;
  const readyImages = list.filter((m) => m.kind === 'Image' && m.status === 'Ready');
  const shown = list.slice(0, MAX_TILES);
  const extra = list.length - shown.length;
  return (
    <div className={'media-gallery media-count-' + Math.min(shown.length, 4) + (compact ? ' media-compact' : '')} aria-label={strings.media.galleryLabel(list.length)} role="group">
      {shown.map((media, i) => {
        const last = i === shown.length - 1 && extra > 0;
        if (media.status !== 'Ready') return <div key={media.id} className="media-tile"><PendingTile media={media} /></div>;
        if (media.kind === 'Video') return <div key={media.id} className="media-tile media-tile-video"><VideoPlayer media={media} /></div>;
        const imageIndex = readyImages.indexOf(media);
        return (
          <button
            key={media.id}
            type="button"
            className="media-tile media-tile-image"
            onClick={() => setViewer(imageIndex)}
            aria-label={strings.media.openImage(imageIndex + 1, readyImages.length) + (media.altText ? ': ' + media.altText : '')}
          >
            <ResponsiveImage media={media} sizes={shown.length === 1 ? '(max-width: 760px) 100vw, 680px' : '(max-width: 760px) 50vw, 340px'} />
            {last && <span className="media-more" aria-hidden="true">+{extra}</span>}
          </button>
        );
      })}
      {viewer !== null && readyImages[viewer] && <ImageViewer images={readyImages} index={viewer} onIndex={setViewer} onClose={() => setViewer(null)} />}
    </div>
  );
}

export default MediaGallery;
