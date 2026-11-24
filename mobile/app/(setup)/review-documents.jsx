import { isUpdatedDocument } from '@trelliskit/shared';
import { Redirect, useRouter } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import CheckRow from '../../src/components/CheckRow';
import Card from '../../src/components/Card';
import LinkButton from '../../src/components/LinkButton';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import { documents as documentsApi } from '../../src/api/endpoints';
import { useAuth } from '../../src/auth/AuthProvider';
import { describeError, fieldErrorsOf } from '../../src/auth/errors';
import { nextRoute } from '../../src/auth/routing';
import { documentPath, pendingDocumentsOf } from '../../src/documents/documents';
import { validateDocumentAcceptance } from '../../src/forms';
import { useProperty } from '../../src/property';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

/**
 * Documents step ("Review and accept"): shown after age attestation while `Me.pendingDocuments`
 * is not empty (a social sign-up that has not accepted the required documents, or a document
 * that changed and must be accepted again). Each document has a Read link and its own checkbox;
 * Continue records the acceptances (POST /me/documents/accept), stores the returned Me, and
 * resumes the setup steps (the screen redirects once nothing is pending).
 */
export default function ReviewDocumentsScreen() {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { api, session, user, updateUser, refreshUser, signOut } = useAuth();
  const t = strings.documents;
  const [checked, setChecked] = useState({});
  const [errors, setErrors] = useState({});
  const [message, setMessage] = useState(null);
  const [busy, setBusy] = useState(false);
  const pending = pendingDocumentsOf(user, property);

  if (!pending.length) return <Redirect href={nextRoute(session)} />;
  // "Updated" wording only when a newer version replaces one the member accepted (never for a first acceptance).
  const changed = pending.some(isUpdatedDocument);

  async function submit() {
    setMessage(null);
    const found = validateDocumentAcceptance(pending, checked);
    setErrors(found);
    if (Object.keys(found).length) return;
    setBusy(true);
    try {
      const me = await documentsApi.accept(api, { documents: pending.map((d) => ({ key: d.key, version: d.version })) });
      // The stored Me has no pending documents, so this screen redirects to the next step.
      if (me && me.id) updateUser(me);
      else await refreshUser();
      setBusy(false);
    } catch (e) {
      setBusy(false);
      const fields = fieldErrorsOf(e);
      if (Object.keys(fields).length) {
        // A stale version: reload Me for the current versions and ask again for those documents.
        setErrors(fields);
        setChecked((c) => {
          const out = { ...c };
          for (const field of Object.keys(fields)) delete out[field.replace(/^documents\./, '')];
          return out;
        });
        setMessage({ tone: 'info', text: t.refreshed });
        refreshUser().catch(() => {});
        return;
      }
      setMessage({ tone: 'error', text: describeError(e) });
    }
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-review-documents">
      <View style={{ gap: theme.spacing.sm }}>
        <AppText variant="title" accessibilityRole="header">
          {t.reviewHeading}
        </AppText>
        <AppText color="textMuted">{t.reviewBody}</AppText>
        {changed ? <AppText color="textMuted">{t.reviewChanged}</AppText> : null}
      </View>
      {pending.map((doc) => {
        const title = doc.title || doc.key;
        return (
          <Card key={doc.key} testID={'review-document-' + doc.key}>
            <AppText variant="heading">{title}</AppText>
            {doc.version ? <AppText color="textMuted">{t.version(doc.version)}</AppText> : null}
            <LinkButton label={t.read} role="link" accessibilityHint={t.readHint(title)} onPress={() => router.push(documentPath(doc.key))} testID={'review-read-' + doc.key} />
            <CheckRow
              label={t.accept(title)}
              accessibilityLabel={t.acceptLabel(title, doc.version)}
              value={Boolean(checked[doc.key])}
              onValueChange={(value) => setChecked((c) => ({ ...c, [doc.key]: value }))}
              error={errors['documents.' + doc.key]}
              testID={'review-accept-' + doc.key}
            />
          </Card>
        );
      })}
      <Notice tone={message ? message.tone : 'error'} testID="review-documents-message">
        {message ? message.text : null}
      </Notice>
      <Button label={busy ? t.saving : t.continue} accessibilityHint={t.continueHint} onPress={submit} disabled={busy} testID="review-documents-continue" />
      <Button label={t.signOut} variant="ghost" onPress={() => signOut()} testID="review-documents-sign-out" />
    </Screen>
  );
}
