import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import useSignupMetric from '../src/hooks/useSignupMetric.js';

describe('signup product metrics', () => {
  it('waits for runtime opt-in and sends one closed payload, never form data', async () => {
    const client = { post: vi.fn().mockResolvedValue(null) };
    const { rerender } = renderHook(({ loaded, consent }) => useSignupMetric(client, { productEventsEnabled: consent }, loaded, false), { initialProps: { loaded: false, consent: true } });
    await act(async () => {});
    expect(client.post).not.toHaveBeenCalled();
    rerender({ loaded: true, consent: false });
    expect(client.post).not.toHaveBeenCalled();
    rerender({ loaded: true, consent: true });
    await waitFor(() => expect(client.post).toHaveBeenCalledTimes(1));
    expect(client.post.mock.calls[0]).toEqual(['/metrics/events', { name: 'signup.started', dimensions: { source: 'web' } }, { retryOnUnauthorized: false }]);
    rerender({ loaded: true, consent: true });
    expect(client.post).toHaveBeenCalledTimes(1);
  });

  it('does not emit for signed-in members and tolerates ingestion failures', async () => {
    const client = { post: vi.fn().mockRejectedValue(new Error('offline')) };
    const { rerender } = renderHook(({ signedIn }) => useSignupMetric(client, { productEventsEnabled: true }, true, signedIn), { initialProps: { signedIn: true } });
    await act(async () => {});
    expect(client.post).not.toHaveBeenCalled();
    rerender({ signedIn: false });
    await waitFor(() => expect(client.post).toHaveBeenCalledTimes(1));
  });
});
