import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { countLoads, signInWebClient, webDeck } from './support/pages.js';

useIsolatedHost('startup-resilience', { prepare: api => api.completeOnboarding() });

const NEW_COMMIT = 'feedbee';

// The service worker answers the shell from its cache, where page.route cannot see it; these tests are
// about the client's own startup and version logic, so it is kept out of the way.
async function signedInClient(browser, host) {
  const context = await browser.newContext({ serviceWorkers: 'block' });
  const page = await context.newPage();
  page.pendingRequests = trackRequests(page);
  await page.goto(host.publicUrl);
  await signInWebClient(page);
  return { context, page };
}

async function reportCommit(route, commit) {
  const response = await route.fetch();
  await route.fulfill({ response, json: { ...await response.json(), commit } });
}

function trackRequests(page) {
  const pending = new Set();
  page.on('request', request => pending.add(request));
  page.on('requestfinished', request => pending.delete(request));
  page.on('requestfailed', request => pending.delete(request));
  page.on('framenavigated', frame => {
    if (frame === page.mainFrame()) {
      pending.clear();
    }
  });
  return pending;
}

// A socket drop and reconnect is what makes a running client ask the host for its build again. The
// client must have finished its own version check first: cutting the network mid-check tests the cut.
async function reconnect(context) {
  const page = context.pages()[0];
  let quietSince = Date.now();
  await expect.poll(() => {
    if (page.pendingRequests.size > 0) {
      quietSince = Date.now();
    }
    return Date.now() - quietSince;
  }, { intervals: [100], timeout: 30_000 }).toBeGreaterThanOrEqual(1_000);
  await context.setOffline(true);
  await new Promise(resolve => setTimeout(resolve, 3_000));
  await context.setOffline(false);
}

test('the Web Client reaches its deck when its first /api responses are delayed or aborted', async ({ browser, host }) => {
  test.setTimeout(120_000);
  const { context, page } = await signedInClient(browser, host);
  let seen = 0;
  await context.route(/\/api\//, async route => {
    seen += 1;
    if (seen <= 2) {
      await route.abort('connectionreset');
    } else if (seen <= 5) {
      await new Promise(resolve => setTimeout(resolve, 8_000));
      await route.continue();
    } else {
      await route.continue();
    }
  });

  await page.reload();

  await expect(webDeck(page)).toBeVisible({ timeout: 60_000 });
  await expect(page.getByText('Sign in to continue')).toHaveCount(0);
  expect(seen).toBeGreaterThan(5);
  await context.close();
});

test('a new build after a host update reloads the Web Client exactly once', async ({ browser, host }) => {
  test.setTimeout(120_000);
  const { context, page } = await signedInClient(browser, host);
  // A shell answered through page.route loses Chrome's local-network status, so the reloaded page
  // cannot carry the new build itself: once it has reloaded, the host reports the build it runs.
  let phase = 'before';
  page.on('request', request => {
    if (phase === 'updated' && request.isNavigationRequest() && request.url().includes('md-reload=')) {
      phase = 'reloaded';
    }
  });
  await context.route(/\/api\/system\/build-info/, async route => {
    if (phase === 'updated') {
      await reportCommit(route, NEW_COMMIT);
    } else {
      await route.continue();
    }
  });
  const loads = countLoads(page);

  phase = 'updated';
  await reconnect(context);

  await expect.poll(() => loads.loads, { timeout: 30_000 }).toBe(1);
  expect(phase).toBe('reloaded');
  await expect(webDeck(page)).toBeVisible({ timeout: 30_000 });
  await expect(page.locator('#macro-deck-outdated-ui')).toHaveCount(0);
  await reconnect(context);
  await expect(webDeck(page)).toBeVisible();
  expect(loads.loads).toBe(1);
  await context.close();
});

test('a build the reload cannot fix shows the outdated screen after a single reload', async ({ browser, host }) => {
  test.setTimeout(120_000);
  const { context, page } = await signedInClient(browser, host);
  await context.route(/\/api\/system\/build-info/, route => reportCommit(route, NEW_COMMIT));
  const loads = countLoads(page);

  await reconnect(context);

  await expect(page.locator('#macro-deck-outdated-ui')).toBeVisible({ timeout: 30_000 });
  await reconnect(context);
  await expect(page.locator('#macro-deck-outdated-ui')).toBeVisible();
  expect(loads.loads).toBe(1);
  await context.close();
});
