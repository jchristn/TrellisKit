import CopyButton from './CopyButton.jsx';

/** Monospace code/JSON block with a copy button. */
function CodeBlock({ value, label, maxHeight = 360 }) {
  const text = value ?? '';
  return (
    <div className="code-block">
      <div className="code-block-actions"><CopyButton value={text} label={label} /></div>
      <pre style={{ maxHeight }}><code>{text}</code></pre>
    </div>
  );
}

export default CodeBlock;
