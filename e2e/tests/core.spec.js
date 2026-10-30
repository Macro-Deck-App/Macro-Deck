import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { flow, PASSWORD, USERNAME } from './support/seed.js';
import {
  clickProfileAction,
  connectWebClient,
  expectLivePush,
  openDesktop,
  openProfileMenu,
  openWebClient,
  pressWebTile,
  selectDesktopProfile,
  signInWebClient,
  watchForUnexpectedFailures,
  webDeck,
} from './support/pages.js';

const PROFILE_NAME = 'E2E Profile';
const RENAMED_PROFILE_NAME = 'E2E Persisted Profile';
const PROFILE_COLUMNS = 6;
const PROFILE_ROWS = 4;
const WIDGET_LABEL = 'E2E Action';
const TEMP_PROFILE_NAME = 'E2E Delete Me';

// The only spec that clicks through first-run setup, so its host starts without an account. The
// locators match on English text, so the host is told to serve English whatever the machine runs.
useIsolatedHost('core', { prepare: api => api.useEnglish() });

async function assertHostServesEnglish(api) {
  const localization = await api.get('/api/localization');
  expect(localization.culture).toBe('en');
  expect(localization.translations['macrodeck:Common.Save']).toBe('Save');
}

test.describe('Macro Deck core E2E', () => {
  test.describe.configure({ mode: 'serial' });

  test('@smoke completes first-run setup through the Admin UI', async ({ page, api, host }) => {
    const assertNoUnexpectedFailures = watchForUnexpectedFailures(page);

    await openDesktop(page, host);
    await expect(page.getByRole('heading', { name: 'Welcome to Macro Deck' })).toBeVisible();

    await page.getByLabel('Username').fill(USERNAME);
    await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
    await page.getByLabel('Confirm password').fill(PASSWORD);
    await page.getByRole('button', { name: 'Create account' }).click();

    await expect(page.getByRole('heading', { name: 'Welcome to Macro Deck', exact: true })).toBeHidden();
    await expect.poll(async () => (await api.get('/api/auth/status')).setupComplete).toBe(true);
    const withoutSecret = await api.request.get(`${host.loopbackUrl}/api/profiles`);
    expect(withoutSecret.status()).toBe(401);

    // Creating the account is what makes the host owe a wizard, and it has no skip and no close
    // button, so walking it to the end is also what leaves the rest of the suite a free deck.
    const onboarding = page.getByRole('dialog', { name: 'Welcome to Macro Deck 3' });
    await expect(onboarding).toBeVisible();
    await onboarding.getByRole('button', { name: 'Next' }).click();

    const connectStep = page.getByRole('dialog', { name: 'Connect to Macro Deck' });
    await expect(connectStep).toBeVisible();
    await connectStep.getByRole('button', { name: 'Next' }).click();

    // The recovery key step will not let go until the key has been shown and acknowledged: a backup
    // encrypted with a key nobody kept cannot be restored.
    const recoveryKeyStep = page.getByRole('dialog', { name: 'Your backup recovery key' });
    await expect(recoveryKeyStep).toBeVisible();
    await recoveryKeyStep.getByRole('button', { name: 'Create recovery key' }).click();

    const keyModal = page.getByRole('dialog', { name: 'Save your recovery key' });
    await expect(keyModal).toBeVisible();
    await keyModal.getByLabel('I have stored this key in a safe place').check();
    await keyModal.getByRole('button', { name: 'Done' }).click();
    await expect(keyModal).toBeHidden();

    await recoveryKeyStep.getByRole('button', { name: 'Next' }).click();

    const linksStep = page.getByRole('dialog', { name: 'Useful links' });
    await expect(linksStep).toBeVisible();
    await linksStep.getByRole('button', { name: 'Get started' }).click();
    await expect(linksStep).toBeHidden();

    await assertHostServesEnglish(api);

    assertNoUnexpectedFailures();
  });

  test('@smoke creates a profile, changes its grid defaults, and adds an Action Button', async ({ page, api, host }) => {
    const assertNoUnexpectedFailures = watchForUnexpectedFailures(page);
    await openDesktop(page, host);

    const profiles = await api.profiles();
    const selectedName = profiles.toSorted((a, b) => a.order - b.order)[0]?.name ?? 'No Profile';

    await openProfileMenu(page, selectedName);
    await page.getByRole('button', { name: 'New Profile' }).click();
    const createModal = page.getByRole('dialog', { name: 'New Profile' });
    await createModal.getByRole('textbox', { name: 'Profile name' }).fill(PROFILE_NAME);
    await createModal.getByRole('button', { name: 'Create', exact: true }).click();
    await expect(page.getByRole('button', { name: PROFILE_NAME, exact: true })).toBeVisible();

    await clickProfileAction(page, PROFILE_NAME, 'Edit');
    const editModal = page.getByRole('dialog', { name: 'Edit Profile' });
    await editModal.getByRole('textbox', { name: 'Name', exact: true }).fill(RENAMED_PROFILE_NAME);
    const sliders = editModal.getByRole('slider');
    await sliders.nth(0).fill(String(PROFILE_COLUMNS));
    await sliders.nth(1).fill(String(PROFILE_ROWS));
    await editModal.getByRole('button', { name: 'Save' }).click();

    await expect(page.getByRole('button', { name: RENAMED_PROFILE_NAME, exact: true })).toBeVisible();
    await expect(page.getByText(`${PROFILE_COLUMNS} × ${PROFILE_ROWS} Grid`)).toBeVisible();

    const firstEmptyCell = page.locator('.empty-cell:not(.occupied)').first();
    await expect(firstEmptyCell).toBeVisible();
    await firstEmptyCell.click();
    const typeSelector = page.getByRole('dialog', { name: 'Select Widget Type' });
    await expect(typeSelector).toBeVisible();
    await typeSelector.getByRole('button', { name: /^Action Button/ }).click();
    await expect(page.getByRole('heading', { name: 'Action Button' })).toBeVisible();
    await page.getByRole('textbox', { name: 'Label', exact: true }).fill(WIDGET_LABEL);
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    // Saving leaves nothing to save, so the button goes - it no longer confirms through a transient
    // "Saved" state that then fades on its own.
    await expect(page.getByRole('button', { name: 'Save', exact: true })).not.toBeVisible();
    await page.getByRole('button', { name: 'Back to deck' }).click();
    await expect(page.locator('shared-widget-item')).toHaveCount(1);
    await expect(page.getByText(WIDGET_LABEL, { exact: true })).toBeVisible();

    await openProfileMenu(page, RENAMED_PROFILE_NAME);
    await page.getByRole('button', { name: 'New Profile' }).click();
    const createTempModal = page.getByRole('dialog', { name: 'New Profile' });
    await createTempModal.getByRole('textbox', { name: 'Profile name' }).fill(TEMP_PROFILE_NAME);
    await createTempModal.getByRole('button', { name: 'Create', exact: true }).click();
    await expect(page.getByRole('button', { name: TEMP_PROFILE_NAME, exact: true })).toBeVisible();
    await clickProfileAction(page, TEMP_PROFILE_NAME, 'Delete');
    const deleteModal = page.getByRole('dialog', { name: 'Delete Profile' });
    await deleteModal.getByRole('button', { name: 'Delete', exact: true }).click();
    await expect.poll(async () => (await api.profiles()).some(profile => profile.name === TEMP_PROFILE_NAME))
      .toBe(false);

    const currentProfiles = await api.profiles();
    expect(currentProfiles.some(profile => profile.name === RENAMED_PROFILE_NAME)).toBe(true);
    await selectDesktopProfile(page, RENAMED_PROFILE_NAME);

    assertNoUnexpectedFailures();
  });

  test('@smoke signs into the Web Client, signs out, and signs back in', async ({ browser, host }) => {
    const context = await browser.newContext();
    const page = await context.newPage();
    const assertNoUnexpectedFailures = watchForUnexpectedFailures(page);

    await openWebClient(page, host);
    await page.getByRole('button', { name: 'Open Macro Deck settings' }).click();
    await page.getByRole('button', { name: 'Sign out' }).click();

    await signInWebClient(page);

    assertNoUnexpectedFailures();
    await context.close();
  });

  test('@smoke signs into the Web Client with the pairing code the desktop shows', async ({ browser, api, host }) => {
    const context = await browser.newContext();
    const page = await context.newPage();
    const assertNoUnexpectedFailures = watchForUnexpectedFailures(page);

    const code = await api.pairingCode();

    await page.goto(host.publicUrl);
    await expect(page.getByText('Sign in to continue')).toBeVisible();
    await page.getByRole('textbox', { name: 'Pairing code' }).fill(`${code.slice(0, 3)} ${code.slice(3)}`);
    await expect(webDeck(page)).toBeVisible();

    assertNoUnexpectedFailures();
    await context.close();
  });

  // E2E-CONN-01: a Web Client that only looks connected after the restart cannot pass, because the
  // label change and the press both have to cross the new socket.
  test('@smoke persists profile, widget, and grid state across a real host restart and reconnects the Web Client', async ({ page, api, host, browser }) => {
    const assertNoUnexpectedFailures = watchForUnexpectedFailures(page);
    await openDesktop(page, host);
    await selectDesktopProfile(page, RENAMED_PROFILE_NAME);
    await expect(page.getByText(`${PROFILE_COLUMNS} × ${PROFILE_ROWS} Grid`)).toBeVisible();
    await expect(page.locator('shared-widget-item')).toHaveCount(1);
    await expect(page.getByText(WIDGET_LABEL, { exact: true })).toBeVisible();

    await api.variable('e2e_restart_presses', { type: 'numeric', value: 0 });
    const reconnectProfile = await api.profile('E2E Reconnect', { columns: 3, rows: 2 });
    const counter = await api.actionButton(reconnectProfile.rootFolder.id, {
      label: 'Count',
      flows: [flow.shortPress(flow.setVariable('e2e_restart_presses', 1, 'add'))],
    });

    const web = await connectWebClient(browser, host, api);
    const assertNoUnexpectedWebFailures = watchForUnexpectedFailures(web.page);
    await api.openProfileOnDevice(web.device.id, reconnectProfile.id);
    await expectLivePush(web.page, api, counter.id);

    await assertNoUnexpectedFailures.whileHostIsDown(() =>
      assertNoUnexpectedWebFailures.whileHostIsDown(async () => {
        const startsBeforeRestart = host.starts;
        // The Admin UI reloads itself once it reconnects to the new host session. A reload of our own
        // would race that one, and the page could then reload again under the next step.
        const adminReloaded = page.waitForEvent('load', { timeout: 30_000 });
        await api.restart('e2e-persistence');
        await host.waitForRestart(startsBeforeRestart);
        await expect.poll(async () => {
          const profile = (await api.profiles()).find(item => item.name === RENAMED_PROFILE_NAME);
          return profile ? `${profile.defaultColumns}x${profile.defaultRows}` : null;
        }, { timeout: 30_000 }).toBe(`${PROFILE_COLUMNS}x${PROFILE_ROWS}`);

        await expectLivePush(web.page, api, counter.id, undefined, 30_000);
        await adminReloaded;
      }));

    await pressWebTile(web, counter.id);
    await expect.poll(() => api.variableValue('e2e_restart_presses')).toBe('1');

    await selectDesktopProfile(page, RENAMED_PROFILE_NAME);
    await expect(page.getByText(`${PROFILE_COLUMNS} × ${PROFILE_ROWS} Grid`)).toBeVisible();
    await expect(page.locator('shared-widget-item')).toHaveCount(1);
    await expect(page.getByText(WIDGET_LABEL, { exact: true })).toBeVisible();

    assertNoUnexpectedFailures();
    assertNoUnexpectedWebFailures();
    await web.context.close();
  });
});
