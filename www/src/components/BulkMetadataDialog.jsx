import { useMemo, useState } from 'react';
import strings from '../strings.js';
import { useAdminAuth } from '../context/AdminAuthContext.jsx';
import { useToast } from '../context/ToastContext.jsx';
import { errorMessage, getFieldErrors } from '../utils/api.js';
import Modal from './Modal.jsx';
import LabelInput from './LabelInput.jsx';
import TagRows, { rowsToTags } from './TagRows.jsx';
import { Banner } from './States.jsx';
import { metadataSuggesters } from './metadataSuggest.js';

const b = strings.admin.bulkMetadata;
const m = strings.metadata;

/** Most objects one `POST /admin/metadata/bulk` call accepts. */
export const BULK_METADATA_MAX = 500;

/**
 * The `POST /admin/metadata/bulk` body for a change, or null when the change is empty.
 * @param {string} targetType
 * @param {string[]} ids
 * @param {{ addLabels: string[], removeLabels: string[], tagRows: Array<object>, removeTagKeys: string[] }} change
 */
export function bulkMetadataBody(targetType, ids, { addLabels, removeLabels, tagRows, removeTagKeys }) {
  const setTags = rowsToTags(tagRows || []);
  const body = { targetType, targetIds: ids.slice(0, BULK_METADATA_MAX) };
  if (addLabels.length) body.addLabels = addLabels;
  if (removeLabels.length) body.removeLabels = removeLabels;
  if (Object.keys(setTags).length) body.setTags = setTags;
  if (removeTagKeys.length) body.removeTagKeys = removeTagKeys;
  return Object.keys(body).length > 2 ? body : null;
}

/**
 * Apply one incremental label and tag change to the selected objects of one type through
 * `POST /api/v1/admin/metadata/bulk` (at most 500 ids), then show a result summary (updated
 * count and ids that were not found). `onDone(result)` runs after a successful change.
 *
 * @param {{ open: boolean, targetType: string, ids: string[], onClose: () => void, onDone?: (result: object) => void }} props
 */
function BulkMetadataDialog({ open, targetType, ids, onClose, onDone }) {
  const { client } = useAdminAuth();
  const toast = useToast();
  const suggest = useMemo(() => metadataSuggesters(client, targetType), [client, targetType]);
  const [addLabels, setAddLabels] = useState([]);
  const [removeLabels, setRemoveLabels] = useState([]);
  const [tagRows, setTagRows] = useState([]);
  const [removeTagKeys, setRemoveTagKeys] = useState([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [result, setResult] = useState(null);
  const tooMany = ids.length > BULK_METADATA_MAX;

  const apply = async (e) => {
    e.preventDefault();
    if (tooMany) {
      setError(b.tooMany(BULK_METADATA_MAX));
      return;
    }
    const body = bulkMetadataBody(targetType, ids, { addLabels, removeLabels, tagRows, removeTagKeys });
    if (!body) {
      setError(b.nothing);
      return;
    }
    setBusy(true);
    setError('');
    try {
      const response = (await client.bulkMetadata(body)) || {};
      const summary = { updated: Number(response.updated) || 0, notFound: Array.isArray(response.notFound) ? response.notFound : [] };
      setResult(summary);
      toast.success(b.toast(summary.updated));
      if (onDone) onDone(summary);
    } catch (err) {
      const fields = getFieldErrors(err);
      const detail = Object.values(fields).flat().filter(Boolean).join(' ');
      setError([errorMessage(err, strings.common.errorGeneric), detail].filter(Boolean).join(' '));
    } finally {
      setBusy(false);
    }
  };

  const formId = 'bulk-metadata-form';
  const footer = result ? (
    <button type="button" className="button button-primary" onClick={onClose}>{b.done}</button>
  ) : (
    <>
      <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
      <button type="submit" form={formId} className="button button-primary" disabled={busy || tooMany}>{busy ? b.applying : b.apply}</button>
    </>
  );

  return (
    <Modal open={open} onClose={busy ? () => {} : onClose} title={result ? b.resultTitle : b.title(ids.length)} subtitle={result ? null : b.cap(BULK_METADATA_MAX)} size="md" footer={footer}>
      {result ? (
        <div className="stack-form" role="status">
          <p>{b.resultUpdated(result.updated)}</p>
          {result.notFound.length > 0 && (
            <>
              <p className="muted">{b.resultNotFound(result.notFound.length)}</p>
              <ul className="bullet-list mono small">{result.notFound.map((id) => <li key={id}>{id}</li>)}</ul>
            </>
          )}
        </div>
      ) : (
        <form id={formId} className="stack-form" onSubmit={apply} noValidate>
          <p className="muted small">{b.intro}</p>
          {tooMany && <Banner tone="warning" icon="alert">{b.tooMany(BULK_METADATA_MAX)}</Banner>}
          <LabelInput label={b.addLabels} values={addLabels} onChange={setAddLabels} suggest={suggest.labels} placeholder={m.labelsPlaceholder} disabled={busy} />
          <LabelInput label={b.removeLabels} values={removeLabels} onChange={setRemoveLabels} suggest={suggest.labels} placeholder={m.labelsPlaceholder} disabled={busy} />
          <TagRows label={b.setTags} rows={tagRows} onChange={setTagRows} suggestKeys={suggest.tagKeys} suggestValues={suggest.tagValues} disabled={busy} />
          <LabelInput label={b.removeTagKeys} values={removeTagKeys} onChange={setRemoveTagKeys} suggest={suggest.tagKeys} placeholder={b.removeTagKeysPlaceholder} disabled={busy} />
          {error && <p className="field-error" role="alert">{error}</p>}
        </form>
      )}
    </Modal>
  );
}

export default BulkMetadataDialog;
