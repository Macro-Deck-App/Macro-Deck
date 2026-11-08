import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { flow } from './support/seed.js';
import { desktopTile, openDesktopProfile } from './support/pages.js';

useIsolatedHost('editor', { prepare: api => api.completeOnboarding() });

const DEFAULT_ICON_DISPLAY = { fit: 'contain', zoom: 100, offsetX: 0, offsetY: 0, opacity: 100 };

async function openEditor(page, host, profile, widgetId) {
  await openDesktopProfile(page, host, profile.name);
  await desktopTile(page, widgetId).click();
  await expect(page.getByText('Loading editor…')).toBeHidden({ timeout: 20_000 });
  await expect(page.getByRole('button', { name: 'Back to deck' })).toBeVisible();
}

async function save(page) {
  const button = page.getByRole('button', { name: 'Save', exact: true });
  await button.click();
  await expect(button).toBeHidden();
  await expect(page.getByText('Could not save the widget')).toHaveCount(0);
}

async function typeInto(field, text) {
  await field.click();
  await field.press('ControlOrMeta+a');
  await field.pressSequentially(text);
}

async function blur(page) {
  await page.getByRole('heading', { level: 1 }).click();
}

function storedFlows(widget) {
  return JSON.parse(widget.parsed.flows);
}

function parameterValues(block) {
  return Object.fromEntries(block.parameters.map(item => [item.name, item.value]));
}

async function importIcon(page, api, host) {
  await page.setContent('<div style="width:64px;height:64px;background:#e53e3e"></div>');
  const png = await page.locator('div').screenshot();
  const response = await api.request.post(`${host.loopbackUrl}/api/icons/import`, {
    headers: api.headers(),
    multipart: { file: { name: 'e2e-icon.png', mimeType: 'image/png', buffer: png } },
  });
  expect(response.ok()).toBe(true);
  await expect.poll(async () => {
    let count = 0;
    for (const pack of (await api.get('/api/icon-packs')).packs) {
      count += (await api.get(`/api/icons?packId=${pack.id}`)).icons.length;
    }
    return count;
  }, { message: 'the imported icon is listed' }).toBeGreaterThan(0);
}

test('the Action Button font size survives blur, save and reload', async ({ page, api, host }) => {
  const profile = await api.profile('E2E Font Size', { columns: 3, rows: 2 });
  const button = await api.actionButton(profile.rootFolder.id, { label: 'size' });
  await openEditor(page, host, profile, button.id);

  const size = page.getByRole('spinbutton', { name: 'Size (%)' });
  await typeInto(size, '23');
  await blur(page);
  await expect(size).toHaveValue('23');
  await save(page);
  expect((await api.findWidget(button.id)).parsed.fontSize).toBe(23);

  await openEditor(page, host, profile, button.id);
  await expect(page.getByRole('spinbutton', { name: 'Size (%)' })).toHaveValue('23');
});

test('the History Graph chart maximum keeps every typed digit through save and reload', async ({ page, api, host }) => {
  const profile = await api.profile('E2E Graph Maximum', { columns: 3, rows: 2 });
  await api.variable('e2e_graph_value', { type: 'numeric', value: 0 });
  const graph = await api.widget(profile.rootFolder.id, {
    type: 'HistoryGraph',
    data: { valueVariable: 'e2e_graph_value', title: 'Graph', maxValue: 100 },
  });
  await openEditor(page, host, profile, graph.id);

  const maximum = page.getByRole('spinbutton', { name: 'Chart maximum' });
  await typeInto(maximum, '64');
  await expect(maximum).toHaveValue('64');
  await blur(page);
  await expect(maximum).toHaveValue('64');
  await save(page);
  expect((await api.findWidget(graph.id)).parsed.maxValue).toBe(64);

  await openEditor(page, host, profile, graph.id);
  await expect(page.getByRole('spinbutton', { name: 'Chart maximum' })).toHaveValue('64');
});

test('a Set Variable value on a numeric variable survives blur, save and reload', async ({ page, api, host }) => {
  const profile = await api.profile('E2E Set Number', { columns: 3, rows: 2 });
  await api.variable('e2e_set_number', { type: 'numeric', value: 0 });
  const button = await api.actionButton(profile.rootFolder.id, {
    label: 'set',
    flows: [flow.shortPress(flow.setVariable('e2e_set_number', 1))],
  });
  await openEditor(page, host, profile, button.id);

  await page.getByText('Variable: e2e_set_number').click();
  const value = page.getByRole('textbox', { name: 'Value', exact: true });
  await typeInto(value, '42');
  await blur(page);
  await expect(value).toHaveText('42');
  await save(page);
  const block = storedFlows(await api.findWidget(button.id))[0].children[0];
  expect(parameterValues(block)).toMatchObject({ variable: 'e2e_set_number', value: '42' });

  await openEditor(page, host, profile, button.id);
  await page.getByText('Variable: e2e_set_number').click();
  await expect(page.getByRole('textbox', { name: 'Value', exact: true })).toHaveText('42');
});

