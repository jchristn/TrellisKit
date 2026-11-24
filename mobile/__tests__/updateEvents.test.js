/**
 * Live update adoption events (docs/LIVE_UPDATES_CONTRACT.md section 9.2): what the app reports,
 * batching, device rate limits, offline handling, and the provider wiring.
 */
import { act, render, screen, waitFor } from '@testing-library/react-native';
import { Text } from 'react-native';
import { ApiError } from '@trelliskit/shared';
import { PropertyProvider } from '../src/property';
import {
  createEventStore,
  MaxEventsPerBatch,
  sendUpdateEvents,
  updateErrorCode,
  UpdateEventReporter,
  UpdatesController,
  UpdatesProvider
} from '../src/updates';
import { resetInstallIdCache } from '../src/updates/installId';
import { installFakeServer, resetEnvironment } from '../test-utils/fakeServer';

const U1 = '6f0b6c1e-1111-4a2b-8c3d-000000000001';
const U2 = '6f0b6c1e-2222-4a2b-8c3d-000000000002';

function memoryStore() {
  return createEventStore(() => null);
}

function makeReporter(overrides = {}) {
  let clock = Date.parse('2026-12-01T17:00:00Z');
  const sent = [];
  const send = jest.fn(async (events, installId) => {
    sent.push({ events, installId });
    return { accepted: events.length, dropped: 0 };
  });
  const reporter = new UpdateEventReporter({
    send,
    installId: async () => 'install-1',
    store: memoryStore(),
    now: () => clock,
    flushDelayMs: 60 * 60 * 1000,
    launchFlushDelayMs: 60 * 60 * 1000,
    stableMs: 60 * 60 * 1000,
    ...overrides
  });
  return { reporter, send, sent, advance: (ms) => { clock += ms; }, now: () => clock };
}

beforeEach(() => {
  resetEnvironment();
  resetInstallIdCache();
});

describe('error codes', () => {
  test('map expo-updates failures to the bounded codes', () => {
    expect(updateErrorCode(new Error('The request timed out.'))).toBe('Timeout');
    expect(updateErrorCode(new Error('Code signing: signature verification failed'))).toBe('SignatureInvalid');
    expect(updateErrorCode(new Error('Asset hash mismatch'))).toBe('HashMismatch');
    expect(updateErrorCode(new Error('The Internet connection appears to be offline.'))).toBe('Network');
    expect(updateErrorCode(new Error('No space left on device'))).toBe('Storage');
    expect(updateErrorCode(new Error('Server responded with status code 503'))).toBe('Server');
    expect(updateErrorCode(new Error('Something odd'))).toBe('Unknown');
    expect(updateErrorCode(null)).toBe('Unknown');
  });
});

