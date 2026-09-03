import { test, expect, Page, Locator } from '@playwright/test';

/**
 * B-089/B-090 — Continuation Settings popup: Tempo + Span UI flow.
 *
 * Validates the real Blazor UI (not a unit/mock layer):
 *  1. The popup opens and renders the Tempo + Span primary rows.
 *  2. Selecting a Tempo updates the "current:" label + finalized §3.7 description.
 *  3. Selecting a Span updates the "current:" label.
 *  4. "Done" saves the override (session persistence via ReloadAndSaveSessionAsync).
 *  5. Reopening the popup shows the persisted Tempo/Span (sticky override).
 *  6. The test restores the original override state so it is non-destructive.
 *
 * LLM-free by design — no model turns are triggered.
 *
 * Env: E2E_BASE_URL (default http://localhost:5177), E2E_SESSION_ID
 *      (default f1d424cc-eb01-47ca-8176-5c280b6fb696).
 */

const BASE_URL = process.env.E2E_BASE_URL ?? 'http://localhost:5177';
const SESSION_ID = process.env.E2E_SESSION_ID ?? 'f1d424cc-eb01-47ca-8176-5c280b6fb696';
const WORKSPACE_URL = `${BASE_URL}/roleplay/workspace/${SESSION_ID}`;

// ── Locators ──
// The popup trigger is the "Settings" chip in the continue row of RolePlayWorkspace.
const settingsButton = (page: Page) =>
  page.locator('button.rw-continue-chip', { hasText: 'Settings' });

const popup = (page: Page) =>
  page.locator('.rw-continuation-settings-popup');

// Rows are anchored by a unique child button so we don't collide with the other rows.
const tempoRow = (page: Page) =>
  page.locator('.rw-cs-row', { has: page.getByRole('button', { name: 'Leap', exact: true }) }).first();

const spanRow = (page: Page) =>
  page.locator('.rw-cs-row', { has: page.getByRole('button', { name: 'Extended Arc', exact: true }) }).first();

// ── Helpers ──
async function openPopup(page: Page): Promise<void> {
  await expect(settingsButton(page)).toBeVisible();
  // Blazor Server: on first navigation the page is prerendered and the interactive SignalR
  // circuit is not attached yet — a click before then is a silent no-op. Probe until the
  // popup actually opens (i.e. the circuit is live), then proceed.
  for (let attempt = 0; attempt < 12; attempt++) {
    await settingsButton(page).click({ timeout: 5000 });
    try {
      await expect(popup(page)).toBeVisible({ timeout: 1500 });
      return;
    } catch {
      await page.waitForTimeout(750);
    }
  }
  await expect(popup(page)).toBeVisible(); // final — surfaces the failure with the real timeout
}

async function closePopup(page: Page): Promise<void> {
  await popup(page).getByRole('button', { name: 'Done', exact: true }).click({ timeout: 5000 });
  await expect(popup(page)).toBeHidden();
  // The popup only hides after SaveContinuationOverrideAsync completes its async
  // ReloadAndSaveSessionAsync (which replaces _session with the freshly-loaded one), so a
  // hidden popup already means the override is persisted. Small settle for the re-render.
  await page.waitForTimeout(400);
}

/** Reads the row's "current: X" label (e.g. "Leap" or "theme (Steady)"). */
async function currentLabel(row: Locator): Promise<string> {
  const text = await row.innerText();
  const m = text.match(/current:\s*([^\n]+)/);
  return m ? m[1].trim() : '';
}

/** Restores a row to its pre-test state: "theme (…)" → No override, else the matching button. */
async function restoreRow(row: Locator, label: string): Promise<void> {
  if (!label) return;
  if (label.startsWith('theme (')) {
    await row.getByRole('button', { name: 'No override', exact: true }).click();
  } else {
    await row.getByRole('button', { name: label, exact: true }).click();
  }
}

// ── Suite-level snapshot / restore (non-destructive) ──
// The suite snapshots Tempo/Span once before the tests and restores them exactly once after,
// so individual tests may mutate freely and the session is left exactly as found.
let originalTempo = '';
let originalSpan = '';

test.describe('Continuation Settings — Tempo + Span popup', () => {
  test.beforeAll(async ({ browser }) => {
    const page = await browser.newPage();
    await page.goto(WORKSPACE_URL);
    await expect(settingsButton(page)).toBeVisible();
    await openPopup(page);
    originalTempo = await currentLabel(tempoRow(page));
    originalSpan = await currentLabel(spanRow(page));
    await closePopup(page);
    await page.close();
  });

  test.afterAll(async ({ browser }) => {
    const page = await browser.newPage();
    await page.goto(WORKSPACE_URL);
    await expect(settingsButton(page)).toBeVisible();
    await openPopup(page);
    await restoreRow(tempoRow(page), originalTempo);
    await restoreRow(spanRow(page), originalSpan);
    await closePopup(page);
    await page.close();
  });

  test.beforeEach(async ({ page }) => {
    await page.goto(WORKSPACE_URL);
    // Wait for the workspace to reach an interactive state (session loaded).
    await expect(settingsButton(page)).toBeVisible();
  });

  test('Tempo/Span select → save → persist (sticky override)', async ({ page }) => {
    // ── Mutate: Tempo = Leap ──
    await openPopup(page);
    await tempoRow(page).getByRole('button', { name: 'Leap', exact: true }).click();
    await expect(tempoRow(page)).toContainText('current: Leap');
    await expect(tempoRow(page)).toContainText('Advance time by a day or more');

    // ── Mutate: Span = Scene ──
    await spanRow(page).getByRole('button', { name: 'Scene', exact: true }).click();
    await expect(spanRow(page)).toContainText('current: Scene');
    await expect(spanRow(page)).toContainText('This moment spans 3 turns');
    await closePopup(page);

    // ── Persisted: reopening shows Leap + Scene (sticky session override) ──
    await openPopup(page);
    await expect(tempoRow(page)).toContainText('current: Leap');
    await expect(spanRow(page)).toContainText('current: Scene');
    await closePopup(page);
  });

  test('Tempo selection clears and reflects only the clicked value', async ({ page }) => {
    await openPopup(page);
    await tempoRow(page).getByRole('button', { name: 'Linger', exact: true }).click();
    await expect(tempoRow(page)).toContainText('current: Linger');
    await expect(tempoRow(page)).toContainText('Stay in this exact moment');

    // Switching to a different Tempo replaces, not stacks.
    await tempoRow(page).getByRole('button', { name: 'Push', exact: true }).click();
    await expect(tempoRow(page)).toContainText('current: Push');
    await expect(tempoRow(page)).not.toContainText('current: Linger');

    // "No override" returns to the theme fallback label.
    await tempoRow(page).getByRole('button', { name: 'No override', exact: true }).click();
    await expect(tempoRow(page)).toContainText(/current: theme \(/);
    await closePopup(page);
  });

  test('Span selection cycles Moment / Scene / Extended Arc', async ({ page }) => {
    await openPopup(page);

    await spanRow(page).getByRole('button', { name: 'Moment', exact: true }).click();
    await expect(spanRow(page)).toContainText('current: Moment');
    await expect(spanRow(page)).toContainText('This moment lasts a single turn');

    await spanRow(page).getByRole('button', { name: 'Extended Arc', exact: true }).click();
    await expect(spanRow(page)).toContainText('current: ExtendedArc');
    await expect(spanRow(page)).toContainText('This moment spans 5 turns');

    await spanRow(page).getByRole('button', { name: 'No override', exact: true }).click();
    await expect(spanRow(page)).toContainText(/current: theme \(/);
    await closePopup(page);
  });
});
