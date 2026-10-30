import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { actionButtonData, flow } from './support/seed.js';
import { connectWebClient, pressWebTile, webTile } from './support/pages.js';

useIsolatedHost('export', { prepare: api => api.completeOnboarding() });

async function widgetVariable(api, widgetId, name) {
  return (await api.variables()).find(variable =>
    variable.scope === 'widget' && variable.scopeRefId === widgetId && variable.name === name) ?? null;
}

async function exportWidgets(api, folderId, widgetIds, password) {
  const response = await api.request.post(`${api.host.loopbackUrl}/api/widgets/export`, {
    headers: api.headers(),
    data: { folderId, widgetIds, includeSecrets: true, password },
  });
  expect(response.ok()).toBe(true);
  return await response.body();
}

async function importWidgets(api, folderId, archive, { x, y, password }) {
  const response = await api.request.post(`${api.host.loopbackUrl}/api/widgets/import`, {
    headers: api.headers(),
    multipart: {
      file: { name: 'button.mdwidgets', mimeType: 'application/zip', buffer: archive },
      folderId,
      anchorX: String(x),
      anchorY: String(y),
      password,
    },
  });
  expect(response.ok()).toBe(true);
  return await response.json();
}

// E2E-EXP-01: a button keeps its widget variable through a password-protected export and import,
// and a changed copy runs its own action when pressed, not the original's.
test('an exported button brings its widget variable along and a copy runs its own action', async ({ browser, api, host }) => {
  const profile = await api.profile('E2E Export', { columns: 4, rows: 2 });
  await api.variable('e2e_pressed_by', { value: 'nobody' });
  const original = await api.actionButton(profile.rootFolder.id, {
    label: 'n={{ vars.e2e_local }}',
    flows: [flow.shortPress(flow.setVariable('e2e_pressed_by', 'original'))],
  });
  await api.post('/api/variables', {
    name: 'e2e_local',
    type: 'numeric',
    scope: 'widget',
    scopeRefId: original.id,
    initialValue: '5',
  });

  const archive = await exportWidgets(api, profile.rootFolder.id, [original.id], 'right-password');

  const refused = await importWidgets(api, profile.rootFolder.id, archive, { x: 1, y: 0, password: 'wrong-password' });
  expect(refused).toMatchObject({ success: false, error: { code: 'InvalidPassword' } });
  expect((await api.rootFolder(profile.id)).widgets).toHaveLength(1);

  const imported = await importWidgets(api, profile.rootFolder.id, archive, { x: 1, y: 0, password: 'right-password' });
  expect(imported.success).toBe(true);
  expect(imported.widgets).toHaveLength(1);
  const importedId = imported.widgets[0].id;
  expect(importedId).not.toBe(original.id);
  expect(await widgetVariable(api, importedId, 'e2e_local')).toMatchObject({ value: '5' });

  const { widget: copy } = await api.post('/api/widgets', {
    folderId: profile.rootFolder.id,
    type: 'ActionButton',
    positionX: 2,
    positionY: 0,
    width: 1,
    height: 1,
    data: JSON.stringify(actionButtonData({
      label: 'n={{ vars.e2e_local }}',
      flows: [flow.shortPress(flow.setVariable('e2e_pressed_by', 'original'))],
    })),
    sourceWidgetId: original.id,
  });
  expect(await widgetVariable(api, copy.id, 'e2e_local')).toMatchObject({ value: '5' });

  const web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, profile.id);
  await expect(webTile(web.page, importedId)).toHaveText('n=5');
  await expect(webTile(web.page, copy.id)).toHaveText('n=5');

  await api.updateWidgetData(copy.id, {
    label: 'copy',
    flows: JSON.stringify([flow.shortPress(flow.setVariable('e2e_pressed_by', 'copy'))]),
  });
  await expect(webTile(web.page, copy.id)).toHaveText('copy');
  await pressWebTile(web, copy.id);
  await expect.poll(() => api.variableValue('e2e_pressed_by')).toBe('copy');

  await pressWebTile(web, original.id);
  await expect.poll(() => api.variableValue('e2e_pressed_by')).toBe('original');
  await web.context.close();
});
