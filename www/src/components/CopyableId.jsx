import CopyButton from './CopyButton.jsx';

/** Monospace, non-wrapping identifier with a copy button. */
function CopyableId({ value, label = 'ID' }) {
  if (!value) return null;
  return (
    <span className="copyable-id">
      <code className="mono nowrap" title={value}>{value}</code>
      <CopyButton value={value} label={label} />
    </span>
  );
}

export default CopyableId;
