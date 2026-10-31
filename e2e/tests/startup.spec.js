import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { flow } from './support/seed.js';

useIsolatedHost('startup', { prepare: api => api.completeOnboarding() });

async function restart(api, host) {
  const starts = host.starts;
  await api.restart('e2e-startup-events');
  await host.waitForRestart(starts);
}

// E2E-STRT-01: start-up events reach the automations listening for them on a real restart. Each
// variable is reset before the restart, so only the new start can have written it.
test('an Integration Connected automation runs after a real restart', async ({ api, host }) => {
  test.setTimeout(120_000);
  await api.variable('e2e_connected', { value: 'never' });
  await api.automation('E2E Integration Connected', [
    flow.onEvent('integration-connected', flow.setVariable('e2e_connected', 'connected')),
  ]);
  expect(await api.variableValue('e2e_connected')).toBe('never');

  await restart(api, host);

  await expect.poll(() => api.variableValue('e2e_connected'), { timeout: 30_000 }).toBe('connected');
});

test('a Server Started automation runs once after a real restart', async ({ api, host }) => {
  test.setTimeout(120_000);
  await api.variable('e2e_started', { value: 'never' });
  await api.variable('e2e_started_count', { type: 'numeric', value: 0 });
  await api.automation('E2E Server Started', [
    flow.onEvent('server-started',
      flow.setVariable('e2e_started', 'started'),
      flow.setVariable('e2e_started_count', 1, 'add')),
  ]);
  expect(await api.variableValue('e2e_started')).toBe('never');

  await restart(api, host);

  await expect.poll(() => api.variableValue('e2e_started'), { timeout: 30_000 }).toBe('started');
  expect(await api.variableValue('e2e_started_count')).toBe('1');
});
