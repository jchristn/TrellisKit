import { useEffect, useRef, useState } from 'react';
import strings from '../strings.js';
import { useAuth } from '../context/AuthContext.jsx';
import { SocialCancelled, appleCredential, facebookCredential, googleButtonWidth, renderGoogleButton } from '../utils/social.js';

const a = strings.auth;

function GoogleButton({ clientId, onCredential, text, disabled }) {
  const ref = useRef(null);
  const callbackRef = useRef(onCredential);
  useEffect(() => {
    callbackRef.current = onCredential;
  }, [onCredential]);
  // Google draws its button at a fixed pixel width, so it is rendered at the container's width (the
  // width of the other sign-in buttons) and again whenever that width changes.
  useEffect(() => {
    let active = true;
    let lastWidth = -1;
    const element = ref.current;
    const render = () => {
      if (!active || !element) return;
      const width = googleButtonWidth(element.getBoundingClientRect().width);
      if (width === lastWidth) return;
      lastWidth = width;
      renderGoogleButton(element, { clientId, text, width, onCredential: (body) => { if (active) callbackRef.current(body); } }).catch(() => {});
    };
    render();
    const observer = typeof ResizeObserver === 'function' && element ? new ResizeObserver(render) : null;
    if (observer) observer.observe(element);
    return () => {
      active = false;
      if (observer) observer.disconnect();
    };
  }, [clientId, text]);
  return <div ref={ref} className={'google-button' + (disabled ? ' is-disabled' : '')} aria-label={a.continueWith('Google')} />;
}

/**
 * Social sign-in buttons, shown only for providers `GET /auth/providers` enables. Each button
 * obtains a provider credential and hands `(provider, body)` to `onCredential`; the parent calls
 * the right route (sign-in or link) and handles errors.
 *
 * @param {{ onCredential: (provider: 'apple'|'google'|'facebook', body: object) => void, onError?: (message: string) => void,
 *   busy?: boolean, mode?: 'signIn'|'signUp', only?: string[] }} props
 */
function SocialButtons({ onCredential, onError, busy = false, mode = 'signIn', only, showDivider = true }) {
  const { getProviders } = useAuth();
  const [providers, setProviders] = useState(null);

  useEffect(() => {
    let active = true;
    getProviders().then((value) => { if (active) setProviders(value); }, () => { if (active) setProviders({}); });
    return () => { active = false; };
  }, [getProviders]);

  if (!providers) return null;
  const allowed = (name) => !only || only.includes(name);
  const google = providers.google && providers.google.enabled && providers.google.webClientId && allowed('google');
  const apple = providers.apple && providers.apple.enabled && providers.apple.webClientId && allowed('apple');
  const facebook = providers.facebook && providers.facebook.enabled && providers.facebook.appId && allowed('facebook');
  if (!google && !apple && !facebook) return null;

  const run = async (provider, label, getCredential) => {
    try {
      const body = await getCredential();
      onCredential(provider, body);
    } catch (err) {
      if (err instanceof SocialCancelled) return;
      if (onError) onError(a.errors.socialFailed(label));
    }
  };

  return (
    <div className="social-buttons">
      {showDivider && <div className="divider" role="separator"><span>{a.orDivider}</span></div>}
      {google && (
        <GoogleButton
          clientId={providers.google.webClientId}
          text={mode === 'signUp' ? 'signup_with' : 'continue_with'}
          disabled={busy}
          onCredential={(body) => onCredential('google', body)}
        />
      )}
      {apple && (
        <button type="button" className="button button-social button-apple" disabled={busy} onClick={() => run('apple', 'Apple', () => appleCredential(providers.apple.webClientId))}>
          {a.continueWith('Apple')}
        </button>
      )}
      {facebook && (
        <button type="button" className="button button-social button-facebook" disabled={busy} onClick={() => run('facebook', 'Facebook', () => facebookCredential(providers.facebook.appId))}>
          {a.continueWith('Facebook')}
        </button>
      )}
    </div>
  );
}

export default SocialButtons;
