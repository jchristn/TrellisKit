import { buildProfilePatch, editorFields, hasProfileValue, isFieldRequired, numberedHandle, onboardingFields, staffSetFields, suggestHandle, toFormValues, validateProfileValues } from '@trelliskit/shared';
import { useEffect, useMemo, useRef, useState } from 'react';
import { View } from 'react-native';
import { account } from '../../api/endpoints';
import { describeError, fieldErrorsOf } from '../../auth/errors';
import { metadataChanged, metadataOf, metadataPayload } from '../../content/metadata';
import { isStaffUser } from '../../content/permissions';
import { useHandleAvailability } from '../../hooks/useHandleAvailability';
import { pickAvatarImage, prepareAvatarUpload } from '../../media/avatar';
import { bioMax, builtInLabel, builtInsOf, displayNameMax, profileErrorTexts, useProfileFields, validateBuiltIns } from '../../profile/fields';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Avatar from '../Avatar';
import Button from '../Button';
import Card from '../Card';
import MetadataEditor from '../filters/MetadataEditor';
import Notice from '../Notice';
import TextField from '../TextField';
import { ProfileValue } from './ProfileDisplay';
import ProfileFieldInput, { FieldShell } from './ProfileFieldInput';

const orNull = (text) => {
  const value = (text || '').trim();
  return value ? value : null;
};

/**
 * Which surfaces of the form are shown for a mode (docs/MULTIPROPERTY_CONTRACT.md section 9.11).
 * Onboarding: the built-ins with `collectAt: SignUp`, then the `SignUp` fields in profile order.
 * Editor: every built-in, then the fields the member edits, then staff-set fields read-only.
 * @param {object[]} fields
 * @param {'onboarding'|'editor'} mode
 */
export function formLayout(fields, mode) {
  const built = builtInsOf(fields);
  const onboarding = mode === 'onboarding';
  const collected = (f) => Boolean(f) && (!onboarding || f.collectAt === 'SignUp');
  return {
    built,
    showAvatar: collected(built.avatar),
    showBio: collected(built.bio),
    editable: onboarding ? onboardingFields(fields) : editorFields(fields),
    staffSet: onboarding ? [] : staffSetFields(fields)
  };
}

/**
 * Build the PUT /me/profile body: built-ins, plus `profile` with only the fields that changed
 * (never the deprecated top-level fields). Labels and tags are sent only when the member changed
 * them (replace semantics).
 * @param {object} values form values `{ displayName, handle, bio, profile, metadata, metadataInitial }`
 * @param {object[]} editable fields this form may write
 * @param {Record<string, any>} initial form values of `profile` when the form opened
 * @param {object[]} fields every definition
 * @param {{ staff?: boolean, withBio?: boolean }} [options]
 */
export function toProfileBody(values, editable, initial, fields, { staff = false, withBio = true } = {}) {
  const body = { displayName: values.displayName.trim(), handle: values.handle.trim() };
  if (withBio) body.bio = orNull(values.bio);
  const patch = buildProfilePatch(editable, values.profile || {}, initial, fields);
  if (Object.keys(patch).length) body.profile = patch;
  if (values.metadata && values.metadataInitial && metadataChanged(values.metadata, values.metadataInitial)) {
    const meta = metadataPayload(values.metadata, { staff });
    body.labels = meta.labels;
    body.tags = meta.tags;
  }
  return body;
}

/**
 * Profile form for onboarding (`mode="onboarding"`) and Account > Edit profile (`mode="editor"`),
 * rendered entirely from the property's `profileFields`. Required fields are validated before
 * saving; in onboarding optional fields can be skipped. Server `fieldErrors` show inline on the
 * field they name.
 * @param {object} props
 * @param {object} props.api MobileApiClient.
 * @param {object} props.user Current Me.
 * @param {(me: object) => void} props.onUserChange Called with Me after an avatar change.
 * @param {(me: object) => void} props.onSaved Called with Me after a successful save.
 * @param {string} props.submitLabel
 * @param {'onboarding'|'editor'} [props.mode]
 * @param {(values: object) => void} [props.onValuesChange] Called with the form draft after every change (profile setup's live "Still needed" note).
 */
