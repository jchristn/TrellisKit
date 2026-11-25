import { useCallback, useEffect, useId, useState } from 'react';
import { REVIEW_LIMITS, venueContributionConfig } from '@trelliskit/shared';
import strings from '../../strings.js';
import MetadataFilterBar from '../MetadataFilterBar.jsx';
import { useSearchParams } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import Icon from '../Icon.jsx';
import Modal from '../Modal.jsx';
import ConfirmModal from '../ConfirmModal.jsx';
import { ErrorBanner } from '../States.jsx';
import ReportDialog from '../feed/ReportDialog.jsx';
import ResponsiveImage from '../feed/ResponsiveImage.jsx';
import VideoPlayer from '../feed/VideoPlayer.jsx';
import AttachmentList from '../feed/AttachmentList.jsx';
import useMediaUploads from '../feed/useMediaUploads.js';
import { authorName, errorText, mediaUrl, postingPermissions } from '../../utils/engagement.js';
import { mediaFileUrl, mediaThumbUrl, phase4Errors, useDirectoryTerms } from '../../utils/phase4.js';

const vm = strings.venues.media;
const PAGE_SIZE = 24;

const kindOf = (media) => (media && media.kind === 'Video' ? 'Video' : 'Image');

/** A gallery tile's picture: image variants, a video poster with a play mark, or the processing state. */
function MediaThumb({ media }) {
  if (media.status && media.status !== 'Ready') {
    return <span className="venue-media-pending"><span className="spinner" aria-hidden="true" /><span className="small">{vm.processing}</span></span>;
  }
  if (kindOf(media) === 'Video') {
    const poster = mediaUrl(media.video && media.video.posterUrl) || mediaThumbUrl(media);
    return (
      <>
        {poster ? <img src={poster} alt="" loading="lazy" /> : <span className="media-video-blank" />}
        <span className="media-play" aria-hidden="true"><Icon name="play" size={22} /></span>
      </>
    );
  }
  if (media.image && media.image.variants) return <ResponsiveImage media={media} prefer="thumb" sizes="(max-width: 760px) 50vw, 220px" alt="" />;
  const src = mediaThumbUrl(media) || mediaFileUrl(media);
  return src ? <img src={src} alt="" loading="lazy" /> : <Icon name="image" size={22} />;
}

/**
 * Lightbox over the gallery entries: Escape closes (Modal), ArrowLeft and ArrowRight move, videos
 * play in place, and the caption shows the alt text and who added it.
 */
export function MediaLightbox({ entries, index, onIndex, onClose }) {
  const entry = entries[index];
  const total = entries.length;
  const move = useCallback((delta) => onIndex((index + delta + total) % total), [index, onIndex, total]);
  useEffect(() => {
    const onKey = (e) => {
      const tag = e.target && e.target.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || tag === 'VIDEO') return;
      if (e.key === 'ArrowRight') { e.preventDefault(); move(1); }
      if (e.key === 'ArrowLeft') { e.preventDefault(); move(-1); }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [move]);
  if (!entry) return null;
  const media = entry.media;
  const kind = kindOf(media);
  const fallback = mediaFileUrl(media);
  return (
    <Modal open size="xl" onClose={onClose} title={vm.viewerTitle(kind, index + 1, total)}>
      <figure className="image-viewer venue-lightbox">
        {kind === 'Video' ? <VideoPlayer key={media.id} media={media} autoPlay />
          : (media.image && media.image.variants ? <ResponsiveImage media={media} prefer="large" sizes="(max-width: 1200px) 100vw, 1200px" className="image-viewer-picture" />
            : (fallback ? <img src={fallback} alt={media.altText || ''} className="image-viewer-picture" /> : null))}
        <figcaption className="media-caption">
          {media.altText && kind !== 'Video' && <span>{media.altText}</span>}
          <span className="muted small">{entry.item ? vm.addedBy(authorName(entry.item.uploader)) : vm.listingPhoto}</span>
        </figcaption>
      </figure>
      {total > 1 && (
        <div className="image-viewer-nav">
          <button type="button" className="button button-secondary button-sm" onClick={() => move(-1)}><Icon name="chevronLeft" size={14} /><span>{strings.media.previous}</span></button>
          <span className="muted small">{index + 1} / {total}</span>
          <button type="button" className="button button-secondary button-sm" onClick={() => move(1)}><span>{strings.media.next}</span><Icon name="chevronRight" size={14} /></button>
        </div>
      )}
    </Modal>
  );
}

/** Remove a member item: a plain confirm for the uploader, an optional reason for staff and linked instructors. */
function RemoveDialog({ item, onConfirm, onCancel, profile = false }) {
  const vm = profile ? { ...strings.venues.media, ...strings.profileMedia } : strings.venues.media;
  const { d } = useDirectoryTerms();
  const id = useId();
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const own = Boolean(item.viewer && item.viewer.isUploader);
  if (own) return <ConfirmModal open title={vm.deleteTitle} message={d(vm.deleteBody)} confirmLabel={strings.common.delete} onCancel={onCancel} onConfirm={() => onConfirm('')} />;
  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    setError('');
    try {
      await onConfirm(reason.trim());
    } catch (err) {
      setError(errorText(err, phase4Errors()));
      setBusy(false);
    }
  };
  return (
    <Modal open size="sm" onClose={busy ? () => {} : onCancel} title={vm.removeTitle}
      footer={<><button type="button" className="button button-secondary" onClick={onCancel} disabled={busy}>{strings.common.cancel}</button><button type="submit" form={id + '-form'} className="button button-danger" disabled={busy}>{vm.remove}</button></>}>
      <form id={id + '-form'} className="stack-form" onSubmit={submit} noValidate>
        <p>{d(vm.removeBody)}</p>
        <ErrorBanner message={error} />
        <div className="field">
          <label className="label-text" htmlFor={id + '-reason'}>{vm.reasonLabel}</label>
          <textarea id={id + '-reason'} rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} disabled={busy} />
          <span className="muted small">{vm.reasonHint}</span>
        </div>
      </form>
    </Modal>
  );
}

