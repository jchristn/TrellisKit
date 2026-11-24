import { useEffect, useMemo, useRef, useState } from 'react';
import {
  buildProfilePatch, builtInField, editorFields, isDisplayName, isHandle, onboardingFields, profileFieldsOf, staffSetFields, toFormValues,
  validateProfileValues,
  numberedHandle, suggestHandle
} from '@trelliskit/shared';
import strings from '../strings.js';
import { useAuth } from '../context/AuthContext.jsx';
import { useProperty } from '../context/PropertyContext.jsx';
import useDebounce from '../hooks/useDebounce.js';
import { getFieldErrors } from '../utils/api.js';
import { authErrorMessage } from '../utils/authErrors.js';
import { profileErrorsText, serverFieldErrors } from '../utils/profileErrors.js';
import AvatarEditor from './AvatarEditor.jsx';
import Icon from './Icon.jsx';
import { ErrorBanner } from './States.jsx';
import { ProfileFieldsEditor } from './profile/ProfileFieldInputs.jsx';
import { ProfileFieldList } from './profile/ProfileFieldDisplay.jsx';

const p = strings.profileForm;
const DEFAULT_BIO_MAX = 1000;

/** The label of a built-in field from its definition, else the engine default. */
export function builtInLabel(fields, key, fallback) {
  const field = builtInField(fields, key);
  return (field && String(field.label || '').trim()) || fallback;
}

/**
 * Which surfaces of the form are shown for a mode (section 9.11): onboarding asks the built-ins
 * collected at sign-up and every `collectAt: SignUp` field; the editor shows every built-in the
 * property enables plus every member-editable field, and staff-set fields read-only.
 * @param {object[]} fields definitions (`profileFieldsOf`)
 * @param {'onboarding'|'editor'} mode
 */
export function profileFormLayout(fields, mode) {
  const avatar = builtInField(fields, 'avatar');
  const bio = builtInField(fields, 'bio');
  const onboarding = mode === 'onboarding';
  return {
    avatar: Boolean(avatar) && (!onboarding || avatar.collectAt === 'SignUp'),
    bio: Boolean(bio) && (!onboarding || bio.collectAt === 'SignUp') ? bio : null,
    editable: onboarding ? onboardingFields(fields) : editorFields(fields),
    staffSet: onboarding ? [] : staffSetFields(fields)
  };
}

/**
 * Validate the form: built-ins (display name, handle, bio) and the configurable fields.
 * `skipOptional` drops the configurable fields that are not required (the onboarding "Skip").
 * Returns an error map of text keyed like the server's `fieldErrors`.
 */
export function validateProfileForm({ base, values, layout, fields, handleStatus, today = new Date() }) {
  const errors = {};
  if (!isDisplayName(base.displayName)) errors.displayName = strings.auth.errors.displayNameInvalid;
  if (!isHandle(base.handle.trim())) errors.handle = p.handleInvalid;
  else if (handleStatus === 'taken') errors.handle = p.handleTaken;
  const bioMax = (layout.bio && layout.bio.validation && layout.bio.validation.maxLength) || DEFAULT_BIO_MAX;
  if (layout.bio && base.bio.length > bioMax) errors.bio = p.tooLong(bioMax);
  return { ...errors, ...profileErrorsText(validateProfileValues(layout.editable, values, { today, fields })) };
}

/** The `PUT /me/profile` body: built-ins plus the `profile` map of changed fields. */
export function buildProfileBody({ base, values, initial, layout, fields, only = null }) {
  const body = { displayName: base.displayName.trim(), handle: base.handle.trim() };
  if (layout.bio) body.bio = base.bio.trim() ? base.bio.trim() : null;
  const editable = only ? layout.editable.filter((f) => only.includes(f.key)) : layout.editable;
  const patch = buildProfilePatch(editable, values, initial, fields);
  if (Object.keys(patch).length) body.profile = patch;
  return body;
}

export { suggestHandle };

