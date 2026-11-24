import { useCallback, useState } from 'react';
import { STACK_TRACE_MAX } from '@trelliskit/shared';
import strings from '../../strings.js';
import useApiQuery from '../../hooks/useApiQuery.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { formatDateTime } from '../../utils/format.js';
import CopyButton from '../../components/CopyButton.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner } from '../../components/States.jsx';
import '../../components/appContent.css';

const sy = strings.appContent.admin.symbolicate;

/** `source:line:column` of a mapped frame. */
export function frameLocation(original) {
  if (!original) return '';
  return [original.source, original.line, original.column].filter((x) => x !== null && x !== undefined && x !== '').join(':');
}

function Frames({ result }) {
  return (
    <div>
      {!result.sourceMapAvailable && <p className="banner banner-warning" role="status">{sy.noSourceMap}</p>}
      {result.truncated && <p className="muted">{sy.truncated}</p>}
      <h3>{sy.frames}</h3>
      {(result.frames || []).length === 0 ? <p className="muted">{sy.noFrames}</p> : (
        <ol className="tk-frames">
          {result.frames.map((frame, i) => (
            <li key={i}>
              <code className="mono">{frame.raw}</code>
              {frame.original ? (
                <span className="tk-frame-original"><code className="mono">{frameLocation(frame.original)}</code>{frame.original.name && <> {sy.inFunction(frame.original.name)}</>}</span>
              ) : (
                <span className="tk-frame-original"><Badge tone="neutral">{sy.unmapped}</Badge></span>
              )}
            </li>
          ))}
        </ol>
      )}
      <div className="toolbar-left">
        <h3>{sy.text}</h3>
        <CopyButton value={result.text || ''} label={sy.text} />
      </div>
      <pre className="tk-symbolicated" data-testid="symbolicated-text">{result.text || ''}</pre>
    </div>
  );
}

function Failures({ client, updateId, onPick, busy }) {
  const fetcher = useCallback((signal) => client.enumerateUpdateFailures(updateId, { maxResults: 25 }, signal), [client, updateId]);
  const failures = useApiQuery(fetcher);
  const rows = failures.data ? normalizePage(failures.data).items : [];
  if (failures.error) return <ErrorBanner message={errorMessage(failures.error, strings.common.errorGeneric)} onRetry={failures.reload} />;
  if (!rows.length) return <p className="muted">{failures.loading ? strings.common.loading : sy.noFailures}</p>;
  return (
    <table className="table">
      <caption className="visually-hidden">{sy.failures}</caption>
      <thead><tr><th>{sy.type}</th><th>{sy.errorCode}</th><th>{sy.message}</th><th>{sy.when}</th><th><span className="visually-hidden">{strings.admin.liveUpdates.columns.actions}</span></th></tr></thead>
      <tbody>
        {rows.map((row) => (
          <tr key={row.id}>
            <td>{row.type}</td>
            <td><code className="mono">{row.errorCode || '-'}</code></td>
            <td>{row.message || ''}</td>
            <td>{formatDateTime(row.occurredUtc || row.receivedUtc)}</td>
            <td>
              {row.hasStackTrace
                ? <button type="button" className="button button-secondary button-sm" disabled={busy} aria-label={sy.symbolicateEvent(row.id)} onClick={() => onPick(row.id)}>{sy.run}</button>
                : <span className="muted small">{sy.noStack}</span>}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/**
 * Symbolication of a platform update's stack traces (server-side source maps, never served): pick a
 * reported failure (`POST /admin/updates/{id}/failures/enumerate`) or paste a stack trace, and read
 * the mapped frames and the symbolicated text.
 * @param {{ client: object, updates: Array<{ id: string, platform: string }> }} props
 */
function SymbolicatePanel({ client, updates }) {
  const list = (updates || []).filter((x) => x && x.id);
  const [updateId, setUpdateId] = useState(list.length ? list[0].id : '');
  const [trace, setTrace] = useState('');
  const [result, setResult] = useState(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  if (!list.length) return null;

  const symbolicate = async (request) => {
    setBusy(true);
    setError('');
    try {
      setResult(await client.symbolicateUpdate(updateId, request));
    } catch (err) {
      setResult(null);
      setError(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="panel" aria-labelledby="symbolicate-title">
      <h2 id="symbolicate-title">{sy.title}</h2>
      <p className="muted">{sy.help}</p>
      {list.length > 1 && (
        <div className="field">
          <label className="label-text" htmlFor="symbolicate-update">{strings.admin.liveUpdates.platform}</label>
          <select id="symbolicate-update" value={updateId} onChange={(e) => { setUpdateId(e.target.value); setResult(null); }}>
            {list.map((x) => <option key={x.id} value={x.id}>{x.platform}</option>)}
          </select>
        </div>
      )}
      <h3>{sy.failures}</h3>
      <Failures key={updateId} client={client} updateId={updateId} busy={busy} onPick={(eventId) => symbolicate({ eventId })} />
      <div className="field">
        <label className="label-text" htmlFor="symbolicate-trace">{sy.paste}</label>
        <textarea id="symbolicate-trace" className="mono" rows={6} value={trace} maxLength={STACK_TRACE_MAX} onChange={(e) => setTrace(e.target.value)} spellCheck="false" />
      </div>
      <button type="button" className="button button-primary button-sm" disabled={busy || !trace.trim()} onClick={() => symbolicate({ stackTrace: trace })}>{sy.run}</button>
      {error && <ErrorBanner message={error} />}
      {result && <Frames result={result} />}
    </section>
  );
}

export default SymbolicatePanel;
