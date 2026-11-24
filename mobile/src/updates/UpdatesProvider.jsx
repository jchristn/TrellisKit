import { createContext, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { Alert, AppState } from 'react-native';
import { useProperty } from '../property';
import strings from '../strings';
import { UpdatesController } from './controller';
import { UpdateEventReporter, UpdateEventType } from './events';
import { applyUpdateRequestHeaders } from './requestHeaders';
import { getUpdatesModule, getUpdatesSettings } from './runtime';

const UpdatesContext = createContext(null);

/** The check interval: the runtime property setting, then the build setting. */
function intervalFrom(property, settings) {
  const runtime = property && property.updates && property.updates.checkIntervalMinutes;
  return runtime || settings.checkIntervalMinutes;
}

/**
 * Create the app's controller: the real expo-updates module, enabled only in release builds that
 * were built with updates on (a code signing certificate present). Before its first check it sets
 * the runtime request headers (channel, install id; src/updates/requestHeaders.js).
 */
function createDefaultController(property) {
  const settings = getUpdatesSettings();
  const isDev = typeof __DEV__ !== 'undefined' && __DEV__ && !process.env.JEST_WORKER_ID;
  const updates = getUpdatesModule();
  return new UpdatesController({
    updates,
    enabled: settings.enabled && !isDev,
    intervalMinutes: intervalFrom(property, settings),
    prepare: () => applyUpdateRequestHeaders({ updates })
  });
}

/**
 * Runs live updates for the whole app (docs/TRELLISKIT.md section 8.1): a check on launch and on
 * every return to the foreground (the controller enforces the interval), background downloads
 * that apply on the next launch, and the "Restart to update" prompt for critical updates (with the
 * manifest's `extra.criticalMessage` when it has one). Adoption events (downloaded, applied,
 * launch failures, check and download failures, the critical prompt) are reported in the
 * background with `POST /api/v1/updates/events` (src/updates/events.js), only when checks run.
 * @param {object} props
 * @param {UpdatesController} [props.controller] Injected controller (tests).
 * @param {UpdateEventReporter} [props.reporter] Injected event reporter (tests).
 * @param {object|null} [props.updatesModule] The expo-updates module the reporter reads launch state from.
 */
export function UpdatesProvider({ children, controller: injected, reporter: injectedReporter, updatesModule }) {
  const property = useProperty();
  const [controller] = useState(() => injected || createDefaultController(property));
  const [reporter] = useState(() => (controller.enabled ? injectedReporter || new UpdateEventReporter() : null));
  const [state, setState] = useState(controller.state);
  const prompted = useRef(new Set());

  useEffect(() => controller.subscribe(setState), [controller]);

  useEffect(() => {
    if (!reporter) return undefined;
    const unsubscribe = controller.subscribeEvents((event) => reporter.handleControllerEvent(event));
    // In the background: launch reconciliation and the first batch never delay startup.
    reporter.onLaunch(updatesModule !== undefined ? updatesModule : controller.updates);
    const sub = AppState.addEventListener('change', (next) => {
      if (next === 'active') reporter.onForeground();
    });
    return () => {
      unsubscribe();
      sub.remove();
      reporter.dispose();
    };
  }, [controller, reporter, updatesModule]);

  useEffect(() => {
    controller.setIntervalMinutes(intervalFrom(property, getUpdatesSettings()));
  }, [controller, property]);

  useEffect(() => {
    if (!controller.enabled) return undefined;
    controller.check({ reason: 'launch' });
    const sub = AppState.addEventListener('change', (next) => {
      if (next === 'active') controller.check({ reason: 'foreground' });
    });
    return () => sub.remove();
  }, [controller]);

  useEffect(() => {
    if (state.status !== 'ready' || !state.critical || !state.pendingUpdateId) return;
    if (prompted.current.has(state.pendingUpdateId)) return;
    prompted.current.add(state.pendingUpdateId);
    const updateId = state.pendingUpdateId;
    if (reporter) reporter.record(UpdateEventType.CriticalPromptShown, { updateId });
    Alert.alert(strings.updates.readyTitle, state.criticalMessage || strings.updates.readyMessage, [
      { text: strings.updates.later, style: 'cancel' },
      {
        text: strings.updates.restart,
        onPress: () => {
          // Stored before the reload so the next launch sends it.
          const saved = reporter ? reporter.record(UpdateEventType.CriticalRestarted, { updateId }) : Promise.resolve();
          saved.then(() => controller.restart());
        }
      }
    ]);
  }, [controller, reporter, state]);

  const value = useMemo(() => ({ controller, state }), [controller, state]);
  return <UpdatesContext.Provider value={value}>{children}</UpdatesContext.Provider>;
}

/** The updates controller and its state ({ controller: null } outside the provider). */
export function useUpdates() {
  return useContext(UpdatesContext) || { controller: null, state: null };
}
