import { normalizeEnumerationResult } from '@trelliskit/shared';
import strings from '../../strings.js';
import { authorName } from '../../utils/engagement.js';
import { formatDateTime } from '../../utils/format.js';
import { Badge } from '../../components/Badges.jsx';

const s = strings.admin.moderation.messageSnapshot;

/** The snapshot messages of a reported direct message or conversation, oldest first. */
export function snapshotMessages(target) {
  if (!target) return [];
  const source = target.messages || (target.snapshot && target.snapshot.messages) || [];
  const list = Array.isArray(source) ? source : normalizeEnumerationResult(source).objects;
  return list.slice().sort((a, b) => (Number(a.seq) || 0) - (Number(b.seq) || 0));
}

/**
 * A reported direct message (or conversation) as its frozen report snapshot (contract section
 * 3.9): the reported message and the context the reporter could see, tombstones as placeholders,
 * attachment counts. Plain text only and never a link into the conversation.
 * @param {{ target: object, reportedId?: string|null }} props
 */
function MessageSnapshotPanel({ target, reportedId = null }) {
  const messages = snapshotMessages(target);
  const reported = reportedId || target.reportedMessageId || null;
  return (
    <section className="detail-block message-snapshot" aria-labelledby="mod-message-snapshot-title" data-testid="message-snapshot">
      <h3 id="mod-message-snapshot-title">{s.title}</h3>
      <p className="muted small">{s.note}</p>
      {messages.length === 0 && <p className="muted small">{s.empty}</p>}
      <ol className="snapshot-messages">
        {messages.map((m) => {
          const isReported = Boolean(m.isReported) || (reported && (m.id === reported || m.messageId === reported));
          const visible = !m.state || m.state === 'Visible';
          const attachments = Array.isArray(m.attachments) ? m.attachments.length : Number(m.attachmentCount) || 0;
          return (
            <li key={m.id || m.messageId || m.seq} className={'snapshot-message' + (isReported ? ' snapshot-reported' : '')}>
              <div className="small"><strong>{authorName(m.author)}</strong> <span className="muted">{formatDateTime(m.createdUtc)}</span> {isReported && <Badge tone="warning">{s.reported}</Badge>}</div>
              {visible ? <p className="preserve-lines">{m.body}</p> : <p className="muted">{m.state === 'Removed' ? s.removed : s.deleted}</p>}
              {attachments > 0 && <p className="muted small">{s.attachments(attachments)}</p>}
            </li>
          );
        })}
      </ol>
    </section>
  );
}

export default MessageSnapshotPanel;