export default function ProfileForm({ api, user, onUserChange, onSaved, submitLabel, mode = 'editor', onValuesChange }) {
  const theme = useTheme();
  const fields = useProfileFields();
  const layout = useMemo(() => formLayout(fields, mode), [fields, mode]);
  const { built, editable, staffSet } = layout;
  const t = strings.profileForm;
  const [initialProfile] = useState(() => toFormValues(fields, user && user.profile));
  const [values, setValues] = useState(() => {
    const u = user || {};
    return { displayName: u.displayName || '', handle: u.handle || suggestHandle(u.displayName, u.email), bio: u.bio || '', profile: initialProfile, metadata: metadataOf(u), metadataInitial: metadataOf(u) };
  });
  const [errors, setErrors] = useState({});
  const [message, setMessage] = useState(null);
  const [busy, setBusy] = useState(false);
  const [photoBusy, setPhotoBusy] = useState(false);
  const [originalHandle] = useState(() => (user && user.handle) || null);
  const handleStatus = useHandleAvailability(api, values.handle, originalHandle);
  // Until the member edits the handle, it follows the display name; a taken suggestion gets a short number.
  const [handleTouched, setHandleTouched] = useState(Boolean(user && user.handle));
  const onboarding = mode === 'onboarding';
  const optional = editable.filter((f) => !isFieldRequired(f));

  const valuesListener = useRef(onValuesChange);
  useEffect(() => {
    valuesListener.current = onValuesChange;
  });
  useEffect(() => {
    if (valuesListener.current) valuesListener.current(values);
  }, [values]);

  const set = (key) => (value) =>
    setValues((v) => {
      const next = { ...v, [key]: value };
      if (key === 'displayName' && !handleTouched) next.handle = suggestHandle(value, user && user.email) || v.handle;
      return next;
    });
  // Adjusted while rendering (not in an effect): the new handle is "checking" on the next render, so this runs once per taken suggestion.
  if (!handleTouched && handleStatus === 'taken' && values.handle) setValues((v) => ({ ...v, handle: numberedHandle(v.handle) }));
  const setField = (key) => (value) => setValues((v) => ({ ...v, profile: { ...v.profile, [key]: value } }));
  const avatarUri = user && user.avatarUrl ? api.url(user.avatarUrl) : null;

  async function choosePhoto() {
    setMessage(null);
    try {
      const picked = await pickAvatarImage();
      if (picked === 'denied') {
        setMessage(t.photoPermission);
        return;
      }
      if (!picked) return;
      setPhotoBusy(true);
      const upload = await prepareAvatarUpload(picked);
      const me = await account.uploadAvatar(api, upload.body, upload.contentType);
      onUserChange(me);
    } catch (error) {
      setMessage(describeError(error));
    } finally {
      setPhotoBusy(false);
    }
  }

  async function removePhoto() {
    setMessage(null);
    setPhotoBusy(true);
    try {
      onUserChange(await account.deleteAvatar(api));
    } catch (error) {
      setMessage(describeError(error));
    } finally {
      setPhotoBusy(false);
    }
  }

  async function submit({ skipOptional = false } = {}) {
    setMessage(null);
    const targets = skipOptional ? editable.filter(isFieldRequired) : editable;
    const found = { ...validateBuiltIns(values, layout.showBio ? built : { ...built, bio: null }), ...profileErrorTexts(validateProfileValues(targets, values.profile, { fields })) };
    if (handleStatus === 'taken') found.handle = t.handleTaken;
    setErrors(found);
    if (Object.keys(found).length) return;
    setBusy(true);
    try {
      const body = toProfileBody(onboarding ? { ...values, metadata: null } : values, targets, initialProfile, fields, { staff: isStaffUser(user), withBio: layout.showBio });
      onSaved(await account.updateProfile(api, body));
    } catch (error) {
      const fieldErrors = fieldErrorsOf(error);
      setErrors(fieldErrors);
      setMessage(Object.keys(fieldErrors).length ? null : describeError(error));
    } finally {
      setBusy(false);
    }
  }

  const handleStatusText = { checking: t.handleChecking, available: t.handleAvailable, taken: t.handleTaken, invalid: t.handleInvalid, error: t.handleError }[handleStatus];
  const handleStatusColor = handleStatus === 'available' ? 'success' : handleStatus === 'checking' || handleStatus === 'error' ? 'textMuted' : 'danger';
  const nameMax = displayNameMax(built.displayName);
  const bioLimit = bioMax(built.bio);
  const profile = (user && user.profile) || {};

  return (
    <View style={{ gap: theme.spacing.lg }} testID="profile-form">
      {layout.showAvatar ? (
        <FieldShell label={builtInLabel(built.avatar, t.photo)} helpText={built.avatar.helpText} testID="profile-avatar-field">
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.lg }}>
            <Avatar uri={avatarUri} token={api.accessToken} name={values.displayName || (user && user.displayName)} testID="profile-avatar" />
            <View style={{ flex: 1, gap: theme.spacing.sm }}>
              <Button label={avatarUri ? t.changePhoto : t.choosePhoto} variant="secondary" onPress={choosePhoto} disabled={photoBusy} testID="choose-photo" />
              {avatarUri ? <Button label={t.removePhoto} variant="ghost" onPress={removePhoto} disabled={photoBusy} testID="remove-photo" /> : null}
              {photoBusy ? (
                <AppText variant="caption" color="textMuted">
                  {t.uploading}
                </AppText>
              ) : null}
            </View>
          </View>
        </FieldShell>
      ) : null}

      <FieldShell label={builtInLabel(built.displayName, t.displayName)} required helpText={built.displayName.helpText} testID="profile-display-name-field">
        <TextField label={builtInLabel(built.displayName, t.displayName)} hideLabel value={values.displayName} onChangeText={set('displayName')} placeholder={built.displayName.placeholder || undefined} error={errors.displayName} maxLength={nameMax} testID="profile-display-name" />
      </FieldShell>
      <FieldShell label={builtInLabel(built.handle, t.handle)} required helpText={built.handle.helpText} testID="profile-handle-field">
        <TextField
          label={builtInLabel(built.handle, t.handle)}
          hideLabel
          value={values.handle}
          onChangeText={(text) => {
            setHandleTouched(true);
            set('handle')(text.replace(/\s/g, ''));
          }}
          placeholder={built.handle.placeholder || undefined}
          hint={t.handleHint}
          status={errors.handle ? null : handleStatusText}
          statusColor={handleStatusColor}
          error={errors.handle}
          autoCapitalize="none"
          autoCorrect={false}
          maxLength={30}
          testID="profile-handle"
        />
      </FieldShell>
      {layout.showBio ? (
        <FieldShell label={builtInLabel(built.bio, t.bio)} helpText={built.bio.helpText} testID="profile-bio-field">
          <TextField label={builtInLabel(built.bio, t.bio)} hideLabel value={values.bio} onChangeText={set('bio')} multiline maxLength={bioLimit} placeholder={built.bio.placeholder || undefined} hint={t.bioCount(values.bio.length, bioLimit)} error={errors.bio} testID="profile-bio" />
        </FieldShell>
      ) : null}

      {editable.map((field) => (
        <ProfileFieldInput key={field.key} field={field} value={values.profile[field.key]} onChange={setField(field.key)} errors={errors} fields={fields} api={api} />
      ))}

      {staffSet.filter((f) => Object.prototype.hasOwnProperty.call(profile, f.key)).length ? (
        <Card testID="profile-staff-set">
          <AppText variant="heading" accessibilityRole="header">
            {strings.profileFields.staffSection}
          </AppText>
          {staffSet
            .filter((f) => Object.prototype.hasOwnProperty.call(profile, f.key))
            .map((field) => (
              <View key={field.key} style={{ gap: theme.spacing.xxs }} testID={'profile-staff-' + field.key}>
                <View style={{ flexDirection: 'row', alignItems: 'baseline', flexWrap: 'wrap', gap: theme.spacing.xs }}>
                  <AppText variant="label">{field.label}</AppText>
                  <AppText variant="caption" color="textMuted">
                    {strings.profileFields.setByStaff}
                  </AppText>
                </View>
                {hasProfileValue(field, profile[field.key]) ? (
                  <ProfileValue field={field} value={profile[field.key]} fields={fields} />
                ) : (
                  <AppText color="textMuted">{strings.profileFields.notSet}</AppText>
                )}
              </View>
            ))}
        </Card>
      ) : null}

      {onboarding ? null : <MetadataEditor value={values.metadata} onChange={(metadata) => setValues((v) => ({ ...v, metadata }))} targetType="User" api={api} user={user} testID="profile-metadata" />}

      <Notice tone="error" testID="profile-form-message">
        {message}
      </Notice>
      <Button label={submitLabel} onPress={() => submit()} disabled={busy} testID="profile-submit" />
      {onboarding && optional.length ? <Button label={strings.profileSetup.skip} accessibilityHint={strings.profileSetup.skipHint} variant="ghost" onPress={() => submit({ skipOptional: true })} disabled={busy} testID="profile-skip" /> : null}
    </View>
  );
}
