import { useRouter } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import CheckRow from '../../src/components/CheckRow';
import DateField from '../../src/components/DateField';
import LinkButton from '../../src/components/LinkButton';
import Notice from '../../src/components/Notice';
import Screen from '../../src/components/Screen';
import TextField from '../../src/components/TextField';
import DocumentLinks from '../../src/components/DocumentLinks';
import { auth as authApi, documents as documentsApi } from '../../src/api/endpoints';
import { useAuth } from '../../src/auth/AuthProvider';
import { describeError, fieldErrorsOf } from '../../src/auth/errors';
import { acceptedDocumentsBody, documentPath, joinTitles, separateDocuments, termsDocuments } from '../../src/documents/documents';
import { validateRegistration } from '../../src/forms';
import { openInAppBrowser, PrivacyUrl, TermsUrl } from '../../src/links';
import { useProperty } from '../../src/property';
import { offerToSavePassword } from '../../src/native/sharedCredentials';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

/**
 * Email registration with the property minimum age rule and document acceptance. The terms
 * checkbox covers the property's terms documents (linked under it); each `RequiredAtSignup`
 * document with `separateCheckbox` has its own required checkbox and Read link, sent as
 * `acceptedDocuments` with the version the app shows (live property, else bundled). A server
 * field error for a document (missing or changed) shows on its checkbox; a changed version is
 * reloaded and the box cleared so the member reviews it again. Without configured documents the
 * terms checkbox links the website's Terms and Privacy pages.
 */
export default function RegisterScreen() {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { api, completeSignIn } = useAuth();
  const t = strings.register;
  const covered = termsDocuments(property);
  const separate = separateDocuments(property);
  const coveredTitles = joinTitles(covered.map((d) => d.title || d.key));
  const [values, setValues] = useState({ displayName: '', email: '', password: '', dob: { month: '', day: '', year: '' }, acceptTerms: false, acceptedDocuments: {} });
  const [versions, setVersions] = useState({});
  const [errors, setErrors] = useState({});
  const [message, setMessage] = useState(null);
  const [busy, setBusy] = useState(false);
  const set = (key) => (value) => setValues((v) => ({ ...v, [key]: value }));
  const setDocument = (key) => (value) => setValues((v) => ({ ...v, acceptedDocuments: { ...v.acceptedDocuments, [key]: value } }));

  /** After a document field error, load the current version and clear its box for another look. */
  async function reviewAgain(keys) {
    setValues((v) => {
      const next = { ...v.acceptedDocuments };
      for (const key of keys) delete next[key];
      return { ...v, acceptedDocuments: next };
    });
    for (const key of keys) {
      try {
        const doc = await documentsApi.get(api, key);
        if (doc && doc.version) setVersions((current) => ({ ...current, [key]: doc.version }));
      } catch {
        // Keep the version the app has; the server message explains what to do.
      }
    }
  }

  async function submit() {
    setMessage(null);
    const { errors: found, dateOfBirth } = validateRegistration(values, new Date(), property);
    setErrors(found);
    if (Object.keys(found).length) return;
    setBusy(true);
    try {
      const result = await authApi.register(api, {
        email: values.email.trim(),
        password: values.password,
        displayName: values.displayName.trim(),
        dateOfBirth,
        acceptTerms: true,
        acceptedDocuments: acceptedDocumentsBody(separate, values.acceptedDocuments, versions)
      });
      offerToSavePassword(property, values.email.trim(), values.password);
      await completeSignIn(result);
    } catch (e) {
      const fields = fieldErrorsOf(e);
      setErrors(fields);
      const documentKeys = Object.keys(fields)
        .filter((f) => f.startsWith('acceptedDocuments.'))
        .map((f) => f.slice('acceptedDocuments.'.length))
        .filter((key) => separate.some((d) => d.key === key));
      if (documentKeys.length) reviewAgain(documentKeys);
      // A document the app does not show yet (an older copy of the property) has no box to carry it.
      const unshown = Object.keys(fields)
        .filter((f) => f.startsWith('acceptedDocuments.') && !documentKeys.includes(f.slice('acceptedDocuments.'.length)))
        .map((f) => fields[f]);
      setMessage(unshown.length ? unshown.join(' ') : Object.keys(fields).length ? null : describeError(e));
      setBusy(false);
    }
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }}>
      <AppText variant="title" accessibilityRole="header">
        {t.heading}
      </AppText>
      <TextField label={t.displayName} hint={t.displayNameHint} value={values.displayName} onChangeText={set('displayName')} error={errors.displayName} maxLength={60} autoComplete="name" testID="register-name" />
      <TextField label={t.email} value={values.email} onChangeText={set('email')} error={errors.email} autoCapitalize="none" autoCorrect={false} keyboardType="email-address" autoComplete="email" testID="register-email" />
      <TextField label={t.password} hint={t.passwordHint} value={values.password} onChangeText={set('password')} error={errors.password} secureTextEntry autoComplete="new-password" textContentType="newPassword" maxLength={128} testID="register-password" />
      <DateField label={t.dateOfBirth} hint={t.dateOfBirthHint} value={values.dob} onChange={set('dob')} error={errors.dateOfBirth} testID="register-dob" />
      <CheckRow
        label={covered.length ? t.acceptDocuments(coveredTitles) : t.acceptTerms}
        accessibilityLabel={covered.length ? t.acceptDocumentsLabel(coveredTitles) : t.acceptTermsLabel}
        value={values.acceptTerms}
        onValueChange={set('acceptTerms')}
        error={errors.acceptTerms}
        testID="register-terms"
      >
        {covered.length ? (
          <DocumentLinks documents={covered} testID="register-terms-link" />
        ) : (
          <View style={{ flexDirection: 'row', gap: theme.spacing.lg, flexWrap: 'wrap' }}>
            <LinkButton label={strings.paywall.terms} role="link" onPress={() => openInAppBrowser(TermsUrl)} />
            <LinkButton label={strings.paywall.privacy} role="link" onPress={() => openInAppBrowser(PrivacyUrl)} />
          </View>
        )}
      </CheckRow>
      {separate.map((doc) => {
        const title = doc.title || doc.key;
        return (
          <CheckRow
            key={doc.key}
            label={t.acceptDocument(title)}
            accessibilityLabel={t.acceptDocumentLabel(title)}
            value={Boolean(values.acceptedDocuments[doc.key])}
            onValueChange={setDocument(doc.key)}
            error={errors['acceptedDocuments.' + doc.key]}
            testID={'register-document-' + doc.key}
          >
            <LinkButton label={t.readDocument(title)} role="link" accessibilityHint={t.readDocumentHint} onPress={() => router.push(documentPath(doc.key))} testID={'register-read-' + doc.key} />
          </CheckRow>
        );
      })}
      <Notice tone="error" testID="register-error">
        {message}
      </Notice>
      <Button label={t.submit} onPress={submit} disabled={busy} testID="register-submit" />
    </Screen>
  );
}
