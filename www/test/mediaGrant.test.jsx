import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import useMediaGrant from '../src/hooks/useMediaGrant.js';
import { refreshMediaGrant } from '../src/utils/mediaGrant.js';

afterEach(() => {
  window.localStorage.clear();
});

const later = (minutes) => new Date(Date.now() + minutes * 60 * 1000).toISOString();

describe('useMediaGrant', () => {
  it('reuses a still-valid grant of the same account across page loads instead of asking again', async () => {
    const client = { getMediaGrant: vi.fn(async () => ({ expiresUtc: later(360) })) };
    const first = renderHook(() => useMediaGrant(client, { cacheKey: 'usr_1' }));
    await waitFor(() => expect(client.getMediaGrant).toHaveBeenCalledTimes(1));
    first.unmount();
    // A reload (new mount) of the same account does not spend another grant.
    const second = renderHook(() => useMediaGrant(client, { cacheKey: 'usr_1' }));
    await new Promise((r) => setTimeout(r, 20));
    expect(client.getMediaGrant).toHaveBeenCalledTimes(1);
    // A 403 on media still renews at once.
    await expect(refreshMediaGrant()).resolves.toBe(true);
    expect(client.getMediaGrant).toHaveBeenCalledTimes(2);
    second.unmount();
  });

  it('asks again for another account, without a cache key, or when the stored grant is close to expiry', async () => {
    const client = { getMediaGrant: vi.fn(async () => ({ expiresUtc: later(5) })) };
    const a = renderHook(() => useMediaGrant(client, { cacheKey: 'usr_1' }));
    await waitFor(() => expect(client.getMediaGrant).toHaveBeenCalledTimes(1));
    a.unmount();
    const b = renderHook(() => useMediaGrant(client, { cacheKey: 'usr_1' }));
    await waitFor(() => expect(client.getMediaGrant).toHaveBeenCalledTimes(2));
    b.unmount();
    const c = renderHook(() => useMediaGrant(client, { cacheKey: 'usr_2' }));
    await waitFor(() => expect(client.getMediaGrant).toHaveBeenCalledTimes(3));
    c.unmount();
    const d = renderHook(() => useMediaGrant(client));
    await waitFor(() => expect(client.getMediaGrant).toHaveBeenCalledTimes(4));
    d.unmount();
  });
});
