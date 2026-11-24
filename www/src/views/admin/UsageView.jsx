import { useMemo } from 'react';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { adminUsageSource } from '../../utils/usageApi.js';
import PageHeader from '../../components/PageHeader.jsx';
import UsageDashboard from '../../components/usage/UsageDashboard.jsx';

const route = strings.admin.routes.usage;

/** Admin, System, Usage: this property's usage over time with exports (docs/USAGE_METERING_CONTRACT.md section 8). */
function UsageView() {
  const { api } = useAdminAuth();
  const source = useMemo(() => adminUsageSource(api), [api]);
  return (
    <div className="view">
      <PageHeader title={route.label} summary={route.summary} />
      <UsageDashboard load={source.load} exportFile={source.exportFile} />
    </div>
  );
}

export default UsageView;
