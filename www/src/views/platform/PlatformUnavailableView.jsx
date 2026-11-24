import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import { usePlatformAuth } from '../../context/PlatformAuthContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import ThemeToggle from '../../components/ThemeToggle.jsx';
import { EmptyState, ErrorBanner } from '../../components/States.jsx';

const u = strings.platform.unavailable;

/**
 * Shown on /platform when the backend says this host has no platform console (SingleProperty, or
 * not a platform host), or when the server cannot be reached.
 */
function PlatformUnavailableView() {
  const { status, code, error, retry } = usePlatformAuth();
  useDocumentMeta(strings.platform.metaTitle(u.title), u.body);
  return (
    <div className="login-page">
      <div className="login-theme"><ThemeToggle /></div>
      <main className="login-card">
        {status === 'error' ? (
          <ErrorBanner message={u.error + ' ' + ((error && error.message) || '')} onRetry={retry} />
        ) : (
          <EmptyState icon="lock" title={u.title}>
            {u.body}
          </EmptyState>
        )}
        {status === 'unavailable' && code && <p className="muted small mono">{u.code(code)}</p>}
        <Link to="/" className="small">{u.backToSite}</Link>
      </main>
    </div>
  );
}

export default PlatformUnavailableView;
