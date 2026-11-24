import { ServerCapability, ThreadType, emptyPollDraft, pollPayload, threadCreateBody, validatePollDraft, validateThreadDraft } from '@trelliskit/shared';
import { useLocalSearchParams, useNavigation, useRouter } from 'expo-router';
import { useCallback, useEffect, useLayoutEffect, useState } from 'react';
import { useAuth } from '../src/auth/AuthProvider';
import AppText from '../src/components/AppText';
import Button from '../src/components/Button';
import KeyboardAvoider from '../src/components/KeyboardAvoider';
import ChipGroup from '../src/components/ChipGroup';
import { CapabilityGate } from '../src/components/ComingSoon';
import ErrorState from '../src/components/ErrorState';
import AttachButtons from '../src/components/feed/AttachButtons';
import MentionInput from '../src/components/feed/MentionInput';
import UploadTray from '../src/components/feed/UploadTray';
import MetadataEditor from '../src/components/filters/MetadataEditor';
import PollBuilder, { closeAfterDays } from '../src/components/forums/PollBuilder';
import Loading from '../src/components/Loading';
import MemberGate from '../src/components/MemberGate';
import Notice from '../src/components/Notice';
import Screen from '../src/components/Screen';
import TextField from '../src/components/TextField';
import { contentFieldErrors, describeContentError } from '../src/content/errors';
import { metadataChanged, metadataOf, metadataPayload } from '../src/content/metadata';
import { attachPermissions, canWrite, ComposerLimits, isMuted, isStaffUser } from '../src/content/permissions';
import { composerError, useUploads } from '../src/media/useUploads';
import strings from '../src/strings';
import { useTheme } from '../src/theme';
import { isSystemAdmin } from '../src/auth/routing';
import { useAccessPrompt } from '../src/components/AccessPrompt';
import AudiencePicker, { useAudiencePicker } from '../src/components/AudiencePicker';

const t = strings.forums;
const c = t.composer;
const MAX_ATTACHMENTS = 10;

/**
 * The thread form (docs/PHASE3_CONTRACT.md sections 4.4 and 4.5). Creating: a type picker limited
 * to the forum's `viewer.allowedThreadTypes`, title, Markdown body with @mentions, media from the
 * library or camera (expo-image-picker; a Feedback thread requires a video), the poll builder,
 * the review focus, and labels and tags. Editing (`mode` edit): the author's fields. `metadata`
 * mode: a moderator's title, type (Discussion or Question), labels, and tags.
 */
