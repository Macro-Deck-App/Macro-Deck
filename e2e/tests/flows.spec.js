import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { startEchoServer } from './support/echo-server.js';
import { flow } from './support/seed.js';
import { connectWebClient, pressWebTile, webTile } from './support/pages.js';

useIsolatedHost('flows', { prepare: api => api.completeOnboarding() });

let echo;

test.beforeAll(async () => {
  echo = await startEchoServer();
});

test.afterAll(async () => {
  await echo?.close();
});

function httpRequest(url, jsonBody) {
  return flow.action('app.macro-deck.http', 'send-request', {
    method: 'POST',
    url,
    bodyType: jsonBody === undefined ? 'none' : 'json',
    ...jsonBody === undefined ? {} : { jsonBody },
  });
}

// E2E-FLOW-03: a flow's steps see each other's effects, grouped conditions evaluate as a whole,
// Liquid labels render in the Web Client and a failing step is reported as a failure.
test('a flow step reads the variable an earlier step set, and the Liquid label shows it', async ({ browser, api, host }) => {
  const profile = await api.profile('E2E Flow Freshness', { columns: 3, rows: 2 });
  await api.variable('e2e_message', { value: 'stale' });
  const button = await api.actionButton(profile.rootFolder.id, {
    label: '{{ vars.e2e_message | upcase }}',
    flows: [flow.shortPress(
      flow.setVariable('e2e_message', 'fresh'),
      httpRequest(echo.url('/fresh'), '{"message":"{{ vars.e2e_message }}"}'),
    )],
  });

  const web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, profile.id);
  await expect(webTile(web.page, button.id)).toHaveText('STALE');

  await pressWebTile(web, button.id);
  await expect.poll(() => echo.requests.filter(request => request.url === '/fresh').length).toBe(1);
  const sent = echo.requests.find(request => request.url === '/fresh');
  expect(sent.method).toBe('POST');
  expect(JSON.parse(sent.body)).toEqual({ message: 'fresh' });
  await expect(webTile(web.page, button.id)).toHaveText('FRESH');
  await web.context.close();
});

test('a grouped condition with one false group takes the else branch', async ({ browser, api, host }) => {
  const profile = await api.profile('E2E Flow Condition', { columns: 3, rows: 2 });
  await api.variable('e2e_a', { value: 'x' });
  await api.variable('e2e_b', { value: 'other' });
  await api.variable('e2e_branch', { value: 'none' });
  const button = await api.actionButton(profile.rootFolder.id, {
    label: 'branch {{ vars.e2e_branch }}',
    flows: [flow.shortPress(flow.condition(
      flow.and(flow.or(flow.equals('e2e_a', 'x'), flow.equals('e2e_a', 'y')), flow.equals('e2e_b', 'z')),
      [flow.setVariable('e2e_branch', 'then')],
      [flow.setVariable('e2e_branch', 'else')],
    ))],
  });

  const web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, profile.id);
  await pressWebTile(web, button.id);
  await expect(webTile(web.page, button.id)).toHaveText('branch else');

  await api.setVariable('e2e_b', 'z');
  await pressWebTile(web, button.id);
  await expect(webTile(web.page, button.id)).toHaveText('branch then');
  await web.context.close();
});

test('a failing step shows an error in the Web Client instead of passing silently', async ({ browser, api, host }) => {
  const profile = await api.profile('E2E Flow Failure', { columns: 3, rows: 2 });
  const failing = await api.actionButton(profile.rootFolder.id, {
    x: 0,
    label: 'fails',
    flows: [flow.shortPress(httpRequest(echo.url('/status/500')))],
  });
  const working = await api.actionButton(profile.rootFolder.id, {
    x: 1,
    label: 'works',
    flows: [flow.shortPress(httpRequest(echo.url('/status/200')))],
  });

  const web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, profile.id);
  const errorToast = web.page.locator('.wc-toast-error');

  await pressWebTile(web, working.id);
  await expect.poll(() => echo.requests.filter(request => request.url === '/status/200').length).toBe(1);
  await pressWebTile(web, failing.id);
  await expect.poll(() => echo.requests.filter(request => request.url === '/status/500').length).toBe(1);
  await expect(errorToast).toHaveCount(1);
  await web.context.close();
});
