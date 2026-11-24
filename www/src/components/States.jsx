import strings from '../strings.js';
import Icon from './Icon.jsx';

/** Inline loading indicator with reserved height so layout does not jump. */
export function Loading({ label = strings.common.loading, inline = false }) {
  return (
    <div className={inline ? 'loading-inline' : 'loading-block'} role="status">
      <span className="spinner" aria-hidden="true" />
      <span>{label}</span>
    </div>
  );
}

/** Error banner with optional retry and dismiss. */
export function ErrorBanner({ message, onRetry, onDismiss }) {
  if (!message) return null;
  return (
    <div className="banner banner-danger" role="alert">
      <Icon name="alert" />
      <span className="banner-text">{message}</span>
      {onRetry && <button type="button" className="button button-secondary button-sm" onClick={onRetry}>{strings.common.retry}</button>}
      {onDismiss && (
        <button type="button" className="icon-button" onClick={onDismiss} aria-label={strings.common.dismiss} title={strings.common.dismiss}>
          <Icon name="close" size={14} />
        </button>
      )}
    </div>
  );
}

/** Generic banner. */
export function Banner({ tone = 'info', icon = 'info', children, actions }) {
  return (
    <div className={'banner banner-' + tone} role={tone === 'danger' ? 'alert' : 'status'}>
      <Icon name={icon} />
      <span className="banner-text">{children}</span>
      {actions}
    </div>
  );
}

/** Page or panel level empty state. */
export function EmptyState({ icon = 'info', title, children, action }) {
  return (
    <div className="empty-state">
      <Icon name={icon} size={28} />
      {title && <h3>{title}</h3>}
      {children && <p>{children}</p>}
      {action}
    </div>
  );
}