function ThreadForm({ forum, thread, mode, transport }) {
  const theme = useTheme();
  const router = useRouter();
  const navigation = useNavigation();
  const { api, user } = useAuth();
  const creating = !thread;
  const metadataOnly = mode === 'metadata';
  const allowed = creating ? ((forum && forum.viewer && forum.viewer.allowedThreadTypes) || []) : metadataOnly && (thread.type === 'Discussion' || thread.type === 'Question') ? ['Discussion', 'Question'] : [thread.type];
  const [type, setType] = useState(thread ? thread.type : allowed[0]);
  const audience = useAudiencePicker({ feature: 'forums', isSystemAdmin: isSystemAdmin(user), floor: forum && forum.audience });
  const accessPrompt = useAccessPrompt();
  const [title, setTitle] = useState(thread ? thread.title || '' : '');
  const [body, setBody] = useState(thread ? thread.body || '' : '');
  const [focus, setFocus] = useState(thread && thread.feedback ? thread.feedback.focus || '' : '');
  const [poll, setPoll] = useState(() => ({ ...emptyPollDraft(), closeInDays: '' }));
  const [initialMeta] = useState(() => metadataOf(thread));
  const [meta, setMeta] = useState(() => metadataOf(thread));
  const [problems, setProblems] = useState({});
  const [pollProblems, setPollProblems] = useState({});
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const staff = isStaffUser(user);
  const uploads = useUploads({ api, initial: thread ? thread.attachments || [] : [], transport });
  const viewerPerms = attachPermissions(user, thread ? thread.viewer : null);
  const permissions = { media: viewerPerms.media && (!forum || forum.allowMedia !== false), video: viewerPerms.video && (!forum || forum.allowVideo !== false) };
  const videos = uploads.items.filter((i) => (i.file && i.file.kind === 'Video') || (i.media && i.media.kind === 'Video')).length;

  useLayoutEffect(() => {
    navigation.setOptions({ title: creating ? c.newTitle : metadataOnly ? c.metadataTitle : c.editTitle });
  }, [navigation, creating, metadataOnly]);

  const close = useCallback(() => {
    if (router.canGoBack()) router.back();
    else router.replace('/forums');
  }, [router]);

  async function submit() {
    setError(null);
    if (uploads.busy || uploads.failed) return setError(strings.composer.waitForUploads);
    const pollDraft = { ...poll, closesUtc: closeAfterDays(poll.closeInDays) };
    if (String(poll.closeInDays || '').trim() && !pollDraft.closesUtc) pollDraft.closesUtc = 'invalid';
    const foundPoll = creating && type === ThreadType.Poll ? validatePollDraft(pollDraft) : {};
    const found = validateThreadDraft({ type, title, body: metadataOnly ? 'x' : body, mediaCount: uploads.mediaIds.length, videoCount: metadataOnly ? 1 : videos, focus, allowedTypes: creating || metadataOnly ? allowed : undefined, pollProblems: foundPoll });
    setProblems(found);
    setPollProblems(foundPoll);
    if (Object.keys(found).length) return undefined;
    setBusy(true);
    try {
      const metaBody = creating || metadataChanged(meta, initialMeta) ? metadataPayload(meta, { staff }) : {};
      let saved;
      if (creating) {
        const { closeInDays: _days, ...draft } = pollDraft;
        saved = await api.createThread(forum.id, threadCreateBody({ type, title, body, mediaIds: uploads.mediaIds, poll: type === ThreadType.Poll ? pollPayload(draft) : null, focus, audience: audience.visible ? audience.value : null, ...metaBody }));
        router.replace('/threads/' + saved.id);
        return undefined;
      }
      if (metadataOnly) saved = await api.updateThread(thread.id, { title: title.trim(), ...(type !== thread.type ? { type } : {}), ...metaBody });
      else saved = await api.updateThread(thread.id, { title: title.trim(), body: body.trim(), mediaIds: uploads.mediaIds, ...(thread.type === ThreadType.Feedback ? { feedback: { focus: focus.trim() } } : {}), ...metaBody });
      if (saved) close();
    } catch (e) {
      if (accessPrompt.handle(e)) return;
      setProblems(contentFieldErrors(e));
      setError(describeContentError(e));
    } finally {
      setBusy(false);
    }
    return undefined;
  }

  const text = (code) => (code ? c.problems[code] || code : null);
  return (
    <KeyboardAvoider style={{ flex: 1 }} iosOffset={96}>
      <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-compose-thread">
        {allowed.length > 1 ? <ChipGroup label={c.typeLabel} options={allowed.map((value) => ({ value, label: t.types[value] }))} value={type} onToggle={setType} error={text(problems.type)} testID="thread-type" /> : null}
        {type ? <AppText color="textMuted">{t.typeHints[type]}</AppText> : null}
        <TextField label={type === ThreadType.Poll ? t.poll.title + ': ' + c.titleLabel : c.titleLabel} hint={c.titleHint} value={title} onChangeText={setTitle} maxLength={160} error={text(problems.title)} testID="thread-title" />
        {!metadataOnly ? (
          <MentionInput label={type === ThreadType.Poll || type === ThreadType.Feedback ? c.bodyOptional : c.bodyLabel} hint={c.bodyHint} error={text(problems.body)} value={body} onChangeText={setBody} maxLength={ComposerLimits.postBody} contextType={thread ? 'Thread' : undefined} contextId={thread ? thread.id : undefined} testID="thread-body" />
        ) : null}
        {!metadataOnly && type === ThreadType.Feedback ? <TextField label={c.focusLabel} value={focus} onChangeText={setFocus} maxLength={500} multiline error={text(problems.focus)} testID="thread-focus" /> : null}
        {!metadataOnly ? (
          <>
            {type === ThreadType.Feedback ? <AppText color="textMuted">{c.videoRequired}</AppText> : null}
            <UploadTray uploads={uploads} showAltText testID="thread-uploads" />
            <AttachButtons permissions={permissions} remaining={MAX_ATTACHMENTS - uploads.items.length} max={MAX_ATTACHMENTS} videoAllowed={videos < 1} onPicked={uploads.add} testID="thread-attach" />
            {problems.media ? (
              <AppText color="danger" accessibilityRole="alert" testID="thread-media-problem">
                {text(problems.media)}
              </AppText>
            ) : null}
          </>
        ) : null}
        {creating ? <AudiencePicker picker={audience} testID="thread-audience" /> : null}
        {creating && type === ThreadType.Poll ? <PollBuilder value={poll} onChange={setPoll} problems={pollProblems} /> : null}
        <MetadataEditor value={meta} onChange={setMeta} targetType="Thread" testID="thread-metadata" />
        <Notice tone="error" testID="thread-error">
          {composerError(error, uploads, strings.composer.waitForUploads)}
        </Notice>
        <Button label={busy ? c.saving : creating ? c.submit : c.save} onPress={submit} disabled={busy} testID="thread-submit" />
        <Button label={c.cancel} variant="ghost" onPress={close} disabled={busy} testID="thread-cancel" />
      </Screen>
    </KeyboardAvoider>
  );
}

