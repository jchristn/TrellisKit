import strings from '../../strings';
import { emptyFilters, filtersToQuery } from '../../content/enumeration';
import { FilterButton } from '../filters/FilterBar';
import { REVIEW_LIMITS, venueContributionConfig } from '@trelliskit/shared';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Modal, Pressable, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useAuth } from '../../auth/AuthProvider';
import { describeFeatureError, imageUrl, useDirectoryStrings } from '../../content/phase4';
import { attachPermissions } from '../../content/permissions';
import { useLayout } from '../../hooks/useLayout';
import { useUploads } from '../../media/useUploads';
import { useProperty } from '../../property';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Notice from '../Notice';
import Sheet from '../Sheet';
import TextField from '../TextField';
import AttachButtons from '../feed/AttachButtons';
import MediaImage from '../feed/MediaImage';
import ReportSheet, { Phase4ReportReasons } from '../feed/ReportSheet';
import UploadTray from '../feed/UploadTray';
import VideoPlayer from '../feed/VideoPlayer';

/** Member media items per page. */
export const MediaPageSize = 24;

/**
 * Gallery entries: the staff photos first (`venue.photos`), then the member items. Each entry is
 * `{ key, media, item }` where `item` is the VenueMediaItem (null for a staff photo).
 */
export function galleryEntries(photos, items) {
  const staff = (Array.isArray(photos) ? photos : []).filter(Boolean).map((media, i) => ({ key: 'photo-' + (media.id || i), media, item: null }));
  const member = (Array.isArray(items) ? items : []).filter((it) => it && it.media).map((it) => ({ key: it.id, media: it.media, item: it }));
  return staff.concat(member);
}

function thumbUrl(media) {
  if (!media) return null;
  if (media.kind === 'Video') return (media.video && (media.video.posterThumbUrl || media.video.posterUrl)) || null;
  return imageUrl(media, 'thumb');
}

/** The add sheet: pick with the composer's buttons, upload with the composer's tray, then attach. */
function AddMediaSheet({ profile, venueId, onAdded, onClose, transport, testID }) {
  const theme = useTheme();
  const directory = useDirectoryStrings();
  const t = profile ? { ...directory, media: { ...directory.media, ...strings.profileMedia } } : directory;
  const { api, user } = useAuth();
  const uploads = useUploads({ api, transport });
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const max = REVIEW_LIMITS.maxMediaPerAdd;
  const permissions = attachPermissions(user);

  async function submit() {
    setError(null);
    if (uploads.busy || uploads.failed) return setError(t.media.waitForUploads);
    if (!uploads.mediaIds.length) return setError(t.media.nothingPicked);
    setBusy(true);
    try {
      const result = await (profile ? api.addProfileMedia(venueId, uploads.mediaIds) : api.addVenueMedia(venueId, uploads.mediaIds));
      uploads.clear();
      onAdded((result && result.objects) || []);
    } catch (e) {
      setError(describeFeatureError(e, 'places', t));
    } finally {
      setBusy(false);
    }
    return undefined;
  }

  function cancel() {
    for (const item of uploads.items) uploads.remove(item.localId);
    onClose();
  }

  return (
    <Sheet visible title={t.media.addTitle} onClose={cancel} testID={testID}>
      <AppText color="textMuted">{t.media.addNote}</AppText>
      <UploadTray uploads={uploads} testID={testID + '-uploads'} />
      <AttachButtons permissions={permissions} remaining={max - uploads.items.length} max={max} videoAllowed onPicked={uploads.add} testID={testID + '-attach'} />
      <Notice tone="error" testID={testID + '-error'}>
        {error}
      </Notice>
      <View style={{ gap: theme.spacing.sm }}>
        <Button label={busy ? t.media.adding : t.media.addSubmit} onPress={submit} disabled={busy} testID={testID + '-submit'} />
        <Button label={t.media.cancel} variant="ghost" onPress={cancel} disabled={busy} testID={testID + '-cancel'} />
      </View>
    </Sheet>
  );
}

