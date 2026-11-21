import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { flow } from './support/seed.js';
import { desktopTile, openDesktopProfile } from './support/pages.js';

useIsolatedHost('key-recorder', { prepare: api => api.completeOnboarding() });

const KEYBOARD = 'app.macro-deck.keyboard';

const SHIFT_LEFT = { key: 'Shift', code: 'ShiftLeft', location: 1, keyCode: 16, shiftKey: true };
const SHIFT_RIGHT = { key: 'Shift', code: 'ShiftRight', location: 2, keyCode: 16, shiftKey: true };
const CONTROL_LEFT = { key: 'Control', code: 'ControlLeft', location: 1, keyCode: 17, ctrlKey: true };
const ALT_GRAPH = { key: 'AltGraph', code: 'AltRight', location: 2, keyCode: 18, ctrlKey: true, altKey: true };

// Playwright's keyboard always types the US layout, so layout-specific keys are dispatched as the
// events a browser on that layout produces: held modifiers first, then the key itself.
const CASES = [
  {
    name: 'numpad-1',
    held: [],
    press: { key: '1', code: 'Numpad1', location: 3, keyCode: 97 },
    expected: { modifiers: [], key: 'Numpad1' },
  },
  {
    name: 'digit-1',
    held: [],
    press: { key: '1', code: 'Digit1', keyCode: 49 },
    expected: { modifiers: [], key: '1' },
  },
  {
    name: 'german-altgr-3',
    held: [CONTROL_LEFT, ALT_GRAPH],
    press: { key: '³', code: 'Digit3', keyCode: 51, ctrlKey: true, altKey: true },
    expected: { modifiers: ['Ctrl', 'RightAlt'], key: '3' },
  },
  {
    name: 'right-shift',
    held: [SHIFT_RIGHT],
    press: { key: 'A', code: 'KeyA', keyCode: 65, shiftKey: true },
    expected: { modifiers: ['RightShift'], key: 'A' },
  },
  {
    name: 'left-shift',
    held: [SHIFT_LEFT],
    press: { key: 'A', code: 'KeyA', keyCode: 65, shiftKey: true },
    expected: { modifiers: ['Shift'], key: 'A' },
  },
  {
    name: 'space',
    held: [],
    press: { key: ' ', code: 'Space', keyCode: 32 },
    expected: { modifiers: [], key: 'Space' },
  },
  {
    name: 'media-play-pause',
    held: [],
    press: { key: 'MediaPlayPause', code: 'MediaPlayPause', keyCode: 179 },
    expected: { modifiers: [], key: 'MediaPlayPause' },
  },
  {
    name: 'german-z',
    held: [],
    press: { key: 'z', code: 'KeyY', keyCode: 90 },
    expected: { modifiers: [], key: 'Z' },
  },
  {
    name: 'german-sharp-s',
    held: [],
    press: { key: 'ß', code: 'Minus', keyCode: 219 },
    expected: { modifiers: [], key: 'Minus' },
  },
];

async function dispatchKeys(page, held, press) {
  await page.evaluate(({ held, press }) => {
    const fire = (type, init) => document.body.dispatchEvent(new KeyboardEvent(type, {
      bubbles: true,
      cancelable: true,
      composed: true,
      ...init,
    }));
    for (const modifier of held) {
      fire('keydown', modifier);
    }
    fire('keydown', press);
    fire('keyup', press);
    for (const modifier of [...held].reverse()) {
      fire('keyup', { ...modifier, ctrlKey: false, altKey: false, shiftKey: false });
    }
  }, { held, press });
}

async function openEditor(page, host, profile, widgetId) {
  await openDesktopProfile(page, host, profile.name);
  await desktopTile(page, widgetId).click();
  await expect(page.getByText('Loading editor…')).toBeHidden({ timeout: 20_000 });
}

function actionCard(page, label) {
  return page.getByRole('button', { name: new RegExp(`^Disable action ${label}\\b`) });
}

test('the key combo recorder stores the key per layout and sided modifiers', async ({ page, api, host }) => {
  test.setTimeout(120_000);
  const profile = await api.profile('E2E Key Recorder', { columns: 3, rows: 2 });
  const actions = CASES.map(item => ({
    ...flow.action(KEYBOARD, 'press-key', {}, item.name),
    parameters: [],
  }));
  const button = await api.actionButton(profile.rootFolder.id, {
    label: 'keys',
    flows: [flow.shortPress(...actions)],
  });
  await openEditor(page, host, profile, button.id);

  for (const item of CASES) {
    await test.step(item.name, async () => {
      // A card only opens once the editor knows its action, which can arrive after the card is shown.
      await expect(actionCard(page, item.name)).toHaveAttribute('aria-expanded', 'false');
      await actionCard(page, item.name).click();
      const combo = page.getByRole('group', { name: 'Key Combination', exact: true });
      await expect(combo).toHaveCount(1);
      const record = combo.locator('.kce-record');
      await record.click();
      await expect(record).not.toContainText('Click to record');
      await dispatchKeys(page, item.held, item.press);
      await expect(record).toContainText(item.expected.key);
      await actionCard(page, item.name).click();
      await expect(combo).toHaveCount(0);
    });
  }

  const save = page.getByRole('button', { name: 'Save', exact: true });
  await save.click();
  await expect(save).toBeHidden();

  const stored = JSON.parse((await api.findWidget(button.id)).parsed.flows)[0].children;
  const recorded = Object.fromEntries(stored.map(block => [
    block.label,
    block.parameters.find(parameter => parameter.name === 'combo')?.value,
  ]));
  for (const item of CASES) {
    expect(recorded[item.name], item.name).toMatchObject({ key: item.expected.key });
    expect(recorded[item.name].modifiers ?? [], item.name).toEqual(item.expected.modifiers);
  }
});
