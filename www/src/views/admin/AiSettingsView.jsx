import { useCallback } from 'react';
import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import PageHeader from '../../components/PageHeader.jsx';
import AiSwitchPanel from '../../components/branding/AiSwitchPanel.jsx';

const route = strings.admin.routes['ai-settings'];

/**
 * /admin/ai-settings (docs/TRELLISKIT.md section 9.5): switch AI on or off for the property over
 * `GET`/`PUT /api/v1/admin/property/ai`. A change refreshes the console's copy of
 * `GET /api/v1/property`, so the AI sections appear or disappear at once.
 */
function AiSettingsView() {
  const { api } = useAdminAuth();
  const { setRuntimeProperty } = useProperty();
  const load = useCallback((signal) => api.getPropertyAi(signal), [api]);
  const save = useCallback((enabled) => api.updatePropertyAi(enabled), [api]);
  const onSaved = useCallback(() => {
    api.client.property().then((body) => { if (body && typeof body === 'object') setRuntimeProperty(body); }, () => {});
  }, [api, setRuntimeProperty]);
  return (
    <div className="view">
      <PageHeader title={route.label} summary={route.summary} />
      <AiSwitchPanel load={load} save={save} onSaved={onSaved} auditLink={<Link to="/admin/audit">{strings.admin.branding.history}</Link>} />
    </div>
  );
}

export default AiSettingsView;
