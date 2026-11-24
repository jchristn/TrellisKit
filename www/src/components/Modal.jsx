import { useEffect, useId, useRef } from 'react';
import { createPortal } from 'react-dom';
import strings from '../strings.js';
import Icon from './Icon.jsx';

const FOCUSABLE = 'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

let openCount = 0;
/** Open dialogs, innermost last; only the top one handles Escape and Tab. */
const stack = [];

/**
 * Accessible modal dialog rendered in a portal. Locks body scroll, closes on Escape and on
 * backdrop click, and keeps keyboard focus inside the dialog.
 *
 * @param {{ open: boolean, onClose: () => void, title: React.ReactNode, subtitle?: React.ReactNode,
 *   size?: 'sm'|'md'|'lg'|'xl', footer?: React.ReactNode, headerExtra?: React.ReactNode, children: React.ReactNode,
 *   variant?: 'dialog'|'drawer' }} props  `drawer` slides in from the right for record detail that stays connected to a list.
 */
function Modal({ open, onClose, title, subtitle, size = 'md', footer, headerExtra, children, variant = 'dialog' }) {
  const panelRef = useRef(null);
  const onCloseRef = useRef(onClose);
  const titleId = useId();

  useEffect(() => {
    onCloseRef.current = onClose;
  }, [onClose]);

  useEffect(() => {
    if (!open) return undefined;
    const previous = document.activeElement;
    const token = {};
    stack.push(token);
    openCount += 1;
    document.body.classList.add('modal-open');

    const panel = panelRef.current;
    const first = panel && panel.querySelector(FOCUSABLE);
    (first || panel)?.focus();

    const handleKey = (e) => {
      if (stack[stack.length - 1] !== token) return;
      if (e.key === 'Escape') {
        e.stopPropagation();
        onCloseRef.current();
        return;
      }
      if (e.key !== 'Tab' || !panelRef.current) return;
      const nodes = Array.from(panelRef.current.querySelectorAll(FOCUSABLE));
      if (nodes.length === 0) return;
      const firstNode = nodes[0];
      const lastNode = nodes[nodes.length - 1];
      if (e.shiftKey && document.activeElement === firstNode) {
        e.preventDefault();
        lastNode.focus();
      } else if (!e.shiftKey && document.activeElement === lastNode) {
        e.preventDefault();
        firstNode.focus();
      }
    };
    document.addEventListener('keydown', handleKey);
    return () => {
      document.removeEventListener('keydown', handleKey);
      const index = stack.indexOf(token);
      if (index >= 0) stack.splice(index, 1);
      openCount -= 1;
      if (openCount <= 0) {
        openCount = 0;
        document.body.classList.remove('modal-open');
      }
      if (previous && typeof previous.focus === 'function') previous.focus();
    };
  }, [open]);

  if (!open) return null;

  return createPortal(
    <div className={'modal-backdrop' + (variant === 'drawer' ? ' drawer-backdrop' : '')} onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div
        ref={panelRef}
        className={'modal modal-' + size + (variant === 'drawer' ? ' drawer' : '')}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
      >
        <div className="modal-header">
          <div className="modal-heading">
            <h2 id={titleId}>{title}</h2>
            {subtitle && <div className="modal-subtitle">{subtitle}</div>}
          </div>
          {headerExtra}
          <button type="button" className="icon-button" onClick={onClose} aria-label={strings.common.close} title={strings.common.close}>
            <Icon name="close" />
          </button>
        </div>
        <div className="modal-body">{children}</div>
        {footer && <div className="modal-footer">{footer}</div>}
      </div>
    </div>,
    document.body
  );
}

export default Modal;