/** Remove confirmation; a non-uploader (staff, a linked instructor) may give a reason. */
function RemoveSheet({ profile, venueId, item, onRemoved, onClose, testID }) {
  const directory = useDirectoryStrings();
  const t = profile ? { ...directory, media: { ...directory.media, ...strings.profileMedia } } : directory;
  const { api } = useAuth();
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const own = Boolean(item.viewer && item.viewer.isUploader);

  async function confirm() {
    setBusy(true);
    setError(null);
    try {
      if (profile) await api.removeProfileMedia(venueId, item.id);
      else await api.removeVenueMedia(venueId, item.id, own ? undefined : reason.trim() || undefined);
      onRemoved(item);
    } catch (e) {
      setError(describeFeatureError(e, 'places', t));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Sheet visible title={t.media.removeTitle} onClose={onClose} testID={testID}>
      <AppText color="textMuted">{own ? t.media.removeOwnBody : t.media.removeOtherBody}</AppText>
      {own ? null : <TextField label={t.media.removeReason} hint={t.media.removeReasonHint} value={reason} onChangeText={setReason} multiline maxLength={500} testID={testID + '-reason'} />}
      <Notice tone="error" testID={testID + '-error'}>
        {error}
      </Notice>
      <Button label={t.media.removeConfirm} onPress={confirm} disabled={busy} testID={testID + '-confirm'} />
      <Button label={t.media.cancel} variant="ghost" onPress={onClose} disabled={busy} testID={testID + '-cancel'} />
    </Sheet>
  );
}

/** Full-screen viewer: one entry at a time with previous and next, video playback, and the item's actions. */
function MediaViewer({ entries, index, onIndex, onClose, onRemove, onReport, testID }) {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const t = useDirectoryStrings();
  const entry = entries[index];
  if (!entry) return null;
  const { media, item } = entry;
  const viewer = (item && item.viewer) || {};
  const ready = media.status === 'Ready' || !media.status;
  const alt = media.altText || (item ? null : t.media.staffPhoto);
  return (
    <Modal visible animationType="fade" onRequestClose={onClose} supportedOrientations={['portrait', 'landscape']}>
      <View testID={testID} accessibilityViewIsModal style={{ flex: 1, backgroundColor: '#000000', paddingTop: insets.top, paddingBottom: insets.bottom }}>
        <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', padding: theme.spacing.sm, gap: theme.spacing.sm }}>
          <AppText style={{ color: '#FFFFFF' }} accessibilityLiveRegion="polite" testID={testID + '-position'}>
            {t.media.position(index + 1, entries.length)}
          </AppText>
          <Button label={t.media.close} variant="secondary" onPress={onClose} style={{ alignSelf: 'auto' }} testID={testID + '-close'} />
        </View>
        <View style={{ flex: 1 }}>
          {!ready ? null : media.kind === 'Video' ? (
            <VideoPlayer media={media} style={{ flex: 1 }} testID={testID + '-video'} />
          ) : (
            <MediaImage url={imageUrl(media, 'large')} blurhash={media.blurhash} alt={alt} contentFit="contain" style={{ flex: 1 }} testID={testID + '-image'} />
          )}
        </View>
        {item && item.uploader ? (
          <AppText variant="caption" style={{ color: '#FFFFFF', textAlign: 'center', padding: theme.spacing.xs }}>
            {t.media.by(item.uploader.displayName || item.uploader.handle || '')}
          </AppText>
        ) : null}
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', justifyContent: 'center', gap: theme.spacing.sm, padding: theme.spacing.sm }}>
          <Button label={t.media.previous} variant="secondary" onPress={() => onIndex(index - 1)} disabled={index <= 0} style={{ alignSelf: 'auto' }} testID={testID + '-previous'} />
          <Button label={t.media.next} variant="secondary" onPress={() => onIndex(index + 1)} disabled={index >= entries.length - 1} style={{ alignSelf: 'auto' }} testID={testID + '-next'} />
          {item && viewer.canRemove ? <Button label={t.media.remove} variant="secondary" onPress={() => onRemove(item)} style={{ alignSelf: 'auto' }} testID={testID + '-remove'} /> : null}
          {item && viewer.canReport ? <Button label={t.media.report} variant="secondary" onPress={() => onReport(item)} style={{ alignSelf: 'auto' }} testID={testID + '-report'} /> : null}
        </View>
      </View>
    </Modal>
  );
}

/**
 * The Media section of a venue (docs/PHASE4_CONTRACT.md section 3.13): a thumbnail grid of the
 * staff photos, then the member photos and videos (paged), a full-screen viewer with video
 * playback, "Add photos or videos" when `viewer.canAddMedia` (and member media is on), and per
 * item Remove and Report from the item's `viewer` flags. Three columns on phones, five on wide
 * windows.
 */
export default function VenueMedia({ profile = false, initialReportId = null, venue, transport, testID = 'venue-media' }) {
  const theme = useTheme();
  const directory = useDirectoryStrings();
  const t = profile ? { ...directory, media: { ...directory.media, ...strings.profileMedia } } : directory;
  const property = useProperty();
  const { api, user } = useAuth();
  const { isWide } = useLayout();
  const [items, setItems] = useState([]);
  const [token, setToken] = useState(null);
  const [status, setStatus] = useState('idle');
  const [width, setWidth] = useState(0);
  const [open, setOpen] = useState(null);
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState(null);
  const [reporting, setReporting] = useState(() => initialReportId ? { id: initialReportId } : null);
  const generation = useRef(0);
  const memberMedia = profile || venueContributionConfig(property).memberMedia;
  const [filters, setFilters] = useState(() => emptyFilters());
  const viewer = venue.viewer || {};
  const signedIn = Boolean(user);
  const venueId = venue.id;
  const hasMemberMedia = profile || (venue.mediaCount || 0) > 0;

  const load = useCallback(
    async (continuationToken) => {
      const gen = continuationToken ? generation.current : (generation.current += 1);
      setStatus(continuationToken ? 'more' : 'loading');
      try {
        const page = await (profile ? api.enumerateProfileMedia : api.enumerateVenueMedia).call(api, venueId, { ...filtersToQuery(filters), maxResults: MediaPageSize, continuationToken: continuationToken || undefined });
        if (gen !== generation.current) return;
        const objects = (page && page.objects) || [];
        setItems((current) => (continuationToken ? current.concat(objects) : objects));
        setToken(page && !page.endOfResults && page.continuationToken ? page.continuationToken : null);
        setStatus('ready');
      } catch {
        if (gen !== generation.current) return;
        setStatus('failed');
      }
    },
    [api, venueId, profile, filters]
  );

  useEffect(() => {
    // Fetch the first page when the section becomes visible (the list state is set from the response).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (signedIn && hasMemberMedia) load(null);
  }, [signedIn, hasMemberMedia, load]);

  const entries = galleryEntries(venue.photos, items);
  const canAdd = Boolean(signedIn && memberMedia && viewer.canAddMedia && attachPermissions(user).media);


  const columns = isWide ? 5 : 3;
  const gap = theme.spacing.xs;
  const tile = width ? Math.floor((width - gap * (columns - 1)) / columns) : 0;

  return (
    <View style={{ gap: theme.spacing.md }} testID={testID}>
      <AppText variant="heading" accessibilityRole="header">
        {t.media.title}
      </AppText>
      {signedIn && memberMedia ? <FilterButton sheetOnly showSearch={false} filters={filters} onChange={setFilters} targetType={profile ? 'ProfileMedia' : 'VenueMedia'} testID={testID + '-filters'} /> : null}
      {entries.length ? (
        <View onLayout={(e) => setWidth(e.nativeEvent.layout.width)} style={{ flexDirection: 'row', flexWrap: 'wrap', gap }} testID={testID + '-grid'}>
          {entries.map((entry, index) => {
            const url = thumbUrl(entry.media);
            const kind = entry.media.kind === 'Video' ? t.media.video : t.media.photo;
            return (
              <Pressable
                key={entry.key}
                onPress={() => setOpen(index)}
                accessibilityRole="imagebutton"
                accessibilityLabel={t.media.item(kind, index + 1, entries.length) + (entry.media.altText ? ', ' + entry.media.altText : '')}
                accessibilityHint={t.media.open}
                testID={testID + '-item-' + index}
                style={({ pressed }) => ({ width: tile || '31%', aspectRatio: 1, borderRadius: theme.radii.md, overflow: 'hidden', backgroundColor: theme.colors.surface, opacity: pressed ? 0.8 : 1 })}
              >
                {url ? <MediaImage url={url} blurhash={entry.media.blurhash} alt={null} style={{ width: '100%', height: '100%' }} /> : null}
                {entry.media.kind === 'Video' ? (
                  <View style={{ position: 'absolute', bottom: 4, left: 4, paddingHorizontal: 6, borderRadius: theme.radii.sm, backgroundColor: 'rgba(0,0,0,0.6)' }}>
                    <AppText variant="caption" style={{ color: '#FFFFFF' }}>
                      {t.media.video}
                    </AppText>
                  </View>
                ) : null}
              </Pressable>
            );
          })}
        </View>
      ) : (
        <AppText color="textMuted">{t.media.empty}</AppText>
      )}
      {status === 'failed' ? <><Notice tone="error">{t.loadMoreFailed}</Notice><Button label={strings.common.retry} onPress={() => load(null)} /></> : null}
      {token ? <Button label={t.media.loadMore} variant="ghost" onPress={() => load(token)} disabled={status === 'more'} style={{ alignSelf: 'auto' }} testID={testID + '-more'} /> : null}
      {canAdd ? <Button label={t.media.add} accessibilityHint={t.media.addHint} variant="secondary" onPress={() => setAdding(true)} style={{ alignSelf: 'auto' }} testID={testID + '-add'} /> : null}
      {open !== null && entries[open] ? (
        <MediaViewer
          entries={entries}
          index={open}
          onIndex={(i) => setOpen(Math.max(0, Math.min(entries.length - 1, i)))}
          onClose={() => setOpen(null)}
          onRemove={(item) => {
            setOpen(null);
            setRemoving(item);
          }}
          onReport={(item) => {
            setOpen(null);
            setReporting(item);
          }}
          testID={testID + '-viewer'}
        />
      ) : null}
      {adding ? (
        <AddMediaSheet
          profile={profile}
          venueId={venueId}
          transport={transport}
          onAdded={(added) => {
            setItems((current) => added.concat(current));
            setAdding(false);
          }}
          onClose={() => setAdding(false)}
          testID={testID + '-add-sheet'}
        />
      ) : null}
      {removing ? (
        <RemoveSheet
          profile={profile}
          venueId={venueId}
          item={removing}
          onRemoved={(item) => {
            setItems((current) => current.filter((i) => i.id !== item.id));
            setRemoving(null);
          }}
          onClose={() => setRemoving(null)}
          testID={testID + '-remove'}
        />
      ) : null}
      {reporting ? <ReportSheet reasons={Phase4ReportReasons} target={{ targetType: profile ? 'ProfileMedia' : 'VenueMedia', targetId: reporting.id }} author={reporting.uploader || null} onClose={() => setReporting(null)} /> : null}
    </View>
  );
}
