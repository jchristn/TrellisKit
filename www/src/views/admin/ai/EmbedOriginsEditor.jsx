import { useState } from 'react';
import { isLocalDevOrigin, normalizeEmbedOrigin } from '@trelliskit/shared';
import strings from '../../../strings.js';
import Icon from '../../../components/Icon.jsx';
import { Badge } from '../../../components/Badges.jsx';
import { lines } from './aiAdminUtils.js';

const s = strings.ai.admin.assistants;

/**
 * The assistant's extra embed origins (docs/AI_CONTRACT.md section 13.3) as a list: add and remove
 * origins, https://host[:port] by default, with plain-http localhost and 127.0.0.1 entries accepted
 * and labeled as local development origins. The value is the form's newline-separated text, so the
 * request body is built exactly as before; the server's validation is authoritative.
 * @param {{ value: string, onChange: (text: string) => void, id?: string }} props
 */
function EmbedOriginsEditor({ value, onChange, id = 'as-eorig' }) {
  const origins = lines(value);
  const [draft, setDraft] = useState('');
  const [error, setError] = useState('');

  const add = (raw) => {
    const normalized = normalizeEmbedOrigin(raw);
    if (!normalized) {
      setError(s.embedOriginInvalid(String(raw || '').trim()));
      return;
    }
    if (origins.some((o) => normalizeEmbedOrigin(o) === normalized)) {
      setError(s.embedOriginDuplicate);
      return;
    }
    onChange(origins.concat(normalized).join('\n'));
    setDraft('');
    setError('');
  };
  const remove = (origin) => onChange(origins.filter((o) => o !== origin).join('\n'));

  return (
    <fieldset className="field embed-origins" aria-describedby={id + '-hint'}>
      <legend className="label-text">{s.embedOrigins}</legend>
      <p id={id + '-hint'} className="muted small">{s.embedOriginsHint}</p>
      {origins.length === 0 ? <p className="muted small">{s.embedOriginsNone}</p> : (
        <ul className="embed-origin-list">
          {origins.map((origin) => {
            const local = isLocalDevOrigin(origin);
            const valid = Boolean(normalizeEmbedOrigin(origin));
            return (
              <li key={origin} className={'embed-origin' + (local ? ' local' : '')} data-testid={'embed-origin-' + origin}>
                <code className="mono">{origin}</code>
                {local && <Badge tone="warning">{s.embedOriginLocal}</Badge>}
                {local && <span className="muted small">{s.embedOriginLocalHint}</span>}
                {!valid && <span className="field-error">{s.embedOriginInvalid(origin)}</span>}
                <button type="button" className="button button-secondary button-sm" onClick={() => remove(origin)} aria-label={s.embedOriginRemove(origin)}><Icon name="trash" size={14} /></button>
              </li>
            );
          })}
        </ul>
      )}
      <div className="embed-origin-add">
        <input id={id} value={draft} placeholder={s.embedOriginPlaceholder} aria-label={s.embedOriginAdd} aria-invalid={Boolean(error)} spellCheck="false" autoComplete="off"
          onChange={(e) => { setDraft(e.target.value); setError(''); }}
          onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); add(draft); } }} />
        <button type="button" className="button button-secondary button-sm" onClick={() => add(draft)}><Icon name="plus" size={14} /><span>{s.embedOriginAdd}</span></button>
        <button type="button" className="button button-secondary button-sm" onClick={() => { setDraft('http://localhost:'); setError(''); }}>{s.embedOriginAddLocal}</button>
      </div>
      {error && <span className="field-error" role="alert">{error}</span>}
    </fieldset>
  );
}

export default EmbedOriginsEditor;
