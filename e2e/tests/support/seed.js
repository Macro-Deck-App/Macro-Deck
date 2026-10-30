import { randomUUID } from 'node:crypto';
import { expect } from '@playwright/test';

export const USERNAME = 'e2e-user';
export const PASSWORD = 'macro-deck-e2e-password';

const VARIABLES_INTEGRATION = 'app.macro-deck.variables';
const DECK_INTEGRATION = 'app.macro-deck.deck';

function blockId() {
  return randomUUID().replaceAll('-', '');
}

function parameter(name, value) {
  return { name, type: 'text', label: name, value };
}

// Builders for the stored flow JSON an action button or automation carries. They write the same
// shape the desktop editor saves, so the host runs them through its ordinary executor.
export const flow = {
  shortPress: (...children) => ({ triggerId: 'onShortPress', triggerType: 'onShortPress', children }),

  onEvent: (eventId, ...children) => ({
    triggerId: blockId(),
    triggerType: 'onEvent',
    event: { providerId: 'macro-deck', eventId, parameters: [] },
    children,
  }),

  action: (integrationId, actionId, parameters, label = actionId) => ({
    id: blockId(),
    type: 'action',
    blockType: `${integrationId}.${actionId}`,
    label,
    integrationId,
    actionId,
    parameters: Object.entries(parameters).map(([name, value]) => parameter(name, value)),
  }),

  setVariable: (variable, value, operation = 'set') => flow.action(VARIABLES_INTEGRATION,
    'set-variable',
    operation === 'toggle' ? { variable, operation } : { variable, operation, value: String(value) }),

  changeFolder: folderId => flow.action(DECK_INTEGRATION, 'change-folder', { folderId }),
  goBack: () => flow.action(DECK_INTEGRATION, 'go-back', {}),
  changeProfile: profileId => flow.action(DECK_INTEGRATION, 'change-profile', { profileId }),

  delay: milliseconds => ({
    id: blockId(),
    type: 'delay',
    blockType: 'delay',
    label: 'Delay',
    parameters: [parameter('duration', String(milliseconds))],
  }),

  condition: (when, then, otherwise = []) => ({
    id: blockId(),
    type: 'condition',
    blockType: 'condition',
    label: 'Condition',
    parameters: [],
    branches: [
      { id: blockId(), kind: 'if', condition: when, children: then },
      { id: blockId(), kind: 'else', children: otherwise },
    ],
  }),

  equals: (variable, value) => ({ kind: 'compare', operator: '==', left: { $var: variable }, right: value }),
  and: (...operands) => ({ kind: 'and', operands }),
  or: (...operands) => ({ kind: 'or', operands }),
};

export function actionButtonData({ label = '', flows = [], states, activeStateId, stateMapping, ...rest } = {}) {
  const data = { label, ...rest, flows: JSON.stringify(flows) };
  if (states) {
    data.stateMode = true;
    data.states = states.map(state => ({
      id: state.id,
      label: state.name ?? state.id,
      appearance: { label: state.label },
    }));
    data.activeStateId = activeStateId ?? states[0].id;
    if (stateMapping) {
      data.stateMapping = stateMapping;
    }
  }
  return data;
}

// Thin client over the host's own REST API on the trusted loopback listener: the same endpoints the
// desktop UI uses, so seeded data goes through the production validation and broadcast paths.
export class HostApi {
  constructor(host, request) {
    this.host = host;
    this.request = request;
  }

  headers() {
    return { 'X-MacroDeck-Loopback-Secret': this.host.secret };
  }

  async call(method, path, data, { expectOk = true } = {}) {
    const response = await this.request.fetch(`${this.host.loopbackUrl}${path}`, {
      method,
      headers: this.headers(),
      data,
    });
    if (expectOk) {
      expect(response.ok(), `${method} ${path} -> ${response.status()} ${await response.text()}`).toBe(true);
    }
    const text = await response.text();
    const body = text.length > 0 && (response.headers()['content-type'] ?? '').includes('json')
      ? JSON.parse(text)
      : text;
    if (expectOk && body && typeof body === 'object' && body.success === false) {
      throw new Error(`${method} ${path} failed: ${JSON.stringify(body.error)}`);
    }
    return { status: response.status(), body };
  }

  get(path) {
    return this.call('GET', path).then(result => result.body);
  }

  post(path, data) {
    return this.call('POST', path, data ?? {}).then(result => result.body);
  }

  put(path, data) {
    return this.call('PUT', path, data).then(result => result.body);
  }

  patch(path, data) {
    return this.call('PATCH', path, data).then(result => result.body);
  }

  delete(path) {
    return this.call('DELETE', path).then(result => result.body);
  }

  async useEnglish() {
    await this.put('/api/settings/localization', { culture: 'en' });
  }

