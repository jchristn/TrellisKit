import { useCallback, useState } from 'react';
import { Link } from 'react-router-dom';
import { DmPolicy, PushPreview } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';

const s = strings.messaging.settings;

/** Setting key for "message pushes ignore notification hours" (resolved decision 12). */
export const EXEMPT_HOURS_KEY = 'exemptFromNotificationHours';

/**
 * /app/settings/messaging: who can message the member (DM policy), read receipts and typing
 * indicators (both reciprocal), the push preview, and whether message pushes ignore the
 * notification hours. Each change saves immediately with `PUT /me/messaging/settings`.
 */
function MessagingSettingsView() {
  const { client } = useAuth();
  const toast = useToast();
  useDocumentMeta(s.meta, s.summary);
  const fetcher = useCallback((signal) => client.messaging.getMessagingSettings(signal), [client]);
  const query = useApiQuery(fetcher);
  const [local, setLocal] = useState(null);
  const [busy, setBusy] = useState(false);
  const settings = local || query.data;

  const save = async (patch) => {
    const before = settings;
    setLocal({ ...settings, ...patch });
    setBusy(true);
    try {
      const saved = await client.messaging.updateMessagingSettings(patch);
      setLocal({ ...before, ...patch, ...(saved || {}), ...patch });
      toast.success(s.saved);
    } catch {
      setLocal(before);
      toast.error(s.saveError);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="member-view settings-view">
      <Link to="/app/profile" className="back-link small"><Icon name="chevronLeft" size={14} /><span>{s.back}</span></Link>
      <header className="page-header">
        <div className="page-header-text">
          <h1>{s.title}</h1>
          <p className="page-summary">{s.summary}</p>
        </div>
      </header>
      {query.loading && !settings && <Loading />}
      <ErrorBanner message={query.error && !settings ? s.loadError : ''} onRetry={query.reload} />
      {settings && (
        <>
          <section className="panel" aria-labelledby="dm-policy-title">
            <h2 id="dm-policy-title">{s.dmPolicyTitle}</h2>
            <fieldset className="radio-list" disabled={busy}>
              <legend className="visually-hidden">{s.dmPolicyTitle}</legend>
              {[DmPolicy.Everyone, DmPolicy.Known, DmPolicy.Nobody].map((policy) => (
                <label key={policy} className="radio-row">
                  <input type="radio" name="dmPolicy" value={policy} checked={settings.dmPolicy === policy} onChange={() => save({ dmPolicy: policy })} />
                  <span>
                    <span className="radio-label">{s.dmPolicy[policy].label}</span>
                    <span className="muted small">{s.dmPolicy[policy].description}</span>
                  </span>
                </label>
              ))}
            </fieldset>
          </section>
          <section className="panel" aria-labelledby="dm-privacy-title">
            <h2 id="dm-privacy-title">{s.privacyTitle}</h2>
            <label className="toggle-row">
              <input type="checkbox" checked={settings.readReceipts !== false} disabled={busy} onChange={(e) => save({ readReceipts: e.target.checked })} />
              <span><span className="radio-label">{s.readReceipts}</span><span className="muted small">{s.readReceiptsBody}</span></span>
            </label>
            <label className="toggle-row">
              <input type="checkbox" checked={settings.typingIndicators !== false} disabled={busy} onChange={(e) => save({ typingIndicators: e.target.checked })} />
              <span><span className="radio-label">{s.typing}</span><span className="muted small">{s.typingBody}</span></span>
            </label>
          </section>
          <section className="panel" aria-labelledby="dm-notify-title">
            <h2 id="dm-notify-title">{s.notificationsTitle}</h2>
            <div className="field">
              <label className="label-text" htmlFor="dm-push-preview">{s.pushPreview}</label>
              <select id="dm-push-preview" className="select" value={settings.pushPreview || PushPreview.SenderOnly} disabled={busy} onChange={(e) => save({ pushPreview: e.target.value })}>
                {[PushPreview.SenderOnly, PushPreview.Full, PushPreview.Hidden].map((v) => <option key={v} value={v}>{s.pushPreviewOptions[v]}</option>)}
              </select>
              <span className="muted small">{s.pushPreviewBody}</span>
            </div>
            <label className="toggle-row">
              <input type="checkbox" checked={Boolean(settings[EXEMPT_HOURS_KEY])} disabled={busy} onChange={(e) => save({ [EXEMPT_HOURS_KEY]: e.target.checked })} />
              <span><span className="radio-label">{s.exemptHours}</span><span className="muted small">{s.exemptHoursBody}</span></span>
            </label>
          </section>
        </>
      )}
    </div>
  );
}

export default MessagingSettingsView;
