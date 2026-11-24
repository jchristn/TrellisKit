import { useMemo, useState } from 'react';
import { splitStaffMetadata, validateMetadata } from '@trelliskit/shared';
import strings from '../strings.js';
import LabelInput from './LabelInput.jsx';
import TagRows, { rowsToTags, tagsToRows } from './TagRows.jsx';
import { metadataSuggesters } from './metadataSuggest.js';

const m = strings.metadata;

/** Prefix tag keys with `staff:` (staff group rows may omit it). */
export function staffPrefixedTags(tags) {
  return Object.fromEntries(Object.entries(tags).map(([k, v]) => [k.toLowerCase().startsWith('staff:') ? k : 'staff:' + k, v]));
}

/** Human text for a validation problem code from `validateMetadata`. */
export function metadataProblemText(code) {
  const [kind, name] = code.split(/:(.*)/s);
  return m.problems[kind] ? m.problems[kind](name || '') : code;
}

/**
 * Labels and tags editor (docs/ENUMERATION_AND_METADATA.md section 1). Members edit public
 * metadata only: `staff:` entries are hidden from them and kept out of what they save. Staff see
 * a second group for `staff:` labels and tags. Controlled: `value` is `{ labels, tags }` (the whole
 * object metadata as served), and `onChange` receives the full set to save.
 *
 * @param {{ value: { labels: string[], tags: Record<string,string> }, onChange: (meta: { labels: string[], tags: Record<string,string> }, problems: string[]) => void,
 *   isStaff?: boolean, client?: object, targetType?: string, disabled?: boolean, legend?: string }} props
 */
function MetadataEditor({ value, onChange, isStaff = false, client, targetType, disabled = false, legend = m.editorTitle, collapsible = false }) {
  const [open, setOpen] = useState(!collapsible);
  const suggest = useMemo(() => metadataSuggesters(client, targetType), [client, targetType]);
  const { publicPart, staffPart } = splitStaffMetadata(value || {});
  const [rows, setRows] = useState(() => tagsToRows(publicPart.tags));
  const [staffRows, setStaffRows] = useState(() => tagsToRows(staffPart.tags));
  const [problems, setProblems] = useState([]);
  const [rejected, setRejected] = useState([]);

  const emit = (next) => {
    // Members cannot add `staff:` labels. Keep them out of the value (they would be hidden from the
    // member by splitStaffMetadata and block saving with no way to remove them) and say why instead.
    const isStaffLabel = (l) => l.toLowerCase().startsWith('staff:');
    const publicLabels = isStaff ? next.publicLabels : next.publicLabels.filter((l) => !isStaffLabel(l));
    setRejected(isStaff ? [] : next.publicLabels.filter(isStaffLabel));
    const merged = {
      labels: [...publicLabels, ...(isStaff ? next.staffLabels : [])],
      tags: { ...rowsToTags(next.rows), ...(isStaff ? staffPrefixedTags(rowsToTags(next.staffRows)) : {}) }
    };
    const result = validateMetadata(merged, { isStaff });
    setProblems(result.problems);
    onChange(isStaff ? merged : { labels: merged.labels, tags: merged.tags }, result.problems);
  };
  const state = { publicLabels: publicPart.labels, staffLabels: staffPart.labels, rows, staffRows };

  const staffPrefixed = (list) => list.map((l) => (l.toLowerCase().startsWith('staff:') ? l : 'staff:' + l));

  if (!open) {
    const count = (value && value.labels ? value.labels.length : 0) + Object.keys((value && value.tags) || {}).length;
    return (
      <button type="button" className="link-button small metadata-toggle" onClick={() => setOpen(true)} aria-expanded="false">
        {count ? m.editorCount(count) : m.editorAdd}
      </button>
    );
  }

  return (
    <fieldset className="metadata-editor" disabled={disabled}>
      <legend className="label-text">{legend}</legend>
      <LabelInput label={m.labels} values={publicPart.labels} onChange={(labels) => emit({ ...state, publicLabels: labels })} suggest={suggest.labels} placeholder={m.labelsPlaceholder} hint={m.labelsHint} />
      <TagRows label={m.tags} rows={rows} onChange={(next) => { setRows(next); emit({ ...state, rows: next }); }} suggestKeys={suggest.tagKeys} suggestValues={suggest.tagValues} />
      {isStaff && (
        <div className="metadata-staff">
          <p className="muted small">{m.staffHint}</p>
          <LabelInput label={m.staffLabels} values={staffPart.labels} onChange={(labels) => emit({ ...state, staffLabels: staffPrefixed(labels) })} suggest={suggest.labels} placeholder="staff:" />
          <TagRows label={m.staffTags} rows={staffRows} onChange={(next) => { setStaffRows(next); emit({ ...state, staffRows: next }); }} suggestKeys={suggest.tagKeys} suggestValues={suggest.tagValues} />
        </div>
      )}
      {(problems.length > 0 || rejected.length > 0) && (
        <ul className="metadata-problems" role="alert">
          {rejected.map((l) => <li key={'rejected:' + l} className="field-error">{m.problems.staffNotAllowed(l)}</li>)}
          {problems.map((p) => <li key={p} className="field-error">{metadataProblemText(p)}</li>)}
        </ul>
      )}
    </fieldset>
  );
}

export default MetadataEditor;
