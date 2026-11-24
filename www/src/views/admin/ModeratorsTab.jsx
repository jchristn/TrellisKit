import { useCallback, useState } from 'react';
import { normalizeEnumerationResult } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDebounce from '../../hooks/useDebounce.js';
import { errorMessage } from '../../utils/api.js';
import { formatShortDate } from '../../utils/format.js';
import { authorName, mediaUrl } from '../../utils/engagement.js';
import { roleText } from '../../utils/labels.js';
import DataTable from '../../components/DataTable.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import Modal from '../../components/Modal.jsx';
import Avatar from '../../components/Avatar.jsx';
import Icon from '../../components/Icon.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';

const s = strings.admin.moderators;
const col = s.columns;

function GrantDialog({ onClose, onGranted }) {
  const { api } = useAdminAuth();
  const [text, setText] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const q = useDebounce(text.trim(), 250);
  const fetcher = useCallback((signal) => (q ? api.listMembers({ search: q, maxResults: 10 }, signal) : Promise.resolve(null)), [api, q]);
  const results = useApiQuery(fetcher, { enabled: Boolean(q) });
  const members = results.data ? normalizeEnumerationResult(results.data).objects : [];
  const grant = async (member) => {
    setBusy(true);
    setError('');
    try {
      await api.grantModerator(member.id, 'Feed');
      onGranted(member);
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };
  return (
    <Modal open size="md" onClose={onClose} title={s.grantTitle}>
      <ErrorBanner message={error} />
      <div className="field">
        <label className="label-text" htmlFor="grant-search">{s.memberSearch}</label>
        <input id="grant-search" type="search" value={text} onChange={(e) => setText(e.target.value)} placeholder={s.memberSearchPlaceholder} autoFocus />
      </div>
      {results.loading && q && <Loading inline />}
      {q && !results.loading && members.length === 0 && <p className="muted small">{s.noMatches}</p>}
      <ul className="who-list">
        {members.map((m) => (
          <li key={m.id} className="who-row">
            <Avatar src={mediaUrl(m.avatarUrl)} name={m.displayName} size={28} />
            <span className="cell-stack"><strong>{m.displayName}</strong><span className="muted small">{m.email || (m.handle ? '@' + m.handle : '')}</span></span>
            <button type="button" className="button button-primary button-sm" onClick={() => grant(m)} disabled={busy}>{s.pick}</button>
          </li>
        ))}
      </ul>
    </Modal>
  );
}

/** Moderators (contract section 10.4): who holds feed moderation, their workload, grant and revoke. */
function ModeratorsTab() {
  const { api } = useAdminAuth();
  const toast = useToast();
  const [granting, setGranting] = useState(false);
  const [revoking, setRevoking] = useState(null);
  const [busy, setBusy] = useState(false);
  const fetcher = useCallback((signal) => api.listModerators({ maxResults: 100 }, signal), [api]);
  const list = useApiQuery(fetcher);
  const rows = list.data ? normalizeEnumerationResult(list.data).objects : [];
  const revoke = async () => {
    setBusy(true);
    try {
      await api.revokeModerator(revoking.user.id, 'Feed');
      toast.success(s.revoked(authorName(revoking.user)));
      setRevoking(null);
      list.reload();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };
  const columns = [
    { key: 'member', label: col.member, render: (row) => <span className="member-cell"><Avatar src={mediaUrl(row.user && row.user.avatarUrl)} name={authorName(row.user)} size={28} /><strong>{authorName(row.user)}</strong></span> },
    { key: 'roles', label: col.roles, render: (row) => (row.roles || []).map(roleText).join(', ') },
    { key: 'granted', label: col.granted, className: 'nowrap', render: (row) => (row.grantedUtc ? formatShortDate(row.grantedUtc) : '-') },
    { key: 'grantedBy', defaultHidden: true, label: col.grantedBy, render: (row) => row.grantedByName || '-' },
    { key: 'open', label: col.open, className: 'num', render: (row) => row.openInScope ?? '-' },
    { key: 'actions30d', label: col.actions30d, className: 'num', render: (row) => row.actions30d ?? '-' },
    { key: 'median', defaultHidden: true, label: col.median, className: 'num', render: (row) => s.minutes(row.medianResponseMinutes30d) },
    { key: 'overdue', label: col.overdue, className: 'num', render: (row) => row.overdueHandled30d ?? '-' },
    { key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>, render: (row) => <button type="button" className="button button-danger-outline button-sm" onClick={() => setRevoking(row)}>{s.revoke}</button> }
  ];
  return (
    <section className="table-section">
      <div className="table-toolbar">
        <p className="muted small toolbar-left">{s.summary}</p>
        <div className="toolbar-right">
          <button type="button" className="button button-primary button-sm" onClick={() => setGranting(true)}><Icon name="plus" size={14} /><span>{s.grant}</span></button>
        </div>
      </div>
      <DataTable caption={s.title} columns={columns} rows={rows} rowKey={(row) => row.user.id} loading={list.loading} error={list.error ? strings.admin.common.unavailable(s.title) + ' ' + errorMessage(list.error, '') : null} onRetry={list.reload} emptyMessage={s.empty} />
      {granting && <GrantDialog onClose={() => setGranting(false)} onGranted={(m) => { toast.success(s.granted(m.displayName)); setGranting(false); list.reload(); }} />}
      <ConfirmModal open={Boolean(revoking)} title={revoking ? s.revokeTitle(authorName(revoking.user)) : ''} message={s.revokeBody} confirmLabel={s.revoke} busy={busy} onCancel={() => setRevoking(null)} onConfirm={revoke} />
    </section>
  );
}

export default ModeratorsTab;