/**
 * Thread composer route: `/compose-thread?forumId=` to create, `?id=` to edit, `?id=&mode=metadata`
 * for a moderator's edit of title, type, labels, and tags.
 */
/** The thread form route; "Coming soon" until the server reports `features.forums`. */
export default function ComposeThreadRoute() {
  return (
    <CapabilityGate capability={ServerCapability.Forums} title={strings.forums.title} summary={strings.forums.summary} items={strings.forums.comingSoonItems} testID="screen-compose-thread-coming-soon">
      <ComposeThreadScreen />
    </CapabilityGate>
  );
}

function ComposeThreadScreen() {
  const theme = useTheme();
  const { api, user } = useAuth();
  const { forumId, id, mode } = useLocalSearchParams();
  const fid = Array.isArray(forumId) ? forumId[0] : forumId;
  const tid = Array.isArray(id) ? id[0] : id;
  const m = Array.isArray(mode) ? mode[0] : mode;
  const [state, setState] = useState({ status: 'loading', forum: null, thread: null, error: null });

  const load = useCallback(async () => {
    setState((s) => ({ ...s, status: 'loading' }));
    try {
      const thread = tid ? await api.getThread(tid) : null;
      const forum = fid ? await api.getForum(fid) : null;
      setState({ status: 'ready', forum, thread, error: null });
    } catch (error) {
      setState({ status: 'error', forum: null, thread: null, error });
    }
  }, [api, fid, tid]);

  useEffect(() => {
    // The first load starts in the loading state already.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    load();
  }, [load]);

  let content;
  if (state.status === 'loading') content = <Loading label={strings.common.loading} />;
  else if (state.status === 'error')
    content = (
      <Screen>
        <ErrorState title={t.threadFailedTitle} message={describeContentError(state.error)} onRetry={load} testID="compose-thread-error" />
      </Screen>
    );
  else {
    const { forum, thread } = state;
    const viewer = (thread && thread.viewer) || {};
    const allowed = thread ? (m === 'metadata' ? viewer.canModerate : viewer.canEdit) && canWrite(user) : forum && forum.viewer && forum.viewer.canCreateThread !== false && (forum.viewer.allowedThreadTypes || []).length > 0 && !forum.isArchived && canWrite(user) && !isMuted(user);
    content = allowed ? (
      <ThreadForm key={(thread && thread.id) || (forum && forum.id)} forum={forum} thread={thread} mode={m} />
    ) : (
      <Screen contentStyle={{ gap: theme.spacing.md }} testID="compose-thread-not-allowed">
        <AppText variant="heading">{c.newTitle}</AppText>
        <AppText color="textMuted">{isMuted(user) ? strings.composer.muted(null) : c.notAllowed}</AppText>
      </Screen>
    );
  }
  return <MemberGate>{content}</MemberGate>;
}
