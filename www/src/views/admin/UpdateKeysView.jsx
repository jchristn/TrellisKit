import { collectEnumeration } from '@trelliskit/shared';
import { useCallback, useState } from 'react';
import { rotationSteps } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { formatShortDate } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import DataTable from '../../components/DataTable.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import CopyButton from '../../components/CopyButton.jsx';
import Modal from '../../components/Modal.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { FormDialog, splitList } from './LiveUpdateDialogs.jsx';

const u = strings.admin.liveUpdates;
const k = u.keys;

/** Badge tone for a signing key status. */
export function keyStatusTone(status) {
  return status === 'Active' ? 'success' : status === 'Standby' ? 'info' : 'neutral';
}

/**
 * Signing and deploy keys: the code signing key ring (status, source, whether the private key is available,
 * certificate expiry, issuing key, last use) with activate and retire, registration (the private key is write-only
 * and never displayed), the rotation checklist with installs by embedded key id, and CI deploy keys with a one-time
 * secret display and revoke.
 */
function UpdateKeysView() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [dialog, setDialog] = useState(null);
  const [secret, setSecret] = useState(null);
  const keysFetcher = useCallback((signal) => collectEnumeration((q, sig) => client.enumerateSigningKeys(q, sig), { ordering: 'KeyIdAscending' }, { signal }), [client]);
  const keys = useApiQuery(keysFetcher);
  const installsFetcher = useCallback((signal) => collectEnumeration((q, sig) => client.getInstallsByKeyId(q, sig), {}, { signal }), [client]);
  const installs = useApiQuery(installsFetcher);
  const deployFetcher = useCallback((signal) => collectEnumeration((q, sig) => client.enumerateDeployKeys(q, sig), {}, { signal }), [client]);
  const deploy = useApiQuery(deployFetcher);
  const ring = keys.data ? normalizePage(keys.data).items : [];
  const deployKeys = deploy.data ? normalizePage(deploy.data).items : [];
  const steps = rotationSteps(ring, installs.data);

  const run = async (fn, message) => {
    try {
      await fn();
      toast.success(message);
      keys.reload();
      installs.reload();
      deploy.reload();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };

  const keyColumns = [
    { key: 'keyId', label: k.keyId, render: (row) => <strong className="mono">{row.keyId}</strong> },
    { key: 'status', label: u.status, render: (row) => <Badge tone={keyStatusTone(row.status)}>{k.keyStatuses[row.status] || row.status}</Badge> },
    { key: 'source', label: k.source, render: (row) => k.sources[row.source] || row.source },
    { key: 'privateKey', label: k.privateKey, render: (row) => <Badge tone={row.privateKeyAvailable ? 'success' : 'warning'}>{row.privateKeyAvailable ? k.available : k.unavailable}</Badge> },
    { key: 'issuedBy', label: k.issuedBy, render: (row) => (row.issuedByKeyId || k.selfSigned) + (row.isCertificateAuthority ? ' (' + k.authority + ')' : '') },
    { key: 'expires', label: k.expires, render: (row) => formatShortDate(row.certificateNotAfterUtc) },
    { key: 'lastUsed', label: k.lastUsed, render: (row) => (row.lastUsedUtc ? formatShortDate(row.lastUsedUtc) : strings.admin.common.never) },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{u.columns.actions}</span>,
      render: (row) => (
        <span className="nowrap">
          {row.status === 'Standby' && <button type="button" className="button button-secondary button-sm" onClick={() => setDialog({ type: 'activate', row })}>{k.activate}</button>}
          {row.status !== 'Retired' && <> <button type="button" className="button button-secondary button-sm" onClick={() => setDialog({ type: 'retire', row })}>{k.retire}</button></>}
        </span>
      )
    }
  ];

  const deployColumns = [
    { key: 'name', label: k.deployName, render: (row) => row.name },
    { key: 'channels', label: k.deployChannels, render: (row) => row.channels.map((c) => <Badge key={c}>{c}</Badge>) },
    { key: 'active', label: k.deployActive, render: (row) => <Badge tone={row.active ? 'success' : 'neutral'}>{row.active ? u.yes : u.no}</Badge> },
    { key: 'expires', label: k.deployExpires, render: (row) => (row.expiresUtc ? formatShortDate(row.expiresUtc) : strings.admin.common.never) },
    { key: 'lastUsed', label: k.lastUsed, render: (row) => (row.lastUsedUtc ? formatShortDate(row.lastUsedUtc) : strings.admin.common.never) },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{u.columns.actions}</span>,
      render: (row) => (row.active ? <button type="button" className="button button-secondary button-sm" onClick={() => setDialog({ type: 'revoke', row })}>{k.revoke}</button> : null)
    }
  ];

  return (
    <div className="view">
      <PageHeader
        title={strings.admin.routes['update-keys'].label}
        summary={strings.admin.routes['update-keys'].summary}
        actions={(
          <>
            <button type="button" className="button button-secondary button-sm" onClick={() => setDialog({ type: 'register' })}><Icon name="key" size={14} /><span>{k.register}</span></button>
            <button type="button" className="button button-primary button-sm" onClick={() => setDialog({ type: 'deploy' })}><Icon name="plus" size={14} /><span>{k.create}</span></button>
          </>
        )}
      />
      <section className="table-section">
        <DataTable caption={k.signingTitle} columns={keyColumns} rows={ring} rowKey={(row) => row.id} loading={keys.loading} error={keys.error ? errorMessage(keys.error, '') : null} onRetry={keys.reload} emptyMessage={k.noKeys} />
      </section>
      <section className="panel">
        <h2>{k.rotationTitle}</h2>
        <ol className="checklist">
          {steps.map((step) => (
            <li key={step.id} className={step.done ? 'done' : undefined}>
              <Icon name={step.done ? 'check' : 'clock'} size={14} /> {k.rotation[step.id]}
            </li>
          ))}
        </ol>
        <h3>{k.installsByKey}</h3>
        <ul>
          {(installs.data && installs.data.objects ? installs.data.objects : []).map((row) => <li key={row.key || 'none'}><code className="mono">{row.key || '-'}</code>: {row.count}</li>)}
        </ul>
      </section>
      <section className="table-section">
        <DataTable caption={k.deployTitle} columns={deployColumns} rows={deployKeys} rowKey={(row) => row.id} loading={deploy.loading} error={deploy.error ? errorMessage(deploy.error, '') : null} onRetry={deploy.reload} emptyMessage={k.noDeployKeys} />
      </section>

      <ConfirmModal
        open={Boolean(dialog && dialog.type === 'activate')}
        title={dialog && dialog.row ? k.activateTitle(dialog.row.keyId) : ''}
        message={k.activateBody}
        danger={false}
        confirmLabel={k.activate}
        onConfirm={() => { const row = dialog.row; setDialog(null); run(() => client.activateSigningKey(row.id), k.activated); }}
        onCancel={() => setDialog(null)}
      />
      <ConfirmModal
        open={Boolean(dialog && dialog.type === 'retire')}
        title={dialog && dialog.row ? k.retireTitle(dialog.row.keyId) : ''}
        message={k.retireBody}
        confirmLabel={k.retire}
        onConfirm={() => { const row = dialog.row; setDialog(null); run(() => client.retireSigningKey(row.id), k.retired); }}
        onCancel={() => setDialog(null)}
      />
      <ConfirmModal
        open={Boolean(dialog && dialog.type === 'revoke')}
        title={dialog && dialog.row ? k.revokeTitle(dialog.row.name) : ''}
        message={k.createHelp}
        confirmLabel={k.revoke}
        onConfirm={() => { const row = dialog.row; setDialog(null); run(() => client.revokeDeployKey(row.id), k.revoked); }}
        onCancel={() => setDialog(null)}
      />
      {dialog && dialog.type === 'register' && (
        <FormDialog
          open
          title={k.registerTitle}
          submitLabel={k.register}
          fields={[
            { key: 'keyId', label: k.keyId, required: true, maxLength: 64 },
            { key: 'certificatePem', label: k.certificate, type: 'textarea', mono: true, required: true },
            { key: 'privateKeyPem', label: k.privateKeyPem, type: 'textarea', mono: true, help: k.privateKeyHelp }
          ]}
          onClose={() => setDialog(null)}
          onSubmit={async (v) => {
            await client.registerSigningKey({ keyId: v.keyId.trim(), certificatePem: v.certificatePem, privateKeyPem: v.privateKeyPem.trim() ? v.privateKeyPem : undefined });
            setDialog(null);
            toast.success(k.registered);
            keys.reload();
          }}
        />
      )}
      {dialog && dialog.type === 'deploy' && (
        <FormDialog
          open
          title={k.createTitle}
          body={k.createHelp}
          submitLabel={k.create}
          fields={[
            { key: 'name', label: k.deployName, required: true, maxLength: 100 },
            { key: 'channels', label: k.deployChannels, required: true },
            { key: 'expiresUtc', label: k.deployExpires, type: 'date' }
          ]}
          initial={{ channels: 'preview' }}
          onClose={() => setDialog(null)}
          onSubmit={async (v) => {
            const created = await client.createDeployKey({ name: v.name.trim(), channels: splitList(v.channels), expiresUtc: v.expiresUtc ? new Date(v.expiresUtc + 'T23:59:59').toISOString() : undefined });
            setDialog(null);
            setSecret(created.secret);
            deploy.reload();
          }}
        />
      )}
      <Modal open={Boolean(secret)} onClose={() => setSecret(null)} title={k.secretTitle} footer={<button type="button" className="button button-primary" onClick={() => setSecret(null)}>{strings.common.close || 'Close'}</button>}>
        <p>{k.secretBody}</p>
        <p><code className="mono">{secret}</code> {secret && <CopyButton value={secret} label={k.secretTitle} />}</p>
      </Modal>
    </div>
  );
}

export default UpdateKeysView;
