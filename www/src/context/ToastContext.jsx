import { createContext, useCallback, useContext, useMemo, useRef, useState } from 'react';
import strings from '../strings.js';
import Icon from '../components/Icon.jsx';

const ToastContext = createContext(null);

/** Provides transient notifications. Toasts auto-dismiss after five seconds. */
export function ToastProvider({ children }) {
  const [toasts, setToasts] = useState([]);
  const nextId = useRef(1);

  const dismiss = useCallback((id) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const push = useCallback((type, message) => {
    const id = nextId.current++;
    setToasts((prev) => [...prev, { id, type, message }]);
    setTimeout(() => dismiss(id), 5000);
    return id;
  }, [dismiss]);

  const value = useMemo(() => ({
    success: (message) => push('success', message),
    error: (message) => push('danger', message),
    info: (message) => push('info', message),
    dismiss
  }), [push, dismiss]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="toast-region" role="status" aria-live="polite">
        {toasts.map((t) => (
          <div key={t.id} className={'toast toast-' + t.type}>
            <span className="toast-message">{t.message}</span>
            <button type="button" className="icon-button" onClick={() => dismiss(t.id)} aria-label={strings.admin.toast.dismiss} title={strings.admin.toast.dismiss}>
              <Icon name="close" size={14} />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

/** Access toast helpers. */
export function useToast() {
  const context = useContext(ToastContext);
  if (!context) throw new Error('useToast must be used within a ToastProvider');
  return context;
}
