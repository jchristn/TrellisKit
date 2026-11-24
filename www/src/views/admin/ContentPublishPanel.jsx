import { useState } from 'react';
import strings from '../../strings.js';
import { useToast } from '../../context/ToastContext.jsx';
import { errorMessage } from '../../utils/api.js';
import { FormDialog } from './LiveUpdateDialogs.jsx';

const fl = strings.admin.liveUpdates.flags;
const ac = strings.appContent.admin;

/**
 * Pending draft counts from `GET /admin/app/drafts` (`counts: { flags, strings, banners, assets }`);
 * older servers without `counts` report flag drafts only (`totalRecords`).
 * @param {object|null} data
 * @returns {{ flags: number, strings: number, banners: number, assets: number, total: number }}
 */
export function draftCounts(data) {
  const c = (data && data.counts) || null;
  const counts = {
    flags: Number(c ? c.flags : data ? data.totalRecords : 0) || 0,
    strings: Number(c && c.strings) || 0,
    banners: Number(c && c.banners) || 0,
    assets: Number(c && c.assets) || 0
  };
  return { ...counts, total: counts.flags + counts.strings + counts.banners + counts.assets };
}

/**
 * The pending content drafts of every type (flags, strings, banners, assets) with Publish (one new
 * content version, `expectedVersion` from the drafts call) and Discard (docs/LIVE_UPDATES_CONTRACT.md
 * section 16). `drafts` is the `useApiQuery` result of `client.getContentDrafts`.
 * @param {{ client: object, drafts: { data: object|null, reload: () => void }, onChanged?: () => void }} props
 */
function ContentPublishPanel({ client, drafts, onChanged }) {
  const toast = useToast();
  const [publishing, setPublishing] = useState(false);
  const counts = draftCounts(drafts.data);
  const changed = () => {
    drafts.reload();
    if (onChanged) onChanged();
  };
  const discard = async () => {
    try {
      await client.discardContentDrafts();
      toast.success(fl.discarded);
      changed();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };
  const parts = ['flags', 'strings', 'banners', 'assets'].filter((k) => counts[k] > 0).map((k) => ac.draftPart[k](counts[k]));
  return (
    <section className="panel" aria-label={ac.draftsLabel}>
      <div className="toolbar-left">
        <strong>{counts.total ? fl.drafts(counts.total) : fl.noDrafts}</strong>
        {parts.length > 0 && <span className="muted">{ac.draftBreakdown(parts)}</span>}
        <button type="button" className="button button-primary button-sm" disabled={!counts.total} onClick={() => setPublishing(true)}>{fl.publish}</button>
        <button type="button" className="button button-secondary button-sm" disabled={!counts.total} onClick={discard}>{fl.discard}</button>
      </div>
      {publishing && (
        <FormDialog
          open
          title={fl.publishTitle}
          body={fl.publishBody}
          submitLabel={fl.publish}
          fields={[{ key: 'message', label: fl.publishMessage, required: true, maxLength: 500 }]}
          onClose={() => setPublishing(false)}
          onSubmit={async (v) => {
            const version = await client.publishContent(v.message.trim(), drafts.data ? drafts.data.contentVersion : undefined);
            setPublishing(false);
            toast.success(fl.published(version.version));
            changed();
          }}
        />
      )}
    </section>
  );
}

export default ContentPublishPanel;
