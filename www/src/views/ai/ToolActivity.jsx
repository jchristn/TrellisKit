import { Link } from 'react-router-dom';
import { toolCallDisplayItems } from '@trelliskit/shared';
import strings from '../../strings.js';

const a = strings.ai;

const FORMAT = { running: a.toolRunning, failed: a.toolFailed, fallbackLabel: a.toolDefault };

function ResultLink({ result, linkBase }) {
  if (linkBase === null || linkBase === undefined || !result.path) return <span>{result.title}</span>;
  return <Link to={linkBase + result.path}>{result.title}</Link>;
}

/**
 * Compact tool activity above an assistant reply (AI contract section 8): one chip per tool call,
 * "Searching the library..." while it runs, "Searched the library" when done with its results in a
 * disclosure (app paths open in place under `linkBase`; `null` shows titles only), failures muted.
 * Renders the streamed calls and the persisted `toolCalls` of loaded messages alike.
 * @param {{ message: object, linkBase?: string|null }} props
 */
function ToolActivity({ message, linkBase = '/app' }) {
  const items = toolCallDisplayItems(message, FORMAT);
  if (items.length === 0) return null;
  return (
    <ul className="ai-tools" aria-label={a.toolActivity} aria-live="polite" data-testid="ai-tool-activity">
      {items.map((item) => (
        <li key={item.id} className="ai-tool" data-state={item.state} data-tool={item.toolName || undefined}>
          {item.state === 'done' && item.results.length > 0 ? (
            <details>
              <summary className="ai-tool-chip">
                <span>{item.text}</span>
                <span className="muted">{a.toolResults(item.resultCount ?? item.results.length)}</span>
              </summary>
              <ul className="ai-tool-results" aria-label={a.toolResultsList(item.text)}>
                {item.results.map((result) => <li key={result.key}><ResultLink result={result} linkBase={linkBase} /></li>)}
              </ul>
            </details>
          ) : (
            <span className={'ai-tool-chip' + (item.state === 'failed' ? ' muted' : '')}>{item.text}</span>
          )}
        </li>
      ))}
    </ul>
  );
}

export default ToolActivity;
