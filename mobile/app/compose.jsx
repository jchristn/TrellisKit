import { useLocalSearchParams, useNavigation, useRouter } from 'expo-router';
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { View } from 'react-native';
import KeyboardAvoider from '../src/components/KeyboardAvoider';
import { feed as feedApi, linkPreviews, media as mediaApi } from '../src/api/community';
import { useAuth } from '../src/auth/AuthProvider';
import AppText from '../src/components/AppText';
import Button from '../src/components/Button';
import CheckRow from '../src/components/CheckRow';
import ChipGroup from '../src/components/ChipGroup';
import ErrorState from '../src/components/ErrorState';
import AttachButtons from '../src/components/feed/AttachButtons';
import MetadataEditor from '../src/components/filters/MetadataEditor';
import ConfirmSheet from '../src/components/feed/ConfirmSheet';
import LinkPreviewCard from '../src/components/feed/LinkPreviewCard';
import MarkdownBody from '../src/components/feed/MarkdownBody';
import MentionInput from '../src/components/feed/MentionInput';
import UploadTray from '../src/components/feed/UploadTray';
import Loading from '../src/components/Loading';
import MemberGate from '../src/components/MemberGate';
import FeatureOff from '../src/components/FeatureOff';
import { useKillSwitch } from '../src/appConfig';
import Notice from '../src/components/Notice';
import Screen from '../src/components/Screen';
import TextField from '../src/components/TextField';
import { contentFieldErrors, describeContentError } from '../src/content/errors';
import { emitContentEvent } from '../src/content/events';
import { firstUrl } from '../src/content/markdown';
import { metadataChanged, metadataOf, metadataPayload } from '../src/content/metadata';
import { attachPermissions, canCreateFeedPost, canWrite, ComposerLimits, isMuted, isStaffUser } from '../src/content/permissions';
import { UploadStatus } from '../src/media/upload';
import { composerError, useUploads } from '../src/media/useUploads';
import strings from '../src/strings';
import { useTheme } from '../src/theme';
import { useAccessPrompt } from '../src/components/AccessPrompt';
import AudiencePicker, { useAudiencePicker } from '../src/components/AudiencePicker';
import { isSystemAdmin } from '../src/auth/routing';

/** Wait this long after typing before asking for a link preview. */
export const LinkPreviewDebounceMs = 600;

function mutedUntilText(user) {
  const until = user && user.flags && user.flags.mutedUntilUtc;
  if (!until) return null;
  const date = new Date(until);
  return Number.isNaN(date.getTime()) ? null : date.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
}

/**
 * The feed post form: title, Markdown body with preview and @mention autocomplete, media from the
 * library or camera (upload sessions with progress, multipart, resume, then processing), alt
 * text and labels and tags per attachment, a live link preview with a remove control, comments on
 * or off, and the post's labels and tags.
 */
