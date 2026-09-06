import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';
import { LOOPBACK_URL, PUBLIC_URL } from '../playwright.config.js';

const USERNAME = 'e2e-user';
const PASSWORD = 'macro-deck-e2e-password';
const PROFILE_NAME = 'E2E Profile';
const RENAMED_PROFILE_NAME = 'E2E Persisted Profile';
const PROFILE_COLUMNS = 6;
const PROFILE_ROWS = 4;
const WIDGET_LABEL = 'E2E Action';
const TEMP_PROFILE_NAME = 'E2E Delete Me';
const SUPERVISOR_LOG = process.env.MACRO_DECK_SUPERVISOR_LOG;

function watchForUnexpectedFailures(page) {
  const failures = [];
  let hostOutageExpected = false;

  page.on('pageerror', error => {
    if (!hostOutageExpected) {
      failures.push(`pageerror: ${error.message}`);
    }
  });
  page.on('response', response => {
    if (!hostOutageExpected && response.status() >= 500 && response.url().includes('/api/')) {
      failures.push(`${response.status()} ${response.request().method()} ${response.url()}`);
    }
  });

  const assertNone = () => expect(failures, failures.join('\n')).toEqual([]);

  // Runs a step that deliberately takes the host down. In-flight calls are lost there however they
  // fail, so those are an expected outcome of the restart, not a regression.
  assertNone.whileHostIsRestarting = async run => {
    hostOutageExpected = true;
    try {
      return await run();
    } finally {
      hostOutageExpected = false;
    }
  };

  return assertNone;
}

async function waitForHost(request) {
  await expect.poll(async () => {
    try {
      const response = await request.get(`${LOOPBACK_URL}/api/auth/status`);
      return response.ok();
    } catch {
      return false;
    }
  }, { timeout: 30_000 }).toBe(true);
}

// Every locator below matches on English text. Stating that dependency here means a moved default
// fails once, instead of later as a pile of unfindable elements.
async function assertHostServesEnglish(request) {
  const response = await request.get(`${LOOPBACK_URL}/api/localization`);
  expect(response.ok()).toBe(true);

  const localization = await response.json();
  expect(localization.culture).toBe('en');
  expect(localization.translations['macrodeck:Common.Save']).toBe('Save');
}

async function getProfiles(request) {
  const response = await request.get(`${LOOPBACK_URL}/api/profiles`);
  expect(response.ok()).toBe(true);
  return (await response.json()).profiles;
}

async function getHostStartCount() {
  if (!SUPERVISOR_LOG) {
    return null;
  }
  try {
    const log = await readFile(SUPERVISOR_LOG, 'utf8');
    return log.split('Starting Macro Deck host').length - 1;
  } catch {
    return 0;
  }
}

async function openProfileMenu(page, profileName) {
  await page.getByRole('button', { name: profileName, exact: true }).click();
}

async function clickProfileAction(page, profileName, action) {
  await openProfileMenu(page, profileName);
  const profileRow = page.locator('.profile-list-item').filter({
    has: page.getByText(profileName, { exact: true }),
  });
  await expect(profileRow).toBeVisible();
  await profileRow.hover();
  await profileRow.getByRole('button', { name: `${action} ${profileName}` }).click();
}

async function selectProfile(page, profileName) {
  const trigger = page.locator('shared-dropdown-menu.profile-dropdown .dropdown-trigger');
  await expect(trigger).toBeVisible();
  if ((await trigger.innerText()).trim() === profileName) {
    return;
  }
  await trigger.click();
  await page.locator('.profile-list-select').filter({ hasText: profileName }).click();
  await expect(trigger).toContainText(profileName);
}

async function loginWebClient(page) {
  await page.goto(PUBLIC_URL);
  await signInWebClient(page);
}

// Waits for the deck, not for the settings gear: the gear sits above the sign-in card too, so
// waiting on it carries on while the sign-in is still in flight.
async function signInWebClient(page) {
  await expect(page.getByText('Sign in to continue')).toBeVisible();
  await page.getByLabel('Username').fill(USERNAME);
  await page.getByLabel('Password').fill(PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.locator('.wc-deck')).toBeVisible();
}

