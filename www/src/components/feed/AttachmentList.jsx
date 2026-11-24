import { useId, useRef } from 'react';
import { MediaContentTypes } from '@trelliskit/shared';
import strings from '../../strings.js';
import Icon from '../Icon.jsx';
import ResponsiveImage from './ResponsiveImage.jsx';

const c = strings.composer;
const u = c.upload;

function statusText(item) {
  switch (item.status) {
    case 'creating': return u.creating;
    case 'uploading': return u.uploading(Math.round((item.progress || 0) * 100));
    case 'completing': return u.completing;
    case 'processing': return item.kind === 'Video' ? u.processingVideo : u.processing;
    case 'ready': return u.ready;
    case 'failed': return u.failed + (item.media && item.media.failureReason ? ': ' + (c.failureReasons[item.media.failureReason] || item.media.failureReason) : '');
    case 'error': return item.error;
    default: return '';
  }
}

/** Accept list for the file input. */
export function acceptFor(allowVideo) {
  return [...MediaContentTypes.Image, ...(allowVideo ? MediaContentTypes.Video : [])].join(',');
}

/**
 * Composer attachments: picker button, per-file progress and processing state, alt text,
 * reorder, retry or resume, and remove.
 * @param {{ uploads: ReturnType<import('./useMediaUploads.js').useMediaUploads>, allowVideo: boolean, allowMedia: boolean, maxCount: number, label?: string, disabled?: boolean, showAlt?: boolean }} props
 */
function AttachmentList({ uploads, allowVideo, allowMedia, maxCount, label, disabled = false, showAlt = true }) {
  const inputRef = useRef(null);
  const inputId = useId();
  const full = uploads.items.length >= maxCount;
  return (
    <div className="attachments">
      {uploads.items.length > 0 && (
        <ul className="attachment-list">
          {uploads.items.map((item, i) => {
            const ready = item.status === 'ready' && item.media;
            const pending = ['creating', 'uploading', 'completing'].includes(item.status);
            return (
              <li key={item.key} className={'attachment attachment-' + item.status}>
                <div className="attachment-thumb" aria-hidden="true">
                  {ready && item.kind === 'Image' ? <ResponsiveImage media={item.media} prefer="thumb" sizes="96px" alt="" />
                    : <Icon name={item.kind === 'Video' ? 'video' : 'image'} size={22} />}
                </div>
                <div className="attachment-body">
                  <div className="attachment-name small">{item.fileName || (item.kind === 'Video' ? strings.media.video : strings.media.image)}</div>
                  {pending && <progress className="attachment-progress" max="1" value={item.progress || 0} aria-label={statusText(item)} />}
                  <div className={'attachment-status small' + (item.status === 'error' || item.status === 'failed' ? ' field-error' : ' muted')} role="status">{statusText(item)}</div>
                  {showAlt && item.kind === 'Image' && item.mediaId && item.status !== 'error' && (
                    <label className="attachment-alt">
                      <span className="visually-hidden">{c.altLabel}</span>
                      <input
                        className="input-compact"
                        value={item.altText}
                        maxLength={1000}
                        placeholder={c.altPlaceholder}
                        onChange={(e) => uploads.setAlt(item.key, e.target.value)}
                        onBlur={() => uploads.saveAlt(item.key)}
                        disabled={disabled}
                        aria-label={c.altLabel}
                      />
                    </label>
                  )}
                </div>
                <div className="attachment-actions">
                  {item.status === 'error' && (
                    <button type="button" className="button button-secondary button-sm" onClick={() => uploads.retry(item.key)} disabled={disabled}>{item.canResume ? u.resume : u.retry}</button>
                  )}
                  {uploads.items.length > 1 && (
                    <>
                      <button type="button" className="icon-button" onClick={() => uploads.move(item.key, -1)} disabled={disabled || i === 0} aria-label={c.moveLeft} title={c.moveLeft}><Icon name="chevronUp" size={14} /></button>
                      <button type="button" className="icon-button" onClick={() => uploads.move(item.key, 1)} disabled={disabled || i === uploads.items.length - 1} aria-label={c.moveRight} title={c.moveRight}><Icon name="chevronDown" size={14} /></button>
                    </>
                  )}
                  <button type="button" className="icon-button" onClick={() => uploads.remove(item.key)} disabled={disabled} aria-label={pending ? u.cancel : c.removeAttachment} title={pending ? u.cancel : c.removeAttachment}><Icon name="trash" size={14} /></button>
                </div>
              </li>
            );
          })}
        </ul>
      )}
      {allowMedia && !full && (
        <>
          <input
            id={inputId}
            ref={inputRef}
            type="file"
            className="visually-hidden"
            accept={acceptFor(allowVideo)}
            multiple={maxCount > 1}
            onChange={(e) => { uploads.addFiles(e.target.files); e.target.value = ''; }}
            disabled={disabled}
            aria-label={label || (allowVideo ? c.attach : c.attachImages)}
            tabIndex={-1}
          />
          <button type="button" className="button button-secondary button-sm" onClick={() => inputRef.current && inputRef.current.click()} disabled={disabled}>
            <Icon name="image" size={14} /><span>{label || (allowVideo ? c.attach : c.attachImages)}</span>
          </button>
        </>
      )}
    </div>
  );
}

export default AttachmentList;
