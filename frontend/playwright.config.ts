import { defineConfig, devices } from '@playwright/test'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

// End-to-end tests run against the REAL backend and the real frontend — nothing is mocked except where a single test
// deliberately stubs a failure (429, API down) with page.route. Ports 5210/5211 are reserved for this, so the suite
// can run while the dev servers (5200/5201) are up.
const here = path.dirname(fileURLToPath(import.meta.url))
const repoRoot = path.resolve(here, '..')
const API_PORT = 5210
const WEB_PORT = 5211

// A fresh, empty data folder per run, seeded from dataset.json by the API on first use: every run starts from the
// same five tickets and never touches the developer's own App_Data.
const dataDir = path.join(os.tmpdir(), 'sst-e2e', String(process.env.SST_E2E_RUN ??= String(Date.now())))

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  // Capped: each worker is a browser, and the backend is one process with a file store.
  workers: process.env.CI ? 2 : 3,
  retries: 0,
  reporter: [['list']],
  timeout: 30_000,
  expect: { timeout: 7_000 },
  use: {
    baseURL: `http://localhost:${WEB_PORT}`,
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 900 } }, testIgnore: /mobile\.spec/ },
    { name: 'mobile', use: { ...devices['Pixel 7'], viewport: { width: 375, height: 812 } }, testMatch: /mobile\.spec/ },
  ],
  webServer: [
    {
      command: `dotnet run --project "${path.join(repoRoot, 'backend', 'SupportTickets.Api')}" --no-launch-profile`,
      url: `http://localhost:${API_PORT}/health`,
      reuseExistingServer: false, // always a fresh backend on fresh data
      timeout: 180_000,
      env: {
        ASPNETCORE_ENVIRONMENT: 'E2E', // dev secrets allowed, high login limit, no AI, console email
        ASPNETCORE_URLS: `http://localhost:${API_PORT}`,
        Storage__DataDirectory: dataDir,
        Storage__SeedFile: path.join(repoRoot, 'dataset.json'),
        App__FrontendBaseUrl: `http://localhost:${WEB_PORT}`,
      },
    },
    {
      command: `npx vite --port ${WEB_PORT} --strictPort`,
      url: `http://localhost:${WEB_PORT}`,
      reuseExistingServer: false,
      timeout: 60_000,
      env: { API_URL: `http://localhost:${API_PORT}` },
    },
  ],
})
