import { expect, test } from './support/fixtures.js';
import { freePort, MacroDeckHost, occupyPort } from './support/host.js';
import { HostApi } from './support/seed.js';
import { openDesktop } from './support/pages.js';

// These hosts are started inside the test, after the test has taken one of their ports: the spec
// is about what a host does when it finds a port in use, so no shared host would do.
async function startWithTakenPorts(name, taken) {
  const ports = { loopback: await freePort(), public: await freePort(), https: await freePort() };
  const blockers = await Promise.all(taken.map(which => occupyPort(ports[which])));
  const blocker = { close: () => Promise.all(blockers.map(item => item.close())) };
  const host = new MacroDeckHost(name, { ports });
  try {
    await host.start({ requirePublic: false });
  } catch (error) {
    await blocker.close();
    throw error;
  }
  return { host, blocker, ports };
}

async function stop(started) {
  await started?.host.stop();
  await started?.blocker.close();
}

// E2E-HOST-02: taken public ports do not stop Macro Deck from starting: the host still serves the
// desktop UI on loopback, and the network settings name the port that could not be opened.
test('taken public ports leave the host running on loopback and the settings name the port', async ({ page, request }) => {
  test.setTimeout(180_000);
  let started;
  try {
    started = await startWithTakenPorts('public-port', ['public', 'https']);
    const { host, ports } = started;
    const api = new HostApi(host, request);
    await api.completeOnboarding({ clearNotifications: false });

    const network = await api.get('/api/settings/network');
    expect(network).toMatchObject({ publicListenerUnavailable: true, activePublicPort: ports.public });

    await openDesktop(page, host);
    await page.getByRole('button', { name: 'Settings', exact: true }).click();
    await page.locator('app-settings-modal').getByRole('button', { name: 'Network', exact: true }).click();
    await expect(page.getByTestId('network-listener-unavailable')).toContainText(String(ports.public));
  } finally {
    await stop(started);
  }
});

test('a taken HTTPS port leaves the plain public listener working and HTTPS reported as not running', async ({ request }) => {
  test.setTimeout(180_000);
  let started;
  try {
    started = await startWithTakenPorts('https-port', ['https']);
    const { host } = started;
    const api = new HostApi(host, request);
    await api.completeOnboarding({ clearNotifications: false });

    const status = await request.get(`${host.publicUrl}/api/auth/status`);
    expect(status.ok()).toBe(true);
    const network = await api.get('/api/settings/network');
    expect(network).toMatchObject({ publicListenerUnavailable: false, activeTlsHttpsPort: null });
  } finally {
    await stop(started);
  }
});
