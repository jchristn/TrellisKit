import { useEffect, useRef, useState } from 'react';
import strings from '../strings.js';
import Icon from './Icon.jsx';
import { copyText as writeClipboard } from '../utils/clipboard.js';

/**
 * Copy-to-clipboard icon button. Shows a checkmark for 1.5 seconds after a successful copy.
 * @param {{ value: string, label?: string, className?: string }} props
 */
function CopyButton({ value, label, className = '' }) {
  const [copied, setCopied] = useState(false);
  const timer = useRef(null);
  useEffect(() => () => clearTimeout(timer.current), []);

  const text = label ? strings.common.copyValue(label) : strings.common.copy;
  const handleClick = async (e) => {
    e.stopPropagation();
    try {
      await writeClipboard(String(value ?? ''));
      setCopied(true);
      clearTimeout(timer.current);
      timer.current = setTimeout(() => setCopied(false), 1500);
    } catch {
      setCopied(false);
    }
  };

  return (
    <button
      type="button"
      className={'icon-button copy-button ' + className}
      onClick={handleClick}
      aria-label={copied ? strings.common.copied : text}
      title={copied ? strings.common.copied : text}
      data-ignore-row-click
    >
      <Icon name={copied ? 'check' : 'copy'} size={14} />
    </button>
  );
}

export default CopyButton;
