import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { startEchoServer } from './support/echo-server.js';
import { flow } from './support/seed.js';
import { connectWebClient, openDesktop, pressWebTile } from './support/pages.js';

useIsolatedHost('approvals', { prepare: api => api.completeOnboarding() });

let echo;

test.beforeAll(async () => {
  echo = await startEchoServer();
});

test.afterAll(async () => {
  await echo?.close();
});

function requestsSent() {
  return echo.requests.map(request => `${request.url} ${request.headers.authorization}`);
}

// E2E-AUTH-02: a secret-using step changed outside the app waits for approval in the desktop UI,
// the rest of its flow still runs, and once approved the step runs in its changed form.
test('a secret-using step changed outside the app waits for approval in the desktop UI', async ({ page, browser, api, host }) => {
  test.setTimeout(180_000);
  const profile = await api.profile('E2E Approvals', { columns: 3, rows: 2 });
  await api.variable('e2e_flow_runs', { type: 'numeric', value: 0 });
  const secret = await api.post('/api/secrets', { value: 'token-from-the-store', kind: 'Secret' });
  const button = await api.actionButton(profile.rootFolder.id, {
    label: 'secret step',
    flows: [flow.shortPress(
      flow.action('app.macro-deck.http', 'send-request', {
        method: 'POST',
        url: echo.url('/approval'),
        bodyType: 'none',
        authType: 'bearer',
        authSecret: { $secret: secret.id },
      }),
      flow.setVariable('e2e_flow_runs', 1, 'add'),
    )],
  });

  let web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, profile.id);
  await pressWebTile(web, button.id);
  await expect.poll(() => requestsSent()).toEqual(['/approval Bearer token-from-the-store']);
  await web.context.close();

  await host.stop();
  const profileFile = join(host.dataDir, 'data', 'profiles', `${profile.id}.json`);
  const stored = readFileSync(profileFile, 'utf8');
  expect(stored.split(echo.url('/approval')).length).toBe(2);
  writeFileSync(profileFile, stored.replace(echo.url('/approval'), echo.url('/changed-on-disk')));
  await host.start();

  await expect.poll(async () => (await api.get('/api/secret-approvals')).count).toBe(1);

  web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, profile.id);
  await pressWebTile(web, button.id);
  await expect.poll(() => api.variableValue('e2e_flow_runs')).toBe('2');
  expect(requestsSent()).toEqual(['/approval Bearer token-from-the-store']);

  await openDesktop(page, host);
  await page.getByRole('link', { name: 'Approvals' }).click();
  const card = page.getByTestId('approval-card');
  await expect(card).toHaveCount(1);
  await card.getByRole('button', { name: 'Approve' }).click();
  await expect(card).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Approvals' })).toHaveCount(0);
  expect((await api.get('/api/secret-approvals')).count).toBe(0);

  await pressWebTile(web, button.id);
  await expect.poll(() => requestsSent())
    .toEqual(['/approval Bearer token-from-the-store', '/changed-on-disk Bearer token-from-the-store']);
  await web.context.close();
});