function ComposerForm({ post, transport }) {
  const theme = useTheme();
  const router = useRouter();
  const navigation = useNavigation();
  const { api, user } = useAuth();
  const editing = Boolean(post);
  const [title, setTitle] = useState(post ? post.title || '' : '');
  const [body, setBody] = useState(post ? post.body || '' : '');
  const [tab, setTab] = useState('write');
  const [commentsEnabled, setCommentsEnabled] = useState(post ? post.commentsEnabled !== false : true);
  const audience = useAudiencePicker({ feature: 'feed', isSystemAdmin: isSystemAdmin(user), current: post ? post.audience : null });
  const accessPrompt = useAccessPrompt();
  const [showLinkPreview, setShowLinkPreview] = useState(post ? Boolean(post.linkPreview) || !firstUrl(post.body) : true);
  const [preview, setPreview] = useState(post ? post.linkPreview : null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [fieldErrors, setFieldErrors] = useState({});
  const [confirmDiscard, setConfirmDiscard] = useState(false);
  const [initialMeta] = useState(() => metadataOf(post));
  const [meta, setMeta] = useState(() => metadataOf(post));
  const staff = isStaffUser(user);
  const uploads = useUploads({ api, initial: post ? post.attachments || [] : [], transport });
  const permissions = attachPermissions(user, post ? post.viewer : null);
  const t = strings.composer;
  const url = firstUrl(body);
  const dirty = title !== (post ? post.title || '' : '') || body !== (post ? post.body || '' : '') || uploads.items.some((i) => !i.existing);

  useLayoutEffect(() => {
    navigation.setOptions({ title: editing ? t.editTitle : t.newTitle });
  }, [navigation, editing, t.editTitle, t.newTitle]);

  const previewRequest = useRef(0);
  useEffect(() => {
    if (!url || !showLinkPreview) {
      // No URL, or the member removed the card.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      if (!url) setPreview(null);
      return undefined;
    }
    if (preview && (preview.url === url || preview.canonicalUrl === url)) return undefined;
    const id = (previewRequest.current += 1);
    const timer = setTimeout(async () => {
      try {
        const result = await linkPreviews.get(api, url);
        if (id === previewRequest.current) setPreview(result);
      } catch {
        if (id === previewRequest.current) setPreview(null);
      }
    }, LinkPreviewDebounceMs);
    return () => clearTimeout(timer);
  }, [api, url, showLinkPreview, preview]);

  const videos = uploads.items.filter((i) => (i.file && i.file.kind === 'Video') || (i.media && i.media.kind === 'Video')).length;
  const remaining = ComposerLimits.postAttachments - uploads.items.length;

  const close = useCallback(() => {
    if (router.canGoBack()) router.back();
    else router.replace('/feed');
  }, [router]);

  async function submit() {
    setError(null);
    setFieldErrors({});
    const text = body.trim();
    if (!text && !uploads.mediaIds.length) return setFieldErrors({ body: t.empty });
    if (title.length > ComposerLimits.postTitle) return setFieldErrors({ title: t.tooLong(ComposerLimits.postTitle) });
    if (text.length > ComposerLimits.postBody) return setFieldErrors({ body: t.tooLong(ComposerLimits.postBody) });
    if (uploads.busy || uploads.failed) return setError(t.waitForUploads);
    setBusy(true);
    try {
      for (const item of uploads.items) {
        if (!item.media) continue;
        const change = {};
        const previous = item.media.altText || '';
        if (item.altText !== undefined && item.altText !== previous) change.altText = item.altText;
        if (item.metadata && metadataChanged(item.metadata, metadataOf(item.media))) Object.assign(change, metadataPayload(item.metadata, { staff }));
        if (Object.keys(change).length) await mediaApi.update(api, item.media.id, change);
      }
      const payload = { title: title.trim() || null, body: text, mediaIds: uploads.mediaIds, commentsEnabled, showLinkPreview };
      if (audience.visible) payload.audience = audience.value;
      if (!editing || metadataChanged(meta, initialMeta)) Object.assign(payload, metadataPayload(meta, { staff }));
      const saved = editing ? await feedApi.update(api, post.id, payload) : await feedApi.create(api, payload);
      emitContentEvent('post', saved);
      close();
    } catch (e) {
      if (accessPrompt.handle(e)) return;
      setFieldErrors(contentFieldErrors(e));
      setError(describeContentError(e));
    } finally {
      setBusy(false);
    }
    return undefined;
  }

  const processing = uploads.items.some((i) => i.status === UploadStatus.Processing);

  return (
    <KeyboardAvoider style={{ flex: 1 }} iosOffset={96}>
      <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-compose">
        <TextField label={t.titleLabel} hint={t.titleHint} value={title} onChangeText={setTitle} maxLength={ComposerLimits.postTitle} error={fieldErrors.title} testID="compose-title" />
        <ChipGroup label={t.bodyLabel} options={[{ value: 'write', label: t.write }, { value: 'preview', label: t.preview }]} value={tab} onToggle={setTab} testID="compose-tab" />
        {tab === 'write' ? (
          <MentionInput label={t.bodyLabel} hint={t.bodyHint} error={fieldErrors.body} value={body} onChangeText={setBody} maxLength={ComposerLimits.postBody} contextType={post ? 'FeedPost' : undefined} contextId={post ? post.id : undefined} testID="compose-body" />
        ) : (
          <View style={{ gap: theme.spacing.sm, borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radii.md, padding: theme.spacing.md }} testID="compose-preview">
            {title.trim() ? <AppText variant="heading">{title.trim()}</AppText> : null}
            {body.trim() ? <MarkdownBody body={body} mentions={post ? post.mentions : []} headings /> : <AppText color="textMuted">{t.previewEmpty}</AppText>}
          </View>
        )}
        <AppText variant="caption" color="textMuted" style={{ textAlign: 'right' }}>
          {t.count(body.length, ComposerLimits.postBody)}
        </AppText>
        {url && showLinkPreview && preview ? (
          <View style={{ gap: theme.spacing.xs }}>
            <AppText variant="label">{t.linkPreview}</AppText>
            <LinkPreviewCard
              preview={preview}
              onRemove={() => {
                setShowLinkPreview(false);
                setPreview(null);
              }}
              testID="compose-link-preview"
            />
          </View>
        ) : null}
        {url && !showLinkPreview ? <CheckRow label={t.showLinkPreview} value={false} onValueChange={(v) => setShowLinkPreview(v)} testID="compose-show-preview" /> : null}
        <UploadTray uploads={uploads} showAltText testID="compose-uploads" />
        {processing ? (
          <AppText variant="caption" color="textMuted">
            {t.stalled}
          </AppText>
        ) : null}
        <AttachButtons permissions={permissions} remaining={remaining} max={ComposerLimits.postAttachments} videoAllowed={videos < ComposerLimits.postVideos} onPicked={uploads.add} testID="compose-attach" />
        <CheckRow label={t.commentsEnabled} value={commentsEnabled} onValueChange={setCommentsEnabled} testID="compose-comments-enabled" />
        <AudiencePicker picker={audience} testID="compose-audience" />
        <MetadataEditor value={meta} onChange={setMeta} targetType="FeedPost" testID="compose-metadata" />
        <Notice tone="error" testID="compose-error">
          {composerError(error, uploads, t.waitForUploads)}
        </Notice>
        <Button label={busy ? (editing ? t.saving : t.publishing) : editing ? t.save : t.publish} onPress={submit} disabled={busy} testID="compose-submit" />
        <Button label={t.cancel} variant="ghost" onPress={() => (dirty ? setConfirmDiscard(true) : close())} disabled={busy} testID="compose-cancel" />
      </Screen>
      <ConfirmSheet
        visible={confirmDiscard}
        title={t.discardTitle}
        body={t.discardBody}
        confirmLabel={t.discard}
        onConfirm={() => {
          setConfirmDiscard(false);
          for (const item of uploads.items) if (!item.existing) uploads.remove(item.localId);
          close();
        }}
        onClose={() => setConfirmDiscard(false)}
        testID="compose-discard"
      />
    </KeyboardAvoider>
  );
}

/**
 * Composer route (`/compose`, `/compose?id={postId}` to edit). Only members allowed to post see
 * the form: SystemAdmin, or `canPostToFeed` and `canPost` (and not muted); editing follows the
 * post's `viewer.canEdit`.
 */
export default function ComposeScreen() {
  const theme = useTheme();
  const { api, user } = useAuth();
  const { id } = useLocalSearchParams();
  const postId = Array.isArray(id) ? id[0] : id;
  const [post, setPost] = useState(null);
  const [status, setStatus] = useState(postId ? 'loading' : 'ready');
  const [error, setError] = useState(null);

  const load = useCallback(async () => {
    if (!postId) return;
    setStatus('loading');
    try {
      setPost(await feedApi.get(api, postId));
      setStatus('ready');
    } catch (e) {
      setError(e);
      setStatus('error');
    }
  }, [api, postId]);

  useEffect(() => {
    // The first load starts in the loading state already.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    load();
  }, [load]);

  const composerKill = useKillSwitch('feedComposer');
  let content;
  if (composerKill.killed) content = <FeatureOff featureKey="feedComposer" message={composerKill.message || strings.appContent.composerOff} testID="compose-off" />;
  else if (status === 'loading') content = <Loading label={strings.common.loading} />;
  else if (status === 'error')
    content = (
      <Screen>
        <ErrorState title={strings.feed.postLoadFailedTitle} message={describeContentError(error)} onRetry={load} testID="compose-load-error" />
      </Screen>
    );
  else if (postId ? !(post && post.viewer && post.viewer.canEdit) || !canWrite(user) : !canCreateFeedPost(user))
    content = (
      <Screen contentStyle={{ gap: theme.spacing.md }} testID="compose-not-allowed">
        <AppText variant="heading">{strings.composer.newTitle}</AppText>
        <AppText color="textMuted">{isMuted(user) ? strings.composer.muted(mutedUntilText(user)) : strings.composer.notAllowed}</AppText>
      </Screen>
    );
  else content = <ComposerForm key={post ? post.id : 'new'} post={post} />;

  return <MemberGate>{content}</MemberGate>;
}
