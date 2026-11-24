import { describe, expect, it } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import { AuthProvider, SESSION_HINT_KEY, useAuth } from '../src/context/AuthContext.jsx';
import { authResponse, createFetch, json, makeMe } from './helpers.jsx';

let captured = null;
function Probe() {
  captured = useAuth();
  return <div data-testid="status">{captured.status}{captured.me ? ':' + captured.me.displayName : ''}</div>;
}

function renderAuth(fetchImpl, props = {}) {
  return render(<AuthProvider fetchImpl={fetchImpl} {...props}><Probe /></AuthProvider>);
}

describe('AuthProvider', () => {
  it('restores the session on load with POST /auth/refresh (cookie, no body, web client headers)', async () => {
    window.localStorage.setItem(SESSION_HINT_KEY, '1');
    const { fetchImpl, calls } = createFetch((call) => (call.path === '/api/v1/auth/refresh' ? json(authResponse(makeMe())) : undefined));
    renderAuth(fetchImpl);
    expect(screen.getByTestId('status')).toHaveTextContent('restoring');
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedIn:Sam Rivera'));
    const refresh = calls.filter((c) => c.path === '/api/v1/auth/refresh');
    expect(refresh).toHaveLength(1);
    expect(refresh[0].method).toBe('POST');
    expect(refresh[0].body).toBeUndefined();
    expect(refresh[0].headers['X-Client']).toBe('web');
    expect(refresh[0].headers['X-Client-Version']).toMatch(/^\d+\.\d+\.\d+/);
  });

  it('skips the restore call for anonymous visitors without a session hint', async () => {
    const { fetchImpl, calls } = createFetch(() => undefined);
    renderAuth(fetchImpl);
    expect(screen.getByTestId('status')).toHaveTextContent('signedOut');
    expect(calls).toHaveLength(0);
  });

  it('skips the restore call on the admin console and member app paths without a session hint', async () => {
    const previous = window.location.pathname;
    for (const path of ['/admin', '/admin/login', '/app/feed']) {
      window.localStorage.removeItem(SESSION_HINT_KEY);
      window.history.replaceState(null, '', path);
      const { fetchImpl, calls } = createFetch(() => json({ error: 'InvalidRefreshToken', message: 'Sign in again.' }, 401));
      const view = renderAuth(fetchImpl);
      expect(screen.getByTestId('status')).toHaveTextContent('signedOut');
      expect(calls.filter((c) => c.path === '/api/v1/auth/refresh')).toHaveLength(0);
      view.unmount();
    }
    window.history.replaceState(null, '', previous);
  });

  it('ends the session and clears the hint when restore is rejected', async () => {
    window.localStorage.setItem(SESSION_HINT_KEY, '1');
    const { fetchImpl } = createFetch(() => json({ error: 'InvalidRefreshToken', message: 'Sign in again.' }, 401));
    renderAuth(fetchImpl);
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedOut'));
    expect(window.localStorage.getItem(SESSION_HINT_KEY)).toBeNull();
  });

  it('refreshes once on 401 and retries the request with the new token', async () => {
    let refreshCount = 0;
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/auth/login') return json(authResponse(makeMe(), { accessToken: 'old' }));
      if (call.path === '/api/v1/auth/refresh') {
        refreshCount += 1;
        return json(authResponse(makeMe(), { accessToken: 'new' }));
      }
      if (call.path === '/api/v1/membership') {
        return call.headers.Authorization === 'Bearer new' ? json({ membership: { isActive: true } }) : json({ error: 'Unauthorized', message: 'Expired.' }, 401);
      }
      return undefined;
    });
    renderAuth(fetchImpl, { restoreOnLoad: false });
    await act(async () => { await captured.signIn('member@example.com', 'correct horse battery'); });
    const result = await captured.api.getMembership();
    expect(result.membership.isActive).toBe(true);
    expect(refreshCount).toBe(1);
    const membershipCalls = calls.filter((c) => c.path === '/api/v1/membership');
    expect(membershipCalls.map((c) => c.headers.Authorization)).toEqual(['Bearer old', 'Bearer new']);
  });

  it('shares one in-flight refresh across concurrent 401s', async () => {
    let refreshCount = 0;
    let releaseRefresh;
    const { fetchImpl } = createFetch(async (call) => {
      if (call.path === '/api/v1/auth/login') return json(authResponse(makeMe(), { accessToken: 'old' }));
      if (call.path === '/api/v1/auth/refresh') {
        refreshCount += 1;
        await new Promise((resolve) => { releaseRefresh = resolve; });
        return json(authResponse(makeMe(), { accessToken: 'new' }));
      }
      if (call.headers.Authorization === 'Bearer new') return json({ ok: call.path });
      return json({ error: 'Unauthorized', message: 'Expired.' }, 401);
    });
    renderAuth(fetchImpl, { restoreOnLoad: false });
    await act(async () => { await captured.signIn('member@example.com', 'correct horse battery'); });
    let results;
    await act(async () => {
      const pending = Promise.all([captured.api.getMembership(), captured.api.getMe(), captured.client.get('/handles/x/availability')]);
      await waitFor(() => expect(releaseRefresh).toBeTypeOf('function'));
      await new Promise((r) => setTimeout(r, 20));
      releaseRefresh();
      results = await pending;
    });
    expect(refreshCount).toBe(1);
    expect(results.map((r) => r.ok)).toEqual(['/api/v1/membership', '/api/v1/auth/me', '/api/v1/handles/x/availability']);
  });

  it('signs out when the refresh after a 401 fails', async () => {
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/auth/login') return json(authResponse(makeMe()));
      if (call.path === '/api/v1/auth/refresh') return json({ error: 'InvalidRefreshToken', message: 'Reused.' }, 401);
      return json({ error: 'Unauthorized', message: 'Expired.' }, 401);
    });
    renderAuth(fetchImpl, { restoreOnLoad: false });
    await act(async () => { await captured.signIn('member@example.com', 'correct horse battery'); });
    expect(screen.getByTestId('status')).toHaveTextContent('signedIn');
    await act(async () => { await expect(captured.api.getMembership()).rejects.toMatchObject({ status: 401 }); });
    expect(screen.getByTestId('status')).toHaveTextContent('signedOut');
  });

  it('does not refresh for anonymous auth routes or credential errors', async () => {
    let refreshCount = 0;
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/auth/refresh') {
        refreshCount += 1;
        return json(authResponse(makeMe()));
      }
      if (call.path === '/api/v1/auth/login' && call.body.password === 'good password!') return json(authResponse(makeMe()));
      if (call.path === '/api/v1/auth/login') return json({ error: 'InvalidCredentials', message: 'Nope.' }, 401);
      if (call.path === '/api/v1/me/password') return json({ error: 'InvalidCredentials', message: 'Wrong.' }, 401);
      return undefined;
    });
    renderAuth(fetchImpl, { restoreOnLoad: false });
    await act(async () => { await expect(captured.signIn('member@example.com', 'bad')).rejects.toMatchObject({ code: 'InvalidCredentials' }); });
    await act(async () => { await captured.signIn('member@example.com', 'good password!'); });
    await expect(captured.api.setPassword({ currentPassword: 'x', newPassword: 'y'.repeat(12) })).rejects.toMatchObject({ code: 'InvalidCredentials' });
    expect(refreshCount).toBe(0);
  });

  it('signOut posts /auth/logout and clears local state even if the call fails', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/auth/login') return json(authResponse(makeMe()));
      if (call.path === '/api/v1/auth/logout') return json({ error: 'Boom', message: 'x' }, 500);
      return undefined;
    });
    renderAuth(fetchImpl, { restoreOnLoad: false });
    await act(async () => { await captured.signIn('member@example.com', 'correct horse battery'); });
    expect(window.localStorage.getItem(SESSION_HINT_KEY)).toBe('1');
    await act(async () => { await captured.signOut(); });
    expect(calls.some((c) => c.path === '/api/v1/auth/logout' && c.method === 'POST')).toBe(true);
    expect(screen.getByTestId('status')).toHaveTextContent('signedOut');
    expect(window.localStorage.getItem(SESSION_HINT_KEY)).toBeNull();
  });
});
