import { useEffect, useRef, useState } from 'react';
import strings from '../strings.js';
import Icon from './Icon.jsx';

/**
 * "Select columns" control. Pinned columns are always shown and are not toggleable.
 * `compact` shows only the icon (for a table's header corner); the name stays as the accessible label.
 * @param {{ columns: Array<{ key: string, label: string, pinned?: boolean }>, hidden: string[], onChange: (hidden: string[]) => void, compact?: boolean }} props
 */
function ColumnSelector({ columns, hidden, onChange, compact = false }) {
  const [open, setOpen] = useState(false);
  const ref = useRef(null);

  useEffect(() => {
    if (!open) return undefined;
    const close = (e) => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    const onKey = (e) => { if (e.key === 'Escape') setOpen(false); };
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  const toggle = (key) => {
    onChange(hidden.includes(key) ? hidden.filter((k) => k !== key) : [...hidden, key]);
  };

  return (
    <div className="popover-anchor" ref={ref}>
      <button type="button" className={compact ? 'icon-button' : 'button button-secondary button-sm'} aria-expanded={open} aria-haspopup="true" aria-label={compact ? strings.admin.table.selectColumns : undefined} title={compact ? strings.admin.table.selectColumns : undefined} onClick={() => setOpen((v) => !v)}>
        <Icon name="columns" size={14} />
        {!compact && <span>{strings.admin.table.selectColumns}</span>}
      </button>
      {open && (
        <div className="popover column-popover">
          {columns.map((col) => (
            <label key={col.key} className={'check-row' + (col.pinned ? ' disabled' : '')} title={col.pinned ? strings.admin.table.pinned : undefined}>
              <input
                type="checkbox"
                checked={col.pinned || !hidden.includes(col.key)}
                disabled={col.pinned}
                onChange={() => toggle(col.key)}
              />
              <span>{col.label}</span>
              {col.pinned && <span className="muted small">{strings.admin.table.pinned}</span>}
            </label>
          ))}
        </div>
      )}
    </div>
  );
}

export default ColumnSelector;