test.describe('Macro Deck core E2E', () => {
  test.describe.configure({ mode: 'serial' });

  test('@smoke completes first-run setup through the Admin UI', async ({ page, request }) => {
    const assertNoUnexpectedFailures = watchForUnexpectedFailures(page);

    await page.goto('/admin');
    await expect(page.getByRole('heading', { name: 'Welcome to Macro Deck' })).toBeVisible();

    await page.getByLabel('Username').fill(USERNAME);
    await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
    await page.getByLabel('Confirm password').fill(PASSWORD);
    await page.getByRole('button', { name: 'Create account' }).click();

    await expect(page.getByRole('heading', { name: 'Welcome to Macro Deck', exact: true })).toBeHidden();
    await expect.poll(async () => {
      const response = await request.get(`${LOOPBACK_URL}/api/auth/status`);
      return response.ok() ? (await response.json()).setupComplete : false;
    }).toBe(true);

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

    await assertHostServesEnglish(request);

    assertNoUnexpectedFailures();
  });

  test('@smoke creates a profile, changes its grid defaults, and adds an Action Button', async ({ page, request }) => {
    const assertNoUnexpectedFailures = watchForUnexpectedFailures(page);
    await page.goto('/admin');

    const profiles = await getProfiles(request);
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
    await expect.poll(async () => (await getProfiles(request)).some(profile => profile.name === TEMP_PROFILE_NAME)).toBe(false);

    const currentProfiles = await getProfiles(request);
    expect(currentProfiles.some(profile => profile.name === RENAMED_PROFILE_NAME)).toBe(true);
    await selectProfile(page, RENAMED_PROFILE_NAME);

    assertNoUnexpectedFailures();
  });

  test('@smoke signs into the Web Client, signs out, and signs back in', async ({ browser }) => {
    const context = await browser.newContext();
    const page = await context.newPage();
    const assertNoUnexpectedFailures = watchForUnexpectedFailures(page);

    await loginWebClient(page);
    await page.getByRole('button', { name: 'Open Macro Deck settings' }).click();
    await page.getByRole('button', { name: 'Sign out' }).click();

    await signInWebClient(page);

    assertNoUnexpectedFailures();
    await context.close();
  });

  test('@smoke persists profile, widget, and grid state across a real host restart and reconnects the Web Client', async ({ page, request, browser }) => {
    const assertNoUnexpectedFailures = watchForUnexpectedFailures(page);
    await page.goto('/admin');
    await selectProfile(page, RENAMED_PROFILE_NAME);
    await expect(page.getByText(`${PROFILE_COLUMNS} × ${PROFILE_ROWS} Grid`)).toBeVisible();
    await expect(page.locator('shared-widget-item')).toHaveCount(1);
    await expect(page.getByText(WIDGET_LABEL, { exact: true })).toBeVisible();

    const webContext = await browser.newContext();
    const webPage = await webContext.newPage();
    const assertNoUnexpectedWebFailures = watchForUnexpectedFailures(webPage);
    await loginWebClient(webPage);

    await assertNoUnexpectedFailures.whileHostIsRestarting(() =>
      assertNoUnexpectedWebFailures.whileHostIsRestarting(async () => {
        const startsBeforeRestart = await getHostStartCount();
        const restart = await request.post(`${LOOPBACK_URL}/api/host/restart`, {
          data: { reason: 'e2e-persistence' },
        });
        expect(restart.ok()).toBe(true);
        expect(await restart.json()).toMatchObject({ supported: true, success: true });

        if (startsBeforeRestart !== null) {
          await expect.poll(async () => await getHostStartCount(), { timeout: 30_000 })
            .toBeGreaterThan(startsBeforeRestart);
        } else {
          await page.waitForTimeout(500);
        }
        await waitForHost(request);
        await expect.poll(async () => {
          const persistedProfiles = await getProfiles(request);
          const profile = persistedProfiles.find(item => item.name === RENAMED_PROFILE_NAME);
          return profile ? `${profile.defaultColumns}x${profile.defaultRows}` : null;
        }, { timeout: 30_000 }).toBe(`${PROFILE_COLUMNS}x${PROFILE_ROWS}`);

        await expect(webPage.getByRole('button', { name: 'Open Macro Deck settings' }))
          .toBeVisible({ timeout: 30_000 });
      }));

    await page.reload();
    await selectProfile(page, RENAMED_PROFILE_NAME);
    await expect(page.getByText(`${PROFILE_COLUMNS} × ${PROFILE_ROWS} Grid`)).toBeVisible();
    await expect(page.locator('shared-widget-item')).toHaveCount(1);
    await expect(page.getByText(WIDGET_LABEL, { exact: true })).toBeVisible();

    assertNoUnexpectedFailures();
    assertNoUnexpectedWebFailures();
    await webContext.close();
  });
});
