import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import Icon from './Icon.jsx';

/**
 * Row action menu. The menu is portaled to document.body so table scroll containers never
 * clip it. Items: [{ label, onSelect, danger?, icon? }].
 */
function ActionMenu({ label, items }) {
  const [open, setOpen] = useState(false);
  const [position, setPosition] = useState({ top: 0, left: 0 });
  const buttonRef = useRef(null);
  const menuRef = useRef(null);

  useLayoutEffect(() => {
    if (!open || !buttonRef.current) return;
    const rect = buttonRef.current.getBoundingClientRect();
    const width = 180;
    const left = Math.max(8, Math.min(rect.right - width, window.innerWidth - width - 8));
    const estimatedHeight = items.length * 36 + 8;
    const below = rect.bottom + 4;
    const top = below + estimatedHeight > window.innerHeight ? Math.max(8, rect.top - estimatedHeight - 4) : below;
    setPosition({ top, left });
  }, [open, items.length]);

  useEffect(() => {
    if (!open) return undefined;
    const close = (e) => {
      if (menuRef.current && menuRef.current.contains(e.target)) return;
      if (buttonRef.current && buttonRef.current.contains(e.target)) return;
      setOpen(false);
    };
    const onKey = (e) => {
      if (e.key === 'Escape') {
        setOpen(false);
        buttonRef.current?.focus();
      }
    };
    const onScroll = () => setOpen(false);
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', onKey);
    window.addEventListener('resize', onScroll);
    window.addEventListener('scroll', onScroll, true);
    menuRef.current?.querySelector('button')?.focus();
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', onKey);
      window.removeEventListener('resize', onScroll);
      window.removeEventListener('scroll', onScroll, true);
    };
  }, [open]);

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        className="icon-button"
        aria-label={label}
        title={label}
        aria-haspopup="menu"
        aria-expanded={open}
        data-ignore-row-click
        onClick={(e) => { e.stopPropagation(); setOpen((v) => !v); }}
      >
        <Icon name="more" />
      </button>
      {open && createPortal(
        <div ref={menuRef} className="action-menu" role="menu" style={{ top: position.top, left: position.left }}>
          {items.map((item) => (
            <button
              key={item.label}
              type="button"
              role="menuitem"
              className={'action-menu-item' + (item.danger ? ' danger' : '')}
              onClick={(e) => { e.stopPropagation(); setOpen(false); item.onSelect(); }}
            >
              {item.icon && <Icon name={item.icon} size={14} />}
              <span>{item.label}</span>
            </button>
          ))}
        </div>,
        document.body
      )}
    </>
  );
}

export default ActionMenu;