function useHandleStatus(handle, currentHandle) {
  const { api } = useAuth();
  const debounced = useDebounce(handle.trim(), 400);
  const [result, setResult] = useState({ handle: '', status: 'idle' });

  const isCurrent = Boolean(currentHandle) && debounced.toLowerCase() === currentHandle.toLowerCase();
  const shouldCheck = Boolean(debounced) && isHandle(debounced) && !isCurrent;

  useEffect(() => {
    if (!shouldCheck) return undefined;
    const controller = new AbortController();
    api.checkHandle(debounced, controller.signal).then(
      (res) => setResult({ handle: debounced, status: res && res.available ? 'available' : 'taken' }),
      (err) => { if (!controller.signal.aborted) setResult({ handle: debounced, status: err && err.status === 400 ? 'invalid' : 'idle' }); }
    );
    return () => controller.abort();
  }, [api, debounced, shouldCheck]);

  const typed = handle.trim();
  if (!typed) return 'idle';
  if (!isHandle(typed)) return 'invalid';
  if (currentHandle && typed.toLowerCase() === currentHandle.toLowerCase()) return 'current';
  if (typed !== debounced || result.handle !== typed) return 'checking';
  return result.status;
}

/**
 * Profile editor shared by onboarding and Account settings, rendered entirely from the property's
 * profile field definitions (`GET /api/v1/property` `profileFields`): photo, display name, handle
 * with a live availability check, bio, then every configurable field in profile order. In
 * onboarding mode only sign-up fields are asked, and optional ones can be skipped.
 *
 * @param {{ me: object, onSaved: (me: object) => void, onMeChange?: (me: object, message?: string) => void, submitLabel?: string, mode?: 'onboarding'|'editor', skipLabel?: string }} props
 */
