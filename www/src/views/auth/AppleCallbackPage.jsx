import { useEffect } from 'react';
import strings from '../../strings.js';
import { fill } from '../../property/current.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import AuthCard from './AuthCard.jsx';

const a = strings.auth;

/**
 * /auth/apple/callback: the redirect URI registered with Apple. Sign in with Apple JS runs in
 * popup mode and returns the result to the opener, so this page only closes a stray popup.
 */
function AppleCallbackPage() {
  useDocumentMeta(a.appleCallbackTitle + ' | {name}', a.metaDescription);
  useEffect(() => {
    if (window.opener && window.opener !== window) window.close();
  }, []);
  return <AuthCard title={a.appleCallbackTitle}><p className="muted">{fill(a.appleCallbackBody)}</p></AuthCard>;
}

export default AppleCallbackPage;
