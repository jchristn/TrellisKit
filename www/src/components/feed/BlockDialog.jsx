import { useState } from 'react';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import Modal from '../Modal.jsx';
import { ErrorBanner } from '../States.jsx';
import { authorName, errorText } from '../../utils/engagement.js';

const s = strings.safety;

/**
 * Block or unblock a member (contract section 9.2). Staff cannot be blocked: the dialog explains
 * why instead of offering the action (the server also answers `400 CannotBlockStaff`).
 * @param {{ user: object, unblock?: boolean, onClose: () => void, onDone?: (user: object, blocked: boolean) => void }} props
 */
function BlockDialog({ user, unblock = false, onClose, onDone }) {
  const { client } = useAuth();
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [staff, setStaff] = useState(Boolean(user && user.isStaff) && !unblock);
  const name = authorName(user);

  if (staff) {
    return (
      <Modal open size="sm" onClose={onClose} title={s.staffTitle} footer={<button type="button" className="button button-primary" onClick={onClose}>{s.done}</button>}>
        <p>{s.staffBody}</p>
      </Modal>
    );
  }

  const confirm = async () => {
    setBusy(true);
    setError('');
    try {
      if (unblock) await client.unblockUser(user.id);
      else await client.blockUser(user.id);
      toast.success(unblock ? s.unblocked(name) : s.blocked(name));
      if (onDone) onDone(user, !unblock);
      onClose();
    } catch (err) {
      if (err && err.code === 'CannotBlockStaff') {
        setStaff(true);
        return;
      }
      setError(errorText(err, s.blockErrors));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open
      size="sm"
      onClose={busy ? () => {} : onClose}
      title={unblock ? s.unblockTitle(name) : s.blockTitle(name)}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="button" className={'button ' + (unblock ? 'button-primary' : 'button-danger')} onClick={confirm} disabled={busy}>{unblock ? s.unblock : s.block}</button>
        </>
      )}
    >
      <ErrorBanner message={error} />
      <p>{unblock ? s.unblockBody : s.blockBody}</p>
    </Modal>
  );
}

export default BlockDialog;
