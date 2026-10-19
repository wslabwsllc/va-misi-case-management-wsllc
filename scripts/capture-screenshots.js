// Captures documentation screenshots of the running demo app (desktop, tablet, phone; cases and test results).
// Runs inside the eval image on the Compose network, using the Playwright driver bundled with the E2E project:
//   scripts/capture-screenshots.sh
const { chromium } = require('/src/tests/CaseIntake.E2E/bin/Release/net10.0/.playwright/package');
const base = process.env.BASE_URL || 'http://web:8080';
const out = process.env.OUT_DIR || '/out';
const password = process.env.DEMO_PASSWORD || 'Demo-Passw0rd!';

async function signIn(page, email) {
  await page.goto(`${base}/account/login`);
  await page.fill('#login-email', email);
  await page.fill('#login-password', password);
  await page.press('#login-password', 'Enter');
  await page.waitForSelector('h1:text("Dashboard")');
  await page.waitForFunction(() => window.Blazor !== undefined);
  await page.waitForTimeout(800);
}

async function shot(page, name, fullPage = true) {
  await page.waitForFunction(() => document.getAnimations().every(a => a.playState !== 'running'));
  await page.screenshot({ path: `${out}/${name}.png`, fullPage, mask: [page.locator('#lookup-ssn'), page.locator('input[type=password]')] });
  console.log(`captured ${name}.png`);
}

async function session(browser, viewport, mobile, email, steps) {
  const context = await browser.newContext({ viewport, isMobile: mobile, hasTouch: mobile, deviceScaleFactor: mobile ? 2 : 1, serviceWorkers: 'block', timezoneId: 'UTC' });
  const page = await context.newPage();
  if (email) {
    await signIn(page, email);
  }
  await steps(page);
  await context.close();
}

(async () => {
  const browser = await chromium.launch();
  const desktop = { width: 1440, height: 900 };
  const tablet = { width: 768, height: 1024 };
  const phone = { width: 390, height: 844 };

  await session(browser, desktop, false, null, async page => {
    await page.goto(`${base}/account/login`);
    await shot(page, 'desktop-sign-in', false);
  });
  await session(browser, desktop, false, 'supervisor@example.test', async page => {
    await shot(page, 'desktop-dashboard');
    await page.goto(`${base}/cases?status=New&status=Triaged&status=InProgress&sort=UpdatedAt&dir=desc`);
    await page.waitForSelector('[data-testid=case-table] tbody tr');
    await shot(page, 'desktop-case-list', false);
    await page.locator('[data-testid=case-table] tbody a').first().click();
    await page.waitForSelector('[data-testid=status-section]');
    await shot(page, 'desktop-case-detail');
    await page.locator('[data-testid^=transition-]').first().click();
    await page.waitForSelector('[data-testid=confirm-status-dialog][open]');
    await shot(page, 'desktop-status-dialog', false);
    await page.keyboard.press('Escape');
    await page.goto(`${base}/cases/new`);
    await page.waitForFunction(() => window.Blazor !== undefined);
    await page.waitForTimeout(500);
    await page.click('[data-testid=create-case]');
    await page.waitForSelector('#error-summary');
    await shot(page, 'desktop-new-case-validation', false);
  });
  await session(browser, desktop, false, 'qa@example.test', async page => {
    await page.goto(`${base}/test-results`);
    await page.waitForSelector('[data-testid=tr-kpis]');
    await shot(page, 'desktop-test-results-dashboard');
    await page.goto(`${base}/test-results/runs`);
    await page.waitForSelector('[data-testid=runs-table]');
    await shot(page, 'desktop-test-runs', false);
    await page.goto(`${base}/test-results/evidence`);
    await page.waitForSelector('[data-testid=evidence-gallery]');
    await shot(page, 'desktop-evidence-gallery', false);
    await page.locator('[data-testid=evidence-gallery] a[href*="executions"]').first().click();
    await page.waitForSelector('[data-testid=screenshots]');
    await shot(page, 'desktop-test-execution-detail');
    await page.locator('[data-testid=screenshots] button').first().click();
    await page.waitForSelector('[data-testid=evidence-preview][open] img');
    await shot(page, 'desktop-screenshot-preview', false);
  });
  await session(browser, tablet, true, 'supervisor@example.test', async page => {
    await shot(page, 'tablet-dashboard');
  });
  await session(browser, phone, true, 'caseworker@example.test', async page => {
    await shot(page, 'phone-dashboard');
    await page.locator('[data-testid=bottom-nav] a', { hasText: 'Cases' }).tap();
    await page.waitForSelector('[data-testid=case-cards] li');
    await shot(page, 'phone-case-list', false);
    await page.locator('[data-testid=case-cards] a').first().tap();
    await page.waitForSelector('[data-testid=record-header]');
    await shot(page, 'phone-case-detail', false);
    await page.locator('[data-testid=bottom-nav-more]').tap();
    await page.waitForSelector('[data-testid=more-sheet][open]');
    await shot(page, 'phone-more-sheet', false);
    await page.keyboard.press('Escape');
    await page.goto(`${base}/cases/new`);
    await page.waitForSelector('#intake-title');
    await shot(page, 'phone-new-case', false);
  });
  await session(browser, phone, true, 'qa@example.test', async page => {
    await page.goto(`${base}/test-results`);
    await page.waitForSelector('[data-testid=tr-kpis]');
    await shot(page, 'phone-test-results-dashboard');
  });
  await browser.close();
})().catch(err => { console.error(err); process.exit(1); });
