import strings from '../strings.js';
import Modal from './Modal.jsx';

/** Confirmation dialog for destructive or consequential actions. */
function ConfirmModal({ open, title, message, confirmLabel = strings.common.confirm, danger = true, busy = false, onConfirm, onCancel }) {
  return (
    <Modal
      open={open}
      onClose={busy ? () => {} : onCancel}
      title={title}
      size="sm"
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onCancel} disabled={busy}>{strings.common.cancel}</button>
          <button type="button" className={'button ' + (danger ? 'button-danger' : 'button-primary')} onClick={onConfirm} disabled={busy}>
            {confirmLabel}
          </button>
        </>
      )}
    >
      <p className="confirm-message">{message}</p>
    </Modal>
  );
}

export default ConfirmModal;
