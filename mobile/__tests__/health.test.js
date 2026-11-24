import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import { resetApiClient } from '../src/api/client';
import AboutScreen from '../app/(tabs)/profile/about';

function jsonResponse(body, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: { get: (name) => (name.toLowerCase() === 'content-type' ? 'application/json; charset=utf-8' : null) },
    json: async () => body,
    text: async () => JSON.stringify(body)
  };
}

const healthy = {
  status: 'Healthy',
  version: '1.0.0',
  startedUtc: '2026-11-11T00:00:00Z',
  uptimeSeconds: 3725.4,
  database: 'Sqlite'
};

async function renderAbout() {
  return renderRouter({ index: AboutScreen }, { initialUrl: '/' });
}

describe('About this app: server health', () => {
  beforeEach(() => {
    resetApiClient();
    global.fetch = jest.fn();
  });

  afterEach(() => {
    delete global.fetch;
  });

  test('shows status, version, database, and uptime when the server is healthy', async () => {
    global.fetch.mockResolvedValue(jsonResponse(healthy));
    await renderAbout();

    expect(await screen.findByTestId('health-result')).toBeOnTheScreen();
    expect(screen.getByTestId('health-status')).toHaveTextContent('Healthy');
    expect(screen.getByTestId('health-version')).toHaveTextContent('1.0.0');
    expect(screen.getByTestId('health-database')).toHaveTextContent('Sqlite');
    expect(screen.getByTestId('health-uptime')).toHaveTextContent('1h 2m 5s');
    expect(screen.getByTestId('app-version')).toHaveTextContent('1.0.0');

    expect(global.fetch).toHaveBeenCalledTimes(1);
    const [url, init] = global.fetch.mock.calls[0];
    expect(url).toMatch(/\/api\/v1\/health$/);
    expect(init.method).toBe('GET');
  });

  test('shows a friendly error when the server is unreachable, then recovers on Retry', async () => {
    global.fetch.mockRejectedValueOnce(new TypeError('Network request failed')).mockResolvedValueOnce(jsonResponse({ ...healthy, status: 'Degraded', database: 'Postgresql' }));
    await renderAbout();

    expect(await screen.findByText('Cannot reach the server')).toBeOnTheScreen();
    expect(screen.queryByTestId('health-result')).toBeNull();

    await fireEvent.press(screen.getByRole('button', { name: 'Retry' }));

    await waitFor(() => expect(screen.getByTestId('health-status')).toHaveTextContent('Degraded'));
    expect(screen.getByTestId('health-database')).toHaveTextContent('Postgresql');
    expect(screen.queryByText('Cannot reach the server')).toBeNull();
    expect(global.fetch).toHaveBeenCalledTimes(2);
  });

  test('shows the server message for an HTTP error', async () => {
    global.fetch.mockResolvedValue(jsonResponse({ error: 'InternalError', message: 'Database offline.' }, 503));
    await renderAbout();

    expect(await screen.findByText('The server returned an error')).toBeOnTheScreen();
    expect(screen.getByText('Database offline.')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeOnTheScreen();
  });
});