  async createAccount() {
    await this.post('/api/auth/setup', { username: USERNAME, password: PASSWORD });
  }

  // Everything the first-run wizard would otherwise walk through: core.spec.js keeps the only
  // clicked path through it.
  async completeOnboarding({ clearNotifications = true } = {}) {
    await this.useEnglish();
    const status = await this.get('/api/auth/status');
    if (!status.setupComplete) {
      await this.createAccount();
    }
    await this.post('/api/settings/onboarding/complete');
    await this.post('/api/backups/recovery-key');
    await this.post('/api/backups/recovery-key/acknowledge');
    if (clearNotifications) {
      await this.call('DELETE', '/api/notifications', undefined, { expectOk: false });
    }
  }

  async profiles() {
    return (await this.get('/api/profiles')).profiles;
  }

  async profile(name, { columns = 5, rows = 3, ...rest } = {}) {
    const { profile } = await this.post('/api/profiles', {
      name,
      defaultColumns: columns,
      defaultRows: rows,
      ...rest,
    });
    return { ...profile, rootFolder: await this.rootFolder(profile.id) };
  }

  async folders(profileId) {
    const query = profileId ? `?profileId=${encodeURIComponent(profileId)}` : '';
    return (await this.get(`/api/folders${query}`)).folders;
  }

  async rootFolder(profileId) {
    const folders = await this.folders(profileId);
    const root = folders.find(folder => folder.profileId === profileId && !folder.parentId);
    expect(root, `root folder of profile ${profileId}`).toBeTruthy();
    return root;
  }

  async folder(profileId, name, parentId) {
    const { folder } = await this.post('/api/folders', { profileId, name, parentId });
    return folder;
  }

  async widget(folderId, { type = 'ActionButton', x = 0, y = 0, width = 1, height = 1, data = {} }) {
    const { widget } = await this.post('/api/widgets', {
      folderId,
      type,
      positionX: x,
      positionY: y,
      width,
      height,
      data: JSON.stringify(data),
    });
    return widget;
  }

  async actionButton(folderId, { x = 0, y = 0, ...button } = {}) {
    return await this.widget(folderId, { x, y, data: actionButtonData(button) });
  }

  async findWidget(widgetId) {
    for (const folder of await this.folders()) {
      const widget = folder.widgets.find(item => item.id === widgetId);
      if (widget) {
        return { ...widget, folderId: folder.id, parsed: JSON.parse(widget.data ?? '{}') };
      }
    }
    return null;
  }

  async updateWidgetData(widgetId, change) {
    const widget = await this.findWidget(widgetId);
    expect(widget, `widget ${widgetId}`).toBeTruthy();
    const data = typeof change === 'function' ? change(widget.parsed) : { ...widget.parsed, ...change };
    return await this.put('/api/widgets', {
      id: widget.id,
      folderId: widget.folderId,
      type: widget.type,
      positionX: widget.positionX,
      positionY: widget.positionY,
      width: widget.width,
      height: widget.height,
      data: JSON.stringify(data),
    });
  }

  async variables() {
    return (await this.get('/api/variables')).variables;
  }

  async findVariable(name) {
    return (await this.variables()).find(variable => variable.name === name && variable.scope === 'global') ?? null;
  }

  async variableValue(name) {
    return (await this.findVariable(name))?.value ?? null;
  }

  async variable(name, { type = 'text', value } = {}) {
    const { variable } = await this.post('/api/variables', {
      name,
      type,
      scope: 'global',
      initialValue: value === undefined ? undefined : String(value),
    });
    return variable;
  }

  async setVariable(name, value) {
    const variable = await this.findVariable(name);
    expect(variable, `variable ${name}`).toBeTruthy();
    await this.patch(`/api/variables/${variable.id}/value`, { value: String(value) });
  }

  async deleteVariable(name) {
    const variable = await this.findVariable(name);
    expect(variable, `variable ${name}`).toBeTruthy();
    await this.delete(`/api/variables/${variable.id}`);
  }

  async automation(name, flows) {
    const { automation } = await this.post('/api/automations', { name, flows: JSON.stringify(flows) });
    if (!automation.enabled) {
      await this.put(`/api/automations/${automation.id}`, { id: automation.id, enabled: true });
    }
    return automation;
  }

  async devices() {
    return (await this.get('/api/devices')).devices;
  }

  async openProfileOnDevice(deviceId, profileId) {
    await this.post(`/api/devices/${deviceId}/open-profile`, { profileId });
  }

  async pairingCode() {
    return (await this.get('/api/auth/pairing-code')).code;
  }

  async restart(reason = 'e2e') {
    const result = await this.post('/api/host/restart', { reason });
    expect(result).toMatchObject({ supported: true, success: true });
  }
}
