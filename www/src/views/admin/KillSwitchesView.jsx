import { useCallback, useState } from 'react';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { formatDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import DataTable from '../../components/DataTable.jsx';
import { Badge } from '../../components/Badges.jsx';
import { FormDialog, splitList } from './LiveUpdateDialogs.jsx';

const u = strings.admin.liveUpdates;
const ks = u.killSwitches;

/** Kill switch rules from the optional version range and platform fields; empty means everyone. */
export function killRules(versionRange, platforms) {
  const conditions = [];
  if (versionRange && versionRange.trim()) conditions.push({ attribute: 'appVersion', operator: 'SemverRange', values: [versionRange.trim()] });
  const list = splitList(platforms);
  if (list.length) conditions.push({ attribute: 'platform', operator: 'In', values: list });
  return conditions.length ? [{ conditions }] : undefined;
}

/** The state label of a kill switch row. */
export function killState(row) {
  if (!row.killed) return { tone: 'success', label: ks.live };
  return row.killedForEveryone ? { tone: 'danger', label: ks.killed } : { tone: 'warning', label: ks.targeted };
}

/**
 * Kill switches (docs/LIVE_UPDATES_CONTRACT.md section 13): one row per engine feature key with its state, message,
 * and last change; turning a feature off asks for a reason, an optional message, and optional targeting (app version
 * range, platforms), and applies immediately.
 */
function KillSwitchesView() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [dialog, setDialog] = useState(null);
  const fetcher = useCallback((signal) => client.enumerateKillSwitches({ maxResults: 100 }, signal), [client]);
  const list = useApiQuery(fetcher);
  const rows = list.data ? normalizePage(list.data).items : [];

  const columns = [
    { key: 'feature', label: ks.feature, render: (row) => <code className="mono">{row.featureKey}</code> },
    { key: 'state', label: ks.state, render: (row) => { const s = killState(row); return <Badge tone={s.tone}>{s.label}</Badge>; } },
    { key: 'message', label: ks.message, render: (row) => row.message || '' },
    { key: 'last', label: ks.lastChange, render: (row) => (row.lastChangedUtc ? formatDateTime(row.lastChangedUtc) + ' (' + row.lastChangedBy + ')' : strings.admin.common.never) },
    { key: 'reason', label: ks.reason, render: (row) => row.lastChangedReason || '' },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{u.columns.actions}</span>,
      render: (row) => (
        <button type="button" className={'button button-sm ' + (row.killed ? 'button-primary' : 'button-danger')} aria-label={(row.killed ? ks.turnOn : ks.turnOff) + ' ' + row.featureKey} onClick={() => setDialog({ row, kill: !row.killed })}>
          {row.killed ? ks.turnOn : ks.turnOff}
        </button>
      )
    }
  ];

  return (
    <div className="view">
      <PageHeader title={strings.admin.routes['kill-switches'].label} summary={strings.admin.routes['kill-switches'].summary} />
      <section className="table-section">
        <DataTable caption={strings.admin.routes['kill-switches'].label} columns={columns} rows={rows} rowKey={(row) => row.featureKey} loading={list.loading} error={list.error ? errorMessage(list.error, '') : null} onRetry={list.reload} emptyMessage={ks.empty} />
      </section>
      {dialog && (
        <FormDialog
          open
          title={dialog.kill ? ks.offTitle(dialog.row.featureKey) : ks.onTitle(dialog.row.featureKey)}
          body={dialog.kill ? ks.offBody : ks.onBody}
          submitLabel={dialog.kill ? ks.turnOff : ks.turnOn}
          danger={dialog.kill}
          fields={[
            { key: 'reason', label: ks.reason, required: true, maxLength: 500, help: strings.admin.common.reasonHint },
            ...(dialog.kill ? [
              { key: 'message', label: ks.message, maxLength: 200 },
              { key: 'versionRange', label: ks.versionRange },
              { key: 'platforms', label: ks.platforms },
              { key: 'blocksServer', label: ks.blocksServer, type: 'checkbox' }
            ] : [])
          ]}
          initial={{ message: dialog.row.message || '', blocksServer: dialog.row.blocksServer !== false }}
          onClose={() => setDialog(null)}
          onSubmit={async (v) => {
            await client.setKillSwitch(dialog.row.featureKey, dialog.kill
              ? { killed: true, reason: v.reason.trim(), message: v.message.trim() || undefined, rules: killRules(v.versionRange, v.platforms), blocksServer: v.blocksServer }
              : { killed: false, reason: v.reason.trim() });
            setDialog(null);
            toast.success(ks.changed);
            list.reload();
          }}
        />
      )}
    </div>
  );
}

export default KillSwitchesView;
