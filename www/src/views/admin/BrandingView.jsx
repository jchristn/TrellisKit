import { useCallback } from 'react';
import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import PageHeader from '../../components/PageHeader.jsx';
import BrandingEditor from '../../components/branding/BrandingEditor.jsx';

const route = strings.admin.routes.branding;

/**
 * /admin/branding (docs/TRELLISKIT.md section 2.1): the property's brand palette over
 * `GET`/`PUT /api/v1/admin/property/branding`, `POST .../revert`, and the versions list with restore. A save refreshes the console's
 * copy of `GET /api/v1/property`, so the console itself switches to the new colors at once.
 */
function BrandingView() {
  const { api } = useAdminAuth();
  const { setRuntimeProperty } = useProperty();
  const load = useCallback((signal) => api.getPropertyBranding(signal), [api]);
  const save = useCallback((body) => api.updatePropertyBranding(body), [api]);
  const revert = useCallback((to) => api.revertPropertyBranding(to), [api]);
  const listVersions = useCallback((query, signal) => api.enumeratePropertyBrandingVersions(query, signal), [api]);
  const restoreVersion = useCallback((version) => api.restorePropertyBrandingVersion(version), [api]);
  const saveMemberPresets = useCallback((body) => api.updatePropertyMemberPresets(body), [api]);
  const onSaved = useCallback(() => {
    api.client.property().then((body) => { if (body && typeof body === 'object') setRuntimeProperty(body); }, () => {});
  }, [api, setRuntimeProperty]);
  return (
    <div className="view branding-view">
      <PageHeader title={route.label} summary={route.summary} />
      <BrandingEditor load={load} save={save} revert={revert} listVersions={listVersions} restoreVersion={restoreVersion} saveMemberPresets={saveMemberPresets} onSaved={onSaved} auditLink={<Link to="/admin/audit">{strings.admin.branding.history}</Link>} />
    </div>
  );
}

export default BrandingView;
