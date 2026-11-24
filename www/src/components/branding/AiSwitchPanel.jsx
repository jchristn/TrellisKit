import { useCallback, useEffect, useState } from 'react';
import strings from '../../strings.js';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import { errorMessage } from '../../utils/api.js';
import { formatDateTime } from '../../utils/format.js';
import ConfirmModal from '../ConfirmModal.jsx';
import { Badge } from '../Badges.jsx';
import { Banner, ErrorBanner, Loading } from '../States.jsx';

const s = strings.admin.aiSettings;

/**
 * The property's AI switch (docs/TRELLISKIT.md section 9.5), shared by the admin portal's AI
 * settings page and the platform console's AI tab. Turning AI off asks for confirmation; the
 * change applies at once on the server (no restart).
 * @param {{ load: (signal?: AbortSignal) => Promise<object>, save: (enabled: boolean) => Promise<object>, onSaved?: (view: object) => void, auditLink?: React.ReactNode }} props
 */
function AiSwitchPanel({ load, save, onSaved, auditLink = null }) {
  const toast = useToast();
  const fetcher = useCallback((signal) => load(signal), [load]);
  const query = useApiQuery(fetcher);
  const [view, setView] = useState(null);
  const [busy, setBusy] = useState(false);
  const [confirming, setConfirming] = useState(false);

  useEffect(() => {
    if (query.data) setView(query.data);
  }, [query.data]);

  if (!view) {
    return (
      <div className="ai-switch-panel">
        {query.loading && <Loading />}
        {query.error && <ErrorBanner message={s.loadError + ' ' + errorMessage(query.error, '')} onRetry={query.reload} />}
      </div>
    );
  }

  const apply = async (enabled) => {
    setConfirming(false);
    setBusy(true);
    try {
      const next = await save(enabled);
      setView(next);
      toast.success(s.saved(next.enabled));
      if (onSaved) onSaved(next);
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="panel ai-switch-panel" aria-labelledby="ai-switch-title">
      <h2 id="ai-switch-title" className="section-title">{s.label}</h2>
      {view.syncsFromFolder && <Banner tone="info" icon="info">{s.folderBanner}</Banner>}
      {view.enabled && !view.deploymentEnabled && <Banner tone="warning" icon="alert">{s.deploymentOff}</Banner>}
      <label className="toggle">
        <input type="checkbox" role="switch" checked={view.enabled} disabled={busy} aria-describedby="ai-switch-hint"
          onChange={(e) => (e.target.checked ? apply(true) : setConfirming(true))} />
        <span className="toggle-track" aria-hidden="true"><span className="toggle-thumb" /></span>
        <span>{view.enabled ? s.on : s.off}</span>
      </label>
      <p id="ai-switch-hint" className="muted small">{s.hint}</p>
      <dl className="kv-grid">
        <div className="kv-item"><dt>{s.available}</dt><dd><Badge tone={view.available ? 'success' : 'neutral'}>{view.available ? s.on : s.off}</Badge></dd></div>
        <div className="kv-item"><dt>{s.seed}</dt><dd>{view.seedEnabled ? s.on : s.off}</dd></div>
        <div className="kv-item"><dt>{s.revision}</dt><dd className="mono">{view.revision}</dd></div>
        <div className="kv-item"><dt>{s.updated}</dt><dd>{view.updatedUtc ? formatDateTime(view.updatedUtc) : s.never}</dd></div>
      </dl>
      <p className="muted small">{s.auditNote} {auditLink}</p>
      <ConfirmModal open={confirming} title={s.confirmOffTitle} message={s.confirmOffBody} confirmLabel={s.confirmOff} onCancel={() => setConfirming(false)} onConfirm={() => apply(false)} />
    </section>
  );
}

export default AiSwitchPanel;
