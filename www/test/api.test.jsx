import { describe, expect, it } from 'vitest';
import { act, render, renderHook, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ApiClient, ApiError } from '@trelliskit/shared';
import { createAdminApi, getFieldErrors } from '../src/utils/api.js';
import useApiQuery from '../src/hooks/useApiQuery.js';
import { AdminAuthProvider, ADMIN_TOKEN_KEY, useAdminAuth } from '../src/context/AdminAuthContext.jsx';
import { AuthProvider } from '../src/context/AuthContext.jsx';
import { createFetch, json } from './helpers.jsx';

describe('createAdminApi', () => {
  it('calls /api/v1 routes with the bearer token from getAccessToken', async () => {
    const { fetchImpl, calls } = createFetch(() => json({ objects: [], endOfResults: true }));
    const client = new ApiClient({ baseUrl: '', getAccessToken: () => 'abc', fetchImpl });
    const api = createAdminApi(client);
    await api.getSettings();
    await api.getRequestHistory({ method: 'GET', pageNumber: 2, pageSize: 50 });
    await api.deleteRequestHistoryBulk({ statusCode: '500' });
    expect(calls[0]).toMatchObject({ method: 'POST', path: '/api/v1/settings/enumerate' });
    expect(calls[0].headers.Authorization).toBe('Bearer abc');
    expect(calls[1].url).toBe('/api/v1/request-history?method=GET&pageNumber=2&pageSize=50');
    expect(calls[2]).toMatchObject({ method: 'DELETE', url: '/api/v1/request-history?statusCode=500' });
  });

  it('fetches /openapi.json outside /api/v1 and encodes ids', async () => {
    const { fetchImpl, calls } = createFetch(() => json({ openapi: '3.0.0', paths: {} }));
    const api = createAdminApi(new ApiClient({ baseUrl: '', getAccessToken: () => 't', fetchImpl }));
    const spec = await api.getOpenApiSpec();
    await api.getRequestHistoryEntry('req_a/b');
    expect(spec.openapi).toBe('3.0.0');
    expect(calls[0].url).toBe('/openapi.json');
    expect(calls[0].headers.Authorization).toBe('Bearer t');
    expect(calls[1].url).toBe('/api/v1/request-history/req_a%2Fb');
  });

  it('invokes onUnauthorized on 401 and exposes fieldErrors', async () => {
    let unauthorized = 0;
    const { fetchImpl } = createFetch((call) => (call.method === 'PUT'
      ? json({ error: 'BadRequest', message: 'Invalid.', fieldErrors: { a: 'Too big.' } }, 400)
      : json({ error: 'Unauthorized', message: 'Nope.' }, 401)));
    const api = createAdminApi(new ApiClient({ baseUrl: '', fetchImpl }), { onUnauthorized: () => { unauthorized += 1; } });
    await expect(api.getSettings()).rejects.toBeInstanceOf(ApiError);
    expect(unauthorized).toBe(1);
    const err = await api.updateSettings({ a: 1 }).catch((e) => e);
    expect(err.status).toBe(400);
    expect(getFieldErrors(err)).toEqual({ a: 'Too big.' });
    expect(unauthorized).toBe(1);
  });
});

describe('useApiQuery', () => {
  it('reports loading, data, and errors, and reloads', async () => {
    let n = 0;
    const fetcher = async () => {
      n += 1;
      if (n === 2) throw new ApiError('Boom', { status: 500 });
      return { n };
    };
    const { result } = renderHook(() => useApiQuery(fetcher));
    expect(result.current.loading).toBe(true);
    await waitFor(() => expect(result.current.data).toEqual({ n: 1 }));
    act(() => result.current.reload());
    await waitFor(() => expect(result.current.error).toBeTruthy());
    expect(result.current.error.message).toBe('Boom');
    expect(result.current.data).toEqual({ n: 1 });
  });
});

function AuthProbe() {
  const { status, login, logout } = useAdminAuth();
  return (
    <div>
      <span data-testid="status">{status}</span>
      <button type="button" onClick={() => login('good').catch(() => {})}>good</button>
      <button type="button" onClick={() => login('bad').catch(() => {})}>bad</button>
      <button type="button" onClick={logout}>logout</button>
    </div>
  );
}

describe('AdminAuthProvider', () => {
  it('validates the token with POST /api/v1/settings/enumerate, stores it in sessionStorage, and clears it on logout', async () => {
    const { fetchImpl, calls } = createFetch((call) => (call.headers.Authorization === 'Bearer good'
      ? json({ objects: [], endOfResults: true })
      : json({ error: 'Unauthorized', message: 'Bad token.' }, 401)));
    render(<AuthProvider fetchImpl={fetchImpl} restoreOnLoad={false}><AdminAuthProvider fetchImpl={fetchImpl}><AuthProbe /></AdminAuthProvider></AuthProvider>);
    const user = userEvent.setup();
    expect(screen.getByTestId('status')).toHaveTextContent('signedOut');

    await user.click(screen.getByText('bad'));
    await waitFor(() => expect(calls).toHaveLength(1));
    expect(screen.getByTestId('status')).toHaveTextContent('signedOut');
    expect(window.sessionStorage.getItem(ADMIN_TOKEN_KEY)).toBeNull();

    await user.click(screen.getByText('good'));
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedIn'));
    expect(calls[1]).toMatchObject({ method: 'POST', path: '/api/v1/settings/enumerate' });
    expect(window.sessionStorage.getItem(ADMIN_TOKEN_KEY)).toBe('good');

    await user.click(screen.getByText('logout'));
    expect(screen.getByTestId('status')).toHaveTextContent('signedOut');
    expect(window.sessionStorage.getItem(ADMIN_TOKEN_KEY)).toBeNull();
  });

  it('restores a stored token after re-validating it, and drops it on 401', async () => {
    window.sessionStorage.setItem(ADMIN_TOKEN_KEY, 'stale');
    const { fetchImpl } = createFetch(() => json({ error: 'Unauthorized', message: 'Bad token.' }, 401));
    render(<AuthProvider fetchImpl={fetchImpl} restoreOnLoad={false}><AdminAuthProvider fetchImpl={fetchImpl}><AuthProbe /></AdminAuthProvider></AuthProvider>);
    expect(screen.getByTestId('status')).toHaveTextContent('checking');
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedOut'));
    expect(window.sessionStorage.getItem(ADMIN_TOKEN_KEY)).toBeNull();
  });
});
