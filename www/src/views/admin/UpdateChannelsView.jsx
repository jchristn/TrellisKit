import useCollectionPage from '../../hooks/useCollectionPage.js';
import TablePagination from '../../components/TablePagination.jsx';
import { useCallback, useState } from 'react';
import { UPDATE_PLATFORMS } from '@trelliskit/shared';
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
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { FormDialog } from './LiveUpdateDialogs.jsx';

const u = strings.admin.liveUpdates;
const ch = u.channels;

function latestText(update) {
  return update ? u.percent(update.rolloutPercentage) + ', ' + formatShortDate(update.createdAt) : u.none;
}

/**
 * Channels and branches: the property's built-in and custom update channels with their newest updates and active
 * installs (add or delete custom channels), and the runtime versions installs report (each runtime version is the
 * branch of updates a binary can receive) with app version counts.
 */
function UpdateChannelsView() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState(null);
  const [platform, setPlatform] = useState('');
  const channelPage = useCollectionPage();
  const { query: channelQuery, setResult: setChannelPage } = channelPage;
  const runtimePage = useCollectionPage();
  const { query: runtimeQuery, setResult: setRuntimePage } = runtimePage;
  const fetcher = useCallback(async (signal) => { const page = await client.enumerateUpdateChannels(channelQuery, signal); setChannelPage(page); return page; }, [client, channelQuery, setChannelPage]);
  const list = useApiQuery(fetcher);
  const runtimeFetcher = useCallback(async (signal) => { const page = await client.enumerateRuntimeVersions({ ...runtimeQuery, filters: platform ? { platform } : {} }, signal); setRuntimePage(page); return page; }, [client, platform, runtimeQuery, setRuntimePage]);
  const runtimes = useApiQuery(runtimeFetcher);
  const channels = list.data ? normalizePage(list.data).items : [];
  const branches = runtimes.data ? normalizePage(runtimes.data).items : [];

  const columns = [
    { key: 'name', label: ch.name, render: (row) => <strong>{row.name}</strong> },
    { key: 'kind', label: u.kind, render: (row) => <Badge tone={row.builtIn ? 'info' : 'neutral'}>{row.builtIn ? ch.builtIn : ch.custom}</Badge> },
    { key: 'ios', label: ch.latestIos, render: (row) => latestText(row.latestByPlatform && row.latestByPlatform.ios) },
    { key: 'android', label: ch.latestAndroid, render: (row) => latestText(row.latestByPlatform && row.latestByPlatform.android) },
    { key: 'installs', label: ch.installs, render: (row) => row.activeInstalls30d },
    { key: 'promotable', label: ch.promotable, render: (row) => (row.promotable ? u.yes : u.no) },
    {
      key: 'actions', label: <span className="visually-hidden">{u.columns.actions}</span>, className: 'cell-actions',
      render: (row) => (row.builtIn ? null : (
        <button type="button" className="icon-button" aria-label={ch.removeTitle(row.name)} title={ch.removeTitle(row.name)} onClick={() => setRemoving(row)}><Icon name="trash" size={14} /></button>
      ))
    }
  ];

  const branchColumns = [
    { key: 'platform', label: u.platform, render: (row) => row.platform },
    { key: 'runtimeVersion', label: u.runtimeVersion, render: (row) => <code className="mono" title={row.runtimeVersion}>{row.runtimeVersion.slice(0, 16)}</code> },
    { key: 'installs', label: ch.installs, render: (row) => row.activeInstalls30d },
    { key: 'firstSeen', label: ch.firstSeen, render: (row) => formatShortDate(row.firstSeenUtc) },
    { key: 'lastSeen', label: ch.lastSeen, render: (row) => formatShortDate(row.lastSeenUtc) },
    { key: 'latest', label: ch.latestUpdate, render: (row) => latestText(row.latestUpdate) },
    { key: 'versions', label: ch.binaryVersions, render: (row) => (row.binaryVersions.objects || []).map((v) => (v.key || '?') + ' (' + v.count + ')').join(', ') }
  ];

  return (
    <div className="view">
      <PageHeader
        title={strings.admin.routes['update-channels'].label}
        summary={strings.admin.routes['update-channels'].summary}
        actions={<button type="button" className="button button-primary button-sm" onClick={() => setAdding(true)}><Icon name="plus" size={14} /><span>{ch.add}</span></button>}
      />
      <section className="table-section">
        <TablePagination {...channelPage.props} loading={list.loading} onRefresh={list.reload} />
        <DataTable caption={ch.title} columns={columns} rows={channels} rowKey={(row) => row.name} loading={list.loading} error={list.error ? errorMessage(list.error, '') : null} onRetry={list.reload} emptyMessage={ch.empty} />
      </section>
      <section className="table-section">
        <div className="table-toolbar">
          <div className="toolbar-left"><h2>{ch.branches}</h2></div>
          <div className="toolbar-right">
            <select aria-label={u.platform} value={platform} onChange={(e) => setPlatform(e.target.value)}>
              <option value="">{u.any}</option>
              {UPDATE_PLATFORMS.map((p) => <option key={p} value={p}>{p}</option>)}
            </select>
          </div>
        </div>
        <p className="muted">{ch.branchesHelp}</p>
        <TablePagination {...runtimePage.props} loading={runtimes.loading} onRefresh={runtimes.reload} />
        <DataTable caption={ch.branches} columns={branchColumns} rows={branches} rowKey={(row) => row.platform + ':' + row.runtimeVersion} loading={runtimes.loading} error={runtimes.error ? errorMessage(runtimes.error, '') : null} onRetry={runtimes.reload} emptyMessage={ch.branchesEmpty} />
      </section>
      {adding && (
        <FormDialog
          open
          title={ch.addTitle}
          submitLabel={ch.add}
          fields={[{ key: 'name', label: ch.name, required: true, maxLength: 32, help: ch.nameHelp }]}
          onClose={() => setAdding(false)}
          onSubmit={async (v) => {
            await client.createUpdateChannel(v.name.trim());
            setAdding(false);
            toast.success(ch.added);
            list.reload();
          }}
        />
      )}
      <ConfirmModal
        open={Boolean(removing)}
        title={removing ? ch.removeTitle(removing.name) : ''}
        message={ch.removeBody}
        onConfirm={async () => {
          const target = removing;
          setRemoving(null);
          try {
            await client.deleteUpdateChannel(target.name);
            toast.success(ch.removed);
            list.reload();
          } catch (err) {
            toast.error(errorMessage(err, strings.common.errorGeneric));
          }
        }}
        onCancel={() => setRemoving(null)}
      />
    </div>
  );
}

export default UpdateChannelsView;
