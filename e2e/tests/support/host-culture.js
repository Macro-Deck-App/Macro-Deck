import { expect, test, useIsolatedHost } from './fixtures.js';
import { flow } from './seed.js';
import { connectWebClient, pressWebTile } from './pages.js';

const CONDITIONS = [
  { title: 'a Numeric 1.5 contains "1.5"', type: 'numeric', value: '1.5', operand: '1.5' },
  { title: 'a Text "007" contains "007"', type: 'text', value: '007', operand: '007' },
];

export function describeHostUnderCulture({ name, locale, culture }) {
  test.skip(process.platform === 'win32', 'LANG and LC_ALL do not set the culture of a Windows process');

  const seeded = { buttons: [] };

  useIsolatedHost(name, {
    env: { LANG: locale, LC_ALL: locale },
    prepare: async api => {
      const localization = await api.get('/api/settings/localization');
      const matches = localization.followSystem === true
        && localization.culture.toLowerCase().startsWith(culture);
      test.skip(!matches && process.platform === 'darwin',
        `the host reported ${JSON.stringify(localization)} for ${locale}; this macOS did not apply it`);
      expect(localization.followSystem, 'no language stored yet, so the host follows the system').toBe(true);
      expect(localization.culture.toLowerCase()).toMatch(new RegExp(`^${culture}(-|$)`));

      await api.completeOnboarding();
      seeded.profile = await api.profile(`E2E Culture ${culture}`, { columns: 3, rows: 2 });
      for (const [index, condition] of CONDITIONS.entries()) {
        const source = `e2e_${culture}_source_${index}`;
        const marker = `e2e_${culture}_marker_${index}`;
        await api.variable(source, { type: condition.type, value: condition.value });
        await api.variable(marker, { type: 'text', value: 'unset' });
        const button = await api.actionButton(seeded.profile.rootFolder.id, {
          x: index,
          label: `cond ${index}`,
          flows: [flow.shortPress(flow.condition(
            { kind: 'compare', operator: 'contains', left: { $var: source }, right: condition.operand },
            [flow.setVariable(marker, 'matched')],
            [flow.setVariable(marker, 'not matched')]))],
        });
        seeded.buttons.push({ ...condition, button, marker });
      }
    },
  });

  test(`the host starts under ${locale} and keeps a profile across a restart`, async ({ api, host }) => {
    test.setTimeout(180_000);
    expect(await api.get('/api/auth/status')).toMatchObject({ setupComplete: true });
    expect((await api.profiles()).map(profile => profile.name)).toContain(seeded.profile.name);

    const startsBefore = host.starts;
    await api.restart(`e2e-culture-${culture}`);
    await host.waitForRestart(startsBefore);
    await expect.poll(async () => (await api.profiles()).some(profile => profile.id === seeded.profile.id),
      { timeout: 30_000 }).toBe(true);
  });

  for (const [index, condition] of CONDITIONS.entries()) {
    test(`under ${locale}, ${condition.title} is true when pressed in the Web Client`, async ({ browser, api, host }) => {
      const { button, marker } = seeded.buttons[index];
      const web = await connectWebClient(browser, host, api);
      await api.openProfileOnDevice(web.device.id, seeded.profile.id);
      await pressWebTile(web, button.id);
      await expect.poll(() => api.variableValue(marker)).toBe('matched');
      await web.context.close();
    });
  }
}