/** Upload photos and videos through the media flow, then attach the uploaded ids to the venue. */
function AddMedia({ venueId, onAdded, uploadOptions, profile = false }) {
  const vm = profile ? { ...strings.venues.media, ...strings.profileMedia } : strings.venues.media;
  const { client, me } = useAuth();
  const { d } = useDirectoryTerms();
  const toast = useToast();
  const perms = postingPermissions(me);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const uploads = useMediaUploads({
    client, maxCount: REVIEW_LIMITS.maxMediaPerAdd, maxVideos: REVIEW_LIMITS.maxMediaPerAdd, allowMedia: perms.canAttachMedia, allowVideo: perms.canAttachVideo,
    isAdmin: perms.isAdmin, onError: setError, ...(uploadOptions || {})
  });
  const submit = async () => {
    setError('');
    if (uploads.busy) {
      setError(vm.waitForUploads);
      return;
    }
    if (!uploads.mediaIds.length) return;
    setBusy(true);
    try {
      const result = await client[profile ? 'addProfileMedia' : 'addVenueMedia'](venueId, uploads.mediaIds);
      const added = (result && result.objects) || [];
      uploads.reset();
      onAdded(added);
      toast.success(vm.added(added.length));
    } catch (err) {
      setError(errorText(err, phase4Errors()));
    } finally {
      setBusy(false);
    }
  };
  if (!perms.canAttachMedia) return null;
  return (
    <div className="venue-media-add panel">
      <p className="muted small">{d(vm.addIntro)}</p>
      <ErrorBanner message={error} />
      <AttachmentList uploads={uploads} allowMedia allowVideo={perms.canAttachVideo} maxCount={REVIEW_LIMITS.maxMediaPerAdd} label={perms.canAttachVideo ? vm.add : vm.addImages} disabled={busy} />
      {uploads.items.length > 0 && (
        <div className="form-actions">
          <button type="button" className="button button-secondary button-sm" onClick={uploads.reset} disabled={busy}>{vm.cancel}</button>
          <button type="button" className="button button-primary button-sm" onClick={submit} disabled={busy || !uploads.mediaIds.length}>{busy ? vm.submitting : d(vm.submit)}</button>
        </div>
      )}
    </div>
  );
}

/**
 * The Media section of a venue (section 3.13): staff photos (`venue.photos`) first, then member
 * items paged with "Load more", a lightbox, member uploads when `viewer.canAddMedia` and the
 * property's `directory.memberMedia` allow it, and remove and report by each item's `viewer`.
 * @param {{ venue: object, uploadOptions?: object }} props `uploadOptions` passes through to the upload hook (tests).
 */
