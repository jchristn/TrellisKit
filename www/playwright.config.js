import { defineConfig, devices } from '@playwright/test';

const PORT = Number(process.env.WWW_E2E_PORT || 5173);
const BASE_URL = process.env.WWW_E2E_BASE_URL || 'http://127.0.0.1:' + PORT;

/**
 * Playwright smoke tests. By default this starts the Vite dev server; set WWW_E2E_BASE_URL to
 * test a running stack instead (for example http://127.0.0.1:8080 for the Docker www service).
 * Install browsers once with: npx playwright install chromium
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 30000,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? 'github' : 'list',
  use: {
    baseURL: BASE_URL,
    trace: 'retain-on-failure'
  },
  projects: [
    { name: 'desktop-light', use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 }, colorScheme: 'light' } },
    { name: 'desktop-dark', use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 }, colorScheme: 'dark' } },
    { name: 'mobile-light', use: { ...devices['Desktop Chrome'], viewport: { width: 360, height: 740 }, isMobile: false, colorScheme: 'light' } },
    { name: 'mobile-dark', use: { ...devices['Desktop Chrome'], viewport: { width: 360, height: 740 }, isMobile: false, colorScheme: 'dark' } }
  ],
  webServer: process.env.WWW_E2E_BASE_URL ? undefined : {
    command: 'npm run dev -- --host 127.0.0.1 --port ' + PORT,
    url: BASE_URL,
    reuseExistingServer: !process.env.CI,
    timeout: 60000
  }
});