test('a multi-state button switched from manual mapping to an action-provided state saves', async ({ page, api, host }) => {
  const profile = await api.profile('E2E State Provider', { columns: 3, rows: 2 });
  await api.variable('e2e_mapping', { type: 'numeric', value: 0 });
  const mute = flow.action('app.macro-deck.system', 'mute-volume', {}, 'Mute volume');
  const button = await api.actionButton(profile.rootFolder.id, {
    label: 'mute',
    flows: [flow.shortPress(mute)],
    states: [{ id: 'off', name: 'Off', label: 'Off' }, { id: 'on', name: 'On', label: 'On' }],
    stateMapping: {
      fallbackStateId: 'off',
      rules: [{ id: 'rule-on', stateId: 'on', when: flow.equals('e2e_mapping', 1) }],
    },
  });
  await openEditor(page, host, profile, button.id);
  await expect(page.getByRole('button', { name: /^State mapping 1 rule/ })).toBeVisible();

  await page.getByRole('button', { name: "Provide this button's state", exact: true }).click();
  await expect(page.getByText(/Provided by\s*Mute volume/)).toBeVisible();
  await save(page);

  const stored = (await api.findWidget(button.id)).parsed;
  expect(stored.stateProvider).toMatchObject({
    blockId: mute.id,
    integrationId: 'app.macro-deck.system',
    actionId: 'mute-volume',
  });
});

test('resetting the icon display of the second state restores the default and stays reset', async ({ page, api, host }) => {
  const profile = await api.profile('E2E Icon Reset', { columns: 3, rows: 2 });
  await importIcon(page, api, host);
  const button = await api.actionButton(profile.rootFolder.id, {
    label: 'icon',
    states: [{ id: 'first', name: 'First', label: 'First' }, { id: 'second', name: 'Second', label: 'Second' }],
  });
  await openEditor(page, host, profile, button.id);

  await page.getByRole('group', { name: /^State being edited/ }).getByRole('combobox').click();
  await page.getByRole('option', { name: /^Second/ }).click();
  await expect(page.getByRole('group', { name: 'State being edited: Second' })).toBeVisible();
  await page.getByRole('button', { name: 'Background', exact: true }).click();
  await page.getByRole('button', { name: 'Choose icon' }).click();
  const picker = page.getByRole('dialog', { name: 'Choose an icon' });
  await picker.getByRole('button', { name: 'e2e-icon' }).click();
  await picker.getByRole('button', { name: 'Use icon' }).click();
  await expect(picker).toBeHidden();

  const zoom = page.getByRole('spinbutton', { name: 'Zoom (%)' });
  const opacity = page.getByRole('spinbutton', { name: 'Opacity (%)' });
  const preview = page.locator('shared-widget-icon-display-control img');
  await expect(preview).toBeVisible();
  const defaultPreview = await preview.getAttribute('style');
  await zoom.fill('150');
  await opacity.fill('40');
  await blur(page);
  await expect(zoom).toHaveValue('150');
  await expect(preview).not.toHaveAttribute('style', defaultPreview ?? '');

  await page.getByRole('button', { name: 'Reset', exact: true }).click();
  await expect(zoom).toHaveValue('100');
  await expect(opacity).toHaveValue('100');
  await expect(preview).toHaveAttribute('style', defaultPreview ?? '');
  // The reported bug painted the reset for a few milliseconds and then reverted, so hold and look again.
  await page.waitForTimeout(1_000);
  await expect(zoom).toHaveValue('100');
  await expect(preview).toHaveAttribute('style', defaultPreview ?? '');
  await save(page);

  const second = (await api.findWidget(button.id)).parsed.states.find(state => state.id === 'second');
  expect({ ...DEFAULT_ICON_DISPLAY, ...second.appearance?.iconDisplay }).toEqual(DEFAULT_ICON_DISPLAY);
});

// #1707: on a multi-state button typing 150 into Zoom (%) ends as 400.
test.fixme('typing an icon zoom digit by digit on a second state keeps the typed number', async ({ page, api, host }) => {
  const profile = await api.profile('E2E Icon Zoom Typing', { columns: 3, rows: 2 });
  await importIcon(page, api, host);
  const button = await api.actionButton(profile.rootFolder.id, {
    label: 'zoom',
    states: [{ id: 'first', name: 'First', label: 'First' }, { id: 'second', name: 'Second', label: 'Second' }],
  });
  await openEditor(page, host, profile, button.id);
  await page.getByRole('group', { name: /^State being edited/ }).getByRole('combobox').click();
  await page.getByRole('option', { name: /^Second/ }).click();

  await page.getByRole('button', { name: 'Background', exact: true }).click();
  await page.getByRole('button', { name: 'Choose icon' }).click();
  const picker = page.getByRole('dialog', { name: 'Choose an icon' });
  await picker.getByRole('button', { name: 'e2e-icon' }).click();
  await picker.getByRole('button', { name: 'Use icon' }).click();

  const zoom = page.getByRole('spinbutton', { name: 'Zoom (%)' });
  await typeInto(zoom, '150');
  await blur(page);
  await expect(zoom).toHaveValue('150');
  await save(page);
  const second = (await api.findWidget(button.id)).parsed.states.find(state => state.id === 'second');
  expect(second.appearance.iconDisplay).toMatchObject({ zoom: 150 });
});

test('Escape in a dialog closes an open dropdown first and the dialog only on a second press', async ({ page, api, host }) => {
  const profile = await api.profile('E2E Escape', { columns: 3, rows: 2 });
  const button = await api.actionButton(profile.rootFolder.id, {
    label: 'escape',
    states: [{ id: 'a', name: 'A', label: 'A' }, { id: 'b', name: 'B', label: 'B' }],
  });
  await openEditor(page, host, profile, button.id);

  await page.getByRole('button', { name: /^State mapping/ }).click();
  const dialog = page.getByRole('dialog', { name: 'State mapping' });
  await expect(dialog).toBeVisible();
  await dialog.getByRole('combobox').last().click();
  const listbox = page.getByRole('listbox');
  await expect(listbox).toBeVisible();

  await page.keyboard.press('Escape');
  await expect(listbox).toBeHidden();
  await expect(dialog).toBeVisible();

  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
});
