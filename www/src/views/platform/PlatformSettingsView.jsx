import { collectEnumeration } from '@trelliskit/shared';
import { useMemo } from 'react';
import strings from '../../strings.js';
import { usePlatformAuth } from '../../context/PlatformAuthContext.jsx';
import { SettingsPanel } from '../admin/SettingsView.jsx';

const route = strings.platform.routes.settings;

/** The generated settings page for the deployment's settings, on GET/PUT /platform/settings. */
function PlatformSettingsView() {
  const { call, client } = usePlatformAuth();
  const api = useMemo(() => ({
    getHealth: (signal) => client.health(signal),
    getSettings: (signal) => call(async (c) => { const page = await collectEnumeration((q, sig) => c.getPlatformSettings(q, sig), {}, { signal }); return { ...page, fields: page.objects }; }),
    updateSettings: (values) => call((c) => c.updatePlatformSettings(values)),
    restartServer: () => call((c) => c.restartPlatformServer())
  }), [call, client]);
  return <SettingsPanel api={api} title={route.label} summary={route.summary} authText={strings.platform.settings.authValue} />;
}

export default PlatformSettingsView;
