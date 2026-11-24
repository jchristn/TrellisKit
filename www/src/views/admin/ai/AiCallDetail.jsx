import strings from '../../../strings.js';
import { formatDateTime } from '../../../utils/format.js';
import Modal from '../../../components/Modal.jsx';
import { usd } from './aiAdminUtils.js';

const d = strings.ai.rolesAdmin.detail;

const MS = ['latencyMs', 'queueMs', 'timeToFirstTokenMs', 'timeToLastTokenMs', 'streamingMs', 'loadMs', 'promptEvalMs', 'evalMs', 'toolExecMs'];
const BYTES = ['requestBytes', 'responseBytes'];
const FLAGS = ['isFallback', 'isTest', 'streamed'];

/** The fields of a ledger row, grouped as the detail view shows them (docs/AI_TELEMETRY.md). */
export const CALL_DETAIL_GROUPS = Object.freeze([
  ['call', ['createdUtc', 'purpose', 'role', 'runtime', 'model', 'providerModel', 'endpointId', 'assistantId', 'executorPosition', 'escalationReason', 'outcome', 'errorCode', 'isFallback', 'isTest']],
  ['timing', ['latencyMs', 'queueMs', 'timeToFirstTokenMs', 'timeToLastTokenMs', 'streamingMs', 'tokensPerSecond', 'loadMs', 'promptEvalMs', 'evalMs']],
  ['tokens', ['inputTokens', 'outputTokens', 'reasoningTokens', 'totalTokens', 'cachedReadTokens', 'cacheCreationTokens', 'inputTextTokens', 'inputImageTokens', 'inputAudioTokens', 'inputVideoTokens']],
  ['tools', ['toolCalls', 'toolExecMs']],
  ['transport', ['streamed', 'chunks', 'retries', 'statusCode', 'finishReason', 'requestBytes', 'responseBytes', 'responseId', 'reasoningEffort', 'exitCode']],
  ['cost', ['costUsd', 'jobKey', 'jobRunId', 'jobItemId']]
]);

/**
 * One value of a ledger row as text. A value the provider did not report (null or undefined) reads
 * "Not reported", so a missing measurement is never shown as zero.
 * @param {string} field
 * @param {object} call AiCall
 */
export function callDetailValue(field, call) {
  const value = call ? call[field] : undefined;
  if (value === null || value === undefined || value === '') return d.notReported;
  if (field === 'createdUtc') return formatDateTime(value);
  if (field === 'costUsd') return usd(value);
  if (field === 'tokensPerSecond') return d.perSecond(value);
  if (field === 'executorPosition') return '#' + value;
  if (MS.includes(field)) return d.ms(value);
  if (BYTES.includes(field)) return d.bytes(value);
  if (FLAGS.includes(field)) return value ? d.yes : d.no;
  if (typeof value === 'number') return value.toLocaleString();
  return String(value);
}

/** Every stored value of one model call (the ledger row's detail view). */
function AiCallDetail({ call, onClose }) {
  return (
    <Modal open size="lg" onClose={onClose} title={d.title} subtitle={call.id}>
      {CALL_DETAIL_GROUPS.map(([group, fields]) => (
        <section key={group} className="stack" data-testid={'call-detail-' + group}>
          <h3>{d.groups[group]}</h3>
          <dl className="kv-grid">
            {fields.map((field) => (
              <div key={field}>
                <dt className="label-text">{d.fields[field]}</dt>
                <dd data-testid={'call-' + field}>{callDetailValue(field, call)}</dd>
              </div>
            ))}
          </dl>
        </section>
      ))}
    </Modal>
  );
}

export default AiCallDetail;
