import { useRouter } from 'expo-router';
import { useState } from 'react';
import { useAuth } from '../../src/auth/AuthProvider';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import TextField from '../../src/components/TextField';
import { describeIdeaError } from '../../src/components/ideas/IdeaDetail';
import { emitContentEvent } from '../../src/content/events';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

const t = strings.ideas;

/** A new idea (`POST /ideas`, only when `ideas.memberSubmissions` is `Direct`). The author votes for it automatically. */
export default function NewIdeaScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { api } = useAuth();
  const [title, setTitle] = useState('');
  const [body, setBody] = useState('');
  const [error, setError] = useState(null);
  const [titleError, setTitleError] = useState(null);
  const [busy, setBusy] = useState(false);

  async function submit() {
    setError(null);
    setTitleError(null);
    if (title.trim().length < 3) return setTitleError(t.titleTooShort);
    setBusy(true);
    try {
      const idea = await api.createIdea({ title: title.trim(), body: body.trim() });
      emitContentEvent('idea', idea);
      router.replace('/ideas/' + idea.id);
    } catch (e) {
      setError(describeIdeaError(e));
    } finally {
      setBusy(false);
    }
    return undefined;
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-new-idea">
      <AppText color="textMuted">{t.summary}</AppText>
      <TextField label={t.titleField} value={title} onChangeText={setTitle} maxLength={140} error={titleError} testID="new-idea-title" />
      <TextField label={t.bodyField} value={body} onChangeText={setBody} multiline maxLength={10000} testID="new-idea-body" />
      <Notice tone="error" testID="new-idea-error">
        {error}
      </Notice>
      <Button label={t.submit} onPress={submit} disabled={busy} testID="new-idea-submit" />
    </Screen>
  );
}
