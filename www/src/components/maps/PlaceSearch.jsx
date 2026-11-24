import { collectEnumeration } from '@trelliskit/shared';
import { useId, useState } from 'react';
import strings from '../../strings.js';
import { errorText } from '../../utils/engagement.js';
import { phase4Errors } from '../../utils/phase4.js';

const v = strings.venues;

/**
 * Place lookup through POST /directory/geocode (the provider key stays server-side). Picking a
 * candidate calls `onPick({ label, lat, lng, bbox })`.
 */
function PlaceSearch({ client, onPick }) {
  const inputId = useId();
  const [text, setText] = useState('');
  const [state, setState] = useState({ busy: false, results: null, error: '' });
  const search = async (e) => {
    e.preventDefault();
    if (text.trim().length < 2) return;
    setState({ busy: true, results: null, error: '' });
    try {
      const result = await collectEnumeration((page, signal) => client.geocodePlace(text, page, signal), { maxResults: 5 });
      setState({ busy: false, results: result.objects, error: '' });
    } catch (err) {
      setState({ busy: false, results: null, error: err && err.code === 'IntegrationNotConfigured' ? v.placeUnavailable : errorText(err, phase4Errors()) });
    }
  };
  return (
    <form className="inline-form" onSubmit={search} role="search" aria-label={v.placeLabel}>
      <label className="visually-hidden" htmlFor={inputId}>{v.placeLabel}</label>
      <input id={inputId} className="input-compact" type="search" value={text} onChange={(e) => setText(e.target.value)} placeholder={v.placePlaceholder} maxLength={200} />
      <button type="submit" className="button button-secondary button-sm" disabled={state.busy}>{v.placeSearch}</button>
      {state.error && <span className="field-error" role="alert">{state.error}</span>}
      {state.results && (
        <div className="chip-row" role="group" aria-label={v.placeResults}>
          {state.results.length === 0 && <span className="muted small">{v.placeNone}</span>}
          {state.results.map((c) => <button key={c.label + c.lat} type="button" className="chip-toggle" onClick={() => { onPick(c); setState({ busy: false, results: null, error: '' }); }}>{c.label}</button>)}
        </div>
      )}
    </form>
  );
}

/** "Use my location": the browser position rounded to 2 places by the caller, or an error message. */
export function locateMe(onPosition, onError) {
  if (typeof navigator === 'undefined' || !navigator.geolocation) {
    onError(v.locationUnavailable);
    return;
  }
  navigator.geolocation.getCurrentPosition(
    (pos) => onPosition(pos.coords.latitude, pos.coords.longitude),
    () => onError(v.locationDenied),
    { enableHighAccuracy: false, maximumAge: 600000, timeout: 15000 }
  );
}

export default PlaceSearch;
