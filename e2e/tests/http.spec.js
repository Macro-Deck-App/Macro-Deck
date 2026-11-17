import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { flow } from './support/seed.js';
import { connectWebClient, openDesktopProfile, webDeck, webTile } from './support/pages.js';

useIsolatedHost('http', { prepare: api => api.completeOnboarding() });

// Files whose URL names one version of their content (or that revalidate) set their own policy; every
// other /api answer is live state.
const ASSET_ROUTES = [
  /^\/api\/icons\/[^/]+\/image$/,
  /^\/api\/integrations\/[^/]+\/icon$/,
  /^\/api\/ui\/resources\/[^/]+$/,
  /^\/api\/music-player\/artwork\/[^/]+$/,
  /^\/api\/store\/review-avatars$/,
  /^\/api\/store\/media\/.+$/,
  /^\/api\/system\/fonts\/[^/]+\/file$/,
  /^\/api\/system\/third-party-notices(\.txt)?$/,
];

function recordApiGets(page) {
  const seen = [];
  page.on('response', response => {
    const url = new URL(response.url());
    if (response.request().method() === 'GET' && url.pathname.startsWith('/api/') && response.status() < 400) {
      seen.push({
        path: url.pathname,
        type: response.headers()['content-type'] ?? '',
        cacheControl: response.headers()['cache-control'] ?? '',
      });
    }
  });
  return seen;
}

function checkPolicies(seen) {
  const problems = [];
  for (const { path, type, cacheControl } of seen) {
    if (ASSET_ROUTES.some(route => route.test(path))) {
      if (cacheControl === '') {
        problems.push(`${path}: asset without a Cache-Control policy`);
      }
    } else if (!cacheControl.split(',').map(part => part.trim()).includes('no-store')) {
      problems.push(`${path} (${type}): Cache-Control "${cacheControl}"`);
    }
  }
  return problems;
}

test('every /api GET the desktop UI and the Web Client make is sent with no-store', async ({ page, browser, api, host }) => {
  test.setTimeout(120_000);
  const profile = await api.profile('E2E Http', { columns: 3, rows: 2 });
  await api.variable('e2e_http', { type: 'numeric', value: 0 });
  const button = await api.actionButton(profile.rootFolder.id, {
    label: 'http',
    flows: [flow.shortPress(flow.setVariable('e2e_http', 1))],
  });

  const desktop = recordApiGets(page);
  await openDesktopProfile(page, host, profile.name);
  await page.goto(`${host.loopbackUrl}/admin/#/settings`);
  await page.goto(`${host.loopbackUrl}/admin/#/flows`);
  await page.goto(`${host.loopbackUrl}/admin/#/deck`);
  await page.waitForLoadState('networkidle');

  const web = await connectWebClient(browser, host, api);
  const client = recordApiGets(web.page);
  await api.openProfileOnDevice(web.device.id, profile.id);
  await expect(webTile(web.page, button.id)).toBeVisible();
  await web.page.reload();
  await expect(webDeck(web.page)).toBeVisible();
  await web.page.waitForLoadState('networkidle');

  expect(desktop.length, 'the desktop UI made /api GETs').toBeGreaterThan(5);
  expect(client.length, 'the Web Client made /api GETs').toBeGreaterThan(2);
  expect(checkPolicies([...desktop, ...client])).toEqual([]);
  await web.context.close();
});

for (const path of ['/admin/chunk-DOESNOTEXIST.js', '/chunk-DOESNOTEXIST.js', '/admin/assets/DOESNOTEXIST.css']) {
  test(`a missing chunk ${path} is 404, not the HTML shell`, async ({ playwright, host }) => {
    const anonymous = await playwright.request.newContext();
    try {
      const response = await anonymous.get(`${host.publicUrl}${path}`);
      expect(response.status()).toBe(404);
      expect(response.headers()['content-type'] ?? '').not.toContain('text/html');
      expect(await response.text()).not.toContain('<html');
    } finally {
      await anonymous.dispose();
    }
  });
}

test('the shells still answer their routes', async ({ playwright, host }) => {
  const anonymous = await playwright.request.newContext();
  try {
    for (const path of ['/', '/admin/', '/admin/index.html']) {
      const response = await anonymous.get(`${host.publicUrl}${path}`);
      expect(response.status(), path).toBe(200);
      expect(response.headers()['content-type'] ?? '', path).toContain('text/html');
    }
  } finally {
    await anonymous.dispose();
  }
});