function ProfileForm({ me, onSaved, onMeChange, submitLabel = p.save, mode = 'editor', skipLabel = null }) {
  const { api } = useAuth();
  const { property } = useProperty();
  const fields = useMemo(() => profileFieldsOf(property), [property]);
  const layout = useMemo(() => profileFormLayout(fields, mode), [fields, mode]);
  const profile = (me && me.profile) || {};
  const [initial] = useState(() => toFormValues(fields, profile));
  const [values, setValues] = useState(initial);
  const [base, setBase] = useState(() => ({ displayName: (me && me.displayName) || '', handle: (me && me.handle) || suggestHandle(me && me.displayName, me && me.email), bio: (me && me.bio) || '' }));
  // Until the member edits the handle, it follows the display name; a taken suggestion gets a short number.
  const [handleTouched, setHandleTouched] = useState(Boolean(me && me.handle));
  const [errors, setErrors] = useState({});
  const [formError, setFormError] = useState('');
  const [busy, setBusy] = useState(false);
  const handleStatus = useHandleStatus(base.handle, me && me.handle);
  const errorRef = useRef(null);
  const bioMax = (layout.bio && layout.bio.validation && layout.bio.validation.maxLength) || DEFAULT_BIO_MAX;
  const optional = layout.editable.filter((f) => !(f.required || (f.validation && f.validation.required)));

  const setBaseValue = (key) => (e) => {
    const value = e.target.value;
    if (key === 'handle') setHandleTouched(true);
    setBase((v) => {
      const next = { ...v, [key]: value };
      if (key === 'displayName' && !handleTouched) next.handle = suggestHandle(value, me && me.email) || v.handle;
      return next;
    });
  };
  useEffect(() => {
    if (handleTouched || handleStatus !== 'taken' || !base.handle) return;
    setBase((v) => ({ ...v, handle: numberedHandle(v.handle) }));
  }, [handleStatus, handleTouched, base.handle]);
  const setField = (key, next) => setValues((v) => ({ ...v, [key]: next }));

  const save = async (skip) => {
    const skipped = skip ? new Set(optional.map((f) => f.key)) : null;
    const checkLayout = skipped ? { ...layout, editable: layout.editable.filter((f) => !skipped.has(f.key)) } : layout;
    const found = validateProfileForm({ base, values, layout: checkLayout, fields, handleStatus });
    setErrors(found);
    setFormError(Object.keys(found).length ? p.fixBelow : '');
    if (Object.keys(found).length) {
      if (errorRef.current) errorRef.current.focus();
      return;
    }
    setBusy(true);
    try {
      const body = buildProfileBody({ base, values, initial, layout, fields, only: skipped ? checkLayout.editable.map((f) => f.key) : null });
      const saved = await api.updateProfile(body);
      onSaved(saved);
    } catch (err) {
      const mapped = serverFieldErrors(getFieldErrors(err));
      setErrors(mapped);
      setFormError(Object.keys(mapped).length ? p.fixBelow : authErrorMessage(err));
      if (errorRef.current) errorRef.current.focus();
    } finally {
      setBusy(false);
    }
  };

  const submit = (e) => {
    e.preventDefault();
    save(false);
  };

  const err = (key) => errors[key] && <span className="field-error" id={'profile-' + key + '-error'}>{errors[key]}</span>;
  const handleHint = {
    checking: <span className="muted small" role="status">{p.handleChecking}</span>,
    available: <span className="success-text small" role="status"><Icon name="check" size={12} /> {p.handleAvailable}</span>,
    taken: <span className="field-error" role="status">{p.handleTaken}</span>,
    current: <span className="muted small">{p.handleCurrent}</span>
  }[handleStatus];
  const displayNameField = builtInField(fields, 'displayName');
  const handleField = builtInField(fields, 'handle');
  const staffProfile = layout.staffSet.length ? profile : null;

  return (
    <form className="profile-form" onSubmit={submit} noValidate>
      <div ref={errorRef} tabIndex={-1}><ErrorBanner message={formError} /></div>
      {me && layout.avatar && <AvatarEditor me={me} onChange={(next, message) => onMeChange && onMeChange(next, message)} />}

      <div className="form-grid">
        <div className="field">
          <label className="label-text" htmlFor="profile-displayName">{builtInLabel(fields, 'displayName', p.displayName)}<span className="required-mark" aria-label={strings.profileFields.required}> *</span></label>
          <input id="profile-displayName" type="text" maxLength={60} placeholder={(displayNameField && displayNameField.placeholder) || undefined} value={base.displayName} onChange={setBaseValue('displayName')} disabled={busy} aria-invalid={Boolean(errors.displayName)} />
          {displayNameField && displayNameField.helpText && <span className="muted small">{displayNameField.helpText}</span>}
          {err('displayName')}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="profile-handle">{builtInLabel(fields, 'handle', p.handle)}<span className="required-mark" aria-label={strings.profileFields.required}> *</span></label>
          <div className="input-prefix">
            <span aria-hidden="true">@</span>
            <input id="profile-handle" type="text" maxLength={30} autoComplete="off" spellCheck="false" placeholder={(handleField && handleField.placeholder) || undefined} value={base.handle} onChange={setBaseValue('handle')} disabled={busy} aria-invalid={Boolean(errors.handle) || handleStatus === 'taken'} aria-describedby="profile-handle-hint" />
          </div>
          <span id="profile-handle-hint" className="muted small">{(handleField && handleField.helpText) || p.handleHint}</span>
          {errors.handle ? err('handle') : handleHint}
        </div>
      </div>

      {layout.bio && (
        <div className="field">
          <label className="label-text" htmlFor="profile-bio">{builtInLabel(fields, 'bio', p.bio)}</label>
          <textarea id="profile-bio" rows={4} placeholder={layout.bio.placeholder || undefined} value={base.bio} onChange={setBaseValue('bio')} disabled={busy} aria-invalid={Boolean(errors.bio)} aria-describedby="profile-bio-count" />
          {layout.bio.helpText && <span className="muted small">{layout.bio.helpText}</span>}
          <span id="profile-bio-count" className={'small ' + (base.bio.length > bioMax ? 'field-error' : 'muted')}>{p.bioCount(base.bio.length, bioMax)}</span>
          {err('bio')}
        </div>
      )}

      {layout.editable.length > 0 && (
        <ProfileFieldsEditor
          fields={layout.editable} allFields={fields} values={values} onChange={setField} errors={errors} disabled={busy}
          idPrefix="profile" markOptional={mode === 'onboarding'}
        />
      )}

      {staffProfile && (
        <section className="fieldset staff-set-fields" aria-labelledby="profile-staff-set">
          <h2 className="label-text" id="profile-staff-set">{p.staffSetTitle}</h2>
          <p className="muted small">{p.staffSetHint}</p>
          <ProfileFieldList fields={layout.staffSet} profile={staffProfile} allFields={fields} showEmpty={layout.staffSet} />
        </section>
      )}

      <div className="form-actions">
        {mode === 'onboarding' && skipLabel && optional.length > 0 && (
          <button type="button" className="button button-secondary" onClick={() => save(true)} disabled={busy}>{skipLabel}</button>
        )}
        <button type="submit" className="button button-primary" disabled={busy}>{busy ? p.saving : submitLabel}</button>
      </div>
    </form>
  );
}

export default ProfileForm;
