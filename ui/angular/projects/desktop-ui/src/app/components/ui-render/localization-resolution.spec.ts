import { TestBed } from '@angular/core/testing';
import { EMPTY, Observable } from 'rxjs';
import { UiNode } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { el, renderTree, tick } from './ui-render-test-support';

interface Catalog {
  culture: string;
  fallbackCulture: string;
  translations: Record<string, string>;
}

function apiSpyWithCatalog(catalog: Catalog): jasmine.SpyObj<ApiService> {
  const api = jasmine.createSpyObj<ApiService>('ApiService', ['getLocalization', 'onNotification']);
  api.getLocalization.and.resolveTo({ ...catalog, followSystem: false, availableCultures: [catalog.culture] });
  api.onNotification.and.callFake(<T>(): Observable<T> => EMPTY);
  return api;
}

async function renderWithCatalog(root: UiNode, catalog: Catalog) {
  const api = apiSpyWithCatalog(catalog);
  const rendered = await renderTree(root, null, [{ provide: ApiService, useValue: api }]);
  await TestBed.inject(LocalizationService).loadFromHost();
  await tick(rendered);
  return { rendered, api };
}

async function type(input: HTMLInputElement, value: string): Promise<void> {
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('shared-ui-tree localization resolution', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
    localStorage.clear();
  });

  it('resolves a localized label, a localized dropdown option label, and a localized validation message', async () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [
        {
          id: 'apiKey',
          type: 'string',
          properties: {
            label: { $localized: { scope: 'plugin:com.example.spotify', key: 'Configuration.ApiKey' } },
            literalOnly: true,
          },
        },
        {
          id: 'color',
          type: 'choice',
          properties: {
            value: 'red',
            options: [{ value: 'red', label: { $localized: { scope: 'macrodeck', key: 'Color.Red' } } }],
          },
        },
        {
          id: 'apiKey-error',
          type: 'validation-message',
          properties: {
            for: 'apiKey',
            text: {
              $localized: { scope: 'macrodeck', key: 'Validation.Required', arguments: { field: 'API key' } },
            },
          },
        },
      ],
    };

    const { rendered } = await renderWithCatalog(root, {
      culture: 'de-DE',
      fallbackCulture: 'en',
      translations: {
        'plugin:com.example.spotify:Configuration.ApiKey': 'API-Schlüssel',
        'macrodeck:Color.Red': 'Rot',
        'macrodeck:Validation.Required': '{field} is required',
      },
    });
    const host = el(rendered);

    expect(host.querySelector('[data-node-id="apiKey"] .config-node-label')?.textContent).toContain('API-Schlüssel');
    expect(host.querySelector('[data-node-id="color"] .sel-label')?.textContent).toContain('Rot');
    expect(host.querySelector('[data-node-id="apiKey"] .config-node-error')?.textContent)
      .toContain('API key is required');
  });

  it('resolves localized chrome text (a Prose node) the same way as an input label', async () => {
    const root: UiNode = {
      id: 'intro',
      type: 'prose',
      properties: { text: { $localized: { scope: 'plugin:com.example.spotify', key: 'Setup.Intro' } } },
    };
    const { rendered } = await renderWithCatalog(root, {
      culture: 'de-DE',
      fallbackCulture: 'en',
      translations: { 'plugin:com.example.spotify:Setup.Intro': 'Willkommen' },
    });

    expect(el(rendered).querySelector('.config-chrome-prose')?.textContent).toContain('Willkommen');
  });

  it('renders an unresolvable key as the exact bracketed fallback, never blank', async () => {
    const root: UiNode = {
      id: 'name',
      type: 'string',
      properties: {
        label: { $localized: { scope: 'plugin:com.example.spotify', key: 'Missing.Key' } },
        literalOnly: true,
      },
    };
    const { rendered } = await renderWithCatalog(root, { culture: 'de-DE', fallbackCulture: 'en', translations: {} });

    const labelText = el(rendered).querySelector('.config-node-label')?.textContent?.trim();
    expect(labelText).toBe('[[plugin:com.example.spotify:Missing.Key]]');
    expect(labelText).not.toBe('');
  });

  it('renders a plain-string label unchanged, and an option with no label falls back to its value', async () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [
        { id: 'name', type: 'string', properties: { label: 'Plain Label', literalOnly: true } },
        { id: 'color', type: 'choice', properties: { value: 'blue', options: [{ value: 'blue' }] } },
      ],
    };
    const { rendered } = await renderWithCatalog(root, { culture: 'en', fallbackCulture: 'en', translations: {} });
    const host = el(rendered);

    expect(host.querySelector('[data-node-id="name"] .config-node-label')?.textContent).toContain('Plain Label');
    expect(host.querySelector('[data-node-id="color"] .sel-label')?.textContent?.trim()).toBe('blue');
  });

  it('re-renders an already-rendered surface on a culture change, preserving typed value and focus, without opening a new session', async () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [
        {
          id: 'name',
          type: 'string',
          properties: {
            label: { $localized: { scope: 'macrodeck', key: 'Common.Save' } },
            value: 'from tree',
            literalOnly: true,
          },
        },
      ],
    };
    const { rendered, api } = await renderWithCatalog(root, {
      culture: 'en',
      fallbackCulture: 'en',
      translations: { 'macrodeck:Common.Save': 'Save' },
    });
    const host = el(rendered);
    const inputBefore = host.querySelector('[data-node-id="name"] input') as HTMLInputElement;

    await type(inputBefore, 'typed by the user');
    await tick(rendered);
    inputBefore.focus();
    expect(document.activeElement).toBe(inputBefore);
    expect(host.querySelector('.config-node-label')?.textContent).toContain('Save');

    api.getLocalization.and.resolveTo({
      culture: 'de-DE',
      fallbackCulture: 'en',
      translations: { 'macrodeck:Common.Save': 'Speichern' },
      followSystem: false,
      availableCultures: ['en', 'de-DE'],
    });
    await TestBed.inject(LocalizationService).loadFromHost();
    await tick(rendered);

    const inputAfter = host.querySelector('[data-node-id="name"] input') as HTMLInputElement;
    expect(host.querySelector('[data-node-id="name"] .config-node-label')?.textContent).toContain('Speichern');
    expect(inputAfter).toBe(inputBefore);
    expect(inputAfter.value).toBe('typed by the user');
    expect(document.activeElement).toBe(inputAfter);
  });
});