describe('UpdateEventReporter', () => {
  test('reports Downloaded with the install id, never with a token', async () => {
    const { reporter, sent } = makeReporter();
    await reporter.noteDownloaded({ updateId: U1 });
    expect(await reporter.flush()).toBe(1);
    expect(sent).toHaveLength(1);
    expect(sent[0].installId).toBe('install-1');
    expect(sent[0].events).toEqual([{ type: 'Downloaded', updateId: U1, occurredUtc: '2026-12-01T17:00:00.000Z' }]);
    expect((await reporter.snapshot()).queue).toEqual([]);
    reporter.dispose();
  });

  test('the default sender posts to /updates/events with X-TK-Install-Id and no Authorization', async () => {
    const server = installFakeServer({ 'POST /updates/events': { status: 202, body: { accepted: 1, dropped: 0 } } });
    await sendUpdateEvents([{ type: 'Applied', updateId: U1, occurredUtc: '2026-12-01T17:00:00Z' }], 'install-9');
    const calls = server.called('POST /updates/events');
    expect(calls).toHaveLength(1);
    expect(calls[0].headers['X-TK-Install-Id']).toBe('install-9');
    expect(calls[0].headers['X-TK-Property']).toBeTruthy();
    expect(calls[0].headers.Authorization).toBeUndefined();
    expect(calls[0].body).toEqual({ events: [{ type: 'Applied', updateId: U1, occurredUtc: '2026-12-01T17:00:00Z' }] });
  });

  test('maps controller failures to CheckFailed and DownloadFailed', async () => {
    const { reporter, sent } = makeReporter();
    reporter.handleControllerEvent({ type: 'failure', stage: 'check', message: 'Network request failed' });
    reporter.handleControllerEvent({ type: 'failure', stage: 'download', updateId: U2, message: 'hash mismatch' });
    reporter.handleControllerEvent({ type: 'failure', stage: 'prepare', message: 'rejected' });
    await reporter.flush();
    expect(sent[0].events.map((e) => [e.type, e.errorCode, e.updateId])).toEqual([
      ['CheckFailed', 'Network', undefined],
      ['DownloadFailed', 'HashMismatch', U2]
    ]);
    reporter.dispose();
  });

  test('a launch of the downloaded update is Applied once, after it stays up', async () => {
    jest.useFakeTimers();
    try {
      const { reporter, sent } = makeReporter({ stableMs: 1000 });
      await reporter.noteDownloaded({ updateId: U1 });
      await reporter.onLaunch({ isEnabled: true, isEmbeddedLaunch: false, updateId: U1, launchDuration: 840 });
      expect((await reporter.snapshot()).queue.map((e) => e.type)).toEqual(['Downloaded']);
      await act(async () => { jest.advanceTimersByTime(1000); });
      await reporter.snapshot();
      await reporter.flush();
      expect(sent[0].events.map((e) => e.type)).toEqual(['Downloaded', 'Applied']);
      expect(sent[0].events[1]).toMatchObject({ updateId: U1, launchDurationMs: 840 });
      const state = await reporter.snapshot();
      expect(state.lastApplied).toBe(U1);
      expect(state.pending).toBeNull();
      // A later launch of the same update reports nothing new.
      await reporter.onLaunch({ isEnabled: true, isEmbeddedLaunch: false, updateId: U1 });
      await act(async () => { jest.advanceTimersByTime(1000); });
      expect((await reporter.snapshot()).queue).toEqual([]);
      reporter.dispose();
    } finally {
      jest.useRealTimers();
    }
  });

  test('a launch without the downloaded update reports LaunchFailed', async () => {
    const { reporter, sent } = makeReporter();
    await reporter.noteDownloaded({ updateId: U2 });
    // The library rolled back to the previous update after U2 failed to launch.
    await reporter.onLaunch({ isEnabled: true, isEmbeddedLaunch: false, updateId: U1 });
    await reporter.flush();
    expect(sent[0].events.map((e) => [e.type, e.updateId, e.errorCode])).toEqual([
      ['Downloaded', U2, undefined],
      ['LaunchFailed', U2, 'Unknown']
    ]);
    expect((await reporter.snapshot()).pending).toBeNull();
    reporter.dispose();
  });

  test('an emergency launch reports LaunchFailed and no Applied', async () => {
    const { reporter } = makeReporter({ stableMs: 0 });
    await reporter.noteDownloaded({ updateId: U2 });
    await reporter.onLaunch({ isEnabled: true, isEmbeddedLaunch: true, isEmergencyLaunch: true, updateId: null });
    const types = (await reporter.snapshot()).queue.map((e) => e.type);
    expect(types).toEqual(['Downloaded', 'LaunchFailed']);
    reporter.dispose();
  });

  test('a rollback directive that took effect reports RolledBackToEmbedded', async () => {
    const { reporter } = makeReporter();
    await reporter.noteDownloaded({ rollBack: true });
    expect((await reporter.snapshot()).queue).toEqual([]);
    await reporter.onLaunch({ isEnabled: true, isEmbeddedLaunch: true, updateId: 'any' });
    expect((await reporter.snapshot()).queue.map((e) => [e.type, e.updateId])).toEqual([['RolledBackToEmbedded', 'embedded']]);
    reporter.dispose();
  });

  test('does nothing when updates are disabled', async () => {
    const { reporter } = makeReporter();
    await reporter.onLaunch({ isEnabled: false, isEmbeddedLaunch: false, updateId: U1 });
    await reporter.onLaunch(null);
    expect((await reporter.snapshot()).queue).toEqual([]);
    reporter.dispose();
  });

  test('sends at most 20 events per request and the rest after the interval', async () => {
    const { reporter, sent, advance } = makeReporter({ minIntervalMs: 60000 });
    for (let i = 0; i < 25; i += 1) await reporter.record('CheckFailed', { errorCode: 'Network' });
    expect(await reporter.flush()).toBe(MaxEventsPerBatch);
    // Rate limited: a second flush right away sends nothing.
    expect(await reporter.flush()).toBe(0);
    advance(60000);
    expect(await reporter.flush()).toBe(5);
    expect(sent.map((s) => s.events.length)).toEqual([20, 5]);
    reporter.dispose();
  });

  test('limits sends per hour on the device', async () => {
    const { reporter, send, advance } = makeReporter({ minIntervalMs: 0, maxSendsPerHour: 2 });
    for (let i = 0; i < 3; i += 1) {
      await reporter.record('CheckFailed', { errorCode: 'Network' });
      await reporter.flush();
    }
    expect(send).toHaveBeenCalledTimes(2);
    advance(60 * 60 * 1000);
    await reporter.flush();
    expect(send).toHaveBeenCalledTimes(3);
    reporter.dispose();
  });

  test('offline: the batch stays queued silently and goes with a later send', async () => {
    const { reporter, send, advance } = makeReporter({ minIntervalMs: 1000 });
    send.mockRejectedValueOnce(new ApiError('Unable to reach the server.', { status: 0, code: 'NetworkError' }));
    await reporter.noteDownloaded({ updateId: U1 });
    await expect(reporter.flush()).resolves.toBe(0);
    expect((await reporter.snapshot()).queue).toHaveLength(1);
    advance(1000);
    expect(await reporter.flush()).toBe(1);
    expect((await reporter.snapshot()).queue).toHaveLength(0);
    reporter.dispose();
  });

  test('a 429 backs off; a 400 drops the batch; events older than 7 days are dropped', async () => {
    const { reporter, send, advance } = makeReporter({ minIntervalMs: 0 });
    send.mockRejectedValueOnce(new ApiError('Slow down.', { status: 429, code: 'RateLimited', retryAfterSeconds: 600 }));
    await reporter.record('CheckFailed', { errorCode: 'Server' });
    await reporter.flush();
    expect(await reporter.flush()).toBe(0);
    expect(send).toHaveBeenCalledTimes(1);
    advance(600 * 1000);
    send.mockRejectedValueOnce(new ApiError('Invalid.', { status: 400, code: 'ValidationFailed' }));
    await reporter.flush();
    expect((await reporter.snapshot()).queue).toEqual([]);
    await reporter.record('CheckFailed', { errorCode: 'Server' });
    advance(8 * 24 * 60 * 60 * 1000);
    expect(await reporter.flush()).toBe(0);
    expect(send).toHaveBeenCalledTimes(2);
    expect((await reporter.snapshot()).queue).toEqual([]);
    reporter.dispose();
  });

  test('a failing store or install id never throws', async () => {
    const reporter = new UpdateEventReporter({
      send: jest.fn(async () => ({})),
      installId: async () => { throw new Error('keychain'); },
      store: { load: async () => { throw new Error('disk'); }, save: async () => {} },
      flushDelayMs: 60 * 60 * 1000
    });
    await expect(reporter.record('CheckFailed', { errorCode: 'Network' })).resolves.toBeUndefined();
    await expect(reporter.flush()).resolves.toBe(0);
    await expect(reporter.onLaunch({ isEnabled: true, isEmbeddedLaunch: false, updateId: U1 })).resolves.toBeUndefined();
    reporter.dispose();
  });
});

