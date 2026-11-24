import { useLocalSearchParams, useRouter } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../../src/auth/AuthProvider';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import CheckRow from '../../src/components/CheckRow';
import ChipGroup from '../../src/components/ChipGroup';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import TextField from '../../src/components/TextField';
import AttachButtons from '../../src/components/feed/AttachButtons';
import UploadTray from '../../src/components/feed/UploadTray';
import SupportEmailCard from '../../src/components/support/SupportEmailCard';
import { describeTicketError } from '../../src/components/support/TicketDetail';
import { contentFieldErrors } from '../../src/content/errors';
import { emitContentEvent } from '../../src/content/events';
import { composerError, useUploads } from '../../src/media/useUploads';
import { useProperty } from '../../src/property';
import strings from '../../src/strings';
import { deviceDiagnostics, supportEnabled, TicketLimits, ticketProblems } from '../../src/support/support';
import { useTheme } from '../../src/theme';

const t = strings.support;

/**
 * A new support request (`POST /tickets`, contract section 6.1): the property's ticket types with
 * their help text, subject, description, screenshots (`purpose: "Ticket"`), and "Attach device
 * details", on by default on mobile, which sends the platform, system version, and device model
 * through the shared `createTicket` diagnostics. `?type=` preselects a type.
 */
export default function NewTicketScreen() {
  const property = useProperty();
  if (!supportEnabled(property)) {
    return (
      <Screen testID="screen-new-ticket">
        <SupportEmailCard />
      </Screen>
    );
  }
  return <NewTicketForm />;
}

function NewTicketForm() {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const params = useLocalSearchParams();
  const preset = Array.isArray(params.type) ? params.type[0] : params.type;
  const [types, setTypes] = useState([]);
  const [typesError, setTypesError] = useState(null);
  const [type, setType] = useState(preset || null);
  const [title, setTitle] = useState('');
  const [body, setBody] = useState('');
  const [diagnostics, setDiagnostics] = useState(true);
  const [problems, setProblems] = useState({});
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const uploads = useUploads({ api, purpose: 'Ticket' });

  const loadTypes = useCallback(async () => {
    setTypesError(null);
    try {
      const result = await api.enumerateTicketTypes({ maxResults: 50 });
      const list = (result && Array.isArray(result.objects) ? result.objects : []).filter((x) => !x.isRetired);
      setTypes(list);
      setType((current) => (current && list.some((x) => x.key === current) ? current : list.length === 1 ? list[0].key : null));
    } catch (e) {
      setTypesError(describeTicketError(e));
    }
  }, [api]);

  useEffect(() => {
    // loadTypes sets state after its request resolves.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    loadTypes();
  }, [loadTypes]);

  async function submit() {
    setError(null);
    const found = ticketProblems({ type, title, body });
    setProblems(found);
    if (Object.keys(found).length) return;
    if (uploads.busy || uploads.failed) return setError(strings.composer.waitForUploads);
    setBusy(true);
    try {
      const ticket = await api.createTicket({ type, title: title.trim(), body: body.trim(), mediaIds: uploads.mediaIds, includeDiagnostics: diagnostics, diagnostics: diagnostics ? deviceDiagnostics() : undefined });
      uploads.clear();
      emitContentEvent('ticket', ticket);
      router.replace('/support/tickets/' + ticket.id);
    } catch (e) {
      const fields = contentFieldErrors(e);
      if (Object.keys(fields).length) setProblems({ ...fields, raw: true });
      setError(describeTicketError(e));
    } finally {
      setBusy(false);
    }
  }

  const selected = types.find((x) => x.key === type);
  const problemText = (key) => (problems[key] ? (problems.raw ? problems[key] : t[problems[key]]) : null);
  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-new-ticket">
      <AppText color="textMuted">{t.summary}</AppText>
      {typesError ? (
        <Notice tone="error" testID="new-ticket-types-error">
          {t.typesLoadFailed}
        </Notice>
      ) : null}
      {types.length ? <ChipGroup label={t.type} options={types.map((x) => ({ value: x.key, label: x.label || x.key }))} value={type} onToggle={setType} error={problemText('type')} testID="new-ticket-type" /> : null}
      {selected && (selected.helpText || selected.description) ? (
        <AppText color="textMuted" testID="new-ticket-help">
          {selected.helpText || selected.description}
        </AppText>
      ) : null}
      <TextField label={t.subject} value={title} onChangeText={setTitle} maxLength={TicketLimits.titleMax} error={problemText('title')} testID="new-ticket-title" />
      <TextField label={t.body} value={body} onChangeText={setBody} multiline maxLength={TicketLimits.bodyMax} error={problemText('body')} testID="new-ticket-body" />
      <UploadTray uploads={uploads} testID="new-ticket-uploads" />
      <AttachButtons permissions={{ media: true, video: false }} remaining={TicketLimits.attachments - uploads.items.length} max={TicketLimits.attachments} videoAllowed={false} onPicked={uploads.add} testID="new-ticket-attach" />
      <CheckRow label={t.diagnostics} value={diagnostics} onValueChange={setDiagnostics} testID="new-ticket-diagnostics">
        <AppText variant="caption" color="textMuted">
          {t.diagnosticsHint}
        </AppText>
      </CheckRow>
      <Notice tone="error" testID="new-ticket-error">
        {composerError(error, uploads, strings.composer.waitForUploads)}
      </Notice>
      <Button label={t.submit} onPress={submit} disabled={busy} testID="new-ticket-submit" />
    </Screen>
  );
}
