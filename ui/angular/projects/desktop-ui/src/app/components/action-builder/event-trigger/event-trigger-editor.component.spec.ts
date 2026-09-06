import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ActionBlockParameter, ActionFlow, EVENT_TRIGGER_TYPE, EventDefinition, EventDefinitionDto } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { EventCatalogService } from '../../../services/event-catalog.service';
import { ActionFlowStore } from '../services/action-flow.store';
import { EventTriggerEditorComponent } from './event-trigger-editor.component';

describe('EventTriggerEditorComponent parameters computed', () => {
  let component: EventTriggerEditorComponent;

  const definition: EventDefinition = {
    id: 'obs::scene-changed',
    providerId: 'obs',
    providerName: 'OBS',
    isIntegration: true,
    name: 'Scene Changed',
    deliveryKind: 'push',
    configurationParameters: [
      { name: 'sceneName', type: 'string', label: 'Scene' },
      { name: 'elapsedSeconds', type: 'number', label: 'Elapsed seconds' },
    ],
    payloadParameters: [
      { name: 'sceneName', type: 'string', label: 'Scene' },
      { name: 'elapsedSeconds', type: 'number', label: 'Elapsed seconds' },
    ],
  };

  function flowWith(parameters: ActionBlockParameter[]): ActionFlow {
    return {
      triggerId: 'trigger-1',
      triggerType: EVENT_TRIGGER_TYPE,
      event: { providerId: 'obs', eventId: 'scene-changed', parameters },
      children: [],
    };
  }

  function parameters(): ActionBlockParameter[] {
    return (component as unknown as { parameters(): ActionBlockParameter[] }).parameters();
  }

  beforeEach(() => {
    const catalog: Partial<EventCatalogService> = {
      loading: signal(false),
      error: signal<string | null>(null),
      groups: () => [],
      find: () => definition,
      load: () => Promise.resolve(),
    };
    const store: Partial<ActionFlowStore> = {
      variables: signal([]),
    };

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [EventTriggerEditorComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: EventCatalogService, useValue: catalog },
        { provide: ActionFlowStore, useValue: store },
        { provide: ApiService, useValue: apiSpy },
      ],
    });

    component = TestBed.createComponent(EventTriggerEditorComponent).componentInstance;
  });

  it('carries a stored operator through the merge', () => {
    component.flow = flowWith([
      { name: 'sceneName', type: 'string', label: 'Scene', value: '' },
      { name: 'elapsedSeconds', type: 'number', label: 'Elapsed seconds', value: 30, operator: '>' },
    ]);

    expect(parameters().find(p => p.name === 'elapsedSeconds')?.operator).toBe('>');
  });

  it('leaves the operator undefined when nothing is stored, same as an absent value', () => {
    component.flow = flowWith([
      { name: 'sceneName', type: 'string', label: 'Scene', value: '' },
    ]);

    expect(parameters().find(p => p.name === 'elapsedSeconds')?.operator).toBeUndefined();
  });

  it('still carries value and valueLabel alongside the operator', () => {
    component.flow = flowWith([
      {
        name: 'elapsedSeconds', type: 'number', label: 'Elapsed seconds', value: 30,
        valueLabel: 'thirty', operator: '>=',
      },
    ]);

    const merged = parameters().find(p => p.name === 'elapsedSeconds');
    expect(merged?.value as number).toBe(30);
    expect(merged?.valueLabel).toBe('thirty');
    expect(merged?.operator).toBe('>=');
  });
});

describe('EventTriggerEditorComponent with the real event catalogue', () => {
  let fixture: ComponentFixture<EventTriggerEditorComponent>;
  let component: EventTriggerEditorComponent;
  let apiSpy: jasmine.SpyObj<ApiService>;

  function localizedRef(scope: string, key: string): { $localized: { scope: string; key: string } } {
    return { $localized: { scope, key } };
  }

  function flowWith(providerId: string, eventId?: string): ActionFlow {
    return {
      triggerId: 'trigger-1',
      triggerType: EVENT_TRIGGER_TYPE,
      event: { providerId, eventId: eventId ?? '', parameters: [] },
      children: [],
    };
  }

  beforeEach(async () => {
    localStorage.clear();

    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification',
      'getEventDefinitions',
      'getLocalization',
      'updateLocalizationSettings',
    ]);
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    apiSpy.getLocalization.and.resolveTo({
      culture: 'en',
      fallbackCulture: 'en',
      translations: {
        'macrodeck.app:Obs.Provider': 'OBS Studio',
        'macrodeck.app:Obs.SceneChanged.Name': 'Scene Changed',
        'macrodeck.app:Obs.SceneChanged.Category': 'Scenes',
        'macrodeck.app:Obs.RecordingStarted.Name': 'Recording Started',
      },
      followSystem: false,
      availableCultures: ['en'],
    });

    apiSpy.getEventDefinitions.and.resolveTo({
      events: [
        {
          id: 'obs::scene-changed',
          providerId: 'obs',
          providerName: localizedRef('macrodeck.app', 'Obs.Provider'),
          isIntegration: true,
          name: localizedRef('macrodeck.app', 'Obs.SceneChanged.Name'),
          category: localizedRef('macrodeck.app', 'Obs.SceneChanged.Category'),
          deliveryKind: 'push',
          configurationParameters: [],
          payloadParameters: [],
        },
        {
          id: 'obs::recording-started',
          providerId: 'obs',
          providerName: localizedRef('macrodeck.app', 'Obs.Provider'),
          isIntegration: true,
          name: localizedRef('macrodeck.app', 'Obs.RecordingStarted.Name'),
          deliveryKind: 'push',
          configurationParameters: [],
          payloadParameters: [],
        },
      ] as unknown as EventDefinitionDto[],
    });

    await TestBed.configureTestingModule({
      imports: [EventTriggerEditorComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        ActionFlowStore,
      ],
    }).compileComponents();

    await TestBed.inject(LocalizationService).loadFromHost();

    fixture = TestBed.createComponent(EventTriggerEditorComponent);
    component = fixture.componentInstance;
    component.flow = flowWith('obs');
    fixture.detectChanges();
    await fixture.whenStable();
  });

  afterEach(() => localStorage.clear());

  it('renders provider options with their resolved names, not [object Object]', () => {
    const providerOptions = (component as unknown as { providerOptions(): { value: string; label: string }[] })
      .providerOptions();

    expect(providerOptions).toEqual([{ value: 'obs', label: 'OBS Studio' }]);
  });

  it('renders event options as the exact "Category - Name" or bare name label', () => {
    const eventOptions = (component as unknown as { eventOptions(): { value: string; label: string }[] })
      .eventOptions();

    expect(eventOptions).toEqual([
      { value: 'obs::scene-changed', label: 'Scenes - Scene Changed' },
      { value: 'obs::recording-started', label: 'Recording Started' },
    ]);
  });

  it('never renders [object Object] anywhere in the trigger editor', () => {
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).not.toContain('[object Object]');
  });
});
