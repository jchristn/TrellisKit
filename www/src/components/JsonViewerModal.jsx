import strings from '../strings.js';
import Modal from './Modal.jsx';
import CodeBlock from './CodeBlock.jsx';

/** Read-only JSON viewer. */
function JsonViewerModal({ open, onClose, title = strings.admin.json.title, subtitle, value }) {
  return (
    <Modal open={open} onClose={onClose} title={title} subtitle={subtitle} size="lg">
      <CodeBlock value={JSON.stringify(value, null, 2)} label="JSON" maxHeight={600} />
    </Modal>
  );
}

export default JsonViewerModal;