describe('UpdatesProvider reporting', () => {
  test('reports a background download through the controller events', async () => {
    const updates = {
      isEnabled: true,
      isEmbeddedLaunch: true,
      updateId: null,
      checkForUpdateAsync: jest.fn(async () => ({ isAvailable: true, manifest: { id: U1 } })),
      fetchUpdateAsync: jest.fn(async () => ({ isNew: true, manifest: { id: U1 } })),
      reloadAsync: jest.fn(async () => {})
    };
    const controller = new UpdatesController({ updates });
    const { reporter } = makeReporter();
    await render(
      <PropertyProvider>
        <UpdatesProvider controller={controller} reporter={reporter}>
          <Text>child</Text>
        </UpdatesProvider>
      </PropertyProvider>
    );
    await waitFor(() => expect(controller.state.status).toBe('ready'));
    await waitFor(async () => expect((await reporter.snapshot()).queue.map((e) => e.type)).toEqual(['Downloaded']));
    await screen.unmount();
  });

  test('creates no reporter when checks are disabled', async () => {
    const controller = new UpdatesController({ updates: null });
    await render(
      <PropertyProvider>
        <UpdatesProvider controller={controller}>
          <Text>child</Text>
        </UpdatesProvider>
      </PropertyProvider>
    );
    expect(screen.getByText('child')).toBeTruthy();
    await screen.unmount();
  });
});
