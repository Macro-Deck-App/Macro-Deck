import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { EventDefinitionDto } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { EventCatalogService } from './event-catalog.service';

function ref(scope: string, key: string): { $localized: { scope: string; key: string } } {
  return { $localized: { scope, key } };
}

describe('EventCatalogService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let service: EventCatalogService;

  beforeEach(() => {
    localStorage.clear();

    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getEventDefinitions',
      'triggerEvent',
      'onNotification',
      'getLocalization',
      'updateLocalizationSettings',
    ]);
    apiSpy.getEventDefinitions.and.resolveTo({ events: [] });
    apiSpy.onNotification.and.returnValue(new Subject().asObservable());

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    service = TestBed.inject(EventCatalogService);
  });

  afterEach(() => localStorage.clear());

  async function setTranslations(translations: Record<string, string>, culture = 'en'): Promise<void> {
    apiSpy.getLocalization.and.resolveTo({
      culture,
      fallbackCulture: 'en',
      translations,
      followSystem: false,
      availableCultures: ['en', 'de'],
    });
    await TestBed.inject(LocalizationService).loadFromHost();
  }

  it('passes a trigger straight through to the host', async () => {
    apiSpy.triggerEvent.and.resolveTo({ success: true, queuedSubscriptions: 3 });

    const response = await service.trigger('obs::scene-changed', { sceneName: 'Intro' });

    expect(apiSpy.triggerEvent).toHaveBeenCalledWith({
      eventId: 'obs::scene-changed',
      parameters: { sceneName: 'Intro' },
    });
    expect(response.queuedSubscriptions).toBe(3);
  });

  it('leaves the catalogue error signal alone when a trigger fails', async () => {
    apiSpy.triggerEvent.and.rejectWith(new Error('offline'));

    await expectAsync(service.trigger('obs::scene-changed', {})).toBeRejected();

    expect(service.error()).toBeNull();
  });

  it('resolves every localized text field on an event, not just the name', async () => {
    await setTranslations({
      'macrodeck.app:Obs.ProviderName': 'OBS Studio',
      'macrodeck.app:Obs.SceneChanged.Name': 'Scene Changed',
      'macrodeck.app:Obs.SceneChanged.Description': 'Fires when the active scene changes.',
      'macrodeck.app:Obs.SceneChanged.Category': 'Scenes',
    });

    apiSpy.getEventDefinitions.and.resolveTo({
      events: [{
        id: 'obs::scene-changed',
        providerId: 'obs',
        providerName: ref('macrodeck.app', 'Obs.ProviderName'),
        isIntegration: true,
        name: ref('macrodeck.app', 'Obs.SceneChanged.Name'),
        description: ref('macrodeck.app', 'Obs.SceneChanged.Description'),
        category: ref('macrodeck.app', 'Obs.SceneChanged.Category'),
        deliveryKind: 'push',
        configurationParameters: [],
        payloadParameters: [],
      } as unknown as EventDefinitionDto],
    });

    await service.load();
    const [event] = service.events();

    expect(event.providerName).toBe('OBS Studio');
    expect(event.name).toBe('Scene Changed');
    expect(event.description).toBe('Fires when the active scene changes.');
    expect(event.category).toBe('Scenes');
  });

  it('leaves a bare-string field untouched instead of treating it as a key', async () => {
    apiSpy.getEventDefinitions.and.resolveTo({
      events: [{
        id: 'macro-deck::custom',
        providerId: 'macro-deck',
        providerName: 'Macro Deck',
        isIntegration: false,
        name: 'Custom event',
        deliveryKind: 'push',
        configurationParameters: [],
        payloadParameters: [],
      } as unknown as EventDefinitionDto],
    });

    await service.load();
    const [event] = service.events();

    expect(event.name).toBe('Custom event');
    expect(event.name).not.toBe('[[macrodeck.app:Custom event]]');
  });

  it('re-resolves labels on a language switch without refetching the catalogue', async () => {
    await setTranslations({
      'macrodeck.app:Obs.SceneChanged.Name': 'Scene Changed',
      'macrodeck.app:Obs.SceneChanged.Category': 'Scenes',
    }, 'en');

    apiSpy.getEventDefinitions.and.resolveTo({
      events: [{
        id: 'obs::scene-changed',
        providerId: 'obs',
        providerName: 'OBS Studio',
        isIntegration: true,
        name: ref('macrodeck.app', 'Obs.SceneChanged.Name'),
        category: ref('macrodeck.app', 'Obs.SceneChanged.Category'),
        deliveryKind: 'push',
        configurationParameters: [],
        payloadParameters: [],
      } as unknown as EventDefinitionDto],
    });

    await service.load();
    expect(service.events()[0].name).toBe('Scene Changed');
    expect(service.events()[0].category).toBe('Scenes');

    await setTranslations({
      'macrodeck.app:Obs.SceneChanged.Name': 'Szene geändert',
      'macrodeck.app:Obs.SceneChanged.Category': 'Szenen',
    }, 'de');

    expect(service.events()[0].name).toBe('Szene geändert');
    expect(service.events()[0].category).toBe('Szenen');
    expect(apiSpy.getEventDefinitions).toHaveBeenCalledTimes(1);
  });

  it('resolves provider and event text for every provider, whatever its localization scope', async () => {
    await setTranslations({
      'macrodeck.app:Obs.Provider': 'OBS Studio',
      'macrodeck.app:Obs.SceneChanged.Name': 'Scene Changed',
      'macrodeck.app:Obs.SceneChanged.Category': 'Scenes',
      'macrodeck.app:Twitch.Provider': 'Twitch',
      'macrodeck.app:Twitch.FollowerGained.Name': 'New Follower',
      'macrodeck.app:Twitch.FollowerGained.Category': 'Community',
      'macrodeck.app:HomeAssistant.Provider': 'Home Assistant',
      'macrodeck.app:HomeAssistant.StateChanged.Name': 'State Changed',
      'macrodeck.app:HomeAssistant.StateChanged.Category': 'Entities',
      'plugin.acme-widgets:Provider': 'Acme Widgets',
      'plugin.acme-widgets:WidgetPressed.Name': 'Widget Pressed',
      'plugin.acme-widgets:WidgetPressed.Category': 'Input',
    });

    apiSpy.getEventDefinitions.and.resolveTo({
      events: [
        {
          id: 'obs::scene-changed', providerId: 'obs', providerName: ref('macrodeck.app', 'Obs.Provider'),
          isIntegration: true, name: ref('macrodeck.app', 'Obs.SceneChanged.Name'),
          category: ref('macrodeck.app', 'Obs.SceneChanged.Category'),
          deliveryKind: 'push', configurationParameters: [], payloadParameters: [],
        },
        {
          id: 'twitch::follower-gained', providerId: 'twitch', providerName: ref('macrodeck.app', 'Twitch.Provider'),
          isIntegration: true, name: ref('macrodeck.app', 'Twitch.FollowerGained.Name'),
          category: ref('macrodeck.app', 'Twitch.FollowerGained.Category'),
          deliveryKind: 'push', configurationParameters: [], payloadParameters: [],
        },
        {
          id: 'home-assistant::state-changed', providerId: 'home-assistant',
          providerName: ref('macrodeck.app', 'HomeAssistant.Provider'), isIntegration: true,
          name: ref('macrodeck.app', 'HomeAssistant.StateChanged.Name'),
          category: ref('macrodeck.app', 'HomeAssistant.StateChanged.Category'),
          deliveryKind: 'push', configurationParameters: [], payloadParameters: [],
        },
        {
          id: 'acme-widgets::widget-pressed', providerId: 'acme-widgets',
          providerName: ref('plugin.acme-widgets', 'Provider'), isIntegration: true,
          name: ref('plugin.acme-widgets', 'WidgetPressed.Name'),
          category: ref('plugin.acme-widgets', 'WidgetPressed.Category'),
          deliveryKind: 'push', configurationParameters: [], payloadParameters: [],
        },
      ] as unknown as EventDefinitionDto[],
    });

    await service.load();
    const groups = service.groups();

    expect(groups.find(g => g.providerId === 'obs')?.providerName).toBe('OBS Studio');
    expect(groups.find(g => g.providerId === 'obs')?.events[0].name).toBe('Scene Changed');
    expect(groups.find(g => g.providerId === 'obs')?.events[0].category).toBe('Scenes');

    expect(groups.find(g => g.providerId === 'twitch')?.providerName).toBe('Twitch');
    expect(groups.find(g => g.providerId === 'twitch')?.events[0].name).toBe('New Follower');
    expect(groups.find(g => g.providerId === 'twitch')?.events[0].category).toBe('Community');

    expect(groups.find(g => g.providerId === 'home-assistant')?.providerName).toBe('Home Assistant');
    expect(groups.find(g => g.providerId === 'home-assistant')?.events[0].name).toBe('State Changed');
    expect(groups.find(g => g.providerId === 'home-assistant')?.events[0].category).toBe('Entities');

    expect(groups.find(g => g.providerId === 'acme-widgets')?.providerName).toBe('Acme Widgets');
    expect(groups.find(g => g.providerId === 'acme-widgets')?.events[0].name).toBe('Widget Pressed');
    expect(groups.find(g => g.providerId === 'acme-widgets')?.events[0].category).toBe('Input');
  });

  it('resolves every user-facing string reachable from groups(), with none left unresolved or blank', async () => {
    await setTranslations({
      'macrodeck.app:Obs.Provider': 'OBS Studio',
      'macrodeck.app:Obs.SceneChanged.Name': 'Scene Changed',
      'macrodeck.app:Obs.SceneChanged.Description': 'Fires when the active scene changes.',
      'macrodeck.app:Obs.SceneChanged.Category': 'Scenes',
      'macrodeck.app:Obs.SceneName.Label': 'Scene name',
      'macrodeck.app:Obs.SceneName.Description': 'Which scene to filter on.',
      'macrodeck.app:Obs.SceneName.Option.Intro': 'Intro',
      'macrodeck.app:Obs.Elapsed.Label': 'Elapsed seconds',
    });

    apiSpy.getEventDefinitions.and.resolveTo({
      events: [{
        id: 'obs::scene-changed',
        providerId: 'obs',
        providerName: ref('macrodeck.app', 'Obs.Provider'),
        isIntegration: true,
        name: ref('macrodeck.app', 'Obs.SceneChanged.Name'),
        description: ref('macrodeck.app', 'Obs.SceneChanged.Description'),
        category: ref('macrodeck.app', 'Obs.SceneChanged.Category'),
        deliveryKind: 'push',
        configurationParameters: [
          {
            name: 'sceneName',
            type: 'Select',
            label: ref('macrodeck.app', 'Obs.SceneName.Label'),
            description: ref('macrodeck.app', 'Obs.SceneName.Description'),
            options: [{ value: 'intro', label: ref('macrodeck.app', 'Obs.SceneName.Option.Intro') }],
          },
        ],
        payloadParameters: [
          { name: 'elapsedSeconds', type: 'Number', label: ref('macrodeck.app', 'Obs.Elapsed.Label') },
        ],
      } as unknown as EventDefinitionDto],
    });

    await service.load();
    const groups = service.groups();

    const strings: unknown[] = [];
    for (const group of groups) {
      strings.push(group.providerName);
      for (const event of group.events) {
        strings.push(event.name, event.description, event.category);
        for (const param of [...event.configurationParameters, ...event.payloadParameters]) {
          strings.push(param.label, param.description);
          for (const option of param.options ?? []) strings.push(option.label);
        }
      }
    }

    expect(strings).toEqual([
      'OBS Studio',
      'Scene Changed',
      'Fires when the active scene changes.',
      'Scenes',
      'Scene name',
      'Which scene to filter on.',
      'Intro',
      'Elapsed seconds',
      undefined,
    ]);

    for (const value of strings) {
      if (value === undefined) continue;
      expect(typeof value).toBe('string');
      expect(value as string).not.toContain('[object Object]');
      expect((value as string).startsWith('[[')).toBeFalse();
      expect((value as string).length).toBeGreaterThan(0);
    }
  });

  it('resolves a configuration parameter and its choice option to exact text', async () => {
    await setTranslations({
      'macrodeck.app:Param.Label': 'Scene name',
      'macrodeck.app:Param.Description': 'Which scene to filter on.',
      'macrodeck.app:Param.Option.Intro': 'Intro',
    });

    apiSpy.getEventDefinitions.and.resolveTo({
      events: [{
        id: 'obs::scene-changed',
        providerId: 'obs',
        providerName: 'OBS',
        isIntegration: true,
        name: 'Scene Changed',
        deliveryKind: 'push',
        configurationParameters: [
          {
            name: 'sceneName',
            type: 'Select',
            label: ref('macrodeck.app', 'Param.Label'),
            description: ref('macrodeck.app', 'Param.Description'),
            options: [{ value: 'intro', label: ref('macrodeck.app', 'Param.Option.Intro') }],
          },
        ],
        payloadParameters: [],
      } as unknown as EventDefinitionDto],
    });

    await service.load();
    const [param] = service.events()[0].configurationParameters;

    expect(param.label).toBe('Scene name');
    expect(param.description).toBe('Which scene to filter on.');
    expect(param.options?.[0].label).toBe('Intro');
  });
});
