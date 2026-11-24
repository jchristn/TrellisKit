import { useEffect, useRef, useState } from 'react';
import strings from '../strings.js';
import { useAuth } from '../context/AuthContext.jsx';
import { authErrorMessage } from '../utils/authErrors.js';
import Avatar from './Avatar.jsx';

const p = strings.profileForm;

export const AVATAR_TYPES = Object.freeze(['image/jpeg', 'image/png', 'image/webp']);
export const AVATAR_MAX_BYTES = 5 * 1024 * 1024;

/** Client-side avatar checks. Returns an error message or ''. */
export function checkAvatarFile(file) {
  if (!file) return '';
  if (!AVATAR_TYPES.includes(file.type)) return p.photoType;
  if (file.size > AVATAR_MAX_BYTES) return p.photoSize;
  return '';
}

/**
 * Profile photo picker: validates type and size, previews immediately, and uploads with
 * `PUT /me/avatar` (raw body). The server returns `Me`, passed to `onChange`.
 */
function AvatarEditor({ me, onChange }) {
  const { api } = useAuth();
  const inputRef = useRef(null);
  const [preview, setPreview] = useState(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => () => { if (preview) URL.revokeObjectURL(preview); }, [preview]);

  const choose = async (e) => {
    const file = e.target.files && e.target.files[0];
    e.target.value = '';
    if (!file) return;
    const problem = checkAvatarFile(file);
    setError(problem);
    if (problem) return;
    setPreview(URL.createObjectURL(file));
    setBusy(true);
    try {
      const next = await api.uploadAvatar(file);
      onChange(next, p.photoUploaded);
      setPreview(null);
    } catch (err) {
      setPreview(null);
      setError(authErrorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    setBusy(true);
    setError('');
    try {
      onChange(await api.deleteAvatar(), p.photoRemoved);
    } catch (err) {
      setError(authErrorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  const name = (me && me.displayName) || '';
  const src = preview || (me && me.avatarUrl) || null;
  return (
    <div className="avatar-editor">
      <Avatar src={src} name={name} size={88} alt={p.photoAlt(name || '?')} />
      <div className="avatar-editor-body">
        <span className="label-text">{p.photo}</span>
        <div className="row-actions">
          <button type="button" className="button button-secondary button-sm" onClick={() => inputRef.current && inputRef.current.click()} disabled={busy}>
            {busy ? p.uploading : (me && me.avatarUrl ? p.changePhoto : p.choosePhoto)}
          </button>
          {me && me.avatarUrl && !busy && <button type="button" className="button button-secondary button-sm" onClick={remove}>{p.removePhoto}</button>}
        </div>
        <input ref={inputRef} type="file" accept={AVATAR_TYPES.join(',')} className="visually-hidden" onChange={choose} aria-label={p.choosePhoto} tabIndex={-1} />
        <span className="muted small">{p.photoHint}</span>
        {error && <span className="field-error" role="alert">{error}</span>}
      </div>
    </div>
  );
}

export default AvatarEditor;
