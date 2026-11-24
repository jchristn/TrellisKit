import { installFatalErrorRecorder } from './fatalError';

export { UpdatesController, isCriticalManifest, criticalMessageFor, normalizeIntervalMinutes, DefaultCheckIntervalMinutes, MaxCriticalMessageLength } from './controller';
export { NewVersionHint, useNewVersionHint, DismissedVersionKey } from './NewVersionHint';
export { getInstallId, InstallIdKey } from './installId';
export { applyUpdateRequestHeaders, resolveBuildChannel, updateRequestHeaders, BuildChannelKey } from './requestHeaders';
export { UpdatesProvider, useUpdates } from './UpdatesProvider';
export { UpdateRequiredGate, UpdateRequiredScreen } from './UpdateRequiredGate';
export * from './minimumVersion';
export { getCurrentUpdateId, getUpdatesModule, getUpdatesSettings } from './runtime';
export { createEventStore, EmbeddedUpdateId, MaxEventsPerBatch, sendUpdateEvents, UpdateErrorCode, UpdateEventReporter, UpdateEventType, updateErrorCode } from './events';
export { createFatalErrorStore, fatalErrorStore, fatalRecordOf, installFatalErrorRecorder, MaxFailureMessageLength, MaxStackTraceLength } from './fatalError';

// Record the last fatal JavaScript error as early as the updates module loads (the root layout
// imports it), so a launch that crashes leaves its stack for the next launch's LaunchFailed event.
installFatalErrorRecorder();
