/**
 * `/app/check-in?t={targetType}&id={targetId}&d={occurrenceDate}&c={code}`: the page a check-in QR
 * code opens. With a code it checks the signed-in member in at once; without one it asks for the
 * code. Offers "Open in the app" when the property has a mobile URL scheme.
 */
import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { checkInAppPath, parseCheckInPayload } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import PageHeader from '../../components/PageHeader.jsx';
import { Loading } from '../../components/States.jsx';
import { CheckInForm, checkInErrorText } from './CheckIn.jsx';

const t = strings.ranks.checkIn;

function CheckInView() {
  const { client } = useAuth();
  const { property } = useProperty();
  const [params] = useSearchParams();
  const queryKey = params.toString();
  const body = useMemo(() => parseCheckInPayload(Object.fromEntries(new URLSearchParams(queryKey))), [queryKey]);
  const [state, setState] = useState(() => ({ status: body && body.code ? 'working' : 'form', message: '' }));
  const started = useRef(false);
  useDocumentMeta(t.title);
  useEffect(() => {
    if (!body || !body.code || started.current) return;
    started.current = true;
    client.checkIn(body).then(
      () => setState({ status: 'done', message: t.done }),
      (err) => setState(err && err.code === 'AlreadyCheckedIn' ? { status: 'done', message: t.errors.AlreadyCheckedIn } : { status: 'form', message: checkInErrorText(err) })
    );
  }, [body, client]);
  const scheme = property && property.mobile && typeof property.mobile.scheme === 'string' && /^[a-z][a-z0-9+.-]*$/i.test(property.mobile.scheme) ? property.mobile.scheme : null;
  const appLink = body && scheme ? scheme + '://' + checkInAppPath(body).slice(1) : null;
  return (
    <div className="member-view stack">
      <PageHeader title={t.title} />
      {!body && <p role="alert">{t.linkInvalid}</p>}
      {body && state.status === 'working' && <Loading label={t.working} />}
      {body && state.status === 'done' && <p className="success-text" role="status">{state.message}</p>}
      {body && state.status === 'form' && (
        <>
          {state.message && <p className="field-error" role="alert">{state.message}</p>}
          <CheckInForm client={client} target={body} onDone={(_a, already) => setState({ status: 'done', message: already ? t.errors.AlreadyCheckedIn : t.done })} />
        </>
      )}
      {appLink && <p><a href={appLink} className="button button-ghost button-sm">{t.openApp}</a></p>}
      <p><Link to="/app/rank">{strings.ranks.nav}</Link></p>
    </div>
  );
}

export default CheckInView;
