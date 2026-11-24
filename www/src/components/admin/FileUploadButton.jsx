import { useId, useRef, useState } from 'react';
import { uploadMedia } from '@trelliskit/shared';
import strings from '../../strings.js';
import { readVideoDuration } from '../feed/useMediaUploads.js';
import { xhrPut } from '../../utils/xhrPut.js';
import Icon from '../Icon.jsx';

const u = strings.admin.phase4Upload;

/** The content type to declare for a file (browsers leave WebVTT files untyped). */
export function uploadContentType(file) {
  if (file.type) return file.type;
  const name = String(file.name || '').toLowerCase();
  if (name.endsWith('.vtt')) return 'text/vtt';
  if (name.endsWith('.pdf')) return 'application/pdf';
  return 'application/octet-stream';
}

/**
 * Upload one file through the Phase 2 media pipeline (`POST /media/uploads`, direct PUT, complete)
 * and report the Media. `kind` is `Image`, `Video`, or `Document` (PDF or WebVTT, docs/PHASE4_CONTRACT.md
 * section 5.3); staff uploads use the admin limits on the server.
 * @param {{ client: object, kind: 'Image'|'Video'|'Document', accept: string, label: string, onUploaded: (media: object) => void, disabled?: boolean, upload?: Function }} props
 */
function FileUploadButton({ client, kind, accept, label, onUploaded, disabled = false, upload = uploadMedia }) {
  const inputId = useId();
  const ref = useRef(null);
  const [state, setState] = useState({ busy: false, percent: 0, error: '' });
  const pick = async (e) => {
    const file = e.target.files && e.target.files[0];
    e.target.value = '';
    if (!file) return;
    setState({ busy: true, percent: 0, error: '' });
    try {
      const durationSeconds = kind === 'Video' ? await readVideoDuration(file) : null;
      const media = await upload({
        client, size: file.size, contentType: uploadContentType(file), fileName: file.name, kind, durationSeconds,
        slice: (start, end) => file.slice(start, end), put: xhrPut,
        onProgress: (p) => setState((s) => ({ ...s, percent: p.totalBytes ? Math.round((100 * p.loadedBytes) / p.totalBytes) : 0 }))
      });
      setState({ busy: false, percent: 100, error: '' });
      onUploaded(media);
    } catch (err) {
      setState({ busy: false, percent: 0, error: (err && err.message) || u.failed });
    }
  };
  return (
    <span className="badge-row">
      <input ref={ref} id={inputId} type="file" accept={accept} className="visually-hidden" onChange={pick} disabled={disabled || state.busy} />
      <button type="button" className="button button-secondary button-sm" onClick={() => ref.current && ref.current.click()} disabled={disabled || state.busy}>
        <Icon name="upload" size={14} /><span>{state.busy ? u.uploading(state.percent) : label}</span>
      </button>
      {state.error && <span className="field-error" role="alert">{state.error}</span>}
    </span>
  );
}

export default FileUploadButton;
