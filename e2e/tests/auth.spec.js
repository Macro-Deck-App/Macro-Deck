import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { connectWebClient, expectLivePush, openDesktop, openWebClient, webDeck } from './support/pages.js';

useIsolatedHost('auth', { prepare: api => api.completeOnboarding() });

const SIGN_IN = 'Sign in to continue';

// E2E-AUTH-01: logging one device out in the desktop UI ends that device's session at once and for
// good, and leaves every other device signed in.
test('logging a device out in the desktop UI signs only that Web Client out', async ({ page, browser, api, host }) => {
  const profile = await api.profile('E2E Auth', { columns: 3, rows: 2 });
  const button = await api.actionButton(profile.rootFolder.id, { label: 'still here' });
  const first = await connectWebClient(browser, host, api);
  const second = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(second.device.id, profile.id);
  expect(first.device.id).not.toBe(second.device.id);

  await openDesktop(page, host);
  await page.getByRole('button', { name: 'Settings', exact: true }).click();
  await page.getByRole('button', { name: 'Devices', exact: true }).click();
  const row = page.locator(`.devices-row[data-device-id="${first.device.id}"]`);
  await row.locator('.devices-dots-btn').click();
  await page.locator('.devices-menu-logout').click();
  await page.getByRole('dialog', { name: 'Log out device?' }).getByRole('button', { name: 'Log out', exact: true }).click();

  await expect(first.page.getByText(SIGN_IN)).toBeVisible({ timeout: 5_000 });
  await first.page.reload();
  await expect(first.page.getByText(SIGN_IN)).toBeVisible();
  await expect(webDeck(first.page)).toHaveCount(0);

  await expect(second.page.getByText(SIGN_IN)).toHaveCount(0);
  await expectLivePush(second.page, api, button.id);
  await first.context.close();
  await second.context.close();
});

// E2E-AUTH-03: a browser that holds a Web Client session and then opens the admin UI on the same
// origin is asked to sign in, and does not hammer the host with forbidden requests.
test('opening the admin UI after a Web Client sign-in asks for a sign-in without a 403 storm', async ({ browser, host }) => {
  const context = await browser.newContext();
  const page = await context.newPage();
  await openWebClient(page, host);

  const forbidden = [];
  page.on('response', response => {
    if (response.status() === 403) {
      forbidden.push(`${response.request().method()} ${response.url()}`);
    }
  });
  await page.goto(`${host.publicUrl}/admin/`);
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Username' })).toBeVisible();
  // A second, independent request proves the page has settled on the form rather than a loop.
  const settled = await page.request.get(`${host.publicUrl}/api/auth/status`);
  expect(settled.ok()).toBe(true);
  expect(forbidden.length, forbidden.join('\n')).toBeLessThanOrEqual(2);
  await context.close();
});
