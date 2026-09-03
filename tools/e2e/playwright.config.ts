import { defineConfig } from '@playwright/test';

/**
 * DreamGenClone Playwright E2E config.
 *
 * Targets the LIVE Blazor Server webapp the user starts themselves:
 *   http://localhost:5177 (Development) — see helpers/start-webapp-dev-clean.ps1
 * These tests are LLM-free: they only exercise the Continuation Settings popup UI
 * (Tempo/Span selection → save → persist), never an actual model turn.
 *
 * Env overrides:
 *   E2E_BASE_URL   – default http://localhost:5177
 *   E2E_SESSION_ID – default f1d424cc-eb01-47ca-8176-5c280b6fb696 (dev session)
 */
export default defineConfig({
  testDir: './tests',
  timeout: 60_000,
  fullyParallel: false,
  retries: 1,
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5177',
    headless: true,
    viewport: { width: 1440, height: 900 },
    actionTimeout: 15_000,
    trace: 'on-first-retry',
  },
  reporter: [['list']],
  projects: [
    { name: 'chromium', use: { browserName: 'chromium' } },
  ],
});
