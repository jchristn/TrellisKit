import { Navigate, Route, Routes } from 'react-router-dom';
import strings from './strings.js';
import { PlatformAuthProvider, usePlatformAuth } from './context/PlatformAuthContext.jsx';
import PlatformLayout from './layouts/PlatformLayout.jsx';
import PlatformLoginView from './views/platform/PlatformLoginView.jsx';
import PlatformUnavailableView from './views/platform/PlatformUnavailableView.jsx';
import PropertiesView from './views/platform/PropertiesView.jsx';
import PropertyCreateView from './views/platform/PropertyCreateView.jsx';
import PropertyDetailView from './views/platform/PropertyDetailView.jsx';
import PlatformAdminsView from './views/platform/PlatformAdminsView.jsx';
import PlatformAuditView from './views/platform/PlatformAuditView.jsx';
import PlatformSettingsView from './views/platform/PlatformSettingsView.jsx';
import PropertyMetricsView from './views/platform/PropertyMetricsView.jsx';
import PlatformHealthView from './views/platform/PlatformHealthView.jsx';
import UsageOverviewView from './views/platform/UsageOverviewView.jsx';
import PlatformRequestsView from './views/platform/PlatformRequestsView.jsx';
import { Loading } from './components/States.jsx';

/** Shows the console only once the backend confirms this host serves platform routes. */
function PlatformGate() {
  const { status } = usePlatformAuth();
  if (status === 'checking') return <div className="center-page"><Loading label={strings.platform.unavailable.checking} /></div>;
  if (status === 'unavailable' || status === 'error') return <PlatformUnavailableView />;
  if (status === 'signedOut') return <PlatformLoginView />;
  return (
    <Routes>
      <Route element={<PlatformLayout />}>
        <Route index element={<Navigate to="properties" replace />} />
        <Route path="properties" element={<PropertiesView />} />
        <Route path="properties/new" element={<PropertyCreateView />} />
        <Route path="properties/:id" element={<PropertyDetailView />} />
        <Route path="health" element={<PlatformHealthView />} />
        <Route path="metrics" element={<PropertyMetricsView />} />
        <Route path="usage" element={<UsageOverviewView />} />
        <Route path="requests" element={<PlatformRequestsView />} />
        <Route path="admins" element={<PlatformAdminsView />} />
        <Route path="audit" element={<PlatformAuditView />} />
        <Route path="settings" element={<PlatformSettingsView />} />
        <Route path="*" element={<Navigate to="properties" replace />} />
      </Route>
    </Routes>
  );
}

/**
 * The TrellisKit platform console, mounted at /platform/* (docs/MULTIPROPERTY_CONTRACT.md section
 * 6.5). It exists only in MultiProperty deployments on a platform host, which the backend decides;
 * everywhere else the route explains that and makes no further calls. Loaded lazily.
 */
function PlatformApp({ fetchImpl = null }) {
  return (
    <PlatformAuthProvider fetchImpl={fetchImpl}>
      <PlatformGate />
    </PlatformAuthProvider>
  );
}

export default PlatformApp;