export function VenueMediaSection({ venue, uploadOptions, profile = false }) {
  const vm = profile ? { ...strings.venues.media, ...strings.profileMedia } : strings.venues.media;
  const [searchParams] = useSearchParams();
  const [meta, setMeta] = useState({ labels: [], tags: {} });
  const [kind, setKind] = useState('');
  const { client, me } = useAuth();
  const { property } = useProperty();
  const toast = useToast();
  const memberMedia = profile || venueContributionConfig(property).memberMedia;
  const venueId = venue.id;
  const viewer = venue.viewer || {};
  const [viewing, setViewing] = useState(null);
  const [removing, setRemoving] = useState(null);
  const [reporting, setReporting] = useState(() => searchParams.get('reportMedia') ? { id: searchParams.get('reportMedia') } : null);
  const fetchPage = useCallback((cursor, signal) => client[profile ? 'enumerateProfileMedia' : 'enumerateVenueMedia'](venueId, { ...meta, filters: kind ? { kind } : {}, continuationToken: cursor, maxResults: PAGE_SIZE }, signal), [client, venueId, profile, meta, kind]);
  const list = useCursorList(fetchPage, { enabled: memberMedia && Boolean(me) });
  const canAdd = Boolean(me) && memberMedia && viewer.canAddMedia === true;

  const entries = [
    ...(venue.photos || []).filter(Boolean).map((media) => ({ key: 'photo-' + media.id, media, item: null })),
    ...(memberMedia ? list.items.filter((item) => item && item.media).map((item) => ({ key: item.id, media: item.media, item })) : [])
  ];

  const remove = async (reason) => {
    const item = removing;
    try {
      await client[profile ? 'removeProfileMedia' : 'removeVenueMedia'](venueId, item.id, reason || undefined);
    } catch (err) {
      if (item.viewer && item.viewer.isUploader) {
        toast.error(errorText(err, phase4Errors()));
        setRemoving(null);
        return;
      }
      throw err;
    }
    list.update((items) => items.filter((x) => x.id !== item.id));
    setRemoving(null);
    setViewing(null);
    toast.success(vm.removed);
  };
  const added = (items) => list.update((current) => [...items, ...current.filter((x) => !items.some((y) => y.id === x.id))]);

  return (
    <section className="p4-section venue-media" aria-labelledby="venue-media">
      <h2 id="venue-media">{vm.title}</h2>
      {me && memberMedia && <>
        <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType={profile ? 'ProfileMedia' : 'VenueMedia'} />
        <label className="field"><span className="label-text">{vm.kind}</span><select value={kind} onChange={(event) => setKind(event.target.value)}><option value="">{vm.allKinds}</option><option value="Image">{vm.images}</option><option value="Video">{vm.videos}</option></select></label>
      </>}
      {entries.length > 0 ? (
        <ul className="venue-media-grid" aria-label={vm.galleryLabel(entries.length)}>
          {entries.map((entry, i) => {
            const kind = kindOf(entry.media);
            const itemViewer = (entry.item && entry.item.viewer) || {};
            return (
              <li key={entry.key} className="venue-media-tile" data-testid={'venue-media-' + (entry.item ? entry.item.id : entry.media.id)}>
                <button type="button" className="venue-media-open" onClick={() => setViewing(i)} aria-label={vm.openItem(kind, i + 1, entries.length) + (entry.media.altText ? ': ' + entry.media.altText : '')}>
                  <MediaThumb media={entry.media} />
                </button>
                {entry.item && (itemViewer.canRemove || itemViewer.canReport) && (
                  <span className="venue-media-actions">
                    {itemViewer.canRemove && <button type="button" className="icon-button" onClick={() => setRemoving(entry.item)} aria-label={vm.removeItem(kind, i + 1)} title={vm.remove}><Icon name="trash" size={14} /></button>}
                    {itemViewer.canReport && <button type="button" className="icon-button" onClick={() => setReporting(entry.item)} aria-label={vm.reportItem(kind, i + 1)} title={vm.report}><Icon name="flag" size={14} /></button>}
                  </span>
                )}
              </li>
            );
          })}
        </ul>
      ) : ((list.loaded || !me || !memberMedia) && <p className="muted small">{vm.empty}</p>)}
      <ErrorBanner message={list.error ? errorText(list.error, phase4Errors(), vm.loadError) : ''} onRetry={list.refresh} />
      {memberMedia && list.loaded && !list.done && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore} disabled={list.loading}>{vm.loadMore}</button>}
      {canAdd && <AddMedia profile={profile} venueId={venueId} onAdded={added} uploadOptions={uploadOptions} />}
      {viewing !== null && entries[viewing] && <MediaLightbox entries={entries} index={viewing} onIndex={setViewing} onClose={() => setViewing(null)} />}
      {removing && <RemoveDialog profile={profile} item={removing} onConfirm={remove} onCancel={() => setRemoving(null)} />}
      {reporting && <ReportDialog target={{ type: profile ? 'ProfileMedia' : 'VenueMedia', id: reporting.id }} author={reporting.uploader} onClose={() => setReporting(null)} />}
    </section>
  );
}

export default VenueMediaSection;
